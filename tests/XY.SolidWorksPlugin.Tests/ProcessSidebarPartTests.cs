using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;
namespace XY.SolidWorksPlugin.Tests;

public sealed class ProcessSidebarPartTests
{
    [Fact]
    public void SidebarKeepsEveryActionAndDraftFieldInCompactTabs() => Sta(() =>
    {
        using var window = new ProcessRecognitionWindow(() => throw new InvalidOperationException("Layout must not access host"));
        var before = ProcessSidebarLayout.Descendants(window).OfType<Button>().Select(button => button.Text).OrderBy(text => text).ToArray();
        var grid = Field<DataGridView>(window, "_grid");
        grid.Rows.Add(1, "自定义工序", "手动", "原依据");
        window.StageAdditionalPropertyForTest("备注", "保留草稿");
        window.PrepareForSidebar(); window.PrepareForSidebar();
        var after = ProcessSidebarLayout.Descendants(window).OfType<Button>().Select(button => button.Text).OrderBy(text => text).ToArray();
        Assert.Equal(before, after);
        var tabs = Assert.Single(ProcessSidebarLayout.Descendants(window).OfType<TabControl>());
        Assert.Equal(new[] { "识别/编辑", "工序组/属性", "外购" }, tabs.TabPages.Cast<TabPage>().Select(page => page.Text));
        Assert.Equal("自定义工序", grid.Rows[0].Cells["Process"].Value);
        Assert.Equal("原依据", grid.Rows[0].Cells["Evidence"].Value);
        Assert.Equal(1, window.PendingAdditionalPropertyCount);
    });

    [Fact]
    public void DropdownModePreservesValuesAndNewRowsUseEditableCandidates() => Sta(() =>
    {
        using var window = new ProcessRecognitionWindow(() => throw new InvalidOperationException("Editing draft must not access host"));
        var grid = Field<DataGridView>(window, "_grid");
        grid.Rows.Add(1, "自定义工序", "手动", "原依据");
        window.PrepareForSidebar();
        Field<CheckBox>(window, "_independentSteps").Checked = true;
        Assert.IsType<ProcessNameComboCell>(grid.Rows[0].Cells["Process"]);
        Assert.Equal("自定义工序", grid.Rows[0].Cells["Process"].Value);
        int row = grid.Rows.Add(2, "新工序", "手动", "新依据");
        Assert.IsType<ProcessNameComboCell>(grid.Rows[row].Cells["Process"]);
        Field<CheckBox>(window, "_independentSteps").Checked = false;
        Assert.IsType<DataGridViewTextBoxCell>(grid.Rows[row].Cells["Process"]);
        Assert.Equal("新工序", grid.Rows[row].Cells["Process"].Value);
        Assert.Equal("原依据", grid.Rows[0].Cells["Evidence"].Value);
    });

    [Fact]
    public void SidebarCloseNotifiesAndPreservesWindowAndDraft() => Sta(() =>
    {
        using var window = new ProcessRecognitionWindow(() => throw new InvalidOperationException("Close must not access host"));
        window.PrepareForSidebar(); window.TopLevel = false;
        int closed = 0; window.SidebarCloseRequested = () => closed++;
        var args = new FormClosingEventArgs(CloseReason.UserClosing, false);
        typeof(ProcessRecognitionWindow).GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { args });
        Assert.True(args.Cancel); Assert.False(window.IsDisposed); Assert.Equal(1, closed);
    });

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
