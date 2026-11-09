using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

// Free editing directly on the preview, the way the CAD build's settings
// window does it: drag "+" onto a gap between two cells to add one, drag "-"
// onto a cell to remove it, drag a shape in to place a new free cell, resize a
// cell with the wheel or its corner handles, and batch-select cells to give
// them one shared look.
internal sealed partial class SettingsWindow
{
    private sealed class CellCountDrag { internal bool Add; }
    private sealed class FreeCellDrag { internal string Shape = "Circle"; }

    private Label? _previewHint;
    private Button _previewResizeButton;
    private Button _previewBatchButton;
    private CheckBox _previewPractice;

    private void EndPreviewEditing()
    {
        _preview.EndGeometryEditing();
        _preview.PracticeMode = false;
        if (_previewPractice != null) _previewPractice.Checked = false;
        ResetPreviewModeButton(_previewResizeButton, "调大小");
        ResetPreviewModeButton(_previewBatchButton, "批量");
    }

    private static void ResetPreviewModeButton(Button button, string text)
    {
        if (button == null) return;
        button.Text = text;
        button.BackColor = Color.White;
        button.ForeColor = Color.FromArgb(35, 45, 55);
    }

    private static readonly (string Text, string Id)[] CellShapes =
    {
        ("圆形", "Circle"), ("圆角方形", "Rounded"), ("方形", "Square"), ("菱形", "Diamond"),
        ("六边形", "Hexagon"), ("星形", "Star"), ("心形", "Heart")
    };

    private Control BuildPreviewCellTools()
    {
        _preview.AllowDrop = true;
        _preview.DragEnter += PreviewDragEnter;
        _preview.DragOver += PreviewDragEnter;
        _preview.DragDrop += PreviewDragDrop;
        _preview.CellGeometryChanged += (_, _) =>
        {
            StoreAllCellGeometry();
            Hint("格子位置或大小已记入草稿，点击应用并保存生效");
        };

        // The cell tools live above the preview wheel. A wrapping row matches the
    // rest of the dialog's toolbars. We anchor the wrapping row to a host
    // Panel with a minimum height: a bare FlowLayoutPanel with Dock=Top +
    // AutoSize can collapse to zero height when its children haven't been
    // laid out yet, swallowing the entire toolbar — the user sees no buttons
    // and concludes the preview editing tools are missing.
    // Two wrapped rows of tools: the extra row holds the position editors and
    // the unassigned list, and a host with a real height keeps the wrapping
    // row from collapsing the way a bare auto-sized flow panel does.
    var host = new Panel
    {
        Dock = DockStyle.Top,
        Height = 96,
        Padding = new Padding(0, 4, 0, 4),
        BackColor = Color.FromArgb(232, 241, 236)
    };
    var buttons = new FlowLayoutPanel
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = true,
        AutoSize = false,
        BackColor = Color.FromArgb(232, 241, 236)
    };
    host.Controls.Add(buttons);
    bool fittingToolbar = false;
    buttons.Layout += (_, _) =>
    {
        if (fittingToolbar || buttons.ClientSize.Width <= 0) return;
        fittingToolbar = true;
        try
        {
            int bottom = buttons.Controls.Cast<Control>().Where(c => c.Visible)
                .Select(c => c.Bottom + c.Margin.Bottom).DefaultIfEmpty(0).Max();
            int required = bottom + host.Padding.Vertical;
            if (required > 0 && host.Height != required) host.Height = required;
        }
        finally { fittingToolbar = false; }
    };

        foreach (bool add in new[] { true, false })
        {
            var button = new Button
            {
                Text = add ? "＋ 加格" : "－ 删格", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(56, 26),
                    FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(35, 45, 55), Margin = new Padding(2),
                    AccessibleName = add ? "拖到两格之间添加格子" : "拖到格子上删除格子"
            };
            Point start = Point.Empty;
            button.MouseDown += (_, e) => start = e.Location;
            button.MouseMove += (_, e) =>
            {
                if (_preview.PracticeMode || e.Button != MouseButtons.Left) return;
                if (Math.Abs(e.X - start.X) < SystemInformation.DragSize.Width && Math.Abs(e.Y - start.Y) < SystemInformation.DragSize.Height) return;
                _preview.ShowInsertionPoints = add;
                _preview.Invalidate();
                try { button.DoDragDrop(new CellCountDrag { Add = add }, DragDropEffects.Copy); }
                finally { _preview.ShowInsertionPoints = false; _preview.Invalidate(); }
            };
            buttons.Controls.Add(button);
        }

        var shape = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 96, Margin = new Padding(2, 4, 2, 2) };
        shape.Items.AddRange(CellShapes.Select(s => (object)s.Text).ToArray());
        shape.SelectedIndex = 0;
        shape.SelectionChangeCommitted += (_, _) =>
        {
            if (_preview.PracticeMode) return;
            DataGridViewRow row = FindSelectedCommandRow();
            if (row == null) { Hint("先在预览或表格里选中一个格子"); return; }
            ComboRowState state = ReadComboRowState(row.Tag);
            state.CellShape = CellShapes[Math.Max(0, shape.SelectedIndex)].Id;
            row.Tag = state;
            RefreshPreview();
            Hint("已设置当前格形状，在轨道与自设计样式中显示");
        };
        _preview.CellSelected += delegate(object _, RadialCellSelectedEventArgs cell)
        {
            SceneMenuConfig scene = GetScene(_activeSceneKey);
            MenuItemConfig item = scene?.Rings.FirstOrDefault(r => r.Index == cell.RingIndex)?.Items.FirstOrDefault(i => i.Slot == cell.Slot);
            string id = string.IsNullOrWhiteSpace(item?.CellShape) ? "Circle" : item.CellShape;
            shape.SelectedIndex = Enumerable.Range(0, CellShapes.Length).FirstOrDefault(i => CellShapes[i].Id == id);
        };
        buttons.Controls.Add(shape);

        var addShape = new Button { Text = "拖入新格", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(72, 26), FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new Padding(2) };
        Point shapeStart = Point.Empty;
        addShape.MouseDown += (_, e) => shapeStart = e.Location;
        addShape.MouseMove += (_, e) =>
        {
            if (_preview.PracticeMode || e.Button != MouseButtons.Left) return;
            if (Math.Abs(e.X - shapeStart.X) < SystemInformation.DragSize.Width && Math.Abs(e.Y - shapeStart.Y) < SystemInformation.DragSize.Height) return;
            addShape.DoDragDrop(new FreeCellDrag { Shape = CellShapes[Math.Max(0, shape.SelectedIndex)].Id }, DragDropEffects.Copy);
        };
        buttons.Controls.Add(addShape);

        // A hairline divider between the cell-content tools (drag/shape/add)
        // and the layout tools (size/align/batch/practice).
        buttons.Controls.Add(new Panel { Width = 6, Height = 24, BackColor = Color.FromArgb(218, 224, 230), Margin = new Padding(2, 8, 2, 0) });

        // Position tools. Dragging "+" onto the wheel can only append at the
        // end, so these are what put a new cell where the user actually wants
        // it and what drop a chosen cell without emptying the ring from the end.
        foreach (string text in new[] { "在前面插入", "在后面插入", "删除所选位置" })
        {
            var position = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(56, 26), FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(35, 45, 55), Margin = new Padding(2) };
            position.Click += (_, _) => ApplyPositionTool(text);
            buttons.Controls.Add(position);
        }

        var slotCount = new Button { Text = "批量增删…", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(56, 26), FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(35, 45, 55), Margin = new Padding(2) };
        slotCount.Click += (_, _) => ChooseSlotCountChange();
        buttons.Controls.Add(slotCount);

        var unassigned = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 136, Margin = new Padding(2, 4, 2, 2) };
        unassigned.Items.Add("未分配命令");
        unassigned.SelectedIndex = 0;
        unassigned.DropDown += (_, _) => RefreshUnassigned(unassigned);
        buttons.Controls.Add(unassigned);

        var restore = new Button { Text = "放回当前格", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(56, 26), FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(35, 45, 55), Margin = new Padding(2) };
        restore.Click += (_, _) => RestoreUnassigned(unassigned);
        buttons.Controls.Add(restore);

        // Toggle buttons share the same accent colour as the toolbar's primary
        // action so the "on" state reads at a glance instead of relying on the
        // ✓ prefix alone.
        var accent = Color.FromArgb(65, 139, 217);
        var accentText = Color.White;
        var idleBack = Color.White;
        var idleText = Color.FromArgb(35, 45, 55);

        var resize = new Button { Text = "调大小", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(56, 26), FlatStyle = FlatStyle.Flat, BackColor = idleBack, ForeColor = idleText, Margin = new Padding(2) };
        resize.Click += (_, _) =>
        {
            if (_preview.PracticeMode) return;
            SceneMenuConfig scene = GetScene(_activeSceneKey);
            string style = scene?.StyleId ?? "01";
            if (style == "classic" || style == "01")
            { Hint("单独调大小适用于节点、分组面板、轨道与自设计；请先换成对应样式"); return; }
            if (_preview.BatchSelectionMode) EndBatchSelection();
            _preview.ResizeMode = !_preview.ResizeMode;
            bool on = _preview.ResizeMode;
            resize.Text = on ? "✓ 结束" : "调大小";
            resize.BackColor = on ? accent : idleBack;
            resize.ForeColor = on ? accentText : idleText;
            _preview.Invalidate();
            Hint(on
                ? "滚轮上滚放大、下滚缩小（每档 5%），也可拖四角手柄；空间不足时保持原大小"
                : "单格尺寸已记入草稿");
        };
        _previewResizeButton = resize;
        buttons.Controls.Add(resize);

        var align = new Button { Text = "微调", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(48, 26), FlatStyle = FlatStyle.Flat, BackColor = idleBack, ForeColor = idleText, Margin = new Padding(2) };
        align.Click += (_, _) =>
        {
            if (_preview.PracticeMode) return;
            if (GetScene(_activeSceneKey)?.StyleId != FreeWheelLayout.StyleId)
            { Hint("微调整齐只用于自由布局：先启用该样式，再摆好格子"); return; }
            Hint(_preview.NudgeFreeLayout()
                ? "已在原位置附近轻微对齐，保留整体图案"
                : "当前格子无需微调，或附近空间不足");
            _preview.Invalidate();
        };
        buttons.Controls.Add(align);

        var batch = new Button { Text = "批量", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(48, 26), FlatStyle = FlatStyle.Flat, BackColor = idleBack, ForeColor = idleText, Margin = new Padding(2) };
        batch.Click += (_, _) =>
        {
            if (_preview.PracticeMode) return;
            if (_preview.ResizeMode)
            {
                _preview.ResizeMode = false;
                resize.Text = "调大小";
                resize.BackColor = idleBack;
                resize.ForeColor = idleText;
            }
            _preview.BatchSelectionMode = !_preview.BatchSelectionMode;
            bool on = _preview.BatchSelectionMode;
            batch.Text = on ? "✓ 统一" : "批量";
            batch.BackColor = on ? accent : idleBack;
            batch.ForeColor = on ? accentText : idleText;
            if (on) Hint("在预览里点选多个格子，再点“统一”套用外观");
            else if (ApplyBatchAppearance()) Hint("已把当前格的外观套用到所选格子");
            _preview.Invalidate();
        };
        _previewBatchButton = batch;
        buttons.Controls.Add(batch);

        var practice = new CheckBox { Text = "练习", AutoSize = true, Margin = new Padding(2, 7, 2, 2) };
        practice.CheckedChanged += (_, _) =>
        {
            _preview.PracticeMode = practice.Checked;
            if (practice.Checked) _preview.EndGeometryEditing();
            batch.Text = "批量"; batch.BackColor = idleBack; batch.ForeColor = idleText;
            resize.Text = "调大小"; resize.BackColor = idleBack; resize.ForeColor = idleText;
            Hint(practice.Checked ? "练习模式：预览可看可悬停，但不会改动格子" : "已退出练习模式");
        };
        _previewPractice = practice;
        buttons.Controls.Add(practice);

        var lens = new CheckBox { Text = "放大镜", AutoSize = true, Margin = new Padding(2, 7, 2, 2) };
        lens.CheckedChanged += (_, _) =>
        {
            _preview.PreviewMagnifierEnabled = lens.Checked;
            _preview.Invalidate();
            Hint(lens.Checked ? "放大镜：鼠标悬停格子时，在预览角落显示该格大图" : "已关闭放大镜");
        };
        buttons.Controls.Add(lens);
        return host;
    }

    // Cap the hint width so a long line wraps inside the preview column
    // instead of overflowing past the pane edge. Returned separately so the
    // caller stacks it as its own Dock=Top row, not as another Flow item.
    private Label MakePreviewHint()
    {
        _previewHint = new Label
        {
            Text = "提示：按住格子直接拖到另一格即可交换命令；拖＋加格、拖－减格；要把新格放在指定位置用“在前面插入／在后面插入”。",
            AutoSize = true, Dock = DockStyle.Top, MaximumSize = new Size(420, 36), Margin = new Padding(0, 0, 0, 6),
            ForeColor = Color.FromArgb(70, 88, 100)
        };
        return _previewHint;
    }

    private void Hint(string text)
    {
        if (_previewHint != null) _previewHint.Text = text;
    }

    private void PreviewDragEnter(object? sender, DragEventArgs e)
    {
        bool known = e.Data != null
            && (e.Data.GetDataPresent(typeof(CellCountDrag)) || e.Data.GetDataPresent(typeof(FreeCellDrag)));
        e.Effect = known ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void PreviewDragDrop(object? sender, DragEventArgs e)
    {
        Point point = _preview.PointToClient(new Point(e.X, e.Y));
        if (e.Data == null) return;
        object? count = e.Data.GetData(typeof(CellCountDrag));
        if (count is CellCountDrag change) { ApplyCountDrop(point, change.Add); return; }
        object? free = e.Data.GetData(typeof(FreeCellDrag));
        if (free is FreeCellDrag cell) AddFreeCell(point, cell.Shape);
    }

    private void ApplyCountDrop(Point point, bool add)
    {
        if (_preview.PracticeMode) return;
        RadialCellSelectedEventArgs? target = add ? _preview.HitInsertion(point) : _preview.HitCell(point);
        if (target == null)
        { Hint(add ? "把 ＋ 拖到两个格子之间的插入点上" : "把 － 拖到要删除的格子上"); return; }
        SceneMenuConfig scene = GetScene(_activeSceneKey);
        RingConfig ring = scene?.Rings.FirstOrDefault(r => r.Index == target.RingIndex && r.Enabled);
        if (scene == null || ring == null) return;
        if (add && ring.Slots >= 32) { Hint("每圈最多 32 格，请选另一圈"); return; }
        if (!add && ring.Slots <= 1) { Hint("每圈至少保留一格"); return; }
        if (!BumpRingSlots(ring.Index, add ? 1 : -1)) return;
        StoreAllCellGeometry();
        SyncCommandRowsToRingDefinitions();
        RefreshPreview();
        Hint(add ? "已加一格：新格在末尾，把它拖到目标格即可交换，或用“在前面插入”直接放好"
                 : "已减一格：末尾格子的命令移出网格，可重新绑定");
    }

    private bool BumpRingSlots(int ringIndex, int delta)
    {
        if (ringIndex < 0 || ringIndex >= _ringSlotInputs.Count) return false;
        NumericUpDown input = _ringSlotInputs[ringIndex];
        input.Value = Math.Max(input.Minimum, Math.Min(input.Maximum, input.Value + delta));
        return true;
    }

    private void AddFreeCell(Point point, string shape)
    {
        if (_preview.PracticeMode) return;
        StoreAllCellGeometry();
        SceneMenuConfig scene = GetScene(_activeSceneKey);
        if (scene == null) return;
        if (scene.StyleId != FreeWheelLayout.StyleId)
        { Hint("拖入新格只用于自由布局：请先在“自由布局”里启用该样式"); return; }
        RingConfig ring = scene.Rings.FirstOrDefault(r => r.Enabled && r.Slots < 32);
        if (ring == null) { Hint("每圈最多 32 格"); return; }
        int slot = ring.Slots;
        if (!BumpRingSlots(ring.Index, 1)) return;
        SyncCommandRowsToRingDefinitions();
        RefreshPreview();
        scene = GetScene(_activeSceneKey);
        ring = scene.Rings.FirstOrDefault(r => r.Index == ring.Index);
        if (ring == null) return;
        MenuItemConfig item = ring.Items.FirstOrDefault(i => i.Slot == slot);
        if (item == null) { item = new MenuItemConfig { Slot = slot }; ring.Items.Add(item); }
        item.CellShape = shape;
        if (!_preview.TryPlaceCell(ring.Index, slot, point))
        { Hint("这里空间不足，请拖到留有间距的空白位置"); _preview.Invalidate(); return; }
        StoreAllCellGeometry();
        Hint("已添加自设计空格，可绑定命令或继续拖动摆放");
    }

    // Geometry lives on the preview's scene, but every refresh rebuilds that
    // scene from the grid. Copying it back into the row state is what makes a
    // moved or resized cell survive the next refresh and the final save.
    private void StoreAllCellGeometry()
    {
        SceneMenuConfig scene = GetScene(_activeSceneKey);
        if (scene == null) return;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow || !TryReadGridRingSlot(row, out int ring, out int slot)) continue;
            MenuItemConfig item = scene.Rings.FirstOrDefault(r => r.Index == ring - 1)?.Items.FirstOrDefault(i => i.Slot == slot - 1);
            if (item == null) continue;
            ComboRowState state = ReadComboRowState(row.Tag);
            state.CopyFrom(item);
            state.CellShape = item.CellShape;
            row.Tag = state;
        }
    }

    private void EndBatchSelection()
    {
        _preview.BatchSelectionMode = false;
        _preview.BatchSelection.Clear();
        ResetPreviewModeButton(_previewBatchButton, "批量");
        _preview.Invalidate();
    }

    // Copies the look of the cell that is currently selected onto every cell
    // collected in batch selection: font, colour, icon and cell shape.
    private bool ApplyBatchAppearance()
    {
        if (_preview.BatchSelection.Count == 0) return false;
        DataGridViewRow source = FindSelectedCommandRow();
        if (source == null) return false;
        ComboRowState template = ReadComboRowState(source.Tag);
        int applied = 0;
        foreach ((int Ring, int Slot) key in _preview.BatchSelection)
        {
            DataGridViewRow row = FindCommandRow(key.Ring + 1, key.Slot + 1);
            if (row == null || ReferenceEquals(row, source)) continue;
            foreach (string column in new[] { "FontSize", "FontName", "TextColor" })
            {
                if (source.Cells[column].Value != null) row.Cells[column].Value = source.Cells[column].Value;
            }
            ComboRowState state = ReadComboRowState(row.Tag);
            state.CellSizePercent = template.CellSizePercent;
            state.CellShape = template.CellShape;
            state.IconRatioPercent = template.IconRatioPercent;
            state.IconId = template.IconId;
            state.IconPath = template.IconPath;
            state.ContentMode = template.ContentMode;
            state.CellBold = template.CellBold;
            state.CellEffect = template.CellEffect;
            row.Tag = state;
            applied++;
        }
        if (applied == 0) return false;
        RefreshPreview();
        return true;
    }

    private DataGridViewRow? FindCommandRow(int ring, int slot)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            if (TryReadGridRingSlot(row, out int ringNumber, out int slotNumber) && ringNumber == ring && slotNumber == slot) return row;
        }
        return null;
    }

    // --- slot positions ---------------------------------------------------

    // The grid numbers cells from 1; everything below takes the grid's numbers
    // as "ring"/"slot" and 0-based indices as "position".
    private bool TrySelectedCell(out int ring, out int slot)
    {
        ring = 0;
        slot = 0;
        DataGridViewRow? row = FindSelectedCommandRow();
        if (row == null) { Hint("先在预览或表格里选中一个格子"); return false; }
        if (!TryReadGridRingSlot(row, out ring, out slot)) { Hint("先在预览或表格里选中一个格子"); return false; }
        return true;
    }

    private int RingSlotCount(int ring)
    {
        int index = ring - 1;
        return index >= 0 && index < _ringSlotInputs.Count ? (int)_ringSlotInputs[index].Value : 0;
    }

    private string SlotLabel(int ring, int position)
    {
        DataGridViewRow? row = FindCommandRow(ring, position + 1);
        string label = row == null ? "" : Convert.ToString(row.Cells["Label"].Value)?.Trim() ?? "";
        return label.Length == 0 ? "空格" : label;
    }

    private void ApplyPositionTool(string text)
    {
        if (_preview.PracticeMode) { Hint("练习模式下不能改动格子"); return; }
        if (!TrySelectedCell(out int ring, out int slot)) return;
        int position = slot - 1;
        if (text == "在前面插入") InsertSlotsAt(ring, position, 1);
        else if (text == "在后面插入") InsertSlotsAt(ring, position + 1, 1);
        else RemoveSlotsAt(ring, new[] { position });
    }

    // Inserts count empty cells so that they occupy 0-based positions
    // position..position+count-1; every later cell keeps its command and moves
    // down, which is what "insert here" means to the user.
    private bool InsertSlotsAt(int ring, int position, int count)
    {
        if (count <= 0) return false;
        int slots = RingSlotCount(ring);
        if (slots <= 0) { Hint("先选中一个格子，再插入"); return false; }
        if (slots + count > 32) { Hint("每圈最多 32 格"); return false; }
        position = Math.Max(0, Math.Min(slots, position));
        bool wasLoading = _loadingScene;
        _loadingScene = true;
        try
        {
            ShiftSlotsFrom(ring, position, count);
            if (!BumpRingSlots(ring - 1, count)) return false;
        }
        finally { _loadingScene = wasLoading; }
        SyncCommandRowsToRingDefinitions();
        RefreshPreview();
        _preview.SelectCell(ring - 1, Math.Min(position, RingSlotCount(ring) - 1));
        Hint(count == 1
            ? $"已在第 {ring} 圈第 {position + 1} 格插入空格，原格子及命令顺移"
            : $"已在第 {ring} 圈第 {position + 1} 格起插入 {count} 个空格，原格子及命令顺移");
        return true;
    }

    private void ShiftSlotsFrom(int ring, int position, int delta)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow || !TryReadGridRingSlot(row, out int rowRing, out int rowSlot)) continue;
            if (rowRing != ring || rowSlot <= position) continue;
            row.Cells["Slot"].Value = (rowSlot + delta).ToString();
        }
    }

    // Removes the given 0-based positions. Their commands are moved to the
    // scene's unassigned list rather than dropped, so shrinking a ring never
    // costs the user the binding they picked.
    private bool RemoveSlotsAt(int ring, IEnumerable<int> positions)
    {
        List<int> indices = positions.Distinct().OrderBy(value => value).ToList();
        if (indices.Count == 0) return false;
        int slots = RingSlotCount(ring);
        if (slots <= 0) { Hint("先选中一个格子，再删除"); return false; }
        if (slots - indices.Count < 1) { Hint("每圈至少保留一格"); return false; }
        SceneMenuConfig scene = GetScene(_activeSceneKey);
        if (scene == null) return false;
        var doomed = new List<DataGridViewRow>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow || !TryReadGridRingSlot(row, out int rowRing, out int rowSlot)) continue;
            if (rowRing == ring && indices.Contains(rowSlot - 1)) doomed.Add(row);
        }
        int kept = 0;
        bool wasLoading = _loadingScene;
        _loadingScene = true;
        try
        {
        foreach (DataGridViewRow row in doomed)
        {
            MenuItemConfig? item = UnassignedFromRow(row);
            if (item != null) { scene.UnassignedItems.Add(item); kept++; }
            _grid.Rows.Remove(row);
        }
        RenumberRing(ring);
        if (!BumpRingSlots(ring - 1, -indices.Count)) return false;
        }
        finally { _loadingScene = wasLoading; }
        SyncCommandRowsToRingDefinitions();
        RefreshPreview();
        _preview.SelectCell(ring - 1, Math.Max(0, Math.Min(indices[0], RingSlotCount(ring) - 1)));
        Hint(kept > 0
            ? $"已删除 {indices.Count} 格，{kept} 条命令保留在“未分配命令”，可放回空格"
            : $"已删除 {indices.Count} 格");
        return true;
    }

    private void RenumberRing(int ring)
    {
        var rows = new List<DataGridViewRow>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow || !TryReadGridRingSlot(row, out int rowRing, out int _)) continue;
            if (rowRing == ring) rows.Add(row);
        }
        rows.Sort((left, right) =>
        {
            TryReadGridRingSlot(left, out int _, out int leftSlot);
            TryReadGridRingSlot(right, out int _, out int rightSlot);
            return leftSlot.CompareTo(rightSlot);
        });
        for (int i = 0; i < rows.Count; i++) rows[i].Cells["Slot"].Value = (i + 1).ToString();
    }

    private MenuItemConfig? UnassignedFromRow(DataGridViewRow row)
    {
        string label = Convert.ToString(row.Cells["Label"].Value)?.Trim() ?? "";
        string value = Convert.ToString(row.Cells["Value"].Value)?.Trim() ?? "";
        ComboRowState state = ReadComboRowState(row.Tag);
        if (label.Length == 0 && value.Length == 0 && state.Commands.Count == 0) return null;
        string kind = DisplayToKind(Convert.ToString(row.Cells["Kind"].Value));
        var item = new MenuItemConfig
        {
            Label = label.Length > 0 ? label : value,
            Kind = kind,
            Value = value,
            FontSizePx = ParseFontSize(Convert.ToString(row.Cells["FontSize"].Value)),
            FontName = NormalizeFontName(Convert.ToString(row.Cells["FontName"].Value)),
            TextColor = NormalizeColorText(Convert.ToString(row.Cells["TextColor"].Value)),
            IconId = state.IconId,
            IconPath = state.IconPath,
            ContentMode = state.ContentMode,
            FontBold = state.CellBold,
            TextEffect = state.CellEffect,
            ComboAdvanceEnabled = state.AdvanceEnabled,
            ComboAdvanceKey = state.AdvanceKey,
            ComboCommands = CloneComboCommands(state.Commands)
        };
        state.CopyTo(item);
        return item;
    }

    // --- multi-cell count dialog -----------------------------------------

    private void ChooseSlotCountChange()
    {
        if (_preview.PracticeMode) { Hint("练习模式下不能改动格子"); return; }
        if (!TrySelectedCell(out int ring, out int slot)) return;
        int current = RingSlotCount(ring);
        if (current <= 0) return;
        using var dialog = new Form
        {
            Text = $"第 {ring} 圈格数", Font = Font, StartPosition = FormStartPosition.CenterParent,
            AutoScaleMode = AutoScaleMode.Dpi, AutoScaleDimensions = new SizeF(96, 96),
            Size = new Size(480, 430), MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false
        };
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        header.Controls.Add(new Label { Text = "目标格数", AutoSize = true, Padding = new Padding(0, 6, 6, 0) });
        var count = new NumericUpDown { Minimum = 1, Maximum = 32, Value = current, Width = 72 };
        header.Controls.Add(count);
        body.Controls.Add(header, 0, 0);
        var hint = new Label { Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(430, 60) };
        body.Controls.Add(hint, 0, 1);
        var insert = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        var remove = new CheckedListBox { Dock = DockStyle.Fill, IntegralHeight = false, CheckOnClick = true };
        void Refresh()
        {
            int target = (int)count.Value;
            int delta = Math.Abs(target - current);
            bool adding = target > current;
            body.Controls.Remove(insert);
            body.Controls.Remove(remove);
            hint.Text = adding ? $"增加 {delta} 格：选择插入点，后续格子连同命令顺移。"
                : delta == 0 ? "格数未变，无需选择位置。"
                : $"减少 {delta} 格：勾选要删除的位置，其命令保留到“未分配命令”。";
            insert.Items.Clear();
            remove.Items.Clear();
            for (int i = 0; i < current; i++)
            {
                string name = SlotLabel(ring, i);
                insert.Items.Add($"第 {i + 1} 格之前 · {name}");
                remove.Items.Add($"第 {i + 1} 格 · {name}", delta == 1 && i == slot - 1);
            }
            insert.Items.Add("最后一格之后");
            insert.SelectedIndex = Math.Max(0, Math.Min(current, slot - 1));
            if (adding) body.Controls.Add(insert, 0, 2);
            else if (delta > 0) body.Controls.Add(remove, 0, 2);
        }
        count.ValueChanged += (_, _) => Refresh();
        Refresh();
        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        var apply = new Button { Text = "确定", AutoSize = true };
        apply.Click += (_, _) =>
        {
            int target = (int)count.Value;
            int delta = Math.Abs(target - current);
            if (target < current && remove.CheckedIndices.Count != delta)
            { hint.Text = $"需要勾选 {delta} 格，当前勾选 {remove.CheckedIndices.Count} 格。"; return; }
            if (delta == 0) { hint.Text = "格数未变。"; return; }
            if (target > current && insert.SelectedIndex < 0) { hint.Text = "请选择插入点。"; return; }
            dialog.DialogResult = DialogResult.OK;
        };
        footer.Controls.Add(cancel);
        footer.Controls.Add(apply);
        body.Controls.Add(footer, 0, 3);
        dialog.Controls.Add(body);
        dialog.CancelButton = cancel;
        dialog.AcceptButton = apply;
        if (dialog.ShowDialog(this) != DialogResult.OK) { Hint("未改动格数"); return; }
        int final = (int)count.Value;
        if (final > current) InsertSlotsAt(ring, Math.Max(0, Math.Min(current, insert.SelectedIndex)), final - current);
        else if (final < current) RemoveSlotsAt(ring, remove.CheckedIndices.Cast<int>());
    }

    // --- unassigned commands ---------------------------------------------

    private sealed class UnassignedChoice
    {
        internal MenuItemConfig Item { get; }
        internal UnassignedChoice(MenuItemConfig item) { Item = item; }
        public override string ToString() => string.IsNullOrWhiteSpace(Item.Label) ? "(未命名命令)" : Item.Label;
    }

    private void RefreshUnassigned(ComboBox combo)
    {
        SceneMenuConfig scene = GetScene(_activeSceneKey);
        combo.Items.Clear();
        if (scene == null) return;
        foreach (MenuItemConfig item in scene.UnassignedItems.Where(item => !ProductFeatureScope.IsRetiredBinding(item)))
            combo.Items.Add(new UnassignedChoice(item));
        if (combo.Items.Count == 0) combo.Items.Add("（没有未分配命令）");
    }

    private bool RestoreUnassigned(ComboBox combo)
    {
        if (_preview.PracticeMode) { Hint("练习模式下不能改动格子"); return false; }
        SceneMenuConfig scene = GetScene(_activeSceneKey);
        MenuItemConfig chosen = (combo.SelectedItem as UnassignedChoice)?.Item;
        int index = scene?.UnassignedItems.IndexOf(chosen) ?? -1;
        if (scene == null || chosen == null || ProductFeatureScope.IsRetiredBinding(chosen) || index < 0)
        { Hint("先在“未分配命令”里选一条命令"); return false; }
        if (!TrySelectedCell(out int ring, out int slot)) return false;
        DataGridViewRow? row = FindCommandRow(ring, slot);
        if (row == null) return false;
        MenuItemConfig item = scene.UnassignedItems[index];
        // A restore is a move of the complete binding; keep a replaced command
        // in the pool instead of silently losing it.
        MenuItemConfig displaced = UnassignedFromRow(row);
        bool wasLoading = _loadingScene;
        _loadingScene = true;
        try
        {
        row.Cells["Label"].Value = item.Label;
        row.Cells["Kind"].Value = KindToDisplay(item.Kind);
        row.Cells["Value"].Value = item.Value;
        row.Cells["FontSize"].Value = item.FontSizePx > 0 ? item.FontSizePx.ToString() : "";
        row.Cells["FontName"].Value = item.ResolvedFontFamily();
        row.Cells["TextColor"].Value = item.TextColor;
        ComboRowState state = new ComboRowState
        {
            Commands = CloneComboCommands(item.ComboCommands),
            AdvanceEnabled = item.ComboAdvanceEnabled,
            AdvanceKey = item.ComboAdvanceKey
        }.WithGeometryFrom(item);
        state.IconId = item.IconId;
        state.IconPath = item.IconPath;
        state.ContentMode = item.ContentMode;
        state.CellBold = item.FontBold;
        state.CellEffect = item.TextEffect;
        row.Tag = state;
        scene.UnassignedItems.RemoveAt(index);
        if (displaced != null) scene.UnassignedItems.Add(displaced);
        }
        finally { _loadingScene = wasLoading; }
        RefreshUnassigned(combo);
        SyncCommandRowsToRingDefinitions();
        RefreshPreview();
        _preview.SelectCell(ring - 1, slot - 1);
        Hint($"已把命令放回第 {ring} 圈第 {slot} 格");
        return true;
    }
}
