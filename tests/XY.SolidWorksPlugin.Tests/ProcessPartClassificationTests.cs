using System;
using System.Linq;
using System.Web.Script.Serialization;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ProcessPartClassificationTests
{
    private static ProcessRecognitionInput BentWelded() => new ProcessRecognitionInput {
        IsPart = true, DocumentTitle = "匿名零件", Geometry = new ProcessGeometryFact { SolidBodyCount = 1, SheetBodyCount = 1, ThicknessMm = 2, BendCount = 1 },
        Features = new[] { new ProcessFeatureFact { TypeName = "SmBaseFlange" }, new ProcessFeatureFact { TypeName = "EdgeFlange" }, new ProcessFeatureFact { TypeName = "CosmeticWeldBead" } }
    };

    [Fact]
    public void BentWeldedSheetDoesNotNeedStructuralMemberToBeMixed()
    {
        Assert.Equal(PartTypes.Mixed, PartTypeCatalog.FromGeometry(true, false, true, false, false, false, false).Type);
        var input = BentWelded();
        Assert.Equal(PartTypes.Mixed, ProcessPartClassification.Resolve(input).Type);
        Assert.Equal(PartTypes.Mixed, AssemblyProcessRecognitionWindow.Classification(input, new[] { new RecognizedProcess { Name = "折弯" }, new RecognizedProcess { Name = "焊接" } }));
        string analysis = ProcessManufacturingPlan.Describe(input, true, new ProcessRecognitionConfig());
        Assert.Contains("分类推定：钣金焊接件", analysis);
        Assert.Contains("分类依据：", analysis);
    }

    [Fact]
    public void AnAnonymousSolidBlockRemainsUnconfirmedAndDoesNotSuggestTypeWrite()
    {
        var input = new ProcessRecognitionInput { IsPart = true, DocumentTitle = "匿名块", Geometry = new ProcessGeometryFact { SolidBodyCount = 1, FaceCount = 6, PlanarFaceCount = 6 } };
        var rows = new[] { new RecognizedProcess { Name = "机加工", Evidence = "通用加工待确认" } };
        Assert.Equal("待确认机加工", AssemblyProcessRecognitionWindow.Classification(input, rows));
        Assert.Equal(0, ProcessPartClassification.Resolve(input, rows).Confidence);
        var values = ProcessRecognitionService.BuildPropertyValues(rows, input.Geometry, new ProcessRecognitionConfig(), input: input);
        Assert.DoesNotContain(values, pair => pair.Key == "类型");
        Assert.Contains("缺少可确认", ProcessPartClassification.Describe(input, rows));
    }

    [Fact]
    public void AStockTubeDoesNotBecomeWeldedFromItsStructuralContainer()
    {
        var input = new ProcessRecognitionInput { IsPart = true, Geometry = new ProcessGeometryFact { SolidBodyCount = 1, StraightTubeBodyCount = 1, ProfileKind = "方通" }, Features = new[] { new ProcessFeatureFact { TypeName = "StructuralMember" } } };
        Assert.Equal(PartTypes.Profile, ProcessPartClassification.Resolve(input).Type);
        Assert.Equal(PartTypes.Profile, ProcessRecognitionService.ResolvePartType(input, false, true, false, true, false, false, false).Type);
        Assert.Contains("型材件", ProcessPartClassification.Describe(input));
    }

    [Fact]
    public void SupplyClassificationAndCompatibleListDisplayRemainDistinct()
    {
        var input = BentWelded(); input.Purchase = "是";
        Assert.Equal(PartTypes.Purchased, ProcessPartClassification.Resolve(input).Type);
        Assert.Equal("标准/外购件", AssemblyProcessRecognitionWindow.Classification(input, Array.Empty<RecognizedProcess>()));
        Assert.Contains("供货分类：外购/标准零件", ProcessPartClassification.Describe(input));
        input.IsPart = false; input.IsAssembly = true;
        Assert.Contains("外购装配总成", ProcessManufacturingPlan.Describe(input, true, new ProcessRecognitionConfig()));
    }

    [Fact]
    public void TypeSuggestionsAreReadOnlyAndOnlyFillBlankOrOtherWhenEvidenceIsReliable()
    {
        var input = BentWelded(); var config = new ProcessRecognitionConfig();
        var rows = new[] { new RecognizedProcess { Name = "折弯" }, new RecognizedProcess { Name = "焊接" } };
        string before = new JavaScriptSerializer().Serialize(input);
        Assert.Contains(ProcessRecognitionService.BuildPropertyValues(rows, input.Geometry, config, input: input), pair => pair.Key == "类型" && pair.Value == PartTypes.Mixed);
        Assert.Equal(before, new JavaScriptSerializer().Serialize(input));
        input.PartType = PartTypes.Other;
        Assert.Equal(PartTypes.Mixed, Assert.Single(ProcessPartClassification.PropertySuggestion(input, config, rows)).Value);
        input.PartType = "人工自制类型";
        Assert.Empty(ProcessPartClassification.PropertySuggestion(input, config, rows));
        input.PartType = ""; input.Geometry.GeometryComplete = false;
        Assert.Empty(ProcessPartClassification.PropertySuggestion(input, config, rows));
        input.DocumentTitle = "钣金支架";
        Assert.Equal(PartTypes.Mixed, Assert.Single(ProcessPartClassification.PropertySuggestion(input, config, rows)).Value);
    }

    [Fact]
    public void NewConfigurationsExposeDefaultBindingsAndStoredEmptyBindingsStayEmpty()
    {
        Assert.Equal(ProcessPresets.DefaultTypeGroups, new ProcessRecognitionConfig().ProcessTypeGroups);
        var old = new JavaScriptSerializer().Deserialize<ProcessRecognitionConfig>("{\"ProcessTypeGroups\":\"\"}");
        ProcessRecognitionConfig.Normalize(old);
        Assert.Equal("", old.ProcessTypeGroups);
    }

    [Fact]
    public void SingleDeclaredAngleUsesStockRouteWhileNativeAndMixedSheetsStaySheetRoutes()
    {
        var sheetLikeAngle = new ProcessGeometryFact { SolidBodyCount = 1, SheetBodyCount = 1, ThicknessMm = 4, BendCount = 1, GeometryComplete = true };
        var input = new ProcessRecognitionInput { IsPart = true, DocumentTitle = "角钢", ProfileDescription = "角钢40x40x4", Geometry = new ProcessGeometryFact { SolidBodyCount = 1, BodyFacts = new[] { sheetLikeAngle } } };
        var plan = ProcessManufacturingPlan.Build(input, true, new ProcessRecognitionConfig());
        Assert.Contains("锯切下料", Assert.Single(plan.BodyRoutes).Processes);
        Assert.DoesNotContain("激光下料", Assert.Single(plan.BodyRoutes).Processes);
        Assert.DoesNotContain("折弯", Assert.Single(plan.BodyRoutes).Processes);
        sheetLikeAngle.IsNativeSheetMetal = true; input.Geometry.IsNativeSheetMetal = true;
        Assert.Contains("激光下料", Assert.Single(ProcessManufacturingPlan.Build(input, true, new ProcessRecognitionConfig()).BodyRoutes).Processes);
        Assert.Equal(PartTypes.SheetMetal, ProcessPartClassification.Resolve(input).Type);
        input.Geometry.SolidBodyCount = 2;
        input.Geometry.BodyFacts = new[] { sheetLikeAngle, new ProcessGeometryFact { SolidBodyCount = 1, StraightTubeBodyCount = 1, ProfileKind = "方通" } };
        var mixed = ProcessManufacturingPlan.Build(input, true, new ProcessRecognitionConfig());
        Assert.Contains("激光下料", mixed.BodyRoutes[0].Processes);
        Assert.Contains("锯切下料", mixed.BodyRoutes[1].Processes);
    }

    [Fact]
    public void PlainAssemblyAnalysisDoesNotInventWeldingCategory()
    {
        var input = new ProcessRecognitionInput { IsAssembly = true, DocumentTitle = "匿名总成" };
        Assert.Equal("装配总成", ProcessPartClassification.Resolve(input).Type);
        Assert.Contains("分类推定：装配总成", ProcessManufacturingPlan.Describe(input, false, new ProcessRecognitionConfig()));
    }

    [Theory]
    [InlineData("L50x50x5", "角钢", "角钢下料")]
    [InlineData("H200x200x8x12", "H型钢", "H型钢下料")]
    [InlineData("C100x50x20x2", "C型钢", "C型钢下料")]
    public void StructuralProfileSpecificationProvidesTypeEvidenceWithoutNameKeyword(string specification, string expectedType, string expectedGroup)
    {
        var input = new ProcessRecognitionInput
        {
            IsPart = true,
            ProfileDescription = specification,
            Geometry = new ProcessGeometryFact { GeometryComplete = true, SolidBodyCount = 1, StraightTubeBodyCount = 1, ProfileKind = expectedType }
        };

        Assert.Equal(PartTypes.Profile, ProcessPartClassification.Resolve(input).Type);
        var match = ProcessTypeGroupPolicy.Resolve(new ProcessRecognitionConfig(), input);
        Assert.True(match.Matched, match.Reason);
        Assert.Equal(expectedGroup, match.Group);
        Assert.Equal(new[] { "锯切下料", "去毛刺", "检验", "包装" }, ProcessRecognitionService.Recognize(input, true).Select(row => row.Name));
    }

    [Fact]
    public void MixedProfileEvidenceDoesNotCollapseIntoOneClassification()
    {
        var input = new ProcessRecognitionInput
        {
            IsPart = true,
            Geometry = ProcessGeometryAnalyzer.Merge(new[]
            {
                new ProcessGeometryFact { BodyName = "方管", SolidBodyCount = 1, StraightTubeBodyCount = 1, ProfileKind = "方通", GeometryComplete = true },
                new ProcessGeometryFact { BodyName = "角件", SolidBodyCount = 1, ProfileKind = "角钢", GeometryComplete = true }
            })
        };

        var type = ProcessPartClassification.Resolve(input);

        Assert.Equal("型材混合待确认", type.Type);
        Assert.Contains("多种型材截面", type.Evidence);
        Assert.Equal(0, type.Confidence);
    }

    [Fact]
    public void DeclaredProfileConflictingWithSectionGeometryStaysPending()
    {
        var input = new ProcessRecognitionInput
        {
            IsPart = true,
            PartType = "角钢",
            Geometry = new ProcessGeometryFact { SolidBodyCount = 1, StraightTubeBodyCount = 1, ProfileKind = "方通", GeometryComplete = true }
        };

        var type = ProcessPartClassification.Resolve(input);

        Assert.Equal("型材混合待确认", type.Type);
        Assert.Contains("不一致", type.Evidence);
    }
}
