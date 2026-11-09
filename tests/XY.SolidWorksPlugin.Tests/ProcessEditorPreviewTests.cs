using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ProcessEditorPreviewTests
{
    [Fact]
    public void EditableDropdownRetainsCustomNamesAndInsertedRowsAndSavesExactOrder() => Sta(() =>
    {
        using var window = new ProcessSequenceEditorWindow("零件", "下料-折弯-包装", "-", new[] { "下料", "去毛刺", "焊接" }, true, confirmText: "保存并写入");
        window.Opacity = 0; window.Show();
        var grid = Field<DataGridView>(window, "_grid");
        grid.CurrentCell = grid.Rows[1].Cells["Process"];
        Assert.True(grid.BeginEdit(true));
        var editor = Assert.IsType<ProcessNameEditingControl>(grid.EditingControl);
        Assert.Contains("去毛刺", editor.Items.Cast<string>());
        editor.Text = "自定义折弯工序";
        Assert.True(grid.EndEdit());
        Assert.Equal("自定义折弯工序", grid.Rows[1].Cells["Process"].Value);
        Button(window, "选中后插入工序").PerformClick();
        editor = Assert.IsType<ProcessNameEditingControl>(grid.EditingControl);
        editor.SelectedItem = "去毛刺";
        Assert.True(grid.EndEdit());
        Assert.Equal("去毛刺", grid.Rows[2].Cells["Process"].Value);
        Assert.True(ProcessSequenceGrid.Move(grid, 2, 1));
        Button(window, "保存并写入").PerformClick();
        Assert.Equal(DialogResult.OK, window.DialogResult);
        Assert.Equal("下料-去毛刺-自定义折弯工序-包装", window.Sequence);
    });

    [Fact]
    public void PreviewIsOwnedAndLoadedOnShowWithoutRestrictingEditing() => Sta(() =>
    {
        int calls = 0;
        using var window = new ProcessSequenceEditorWindow("零件", "下料-包装", "-", preview: () =>
        {
            calls++;
            return new ProcessModelPreview(new Bitmap(30, 60), "上次保存的模型预览 · 引用配置");
        });
        Assert.Equal(0, calls);
        window.Opacity = 0; window.Show(); Application.DoEvents();
        Assert.Equal(1, calls);
        var image = Field<PictureBox>(window, "_previewImage");
        Assert.NotNull(image.Image);
        Assert.Equal(PictureBoxSizeMode.Zoom, image.SizeMode);
        Assert.Contains("引用配置", Field<Label>(window, "_previewStatus").Text);
        Button(window, "取消").PerformClick();
        Assert.Null(window.Sequence);
        Assert.Equal(DialogResult.Cancel, window.DialogResult);
    });

    [Fact]
    public void UnavailablePreviewReportsReasonAndStillAllowsProcessEditing() => Sta(() =>
    {
        using var preview = ProcessModelPreview.Read(null!, "", "默认");
        Assert.Null(preview.Image);
        Assert.Contains("尚未保存", preview.Description);
        using var window = new ProcessSequenceEditorWindow("零件", "下料", "-", preview: () => throw new InvalidOperationException("模拟预览不可读"));
        window.Opacity = 0; window.Show(); Application.DoEvents();
        Assert.Contains("预览读取失败", Field<Label>(window, "_previewStatus").Text);
        Assert.True(Field<DataGridView>(window, "_grid").Enabled);
        Button(window, "应用到草稿").PerformClick();
        Assert.Equal("下料", window.Sequence);
    });

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
    private static Button Button(Control control, string text) => control.Controls.Cast<Control>().SelectMany(Descendants).OfType<Button>().Single(button => button.Text == text);
    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control control)
    { yield return control; foreach (Control child in control.Controls) foreach (var nested in Descendants(child)) yield return nested; }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
