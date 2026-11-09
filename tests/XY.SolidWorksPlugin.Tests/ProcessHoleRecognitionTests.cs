using System;
using System.Linq;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ProcessHoleRecognitionTests
{
    [Fact]
    public void GenericThreadOnMachinedShaftDoesNotProveTappedHole()
    {
        var input = new ProcessRecognitionInput { IsPart = true, DocumentTitle = "自制轴", Purchase = "否", Features = new[] {
            new ProcessFeatureFact { Name = "旋转1", TypeName = "Revolution" },
            new ProcessFeatureFact { Name = "螺纹1", TypeName = "Thread" } } };
        var rows = ProcessRecognitionService.Recognize(input, false, new ProcessRecognitionConfig());
        Assert.Contains(rows, row => row.Name == "螺纹加工");
        Assert.DoesNotContain(rows, row => row.Name == "攻丝" || row.Name == "攻牙");
    }

    [Fact]
    public void HoleLinkUsesConfiguredThreadNameWithoutDuplicateStep()
    {
        var config = new ProcessRecognitionConfig { ProcessNameMappings = "攻丝=丝锥攻牙" };
        var facts = new[] { new ProcessHoleFact { ProcessName = "攻牙", Source = "NativeJournal", IsConfirmed = true } };
        var rows = ProcessRecognitionService.MergeHoleProcesses(new[] { new RecognizedProcess { Name = "丝锥攻牙", Confidence = 90 } }, facts, config);
        Assert.Equal("丝锥攻牙", Assert.Single(rows).Name);
        Assert.Equal("丝锥攻牙", Assert.Single(ProcessRecognitionService.MergeHoleProcesses(Array.Empty<RecognizedProcess>(), facts, config)).Name);
    }

    [Fact]
    public void ConfirmedHoleResultsAugmentAutomaticRouteAndKeepSource()
    {
        var input = new ProcessRecognitionInput { IsPart = true, PartType = "钣金", HoleFacts = new[] {
            new ProcessHoleFact { ProcessName = "沉孔", Source = "NativeFeature", IsConfirmed = true, Evidence = "孔向导沉孔" },
            new ProcessHoleFact { ProcessName = "压铆", Source = "NativeJournal", IsConfirmed = true, Evidence = "已确认压铆" },
            new ProcessHoleFact { ProcessName = "攻牙", Source = "NativeJournal", IsConfirmed = false, Evidence = "仅孔径相符" } } };
        var rows = ProcessRecognitionService.Recognize(input, false, new ProcessRecognitionConfig());
        Assert.Contains(rows, row => row.Name == "沉孔" && row.Evidence.Contains("NativeFeature"));
        Assert.Contains(rows, row => row.Name == "压铆" && row.Evidence.Contains("已确认压铆"));
        Assert.DoesNotContain(rows, row => row.Name == "攻牙" || row.Name == "攻丝");
    }

    [Fact]
    public void TapAliasesDoNotDuplicateAndKnownRouteRemains()
    {
        var rows = ProcessRecognitionService.MergeHoleProcesses(new[] {
            new RecognizedProcess { Name = "钻孔", Evidence = "特征", Confidence = 80 },
            new RecognizedProcess { Name = "攻丝", Evidence = "螺纹", Confidence = 90 },
            new RecognizedProcess { Name = "折弯", Confidence = 80 } }, new[] {
                new ProcessHoleFact { ProcessName = "攻牙", Source = "NativeJournal", IsConfirmed = true, Evidence = "M4" } });
        Assert.Equal(new[] { "钻孔", "攻丝", "折弯" }, rows.Select(row => row.Name));
        Assert.Contains("NativeJournal", rows[1].Evidence);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SupplyAndExistingProcessDoNotGetImplicitManufacturing(bool purchased)
    {
        var input = new ProcessRecognitionInput { IsPart = true, PartType = purchased ? "外购" : "方通",
            ExistingProcess = purchased ? "" : "人工检验", HoleFacts = new[] {
            new ProcessHoleFact { ProcessName = "压铆", Source = "NativeJournal", IsConfirmed = true } } };
        var rows = ProcessRecognitionService.Recognize(input, false, new ProcessRecognitionConfig());
        Assert.DoesNotContain(rows, row => row.Name == "压铆");
        Assert.Contains(rows, row => row.Name == (purchased ? "外购" : "人工检验"));
    }

    [Fact]
    public void SnapshotRetainsHoleSourcesAndCandidateState()
    {
        var input = new ProcessRecognitionInput { IsPart = true, HoleFacts = new[] {
            new ProcessHoleFact { ProcessName = "沉孔", Source = "FeatureCandidate", IsConfirmed = false, Evidence = "名称提示", Reference = "test" } } };
        var snapshot = ProcessRecognitionSnapshot.Decode(ProcessRecognitionSnapshot.Create(input, Array.Empty<RecognizedProcess>(), false).Encode());
        var fact = Assert.Single(snapshot.Input.Restore().HoleFacts);
        Assert.False(fact.IsConfirmed);
        Assert.Equal("FeatureCandidate", fact.Source);
        Assert.Equal("test", fact.Reference);
    }
}
