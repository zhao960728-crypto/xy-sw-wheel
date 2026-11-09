using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using Xunit;
namespace XY.SolidWorksPlugin.Tests;

public sealed class ProcessRulesEditorTests
{
    [Fact]
    public void OpeningAndSavingDirectoryPreservesMoreThanTwentyCustomNamesWithoutAutomaticProcesses()
    {
        Sta(() => {
            var names = Enumerable.Range(1, 45).Select(i => "工序" + i).ToArray();
            var config = new ProcessRecognitionConfig { AvailableProcessNames = string.Join("|", names), AdditionalProcesses = "人工统一收尾" };
            using var window = new ProcessRulesWindow(config);
            Assert.Equal(45, Field<TextBox>(window, "_available").Lines.Length);
            Invoke(window, "Save"); Assert.Equal(DialogResult.OK, window.DialogResult);
            Assert.Equal(config.AvailableProcessNames, window.Result.AvailableProcessNames);
            Assert.Equal("人工统一收尾", window.Result.AdditionalProcesses);
            Assert.Empty(config.CustomRules);
        });
    }
    [Fact]
    public void RenamedGroupCannotLeaveRulePointingAtRemovedDefinition()
    {
        Sta(() => {
            var config = new ProcessRecognitionConfig { ProcessCombinations = "焊接组=折弯>焊接", ProcessTypeGroups = "", CustomRules = new List<ProcessRecognitionRule> { new ProcessRecognitionRule { Name = "焊件规则", NameContains = "柜", Action = "group", GroupName = "焊接组" } } };
            using var window = new ProcessRulesWindow(config);
            Field<DataGridView>(window, "_combinations").Rows[0].Cells[0].Value = "新焊接组";
            Invoke(window, "Save"); Assert.NotEqual(DialogResult.OK, window.DialogResult);
            Assert.Contains("存在", Field<Label>(window, "_status").Text);
            Assert.Equal("焊接组=折弯>焊接", window.Result.ProcessCombinations);
            Assert.Equal("焊接组", config.CustomRules[0].GroupName);
        });
    }
    [Fact]
    public void RuleEditorRejectsInvalidRangeThenMakesValidRuleImmediatelyExecutable()
    {
        Sta(() => {
            var config = new ProcessRecognitionConfig();
            using var editor = new ProcessRuleEditorWindow(new ProcessRecognitionRule { Name = "导入支架", NameContains = "支架", ProcessNames = "点焊>打磨", MinBodyCount = 3, MaxBodyCount = 1 }, config);
            Invoke(editor, "Save"); Assert.NotEqual(DialogResult.OK, editor.DialogResult);
            var fields = Field<Dictionary<string, TextBox>>(editor, "_fields"); fields["MinBodyCount"].Text = "1"; fields["MaxBodyCount"].Text = "";
            fields["BeforeProcess"].Text = "折弯";
            Invoke(editor, "Save"); Assert.Equal(DialogResult.OK, editor.DialogResult);
            Assert.Equal("折弯", editor.Result.BeforeProcess);
            config.CustomRules.Add(editor.Result);
            var result = ProcessRuleEngine.Apply(config, new ProcessRecognitionInput { IsPart = true, DocumentTitle = "柜体支架", Geometry = new ProcessGeometryFact { SolidBodyCount = 2 } },
                new[] { new RecognizedProcess { Name = "下料" }, new RecognizedProcess { Name = "折弯" }, new RecognizedProcess { Name = "检验" } });
            Assert.Equal(new[] { "下料", "点焊", "打磨", "折弯", "检验" }, result.Processes.Select(row => row.Name)); Assert.Single(result.Matches);
        });
    }
    [Fact]
    public void AnalysisDisplaysCurrentDraftAndSuggestionsSeparatelyWithoutChangingConfiguration()
    {
        var config = new ProcessRecognitionConfig { AvailableProcessNames = "用户检验", ProcessCombinations = "加工组=切割>焊接" };
        string before = ProcessRecognitionBatchCache.SettingsKey(config);
        var text = ProcessAnalysisWindow.Describe(config, new ProcessRecognitionInput { IsPart = true, DocumentTitle = "方通", PartType = "方通" }, false,
            new[] { new RecognizedProcess { Name = "手工终检", Evidence = "人工调整" } });
        Assert.Contains("手工终检", text); Assert.Contains("重新计算的建议", text); Assert.Contains("快速", text);
        Assert.Equal(before, ProcessRecognitionBatchCache.SettingsKey(config));
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Invoke(object target, string name) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, null);
    private static void Sta(Action action)
    {
        Exception? error = null; var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
