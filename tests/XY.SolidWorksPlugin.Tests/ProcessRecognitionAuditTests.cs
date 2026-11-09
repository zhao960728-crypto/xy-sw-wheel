using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ProcessRecognitionAuditTests
{
    [Theory]
    [InlineData(swWzdHoleTypes_e.swTapered)]
    [InlineData(swWzdHoleTypes_e.swTaperedDrilled)]
    public void LegacyTaperedHolesDoNotAcquireAnUnspecifiedThread(swWzdHoleTypes_e kind)
    {
        Assert.False(ProcessRecognitionService.WizardHoleIsTapped((int)kind));
        var features = new[] { new ProcessFeatureFact { Name = "LegacyHole1", TypeName = "HoleWzd", HoleWizardType = (int)kind } };
        Assert.DoesNotContain(ProcessHoleFactBridge.FromFeatures(features), fact => fact.IsConfirmed && fact.ProcessName == "攻牙");
        var rows = ProcessRecognitionService.Recognize(new ProcessRecognitionInput { IsPart = true, Features = features }, true);
        Assert.Contains(rows, row => row.Name == "钻孔");
        Assert.DoesNotContain(rows, row => row.Name == "攻丝" || row.Name == "螺纹加工");
    }

    [Theory]
    [InlineData(swWzdHoleTypes_e.swTapBlind)]
    [InlineData(swWzdHoleTypes_e.swTapThru)]
    [InlineData(swWzdHoleTypes_e.swTapBlindCosmeticThread)]
    [InlineData(swWzdHoleTypes_e.swTapThruThreadThru)]
    [InlineData(swWzdHoleTypes_e.swPipeTapBlind)]
    public void ExplicitTapDefinitionsStillProduceOneConfirmedTappingStep(swWzdHoleTypes_e kind)
    {
        var features = new[] { new ProcessFeatureFact { Name = "Hole1", TypeName = "HoleWzd", HoleWizardType = (int)kind } };
        var input = new ProcessRecognitionInput { IsPart = true, Features = features, HoleFacts = ProcessHoleFactBridge.FromFeatures(features) };
        Assert.True(ProcessRecognitionService.WizardHoleIsTapped((int)kind));
        Assert.Single(ProcessRecognitionService.Recognize(input, true), row => row.Name == "攻丝");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingManualRouteSurvivesWithoutATypePresetAndKeepsRepeatedInspection(bool assembly)
    {
        var input = new ProcessRecognitionInput { IsPart = !assembly, IsAssembly = assembly, PartType = "自定义件", ExistingProcess = "来料检验 → 人工修配 → 来料检验", SurfaceTreatment = "喷粉",
            Features = new[] { new ProcessFeatureFact { TypeName = "EdgeFlange" } },
            HoleFacts = new[] { new ProcessHoleFact { IsConfirmed = true, ProcessName = "攻牙" } } };
        var config = new ProcessRecognitionConfig { AdditionalProcesses = "包装", ProcessNameMappings = "来料检验=改名检验", CustomRules = new List<ProcessRecognitionRule> { new ProcessRecognitionRule { Name = "不应覆盖", NameContains = "", MinBodyCount = 0, ProcessNames = "焊接" } } };
        Assert.Equal(new[] { "来料检验", "人工修配", "来料检验" }, ProcessRecognitionService.Recognize(input, true, config).Select(row => row.Name));
    }

    [Fact]
    public void QuickOrPartialGeometryNeverOverwritesModelCounts()
    {
        var config = new ProcessRecognitionConfig { WriteGeometryProperties = true };
        var geometry = new ProcessGeometryFact { GeometryComplete = false, SolidBodyCount = 1, SheetBodyCount = 1, ThicknessMm = 2, BendCount = 0, CutoutCount = 0 };
        var values = ProcessRecognitionService.BuildPropertyValues(new[] { new RecognizedProcess { Name = "折弯" } }, geometry, config);
        Assert.DoesNotContain(values, pair => new[] { config.ThicknessPropertyName, config.BendCountPropertyName, config.CutoutCountPropertyName }.Contains(pair.Key));
        Assert.Contains(values, pair => pair.Key == config.SummaryPropertyName && pair.Value == "折弯");
    }

    [Fact]
    public void MixedGaugesKeepAggregateCountsButDoNotClaimOneSheetThickness()
    {
        var geometry = ProcessGeometryAnalyzer.Merge(new[] {
            new ProcessGeometryFact { SolidBodyCount = 1, SheetBodyCount = 1, ThicknessMm = 2, BendCount = 1, CutoutCount = 2 },
            new ProcessGeometryFact { SolidBodyCount = 1, SheetBodyCount = 1, ThicknessMm = 3, BendCount = 2, CutoutCount = 4 } });
        var config = new ProcessRecognitionConfig { WriteGeometryProperties = true };
        var values = ProcessRecognitionService.BuildPropertyValues(new[] { new RecognizedProcess { Name = "折弯" } }, geometry, config);
        Assert.DoesNotContain(values, pair => pair.Key == config.ThicknessPropertyName);
        Assert.Contains(values, pair => pair.Key == config.BendCountPropertyName && pair.Value == "3");
        Assert.Contains(values, pair => pair.Key == config.CutoutCountPropertyName && pair.Value == "6");
    }

    [Theory]
    [InlineData("激光打标")]
    [InlineData("丝印")]
    public void ExplicitCoatingDoesNotDeleteIndependentMarkingOperations(string marking)
    {
        var input = new ProcessRecognitionInput { IsPart = true, SurfaceTreatment = "镀锌", Features = new[] {
            new ProcessFeatureFact { TypeName = "SMBaseFlange", Name = "基体法兰1" },
            new ProcessFeatureFact { TypeName = "Cut", Name = marking + "1" } } };
        var names = ProcessRecognitionService.Recognize(input, true).Select(row => row.Name).ToArray();
        Assert.Contains(marking, names); Assert.Contains("镀锌", names);
        Assert.DoesNotContain("表面处理", names);
    }

    [Fact]
    public void MarkingByItselfDoesNotSuppressTheConfiguredSurfaceStage()
    {
        var input = new ProcessRecognitionInput { IsPart = true, Features = new[] {
            new ProcessFeatureFact { TypeName = "SMBaseFlange", Name = "基体法兰1" },
            new ProcessFeatureFact { TypeName = "Cut", Name = "激光打标1" } } };
        var names = ProcessRecognitionService.Recognize(input, true).Select(row => row.Name).ToArray();
        Assert.Contains("激光打标", names); Assert.Contains("表面处理", names);
    }

    [Fact]
    public void AssemblyCompositeSurfaceUsesSeparateOrderedStepsBeforeInspection()
    {
        var input = new ProcessRecognitionInput { IsAssembly = true, SurfaceTreatment = "清洗+烤漆/丝印" };
        Assert.Equal(new[] { "装配", "清洗", "烤漆", "丝印", "检验" }, ProcessRecognitionService.Recognize(input, true).Select(row => row.Name));
    }

    [Fact]
    public void CutListBookkeepingDoesNotDeclareAMachinedPart()
    {
        var input = new ProcessRecognitionInput { IsPart = true, Features = new[] { new ProcessFeatureFact { TypeName = "CutListFolder", Name = "切割清单" } }, Geometry = new ProcessGeometryFact { SolidBodyCount = 1 } };
        Assert.Equal("待确认机加工", ProcessPartClassification.Resolve(input).Type);
        Assert.Empty(ProcessPartClassification.PropertySuggestion(input, new ProcessRecognitionConfig()));
    }

    [Fact]
    public void PartialBodyInventoryCannotSatisfyAnUpperBoundRule()
    {
        var config = new ProcessRecognitionConfig { CustomRules = new List<ProcessRecognitionRule> { new ProcessRecognitionRule { Name = "单体规则", MaxBodyCount = 1, ProcessNames = "铣削" } } };
        var input = new ProcessRecognitionInput { IsPart = true, Geometry = new ProcessGeometryFact { SolidBodyCount = 1, GeometryComplete = false } };
        Assert.Empty(ProcessRuleEngine.Preview(config, input).Matches);
        input.Geometry.GeometryComplete = true;
        Assert.Single(ProcessRuleEngine.Preview(config, input).Matches);
    }

    [Fact]
    public void ExhaustedChildFeatureBudgetAndUnknownSuppressionAreNeverComplete()
    {
        var parent = Feature(call => call.MethodName == "GetFirstSubFeature" ? Feature(child => child.MethodName == "IsSuppressed2" ? new[] { false } : null)
            : call.MethodName == "IsSuppressed2" ? new[] { false } : null);
        var method = typeof(SolidWorksProcessFeatureReader).GetMethod("AddSubFeatures", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.False((bool)method.Invoke(null, new object[] { new List<ProcessFeatureFact>(), parent, 0, Stopwatch.StartNew(), 0 })!);
        var add = typeof(SolidWorksProcessFeatureReader).GetMethod("AddFeature", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.False((bool)add.Invoke(null, new object[] { new List<ProcessFeatureFact>(), Feature(call => null), false })!);
    }

    private static IFeature Feature(Func<IMethodCallMessage, object?> body) => (IFeature)new FeatureProxy(body).GetTransparentProxy();
    private sealed class FeatureProxy : RealProxy
    {
        private readonly Func<IMethodCallMessage, object?> _body;
        internal FeatureProxy(Func<IMethodCallMessage, object?> body) : base(typeof(IFeature)) => _body = body;
        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            try { return new ReturnMessage(_body(call), call.Args, call.Args.Length, call.LogicalCallContext, call); }
            catch (Exception error) { return new ReturnMessage(error, call); }
        }
    }
}
