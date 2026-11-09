using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ProcessRuleEngineTests
{
    private static ProcessRecognitionConfig Config(params ProcessRecognitionRule[] rules) => new ProcessRecognitionConfig { CustomRules = rules.ToList() };
    private static ProcessRecognitionRule Rule() => new ProcessRecognitionRule { Name = "不锈钢折弯焊接", MaterialContains = "SUS", MinBendCount = 1, HasWeldEvidence = true, ProcessNames = "焊接>打磨" };
    private static ProcessRecognitionInput Input() => new ProcessRecognitionInput { IsPart = true, Material = "SUS304", Geometry = new ProcessGeometryFact { SolidBodyCount = 2, BendCount = 2 }, Features = new[] { new ProcessFeatureFact { TypeName = "CosmeticWeldBead" } } };

    [Fact]
    public void RuleRequiresAllEvidenceAndRecordsReasonWithoutChangingSource()
    {
        var config = Config(Rule()); var input = Input();
        var source = new[] { new RecognizedProcess { Name = "折弯", Evidence = "实体折弯" } };
        var result = ProcessRuleEngine.Apply(config, input, source);
        Assert.Equal(new[] { "折弯", "焊接", "打磨" }, result.Processes.Select(item => item.Name));
        Assert.Contains("不锈钢折弯焊接", Assert.Single(result.Matches));
        Assert.Single(source);
        input.Material = "Q235";
        Assert.Empty(ProcessRuleEngine.Preview(config, input).Matches);
        input.Material = "SUS304"; input.Features = new[] { new ProcessFeatureFact { TypeName = "CosmeticWeldBead", IsSuppressed = true } };
        Assert.Empty(ProcessRuleEngine.Preview(config, input).Matches);
    }

    [Fact]
    public void GroupRuleRetainsRepeatedStepsAndListOrder()
    {
        var config = Config(new ProcessRecognitionRule { Name = "二次检验", NameContains = "测试", Action = "group", GroupName = "二次检验组" }, new ProcessRecognitionRule { Name = "交付", NameContains = "测试", ProcessNames = "包装" });
        config.ProcessCombinations = "二次检验组=检验>焊接>检验";
        var result = ProcessRuleEngine.Preview(config, new ProcessRecognitionInput { DocumentTitle = "测试零件" });
        Assert.Equal(new[] { "检验", "焊接", "检验", "包装" }, result.Processes.Select(item => item.Name));
        Assert.Equal(2, result.Matches.Count);
    }

    [Fact]
    public void InvalidRulesAreRejectedWithoutPartialApplicationOrTruncation()
    {
        var config = Config(Rule(), new ProcessRecognitionRule { Name = "无效范围", MinBendCount = 4, MaxBendCount = 1, ProcessNames = "下料>>" + new string('长', 65) });
        string original = ProcessRecognitionBatchCache.SettingsKey(config);
        var result = ProcessRuleEngine.Apply(config, Input(), new[] { new RecognizedProcess { Name = "折弯" } });
        Assert.NotEmpty(result.Errors);
        Assert.Equal("折弯", Assert.Single(result.Processes).Name);
        Assert.Empty(result.Matches);
        Assert.Equal(original, ProcessRecognitionBatchCache.SettingsKey(config));
        config.CustomRules = new List<ProcessRecognitionRule> { new ProcessRecognitionRule { Name = "无条件", ProcessNames = "焊接" } };
        Assert.Contains(ProcessRuleEngine.Validate(config), message => message.Contains("至少填写一个条件"));
    }

    [Fact]
    public void PurchaseIdentityBlocksManufacturingRuleAndIncompleteGeometryDoesNotProveAbsence()
    {
        var config = Config(new ProcessRecognitionRule { Name = "无折弯", MaxBendCount = 0, ProcessNames = "检验" });
        Assert.Empty(ProcessRuleEngine.Preview(config, new ProcessRecognitionInput()).Matches);
        Assert.Empty(ProcessRuleEngine.Preview(config, new ProcessRecognitionInput { Geometry = new ProcessGeometryFact { SolidBodyCount = 1, GeometryComplete = false } }).Matches);
        var input = Input(); input.Purchase = "是";
        var purchased = ProcessRuleEngine.Preview(Config(Rule()), input);
        Assert.Empty(purchased.Processes);
        Assert.Contains("外购", Assert.Single(purchased.Matches));
        Assert.False(ProcessRuleEngine.HasWeldEvidence(new ProcessRecognitionInput { Features = new[] { new ProcessFeatureFact { TypeName = "Weldment", Name = "结构构件" } } }));
    }

    [Fact]
    public void NewFieldsRoundTripAndLegacyHasNoAutomaticRuleOrBinding()
    {
        var config = Config(Rule()); config.AvailableProcessNames = "激光刻字|清洗";
        var serializer = new JavaScriptSerializer();
        var restored = serializer.Deserialize<ProcessRecognitionConfig>(serializer.Serialize(config));
        Assert.Equal("SUS", Assert.Single(restored.CustomRules).MaterialContains);
        Assert.Equal(1, Assert.Single(restored.CustomRules).MinBendCount);
        Assert.Equal(config.AvailableProcessNames, restored.AvailableProcessNames);
        var legacy = serializer.Deserialize<ProcessRecognitionConfig>("{\"ProcessCombinations\":\"人工组=清洗>检验\",\"ProcessTypeGroups\":\"\"}");
        ProcessRecognitionConfig.Normalize(legacy);
        Assert.Empty(legacy.CustomRules);
        Assert.Equal("", legacy.ProcessTypeGroups);
        Assert.Equal("人工组=清洗>检验", legacy.ProcessCombinations);
    }

    [Fact]
    public void ExplicitPresetsPreserveCustomItemsAndCatalogueDoesNotAppendChoices()
    {
        var config = new ProcessRecognitionConfig { ProcessCombinations = "总焊=人工焊接>自定义检验", ProcessTypeGroups = "方管=总焊", ProcessCategories = "人工类别=总焊", AvailableProcessNames = string.Join("|", Enumerable.Range(0, 101).Select(i => "用户工序" + i)) };
        ProcessPresets.AddMissing(config);
        Assert.Equal("人工焊接", ProcessRecognitionConfig.ResolveCombination(config, "总焊")[0].Name);
        Assert.Contains("方管=总焊", config.ProcessTypeGroups);
        Assert.DoesNotContain("方通=", config.ProcessTypeGroups);
        Assert.Contains("人工类别=总焊", config.ProcessCategories);
        var catalogue = ProcessPresets.Catalog(config);
        Assert.Contains(catalogue, item => item.Name == "用户工序100" && item.Source.Contains("用户目录"));
        Assert.Equal("", config.AdditionalProcesses);
        Assert.Empty(ProcessRuleEngine.Validate(config));
    }

    [Fact]
    public void PresetFailureIsAtomicAndLongTypeChainsRetainAllSteps()
    {
        var config = new ProcessRecognitionConfig { ProcessTypeGroups = "坏关联", ProcessCombinations = "人工组=清洗>检验" };
        Assert.Throws<InvalidOperationException>(() => ProcessPresets.AddMissing(config));
        Assert.Equal("人工组=清洗>检验", config.ProcessCombinations);
        string steps = string.Join(">", Enumerable.Range(1, 20).Select(i => "工序" + i));
        config.PartTypeChains = "自定义类型=" + steps;
        ProcessRecognitionConfig.Normalize(config);
        Assert.Equal(20, ProcessRecognitionConfig.ResolveTypeChain(config, "自定义类型").Count);
        Assert.Equal(steps, ProcessRecognitionConfig.ParseCombinations(config.PartTypeChains).Single().Value);
    }

    [Theory]
    [InlineData("压铆螺母M6")]
    [InlineData("压铆螺柱M4")]
    [InlineData("压铆螺钉M3")]
    [InlineData("PEM螺母-M5")]
    public void PressFastenerPartNamesAreSupplyTypes(string title)
        => Assert.Equal(PartTypes.Standard, PartTypeCatalog.FromText("", title).Type);

    [Theory]
    [InlineData("直线导轨安装板.sldprt", PartTypes.SheetMetal)]
    [InlineData("气缸固定座", PartTypes.Machined)]
    [InlineData("外购直线导轨安装板", PartTypes.Purchased)]
    [InlineData("ball screw-1605", PartTypes.Purchased)]
    [InlineData("compression latch", PartTypes.Purchased)]
    [InlineData("solenoid valve", PartTypes.Purchased)]
    public void SupplyFamiliesAndManufacturedAccessoriesKeepTheirIdentity(string name, string expected)
        => Assert.Equal(expected, PartTypeCatalog.FromText("", name).Type);

    [Fact]
    public void TypeBindingAcceptsOnlyCompleteUniformTubeGeometry()
    {
        var config = new ProcessRecognitionConfig { ProcessTypeGroups = "圆管=圆管下料" };
        var input = new ProcessRecognitionInput { Geometry = new ProcessGeometryFact { SolidBodyCount = 2, StraightTubeBodyCount = 2, ProfileKind = "圆管", GeometryComplete = true } };
        Assert.Equal("圆管下料", ProcessTypeGroupPolicy.Resolve(config, input).Group);
        input.Geometry.StraightTubeBodyCount = 1;
        Assert.False(ProcessTypeGroupPolicy.Resolve(config, input).Matched);
        input.Geometry.StraightTubeBodyCount = 2; input.Geometry.GeometryComplete = false;
        Assert.False(ProcessTypeGroupPolicy.Resolve(config, input).Matched);
    }

    [Fact]
    public void AppendCanInsertBeforeBendAndUnknownAnchorRollsBackAllRules()
    {
        var input = new ProcessRecognitionInput { DocumentTitle = "测试安装板" };
        var rule = new ProcessRecognitionRule { Name = "折弯前装五金", NameContains = "测试", ProcessNames = "压铆>压钉", BeforeProcess = "折弯" };
        var config = Config(rule);
        var source = new[] { "下料", "折弯", "检验", "出货" }.Select(name => new RecognizedProcess { Name = name, Evidence = "原始证据" }).ToArray();
        Assert.Equal(new[] { "下料", "压铆", "压钉", "折弯", "检验", "出货" }, ProcessRuleEngine.Apply(config, input, source).Processes.Select(item => item.Name));
        config.CustomRules.Insert(0, new ProcessRecognitionRule { Name = "前一条补充", NameContains = "测试", ProcessNames = "清洗" });
        rule.BeforeProcess = "不存在";
        var rejected = ProcessRuleEngine.Apply(config, input, source);
        Assert.Equal(source.Select(item => item.Name), rejected.Processes.Select(item => item.Name));
        Assert.All(rejected.Processes, item => Assert.Equal("原始证据", item.Evidence));
        Assert.Empty(rejected.Matches);
        Assert.Contains("插入位置", Assert.Single(rejected.Errors));
    }

    [Fact]
    public void PositionedInspectionPreservesFinalInspectionAsSeparateStage()
    {
        var config = Config(new ProcessRecognitionRule { Name = "折弯前检查", NameContains = "检查", ProcessNames = "检验", BeforeProcess = "折弯" });
        var input = new ProcessRecognitionInput { DocumentTitle = "检查板" };
        var result = ProcessRuleEngine.Apply(config, input, new[] { "下料", "折弯", "检验" }.Select(name => new RecognizedProcess { Name = name }));
        Assert.Equal(new[] { "下料", "检验", "折弯", "检验" }, result.Processes.Select(item => item.Name));
        var rerun = ProcessRuleEngine.Apply(config, input, result.Processes);
        Assert.Equal(result.Processes.Select(item => item.Name), rerun.Processes.Select(item => item.Name));
    }

    [Fact]
    public void IncompleteFeatureScanCannotEstablishWeldAbsenceOrBendUpperBound()
    {
        var input = new ProcessRecognitionInput { IsPart = true, FeatureReadComplete = false,
            Geometry = new ProcessGeometryFact { SolidBodyCount = 1, GeometryComplete = true, BendCount = 0 },
            Features = new[] { new ProcessFeatureFact { Name = "已读取孔", TypeName = "HoleWzd" } } };
        var config = Config(new ProcessRecognitionRule { Name = "没有焊接", HasWeldEvidence = false, ProcessNames = "检查未焊" },
            new ProcessRecognitionRule { Name = "没有折弯", MaxBendCount = 0, ProcessNames = "检查平板" },
            new ProcessRecognitionRule { Name = "已找到孔", FeatureContains = "HoleWzd", ProcessNames = "钻孔" });
        var partial = ProcessRuleEngine.Preview(config, input);
        Assert.Equal("钻孔", Assert.Single(partial.Processes).Name);
        Assert.Contains("已找到孔", Assert.Single(partial.Matches));
        input.FeatureReadComplete = true;
        Assert.Equal(new[] { "检查未焊", "检查平板", "钻孔" }, ProcessRuleEngine.Preview(config, input).Processes.Select(item => item.Name));
        input.FeatureReadComplete = false;
        input.Features = new[] { new ProcessFeatureFact { TypeName = "CosmeticWeldBead" } };
        var positiveWeld = Config(new ProcessRecognitionRule { Name = "已找到焊缝", HasWeldEvidence = true, ProcessNames = "焊缝检验" });
        Assert.Equal("焊缝检验", Assert.Single(ProcessRuleEngine.Preview(positiveWeld, input).Processes).Name);
    }
}
