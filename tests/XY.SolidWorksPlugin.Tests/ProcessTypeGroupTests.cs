using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;
namespace XY.SolidWorksPlugin.Tests;

public sealed class ProcessTypeGroupTests
{
    private static ProcessRecognitionConfig Config() => new ProcessRecognitionConfig { ProcessCombinations = "方通组=锯切>钻孔>检验|角钢组=下料>去毛刺|自选组=焊接>喷粉", ProcessTypeGroups = "方通=方通组|角钢=角钢组" };
    [Theory]
    [InlineData("方通", "角钢支架", "方通组")]
    [InlineData("方管", "", "方通组")]
    [InlineData("角铁", "方通零件", "角钢组")]
    [InlineData("型材件", "角钢支架", "角钢组")]
    [InlineData("", "方通横梁", "方通组")]
    [InlineData("钣金件", "方通盖板", "")]
    [InlineData("", "方通角钢组合", "")]
    [InlineData("", "普通零件", "")]
    public void TypeEvidenceIsSpecificAndConflictsAreNotGuessed(string type, string title, string expected)
    {
        Assert.Equal(expected, ProcessTypeGroupPolicy.Resolve(Config(), new ProcessRecognitionInput { PartType = type, DocumentTitle = title }).Group);
    }
    [Fact]
    public void RecognitionUsesOneSavedGroupDefinitionAndPreservesExistingChoices()
    {
        var config = Config();
        var input = new ProcessRecognitionInput { IsPart = true, PartType = "方通" };
        Assert.Equal(new[] {"锯切","钻孔","检验"}, ProcessRecognitionService.Recognize(input, false, config).Select(row => row.Name));
        config.ProcessCombinations = "方通组=切管>攻丝|角钢组=下料>去毛刺|自选组=焊接>喷粉";
        Assert.Equal(new[] {"切管","攻丝"}, ProcessRecognitionService.Recognize(input, false, config).Select(row => row.Name));
        input.ProcessType = "自选组";
        Assert.Equal(new[] {"焊接","喷粉"}, ProcessRecognitionService.Recognize(input, false, config).Select(row => row.Name));
        input.ProcessType = ""; input.ExistingProcess = "手工下料-终检";
        Assert.Equal(new[] {"手工下料","终检"}, ProcessRecognitionService.Recognize(input, false, config).Select(row => row.Name));
    }
    [Fact]
    public void MissingGroupsAndAliasConflictsAreNotApplied()
    {
        var config = Config(); config.ProcessTypeGroups = "方通=不存在";
        Assert.False(ProcessTypeGroupPolicy.Resolve(config, new ProcessRecognitionInput { PartType = "方通" }).Matched);
        config.ProcessTypeGroups = "方通=方通组|方管=角钢组";
        Assert.False(ProcessTypeGroupPolicy.Resolve(config, new ProcessRecognitionInput { PartType = "方通" }).Matched);
    }
    [Fact]
    public void NewMappingsRoundTripAndInvalidateRecognitionCache()
    {
        var config = Config(); var oldKey = ProcessRecognitionBatchCache.SettingsKey(config);
        var copy = ProcessRulesWindow.Copy(config); Assert.Equal(config.ProcessTypeGroups, copy.ProcessTypeGroups);
        copy.ProcessTypeGroups = "方通=角钢组"; Assert.NotEqual(oldKey, ProcessRecognitionBatchCache.SettingsKey(copy));
        ProcessRulesWindow.CopyRules(copy, config); Assert.Equal(copy.ProcessTypeGroups, config.ProcessTypeGroups);
        var legacy = ConfigStore.Deserialize("{\"ProcessRecognition\":{\"ProcessTypeGroups\":\"\"}}"); ConfigStore.Normalize(legacy); Assert.Equal("", legacy.ProcessRecognition.ProcessTypeGroups);
    }
    [Fact]
    public void RulesRejectDanglingReferencesBeforeSaving()
    {
        Exception? failure=null;
        var thread=new Thread(()=> {try {
            using var window = new ProcessRulesWindow(Config());
            var grid=(DataGridView)typeof(ProcessRulesWindow).GetField("_typeGroups",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
            grid.Rows[0].Cells[1].Value="不存在";
            typeof(ProcessRulesWindow).GetMethod("Save",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,null);
            Assert.NotEqual(DialogResult.OK,window.DialogResult);
            Assert.Equal("方通=方通组|角钢=角钢组",window.Result.ProcessTypeGroups);
            grid.Rows[0].Cells[1].Value="角钢组";
            typeof(ProcessRulesWindow).GetMethod("Save",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,null);
            Assert.Equal(DialogResult.OK,window.DialogResult);
            Assert.Equal("方通=角钢组|角钢=角钢组",window.Result.ProcessTypeGroups);
        } catch(Exception ex) {failure=ex;} });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void DoctorStyleProfileMetadataSupportsUniformAngleBodies()
    {
        var config = new ProcessRecognitionConfig
        {
            ProcessCombinations = "角钢组=角钢下料>去毛刺",
            ProcessTypeGroups = "角钢=角钢组"
        };
        var input = new ProcessRecognitionInput
        {
            IsPart = true,
            Geometry = ProcessGeometryAnalyzer.Merge(new[]
            {
                new ProcessGeometryFact { BodyName = "L1", SolidBodyCount = 1, ProfileKind = "角钢", GeometryComplete = true },
                new ProcessGeometryFact { BodyName = "L2", SolidBodyCount = 1, ProfileKind = "角钢", GeometryComplete = true }
            })
        };

        var match = ProcessTypeGroupPolicy.Resolve(config, input);

        Assert.True(match.Matched, match.Reason);
        Assert.Equal("角钢组", match.Group);
        Assert.Contains("角钢", match.Reason);
    }

    [Fact]
    public void MixedStructuralProfilesStayUnmatchedAndExplainTheConflict()
    {
        var config = new ProcessRecognitionConfig
        {
            ProcessCombinations = "方通组=锯切下料>去毛刺|角钢组=角钢下料>去毛刺",
            ProcessTypeGroups = "方通=方通组|角钢=角钢组"
        };
        var input = new ProcessRecognitionInput
        {
            IsPart = true,
            Geometry = ProcessGeometryAnalyzer.Merge(new[]
            {
                new ProcessGeometryFact { BodyName = "方管", SolidBodyCount = 1, StraightTubeBodyCount = 1, ProfileKind = "方通", GeometryComplete = true },
                new ProcessGeometryFact { BodyName = "角件", SolidBodyCount = 1, ProfileKind = "角钢", GeometryComplete = true }
            })
        };

        var match = ProcessTypeGroupPolicy.Resolve(config, input);

        Assert.False(match.Matched);
        Assert.Contains("多种型材截面", match.Reason);
    }
}
