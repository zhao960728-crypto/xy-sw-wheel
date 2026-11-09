using System;
using System.Linq;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ProcessPresetCompatibilityTests
{
    [Fact]
    public void RequestedManufacturingNamesAreAllSeparateChoicesAndGroupsKeepTheirOrder()
    {
        var config = new ProcessRecognitionConfig();
        var names = ProcessPresets.Catalog(config).Select(entry => entry.Name).ToArray();
        var required = new[] { "激光", "去毛刺", "沉孔", "攻牙", "压铆", "冲百叶窗", "打凸包", "折弯", "组焊", "龙门铣", "打磨", "抛光", "拉丝", "烤漆", "丝印", "装配" };
        foreach (string name in required) Assert.Contains(name, names);
        Assert.DoesNotContain("激光去毛刺沉孔攻牙", names);
        Assert.Equal(new[] { "激光", "去毛刺", "沉孔", "攻牙", "检验" }, ProcessRecognitionConfig.ResolveCombination(config, "激光沉孔攻牙").Select(item => item.Name));
        Assert.Equal(new[] { "数冲", "冲百叶窗", "打凸包", "去毛刺", "折弯", "检验" }, ProcessRecognitionConfig.ResolveCombination(config, "百叶凸包成形").Select(item => item.Name));
        var finish = ProcessRecognitionConfig.ResolveCombination(config, "烤漆丝印总装").Select(item => item.Name).ToList();
        Assert.True(finish.IndexOf("烤漆") < finish.IndexOf("丝印"));
        Assert.True(finish.IndexOf("丝印") < finish.IndexOf("装配"));
        Assert.Contains(ProcessPresets.Catalog(config), entry => entry.Name == "龙门铣" && entry.Logic.Contains("不凭模型外形"));
    }

    [Fact]
    public void OlderCustomGroupsMissingBindingFieldKeepTheirUnboundMeaning()
    {
        var config = ConfigStore.Deserialize("{\"ProcessRecognition\":{\"ProcessCombinations\":\"自用组=激光>沉孔>攻牙\"}}");
        ConfigStore.Normalize(config);
        Assert.Equal("自用组=激光>沉孔>攻牙", config.ProcessRecognition.ProcessCombinations);
        Assert.Equal("", config.ProcessRecognition.ProcessTypeGroups);
        var roundTrip = ConfigStore.Clone(config);
        Assert.Equal("", roundTrip.ProcessRecognition.ProcessTypeGroups);
        Assert.Equal(config.ProcessRecognition.ProcessCombinations, roundTrip.ProcessRecognition.ProcessCombinations);
    }

    [Theory]
    [InlineData("")]
    [InlineData("方通=自用组")]
    public void ExistingBindingFieldIsPreservedEvenWhenEmpty(string bindings)
    {
        string json = "{\"ProcessRecognition\":{\"ProcessCombinations\":\"自用组=切管>检验\",\"ProcessTypeGroups\":\"" + bindings + "\"}}";
        var config = ConfigStore.Deserialize(json);
        ConfigStore.Normalize(config);
        Assert.Equal(bindings, config.ProcessRecognition.ProcessTypeGroups);
        Assert.Equal("自用组=切管>检验", config.ProcessRecognition.ProcessCombinations);
    }

    [Fact]
    public void ANewSectionGetsPresetsWhileExplicitSupplementKeepsOldDefinitions()
    {
        var fresh = ConfigStore.Deserialize("{}");
        Assert.Equal(ProcessPresets.DefaultTypeGroups, fresh.ProcessRecognition.ProcessTypeGroups);
        Assert.All(ProcessRecognitionConfig.ParseProcessNameMappings(fresh.ProcessRecognition.ProcessTypeGroups), pair => Assert.NotEmpty(ProcessRecognitionConfig.ResolveCombination(fresh.ProcessRecognition, pair.Value)));
        var old = ConfigStore.Deserialize("{\"ProcessRecognition\":{\"ProcessCombinations\":\"总焊=人工组对>特殊打磨\",\"ProcessCategories\":\"自用类别=总焊\"}}");
        ProcessPresets.AddMissing(old.ProcessRecognition);
        Assert.Equal(new[] { "人工组对", "特殊打磨" }, ProcessRecognitionConfig.ResolveCombination(old.ProcessRecognition, "总焊").Select(item => item.Name));
        Assert.Contains("自用类别=总焊", old.ProcessRecognition.ProcessCategories);
        Assert.All(ProcessRecognitionConfig.ParseProcessNameMappings(old.ProcessRecognition.ProcessTypeGroups), pair => Assert.NotEmpty(ProcessRecognitionConfig.ResolveCombination(old.ProcessRecognition, pair.Value)));
    }
}
