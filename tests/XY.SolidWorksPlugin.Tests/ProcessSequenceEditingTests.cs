using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;
namespace XY.SolidWorksPlugin.Tests;

public sealed class ProcessSequenceEditingTests
{
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static DataGridView Grid(object target) => (DataGridView)target.GetType().GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Call(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

    [Fact]
    public void DraftReorderMovesEvidenceWithItsProcessAndInsertsImmediatelyAfterSelection() => Sta(() =>
    {
        using var window = new ProcessRecognitionWindow(() => throw new Exception("Draft editing must not access SOLIDWORKS"));
        var grid = Grid(window);
        grid.Rows.Add(1, "下料", "90%", "切割依据");
        grid.Rows.Add(2, "折弯", "85%", "折弯依据");
        grid.Rows.Add(3, "喷粉", "手动", "人工");
        grid.CurrentCell = grid.Rows[0].Cells["Process"];
        Assert.True(ProcessSequenceGrid.Move(grid, 0, 2));
        Assert.Equal(new[] { "折弯", "喷粉", "下料" }, grid.Rows.Cast<DataGridViewRow>().Select(row => Convert.ToString(row.Cells["Process"].Value)));
        Assert.Equal("切割依据", grid.Rows[2].Cells["Evidence"].Value);
        grid.CurrentCell = grid.Rows[0].Cells["Process"];
        Call(window, "AddRow");
        Assert.Equal(1, grid.CurrentRow.Index);
        grid.CurrentCell.Value = "去毛刺"; grid.EndEdit();
        Assert.Equal("喷粉", grid.Rows[2].Cells["Process"].Value);
        Call(window, "RemoveSelected");
        Assert.Equal(3, grid.Rows.Count);
        Assert.Equal(new[] { 1, 2, 3 }, grid.Rows.Cast<DataGridViewRow>().Select(row => Convert.ToInt32(row.Cells["Order"].Value)));
    });

    [Fact]
    public void DoubleClickEditsTheClickedStepWithoutMovingOtherRows() => Sta(() =>
    {
        using var window = new ProcessSequenceEditorWindow("零件一", "下料→折弯→喷粉", "-");
        var grid = Grid(window);
        Assert.Equal(DataGridViewEditMode.EditProgrammatically, grid.EditMode);
        Assert.True(grid.AllowDrop);
        var handle = grid.Handle;
        typeof(DataGridView).GetMethod("OnCellDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(grid, new object[] { new DataGridViewCellEventArgs(0, 1) });
        Assert.Equal(1, grid.CurrentRow.Index);
        Assert.Equal("Process", grid.CurrentCell.OwningColumn.Name);
        Assert.True(grid.IsCurrentCellInEditMode);
        var editor = Assert.IsType<DataGridViewTextBoxEditingControl>(grid.EditingControl);
        editor.Text = "精折弯";
        grid.EndEdit();
        Assert.Equal("精折弯", grid.Rows[1].Cells["Process"].Value);
        Assert.Equal("下料", grid.Rows[0].Cells["Process"].Value);
        Assert.False(ProcessSequenceGrid.Move(grid, -1, 2));
        Assert.False(ProcessSequenceGrid.Move(grid, 1, 99));
        grid.EndEdit();
    });


    [Fact]
    public void DragDropEventReordersOnlyItsOwnDraftGrid() => Sta(() =>
    {
        using var window = new ProcessSequenceEditorWindow("零件", "下料-折弯-喷粉", "-");
        using var other = new ProcessSequenceEditorWindow("另一零件", "切割-焊接", "-");
        window.Opacity = 0; window.ShowInTaskbar = false; window.Show();
        DataGridView grid = Grid(window);
        var handle = grid.Handle;
        Type dragType = typeof(ProcessSequenceGrid).GetNestedType("DragRow", BindingFlags.NonPublic);
        Action<DataGridView, DataGridViewRow, int> drop = (source, row, target) =>
        {
            object payload = Activator.CreateInstance(dragType, true);
            dragType.GetField("Grid", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(payload, source);
            dragType.GetField("Row", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(payload, row);
            var data = new DataObject(); data.SetData(dragType, payload);
            var rectangle = grid.GetCellDisplayRectangle(1, target, false);
            Assert.Equal(target, grid.HitTest(rectangle.Left + 8, rectangle.Top + 8).RowIndex);
            var point = grid.PointToScreen(new System.Drawing.Point(rectangle.Left + 8, rectangle.Top + 8));
            var args = new DragEventArgs(data, 0, point.X, point.Y, DragDropEffects.Move, DragDropEffects.Move);
            typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(grid, new object[] { args });
        };
        drop(grid, grid.Rows[0], 2);
        Assert.Equal(new[] { "折弯", "喷粉", "下料" }, grid.Rows.Cast<DataGridViewRow>().Select(row => Convert.ToString(row.Cells["Process"].Value)));
        Assert.Equal(new[] { 1, 2, 3 }, grid.Rows.Cast<DataGridViewRow>().Select(row => Convert.ToInt32(row.Cells["Order"].Value)));
        drop(Grid(other), Grid(other).Rows[0], 0);
        Assert.Equal("折弯", grid.Rows[0].Cells["Process"].Value);
        var detached = new DataGridViewRow();
        drop(grid, detached, 0);
        Assert.Equal(3, grid.Rows.Count);
        Assert.Equal("切割", Grid(other).Rows[0].Cells["Process"].Value);
    });

    [Fact]
    public void CancelledSequenceDoesNotProduceAnAppliedResult() => Sta(() =>
    {
        using var window = new ProcessSequenceEditorWindow("零件二", "下料-折弯", "-");
        Grid(window).Rows[0].Cells["Process"].Value = "锯切";
        Assert.Null(window.Sequence);
        Assert.Equal(new[] { "下料", "折弯" }, ProcessSequenceEditorWindow.Split("下料 / 折弯", " / "));
    });
}

