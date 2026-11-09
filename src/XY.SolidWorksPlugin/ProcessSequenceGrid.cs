using System;
using System.Drawing;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

// Operates only on the draft grid. No model or COM access is permitted here.
internal static class ProcessSequenceGrid
{
    private sealed class DragRow { internal DataGridView Grid; internal DataGridViewRow Row; }

    internal static void Attach(DataGridView grid, Action changed)
    {
        grid.EditMode = DataGridViewEditMode.EditProgrammatically;
        grid.AllowDrop = true;
        foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
        Point origin = Point.Empty;
        DataGridViewRow pending = null;
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            pending = null;
            grid.CurrentCell = grid.Rows[e.RowIndex].Cells["Process"];
            grid.BeginEdit(true);
        };
        grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F2 && grid.CurrentRow != null)
            {
                grid.CurrentCell = grid.CurrentRow.Cells["Process"];
                grid.BeginEdit(true);
                e.Handled = true;
            }
        };
        grid.MouseDown += (_, e) =>
        {
            pending = null;
            if (e.Button != MouseButtons.Left || grid.IsCurrentCellInEditMode) return;
            int index = grid.HitTest(e.X, e.Y).RowIndex;
            if (index >= 0) { pending = grid.Rows[index]; origin = e.Location; }
        };
        grid.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || pending == null || grid.IsCurrentCellInEditMode) return;
            Size threshold = SystemInformation.DragSize;
            if (Math.Abs(e.X - origin.X) < threshold.Width / 2 && Math.Abs(e.Y - origin.Y) < threshold.Height / 2) return;
            DataGridViewRow row = pending;
            pending = null;
            if (row.DataGridView == grid) grid.DoDragDrop(new DragRow { Grid = grid, Row = row }, DragDropEffects.Move);
        };
        grid.MouseUp += (_, __) => pending = null;
        grid.DragOver += (_, e) =>
        {
            var item = e.Data.GetData(typeof(DragRow)) as DragRow;
            e.Effect = item?.Grid == grid && item.Row.DataGridView == grid ? DragDropEffects.Move : DragDropEffects.None;
        };
        grid.DragDrop += (_, e) =>
        {
            var item = e.Data.GetData(typeof(DragRow)) as DragRow;
            if (item?.Grid != grid || item.Row.DataGridView != grid) return;
            Point point = grid.PointToClient(new Point(e.X, e.Y));
            int target = grid.HitTest(point.X, point.Y).RowIndex;
            if (target < 0) return;
            if (Move(grid, item.Row.Index, target)) changed();
        };
    }

    internal static bool Move(DataGridView grid, int source, int target)
    {
        if (source < 0 || target < 0 || source >= grid.Rows.Count || target >= grid.Rows.Count || source == target || !grid.EndEdit()) return false;
        DataGridViewRow row = grid.Rows[source];
        grid.Rows.RemoveAt(source);
        grid.Rows.Insert(target, row);
        grid.ClearSelection();
        row.Selected = true;
        grid.CurrentCell = row.Cells["Process"];
        return true;
    }

    internal static int InsertAfterSelection(DataGridView grid, params object[] values)
    {
        if (!grid.EndEdit()) return -1;
        int index = grid.CurrentRow == null ? grid.Rows.Count : grid.CurrentRow.Index + 1;
        grid.Rows.Insert(index, values);
        grid.ClearSelection();
        grid.CurrentCell = grid.Rows[index].Cells["Process"];
        grid.Rows[index].Selected = true;
        return index;
    }
}
