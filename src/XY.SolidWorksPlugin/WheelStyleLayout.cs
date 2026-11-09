using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

internal sealed class RadialCellsSwappedEventArgs : EventArgs
{
    public int SourceRingIndex { get; }
    public int SourceSlot { get; }
    public int TargetRingIndex { get; }
    public int TargetSlot { get; }
    public RadialCellsSwappedEventArgs(int sourceRing, int sourceSlot, int targetRing, int targetSlot)
    { SourceRingIndex = sourceRing; SourceSlot = sourceSlot; TargetRingIndex = targetRing; TargetSlot = targetSlot; }
}

internal sealed partial class RadialMenuControl
{
    private const int GroupSlot = -2, BackSlot = -3;
    private int _activeGroup = -1;
    private List<StyleCell>? _hitGeometry;
    private int _hitGeometrySignature;
    internal int HitGeometryBuildCount { get; private set; }
	internal int StyleGeometryBuildCount { get; private set; }
    private static readonly HashSet<string> KnownIconIds = new HashSet<string>(WheelIconCatalog.Entries.Select(icon => icon.Id), StringComparer.OrdinalIgnoreCase);
    private readonly Timer _holdTimer = new Timer { Interval = 450 };
    private HitResult _dragSource = HitResult.None;
    private HitResult _cellDragTarget = HitResult.None;
    private Point _dragOrigin;
    private bool _cellDragArmed, _cellDragMoved;
    // A swap is committed once the pointer has travelled well past the point
    // where it merely wobbled, so a sloppy click never moves a command.
    private const int CellDragCommitDistance = 8;
    public event EventHandler<RadialCellsSwappedEventArgs>? CellsSwapped;
    public event EventHandler? ViewChanged;
    private Color IconAccent => string.Equals(_scene.IconColor, "Purple", StringComparison.OrdinalIgnoreCase)
        ? Color.FromArgb(130, 76, 190) : Color.FromArgb(40, 143, 102);
    private sealed class StyleCell : IDisposable
    {
        public HitResult Hit;
        public GraphicsPath Path = new GraphicsPath();
        public RectangleF Content;
        public MenuItemConfig? Item;
        public string Label = "";
        public void Dispose() => Path.Dispose();
    }

    public void CopyNavigationFrom(RadialMenuControl source)
    { _activeGroup = source._activeGroup; InvalidateStaticLayer(); Invalidate(); }
    public void EndDragMode() => _dragMode = false;
    private bool NavigateStyle(HitResult hit)
    {
        if (hit.Slot != GroupSlot && hit.Slot != BackSlot) return false;
        _activeGroup = hit.Slot == GroupSlot ? hit.RingIndex : -1;
        _hover = HitResult.None;
        InvalidateStaticLayer(); Invalidate(); ViewChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
    private void BeginCellDrag(Point point)
    {
        CancelCellDrag();
        var hit = HitTest(point);
        if (NavigateStyle(hit)) return;
        if (!hit.HasSlot) return;
        SelectCell(hit.RingIndex, hit.Slot);
        _dragSource = hit; _cellDragTarget = hit; _dragOrigin = point; Capture = true;
        // The hold timer is only a courtesy now: dragging arms on the first
        // real movement, and pressing and waiting still lights the drag cursor
        // for anyone who expects a long press.
        _holdTimer.Start();
    }
    // Fired by the hold timer, where the only way to know the button is still
    // down is the process-wide mouse state.
    private void ArmCellDrag()
    {
        _holdTimer.Stop();
        if (!EnableCellSelection || !_dragSource.HasSlot || (MouseButtons & MouseButtons.Left) == 0) { CancelCellDrag(); return; }
        SetCellDragArmed();
    }

    // Fired by a real move: the event already carries the pressed button.
    private void SetCellDragArmed()
    {
        if (_cellDragArmed) return;
        _cellDragArmed = true; Cursor = Cursors.SizeAll; Invalidate();
    }
    private void UpdateCellDrag(MouseEventArgs e)
    {
        if (!_dragSource.HasSlot) return;
        Point point = e.Location;
        int distance = Math.Abs(point.X - _dragOrigin.X) + Math.Abs(point.Y - _dragOrigin.Y);
        // Standard drag semantics: leaving the press point with the button
        // still down arms the drag. The old rule cancelled the drag on that
        // same movement, so the gesture could only ever start from a 450 ms
        // long press - nobody discovers that, and cells looked undraggable.
        if (!_cellDragArmed)
        {
            if (distance <= SystemInformation.DragSize.Width) return;
            if ((e.Button & MouseButtons.Left) == 0) { CancelCellDrag(); return; }
            _holdTimer.Stop();
            SetCellDragArmed();
        }
        if (!_dragSource.HasSlot) return;
        if (distance > CellDragCommitDistance) _cellDragMoved = true;
        var target = HitTest(point);
        if (target.Equals(_cellDragTarget)) return;
        _cellDragTarget = target;
        Invalidate();
    }
    private void EndCellDrag(Point point)
    {
        var source = _dragSource; var target = HitTest(point);
        bool swap = _cellDragArmed && _cellDragMoved && source.HasSlot && target.HasSlot && !source.Equals(target);
        CancelCellDrag();
        if (swap) CellsSwapped?.Invoke(this, new RadialCellsSwappedEventArgs(source.RingIndex, source.Slot, target.RingIndex, target.Slot));
    }
    private void CancelCellDrag()
    {
        _holdTimer.Stop(); _dragSource = HitResult.None; _cellDragTarget = HitResult.None;
        _cellDragArmed = _cellDragMoved = false;
        Cursor = Cursors.Default;
        if (EnableCellSelection) Capture = false;
    }
    // The drag gesture is invisible without this: the source keeps a dashed
    // outline and the cell under the pointer is tinted, so a swap reads as a
    // deliberate action before the mouse is released.
    internal void DrawCellDragFeedback(Graphics graphics)
    {
        if (!EnableCellSelection || !_cellDragArmed || !_dragSource.HasSlot) return;
        List<StyleCell> cells = BuildStyledCells();
        try
        {
            StyleCell? source = cells.FirstOrDefault(cell => cell.Hit.Equals(_dragSource));
            if (source != null)
            {
                using var pen = new Pen(Color.FromArgb(170, 60, 72, 88), 2f) { DashStyle = DashStyle.Dash };
                graphics.DrawPath(pen, source.Path);
            }
            if (!_cellDragMoved || !_cellDragTarget.HasSlot || _cellDragTarget.Equals(_dragSource)) return;
            StyleCell? target = cells.FirstOrDefault(cell => cell.Hit.Equals(_cellDragTarget));
            if (target == null) return;
            using var fill = new SolidBrush(Color.FromArgb(70, WheelAccentColor));
            graphics.FillPath(fill, target.Path);
            using var targetPen = new Pen(WheelAccentColor, 2f) { DashStyle = DashStyle.Dash };
            graphics.DrawPath(targetPen, target.Path);
        }
        finally { foreach (StyleCell cell in cells) cell.Dispose(); }
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture && CancelGeometryDrag()) return;
        if (!Capture && _dragSource.HasSlot) CancelCellDrag();
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (EnableCellSelection && (keyData & Keys.KeyCode) == Keys.Escape)
        {
            // A geometry drag is undone back to where it started; the cell
            // swap gesture only drops its armed state.
            if (CancelGeometryDrag()) { Invalidate(); return true; }
            if (_dragSource.HasSlot) { CancelCellDrag(); Invalidate(); return true; }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private List<StyleCell> BuildStyledCells()
    {
		StyleGeometryBuildCount++;
        var bounds = GetMenuBounds(); var size = bounds.Width;
        var center = new PointF(bounds.Left + size / 2, bounds.Top + size / 2);
        var rings = _scene.Rings.Where(r => r.Enabled).OrderBy(r => r.Index).ToList();
        var cells = new List<StyleCell>();
        // Discrete tiles (orbit discs and free-placed cells) can take a shape of
        // their own. Ring wedges and grid panels keep the geometry their style
        // needs, so a scene's existing layout never silently changes shape.
        bool shapeFromItem = _scene.StyleId == "orbit" || _scene.StyleId == "free";
        void Add(HitResult hit, RectangleF rect, bool round, MenuItemConfig? item, string label = "")
        {
            var cell = new StyleCell { Hit = hit, Content = rect, Item = item, Label = label };
            string shape = shapeFromItem && item != null ? item.CellShape : (round ? "Circle" : "Square");
            AddCellShape(cell.Path, rect, shape);
            cell.Content.Inflate(-rect.Width * (round ? .15f : .07f), -rect.Height * (round ? .15f : .07f));
            cells.Add(cell);
        }
        var style = _scene.StyleId;
        if (style == "grid" || style == "honeycomb") return BuildPanelStyleCells(bounds, rings, style == "honeycomb");
        if (style == "11" || style == "06")
        {
            bool nodes = style == "06";
            var group = rings.FirstOrDefault(r => r.Index == _activeGroup);
            if (!nodes && group == null) group = rings.FirstOrDefault();
            if (nodes && group == null)
            {
                for (int i = 0; i < rings.Count; i++)
                    Add(new HitResult(rings[i].Index, GroupSlot, false), RectAround(PointOnCircle(center, size * .30f, -90 + i * 360f / rings.Count), size * .11f), true, null, "第" + (rings[i].Index + 1) + "圈");
            }
            if (!nodes)
            {
                var width = size * .86f / Math.Max(1, rings.Count);
                for (int i = 0; i < rings.Count; i++)
                    Add(new HitResult(rings[i].Index, GroupSlot, false), new RectangleF(bounds.Left + size * .07f + i * width, bounds.Top + size * .09f, width - 3, size * .09f), false, null, "第" + (rings[i].Index + 1) + "组");
            }
            if (group != null)
            {
                var positions = nodes ? BuildNodeCells(EffectiveMenuSizePx(), group.Slots).Select(rect => new RectangleF(rect.X * size / EffectiveMenuSizePx(), rect.Y * size / EffectiveMenuSizePx(), rect.Width * size / EffectiveMenuSizePx(), rect.Height * size / EffectiveMenuSizePx())).ToList() : null;
                int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(group.Slots * 1.25)));
                int rows = Math.Max(1, (int)Math.Ceiling(group.Slots / (double)columns));
                for (int i = 0; i < group.Slots; i++)
                {
                    var rect = nodes ? Offset(positions![i], center.X, center.Y)
                        : new RectangleF(bounds.Left + size * .07f + i % columns * size * .86f / columns, bounds.Top + size * .21f + i / columns * size * .60f / rows, size * .86f / columns - 4, size * .60f / rows - 4);
                    Add(new HitResult(group.Index, i, false), rect, nodes, group.Items.FirstOrDefault(item => item.Slot == i));
                }
            }
            Add(nodes && group != null ? new HitResult(-1, BackSlot, false) : HitResult.Center,
                nodes ? RectAround(center, size * .06f) : new RectangleF(center.X - size * .13f, bounds.Top + size * .86f, size * .26f, size * .08f), nodes, null, nodes && group != null ? "返回" : "取消");
            ApplyCellSizes(cells);
            return cells;
        }
        var layouts = BuildRingLayouts(bounds, rings);
        foreach (var layout in layouts)
        {
            var ring = layout.Ring; var sweep = 360f / Math.Max(1, ring.Slots);
            var radius = (layout.InnerRadius + layout.OuterRadius) / 2;
            // Same card sizing the CAD wheel uses: the card is limited by both
            // the band width and the arc each slot gets, at 70% of each, so a
            // generous ring buys separation and captions instead of artwork.
            var tangentSpace = ring.Slots <= 2 ? layout.Width * 1.4f : 2 * radius * (float)Math.Sin(Math.PI / Math.Max(2, ring.Slots));
            var side = Math.Max(1, Math.Min(Math.Max(1, layout.Width - 4) * .70f, tangentSpace * .70f));
            for (int i = 0; i < ring.Slots; i++)
            {
                float start = -90 + (style == "classic" ? i : i - .5f) * sweep;
                var at = PointOnCircle(center, radius, start + sweep / 2);
                var item = ring.Items.FirstOrDefault(it => it.Slot == i);
                if (style == "orbit") Add(new HitResult(ring.Index, i, false), RectAround(at, side / 2), true, item);
                else
                {
                    var cell = new StyleCell { Hit = new HitResult(ring.Index, i, false), Item = item,
                        Content = RectAround(at, side / 2) };
                    cell.Path.Dispose(); cell.Path = CreateSlotPath(center, layout.InnerRadius, layout.OuterRadius, start, sweep);
                    cells.Add(cell);
                }
            }
        }
        Add(HitResult.Center, RectAround(center, Math.Max(8, EffectiveCenterRadiusPx() - 2)), true, null, EnableCellSelection ? "取消" : "");
        ApplyCellSizes(cells);
        ApplyFreePositions(cells, bounds);
        return cells;
    }

    // A per-cell size only applies where the cell is a tile of its own - the
    // node and panel styles, the orbit discs and free-placed cells. Wedge
    // slots and the classic wheel are sized by their ring geometry, which is
    // exactly the split the CAD build documents in its own settings window.
    private void ApplyCellSizes(List<StyleCell> cells)
    {
        if (_scene.StyleId != "11" && _scene.StyleId != "06" && _scene.StyleId != "orbit" && _scene.StyleId != "free"
            && _scene.StyleId != "grid" && _scene.StyleId != "honeycomb") return;
        foreach (StyleCell cell in cells)
        {
            if (cell.Item == null || cell.Path.PointCount == 0) continue;
            int percent = Math.Max(40, Math.Min(160, cell.Item.CellSizePercent));
            if (percent == 100) continue;
            float factor = percent / 100f;
            RectangleF rect = cell.Path.GetBounds();
            float cx = rect.Left + rect.Width / 2, cy = rect.Top + rect.Height / 2;
            using var matrix = new Matrix();
            matrix.Translate(cx, cy);
            matrix.Scale(factor, factor);
            matrix.Translate(-cx, -cy);
            cell.Path.Transform(matrix);
            RectangleF content = cell.Content;
            cell.Content = new RectangleF(cx + (content.Left - cx) * factor, cy + (content.Top - cy) * factor,
                content.Width * factor, content.Height * factor);
        }
    }

    // A cell carrying FreeX/FreeY is drawn where the user put it. Everything
    // downstream - hit testing, text fitting, highlighting - follows the cell
    // rectangle, so moving it here is enough for the whole wheel to agree.
    private void ApplyFreePositions(List<StyleCell> cells, RectangleF bounds)
    {
        if (!FreeWheelLayout.HasAny(_scene)) return;
        float gap = bounds.Width * FreeWheelLayout.GapRatio;
        var placed = new List<RectangleF>();
        var movable = cells.Where(c => c.Item != null && c.Item.FreeX.HasValue && c.Item.FreeY.HasValue).ToList();
        foreach (StyleCell cell in movable)
        {
            var center = FreeWheelLayout.CenterOf(bounds, cell.Item.FreeX.Value, cell.Item.FreeY.Value);
            var proposed = FreeWheelLayout.RectAt(bounds, center, cell.Content.Size);
            // Keep the wheel readable: a proposed box that collides with an
            // already placed cell falls back to its ring position.
            if (placed.Any(other => FreeWheelLayout.Overlaps(proposed, other, gap))) continue;
            placed.Add(proposed);
            float dx = proposed.Left + proposed.Width / 2 - (cell.Content.Left + cell.Content.Width / 2);
            float dy = proposed.Top + proposed.Height / 2 - (cell.Content.Top + cell.Content.Height / 2);
            cell.Content = proposed;
            cell.Path.Dispose();
            cell.Path = new System.Drawing.Drawing2D.GraphicsPath();
            AddCellShape(cell.Path, proposed, cell.Item.CellShape);
            // The caption rectangle travels with the cell.
            cell.Content.Inflate(-proposed.Width * .15f, -proposed.Height * .15f);
            _ = dx; _ = dy;
        }
    }
    private static List<RectangleF> BuildNodeCells(float size, int count)
    {
        var radius = size * .085f; var positions = new List<RectangleF>();
        for (; radius > 1; radius -= .5f)
        {
            positions.Clear(); int remaining = count;
            for (int row = 0; row < 3 && remaining > 0; row++)
            {
                var orbit = size / 2 - radius - 8 - row * (radius * 2 + 8);
                if (orbit < radius + size * .065f + 8) continue;
                int capacity = Math.Max(1, (int)Math.Floor(Math.PI / Math.Asin(Math.Min(1, (radius + 4) / orbit))));
                int take = Math.Min(remaining, capacity);
                for (int i = 0; i < take; i++) positions.Add(RectAround(PointOnCircle(PointF.Empty, orbit, -90 + i * 360f / take), radius));
                remaining -= take;
            }
            if (remaining == 0) return positions;
        }
        return positions;
    }
    private HitResult HitStyledWheel(Point point)
    {
		return PreparedHitGeometry().FirstOrDefault(cell => cell.Path.IsVisible(point))?.Hit ?? HitResult.None;
	}

	private List<StyleCell> PreparedHitGeometry()
	{
        // Match the reference wheel's prepared scene: pointer motion only tests
        // existing geometry. GDI paths are rebuilt after a scene/layout edit.
        int signature = StaticLayerSignature();
        if (_hitGeometry == null || _hitGeometrySignature != signature)
        {
            DisposeHitGeometry();
            _hitGeometry = BuildStyledCells();
            _hitGeometrySignature = signature;
            HitGeometryBuildCount++;
        }
        return _hitGeometry;
    }
    private void DisposeHitGeometry()
    {
        if (_hitGeometry != null) foreach (var cell in _hitGeometry) cell.Dispose();
        _hitGeometry = null;
    }
    private void DrawStyledWheel(Graphics graphics, bool includeHighlights, bool highlightsOnly = false)
    {
        var bounds = GetMenuBounds(); var cells = highlightsOnly ? PreparedHitGeometry() : BuildStyledCells();
        // A soft shadow and a frosted outer rim need real alpha compositing. The
        // live popup is a colour-keyed window where any non-key pixel is opaque,
        // so those two are only drawn where a genuine backdrop exists.
        bool composited = ScaleMenuForPreview || EnableCellSelection;
        // The icon wheel is painted the way the CAD build paints it: the ring
        // band is one annulus, so slots meet without an antialiased seam.
        bool swStyle = string.Equals(_scene.StyleId, "01", StringComparison.OrdinalIgnoreCase);
        var center = new PointF(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        var band = swStyle ? BuildRingLayouts(bounds, _scene.Rings.Where(r => r.Enabled).OrderBy(r => r.Index).ToList()) : new List<RingLayout>();
        try
        {
            if (!highlightsOnly && DrawBackdrop && composited) DrawGlassShadow(graphics, bounds, cells);
            if (!highlightsOnly && DrawBackdrop && swStyle && band.Count > 0)
            {
                using var annulus = new GraphicsPath { FillMode = FillMode.Alternate };
                annulus.AddEllipse(RectAround(center, band[band.Count - 1].OuterRadius));
                annulus.AddEllipse(RectAround(center, band[0].InnerRadius));
                using var surface = CreateSurfaceBrush(bounds);
                graphics.FillPath(surface, annulus);
                if (Wheel.Glass) DrawGlassSheen(graphics, annulus);
            }
            foreach (var cell in cells)
            {
                bool selected = cell.Hit.Equals(_selected), hover = cell.Hit.Equals(_hover);
                bool highlight = includeHighlights && DrawHover && (selected || hover);
                if (highlightsOnly && !highlight) continue;
                if (!highlightsOnly && DrawBackdrop && !cell.Hit.IsCenter)
                {
                    // The icon wheel's band is already painted as one annulus.
                    if (!swStyle)
                    {
                        using var brush = CreateSurfaceBrush(cell.Path.GetBounds()); graphics.FillPath(brush, cell.Path);
                        if (Wheel.Glass) DrawGlassSheen(graphics, cell.Path);
                    }
                    if (_backgroundImage != null)
                    {
                        var state = graphics.Save(); graphics.SetClip(cell.Path, CombineMode.Intersect);
                        using var attributes = new ImageAttributes(); attributes.SetColorMatrix(new ColorMatrix { Matrix33 = Math.Max(0, Math.Min(100, _scene.BackgroundOpacityPercent)) / 100f });
                        graphics.DrawImage(_backgroundImage, Rectangle.Round(Cover(bounds, _backgroundImage.Size)), 0, 0, _backgroundImage.Width, _backgroundImage.Height, GraphicsUnit.Pixel, attributes); graphics.Restore(state);
                    }
                }
                if (highlight)
                {
                    // Same strengths the CAD wheel uses: the slot under the
                    // cursor reads clearly, a committed selection a little softer.
                    Color accent = hover ? WheelHoverColor : WheelAccentColor;
                    using var fill = new SolidBrush(Color.FromArgb(hover ? 135 : 95, accent)); graphics.FillPath(fill, cell.Path);
                    if (Wheel.Glass) DrawGlassSheen(graphics, cell.Path, .10f);
                    using var pen = new Pen(accent, selected ? 2f : 1.4f); graphics.DrawPath(pen, cell.Path);
                }
                if (!highlightsOnly && DrawGuides && !cell.Hit.IsCenter)
                {
                    using var pen = new Pen(Color.FromArgb(190, WheelLineColor), 1f);
                    if (_scene.StyleId != "01") graphics.DrawPath(pen, cell.Path);
                }
                if (!highlightsOnly && DrawForeground && DrawLabels)
                {
                    var state = graphics.Save(); graphics.SetClip(cell.Path, CombineMode.Intersect);
                    if (cell.Item != null) DrawStyledContent(graphics, cell.Item, cell.Content, cell.Hit.RingIndex);
                    else if (cell.Label.Length > 0 || EnableCellSelection)
                    {
                        using var font = CreateWheelFont(_config.FontName, Math.Max(7, Math.Min(14, cell.Content.Height * .25f)), _config.FontBold);
                        using var ink = new SolidBrush(WheelTextColor);
                        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        graphics.DrawString(cell.Label.Length > 0 ? cell.Label : "+", font, ink, cell.Content, format);
                    }
                    graphics.Restore(state);
                }
            }
            if (!highlightsOnly && DrawGuides && swStyle && band.Count > 0)
            {
                // One rim per ring just inside its outer edge, plus the inner
                // edge of the innermost ring - the CAD wheel's guide lines.
                using var pen = new Pen(Color.FromArgb(150, Wheel.GetBorderColor()), 1f);
                foreach (var layout in band) DrawCircle(graphics, pen, center, Math.Max(1f, layout.OuterRadius - 1));
                DrawCircle(graphics, pen, center, band[0].InnerRadius + 1);
            }
            if (!highlightsOnly && DrawBackdrop && composited && Wheel.Glass
                && _scene.StyleId != "grid" && _scene.StyleId != "honeycomb") DrawGlassRim(graphics, bounds);
            // The CAD wheel's centre mark: a small cross, sized from the wheel
            // and drawn in the caption colour. Only the live label layer asks
            // for it; the colour-keyed surface stays open everywhere else.
            if (!highlightsOnly && DrawCenterMark && swStyle && DrawLabels && DrawForeground)
            {
                float mark = Math.Max(4f, Math.Min(7f, bounds.Width / 85f));
                using var close = new Pen(WheelTextColor, Math.Max(1f, bounds.Width / 400f))
                { StartCap = LineCap.Round, EndCap = LineCap.Round };
                graphics.DrawLine(close, center.X - mark, center.Y - mark, center.X + mark, center.Y + mark);
                graphics.DrawLine(close, center.X - mark, center.Y + mark, center.X + mark, center.Y - mark);
            }
        }
        finally { if (!highlightsOnly) foreach (var cell in cells) cell.Dispose(); }
    }

    // "Circle" is the historical tile shape; "Rounded" and "Square" were added
    // with the cell shape setting. Unknown values keep the circle.
    private static void AddCellShape(GraphicsPath path, RectangleF rect, string shape)
    {
        if (string.Equals(shape, "Hexagon", StringComparison.OrdinalIgnoreCase))
        {
            path.AddPolygon(new[] {
                new PointF(rect.Left + rect.Width / 2, rect.Top),
                new PointF(rect.Right, rect.Top + rect.Height / 4),
                new PointF(rect.Right, rect.Bottom - rect.Height / 4),
                new PointF(rect.Left + rect.Width / 2, rect.Bottom),
                new PointF(rect.Left, rect.Bottom - rect.Height / 4),
                new PointF(rect.Left, rect.Top + rect.Height / 4) });
            return;
        }
        if (string.Equals(shape, "Square", StringComparison.OrdinalIgnoreCase)) { path.AddRectangle(rect); return; }
        if (string.Equals(shape, "Rounded", StringComparison.OrdinalIgnoreCase))
        { AddRoundedRectangle(path, rect, Math.Max(1f, Math.Min(rect.Width, rect.Height) * .28f)); return; }
        path.AddEllipse(rect);
    }

    private static void AddRoundedRectangle(GraphicsPath path, RectangleF rect, float radius)
    {
        float d = radius * 2f;
        if (d >= rect.Width || d >= rect.Height) { path.AddRectangle(rect); return; }
        path.AddArc(rect.Left, rect.Top, d, d, 180f, 90f);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270f, 90f);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90f, 90f);
        path.CloseFigure();
    }

    // A frosted highlight across the upper part of a cell. It is clipped to the
    // cell, so it works for wedges, discs and free-placed tiles alike.
    private static void DrawGlassSheen(Graphics graphics, GraphicsPath path, float strength = .18f)
    {
        var box = path.GetBounds();
        if (box.Width <= 0f || box.Height <= 0f) return;
        var state = graphics.Save();
        graphics.SetClip(path, CombineMode.Intersect);
        using var sheen = new LinearGradientBrush(new RectangleF(box.Left, box.Top, box.Width, box.Height * .58f),
            Color.FromArgb((int)Math.Max(0, Math.Min(255, strength * 255f)), Color.White),
            Color.FromArgb(0, Color.White), LinearGradientMode.Vertical);
        graphics.FillRectangle(sheen, box.Left, box.Top, box.Width, box.Height * .58f);
        graphics.Restore(state);
    }

    // Stacked, slightly grown copies of the wheel's own outline stand in for a
    // blurred drop shadow: no bitmap filter, no extra allocation. Following the
    // real cell outline keeps the neutral centre and the corners clear, which a
    // plain ellipse painted over the whole disc would not.
    private void DrawGlassShadow(Graphics graphics, RectangleF bounds, List<StyleCell> cells)
    {
        int percent = Wheel.ShadowPercent;
        if (percent <= 0) return;
        using var shape = new GraphicsPath(FillMode.Winding);
        foreach (StyleCell cell in cells)
        {
            if (cell.Hit.IsCenter) continue;
            shape.AddPath(cell.Path, connect: false);
        }
        var box = shape.GetBounds();
        if (box.Width <= 0f || box.Height <= 0f) return;
        const int layers = 4;
        float spread = Math.Max(2f, bounds.Width * .022f);
        var state = graphics.Save();
        float cx = bounds.Left + bounds.Width / 2f, cy = bounds.Top + bounds.Height / 2f;
        for (int i = layers; i >= 1; i--)
        {
            float grow = spread * i / layers;
            int alpha = percent * 255 / 100 * (layers - i + 1) / (layers * 2);
            if (alpha <= 0) continue;
            graphics.TranslateTransform(cx, cy);
            graphics.ScaleTransform((bounds.Width + grow * 2) / bounds.Width, (bounds.Height + grow * 2) / bounds.Height);
            graphics.TranslateTransform(-cx, -(cy - grow * .4f));
            using var brush = new SolidBrush(Color.FromArgb(Math.Min(120, alpha), Color.Black));
            graphics.FillPath(brush, shape);
            graphics.ResetTransform();
        }
        graphics.Restore(state);
    }

    // A bright hairline just inside the outer edge; the single detail that reads
    // as glass rather than a flat dark disc.
    private void DrawGlassRim(Graphics graphics, RectangleF bounds)
    {
        var rim = new RectangleF(bounds.Left + .75f, bounds.Top + .75f, bounds.Width - 1.5f, bounds.Height - 1.5f);
        using var path = new GraphicsPath();
        path.AddEllipse(rim);
        using var pen = new Pen(Color.FromArgb(150, WheelLineColor), 1.5f);
        graphics.DrawPath(pen, path);
    }

    // Imported wheels may carry the catalogue reference in IconData when the
    // importer could not tell which field the source used. It is only a
    // fallback: IconId always wins, and an unknown id still draws the default.
    private static string EffectiveIconId(MenuItemConfig item)
    {
        string id = (item.IconId ?? "").Trim();
        return id.Length > 0 ? id : (item.IconData ?? "").Trim();
    }

    // "Icon" stretches the source to fill the cell, which is what a symbol wants.
    // "Image" letterboxes it, so a user's photograph keeps its proportions.
    private static void DrawPlacedImage(Graphics graphics, Image source, RectangleF target, string imageMode)
    {
        if (!string.Equals(imageMode, "Image", StringComparison.OrdinalIgnoreCase)
            || source.Width <= 0 || source.Height <= 0)
        {
            WheelIconDrawing.Draw(graphics, source, target);
            return;
        }
        float ratio = Math.Min(target.Width / source.Width, target.Height / source.Height);
        float width = source.Width * ratio, height = source.Height * ratio;
        WheelIconDrawing.Draw(graphics, source, new RectangleF(
            target.Left + (target.Width - width) / 2f,
            target.Top + (target.Height - height) / 2f,
            width, height));
    }

    private void DrawStyledContent(Graphics graphics, MenuItemConfig item, RectangleF bounds, int ringIndex)
    {
        float scale = GetMenuBounds().Width / ComputeEffectiveMenuSizePx(_scene.MenuSizePx > 0 ? _scene.MenuSizePx : _config.MenuSizePx, 96);
        // Discrete cells have already scaled their path and content together.
        // Sector styles retain their ring-defined geometry, as in CAD.
        string mode = string.IsNullOrWhiteSpace(item.ContentMode) ? _scene.ContentMode : item.ContentMode;
        if (mode == "Auto") mode = bounds.Height >= 48 * scale ? "IconAndText" : "IconOnly";
        bool showIcon = mode != "TextOnly", showText = mode != "IconOnly";
        if (showIcon)
        {
            bool tintCatalogueIcon = false;
            // IconRatioPercent decides how much of the cell's height the symbol
            // takes when a caption is drawn under it.
            float iconShare = showText ? Math.Max(.2f, Math.Min(.9f, item.IconRatioPercent / 100f)) : .9f;
            float side = Math.Max(1, Math.Min(bounds.Width, bounds.Height * iconShare));
            var iconBounds = new RectangleF(bounds.Left + (bounds.Width - side) / 2, showText ? bounds.Top : bounds.Top + (bounds.Height - side) / 2, side, side);
            var iconTarget = Rectangle.Round(iconBounds);
            iconTarget.Width = Math.Max(1, iconTarget.Width); iconTarget.Height = Math.Max(1, iconTarget.Height);
            using var bitmap = new Bitmap(iconTarget.Width, iconTarget.Height);
            using (var ig = Graphics.FromImage(bitmap))
            {
                ig.SmoothingMode = SmoothingMode.AntiAlias;
                var local = new RectangleF(0, 0, bitmap.Width, bitmap.Height);
                var external = GetIcon(item.IconPath);
                if (external != null) DrawPlacedImage(ig, external, local, item.ImageMode);
                else if (_customIcons.Draw(ig, _scene.Key, ringIndex, item, local)) { }
                else if (!WheelIconPreference.IsExplicitCatalogue(item)
                    && RadialCommandIcons.TryDrawExactNative(ig, item, local, _scene.Key)) { }
                else if (KnownIconIds.Contains(EffectiveIconId(item)))
                {
                    tintCatalogueIcon = true;
                    // WheelIconCatalog intentionally keeps a usable source
                    // bitmap floor (16 px).  A narrow inner-ring cell can be
                    // smaller than that at high DPI or with many rings;
                    // passing the raw cell width used to throw from Render()
                    // during OnPaint and WinForms replaced the whole preview
                    // with its red error-X placeholder.  Render at the floor
                    // and let DrawImage scale it into the actual cell.
                    int sourceSize = Math.Max(16, Math.Min(1024, bitmap.Width));
                    using var icon = WheelIconCatalog.Render(EffectiveIconId(item), sourceSize);
                    if (icon != null) WheelIconDrawing.Draw(ig, icon, local);
                }
                else RadialCommandIcons.Draw(ig, item, local, _scene.Key);
            }
            using var attributes = new ImageAttributes(); var color = IconAccent;
            // Original host/extension artwork keeps its actual colours, as in
            // the reference wheel. Accent tint belongs to our vector catalogue.
            if (tintCatalogueIcon && !string.Equals(item.ImageMode, "Image", StringComparison.OrdinalIgnoreCase))
                attributes.SetColorMatrix(new ColorMatrix(new[] {
                new float[]{0,0,0,0,0}, new float[]{0,0,0,0,0}, new float[]{0,0,0,0,0}, new float[]{0,0,0,1,0}, new float[]{color.R/255f,color.G/255f,color.B/255f,0,1} }));
            WheelIconDrawing.Draw(graphics, bitmap, iconTarget, attributes);
            if (showText) { bounds.Y += side + 3 * scale; bounds.Height = Math.Max(1, bounds.Height - side - 3 * scale); }
        }
        if (!showText) return;
        // FontFamily is the field the CAD wheel writes; FontName is the older
        // one. ResolvedFontFamily picks whichever is populated.
        string family = string.IsNullOrWhiteSpace(item.ResolvedFontFamily()) ? _config.FontName : item.ResolvedFontFamily();
        bool bold = item.FontBold ?? _config.FontBold;
        string effect = TextEffects.Normalize(string.IsNullOrWhiteSpace(item.TextEffect) ? _config.TextEffect : item.TextEffect);
        var colorText = ParseTextColor(string.IsNullOrWhiteSpace(item.TextColor) ? _config.TextColor : item.TextColor);
        float requested = item.FontSizePx * scale;
        float fontSize = requested > 0 ? requested : Math.Min(24 * scale, bounds.Height / 2);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        var label = (item.Label ?? "").Trim();
        // Automatic sizes shrink to a readable floor. Explicit sizes remain the
        // user's choice; clipping/ellipsis and hover text preserve dense layouts.
        if (requested <= 0)
            while (fontSize > 8 * scale)
            { using var candidate = CreateWheelFont(family, fontSize, bold); if (graphics.MeasureString(label, candidate, new SizeF(bounds.Width, 10000)).Height <= bounds.Height) break; fontSize -= .5f * scale; }
        using var font = CreateWheelFont(family, Math.Max(.5f, fontSize), bold);
        using var ink = new SolidBrush(colorText);
        if (effect == TextEffects.Shadow || effect == TextEffects.WordArt)
        { using var shadow = new SolidBrush(Color.FromArgb(130, Color.Black)); graphics.DrawString(label, font, shadow, Offset(bounds, 1, 1), format); }
        if (effect == TextEffects.Outline || effect == TextEffects.WordArt)
        { using var outline = new SolidBrush(IsLightColor(colorText) ? Color.Black : Color.White); foreach (var d in new[] { new Point(-1,0),new Point(1,0),new Point(0,-1),new Point(0,1) }) graphics.DrawString(label, font, outline, Offset(bounds,d.X,d.Y),format); }
        graphics.DrawString(label, font, ink, bounds, format);
        var measured = graphics.MeasureString(label, font, new SizeF(bounds.Width, 10000));
        _textDiagnostics.Add(new TextLayoutDiagnostic { Label = label, LogicalFontSizePx = font.Size / Math.Max(.01f, scale), FullText = measured.Height <= bounds.Height && measured.Width <= bounds.Width, FitsBounds = true, LineCount = Math.Max(1, (int)Math.Ceiling(measured.Height / font.GetHeight(graphics))) });
    }

    // 100% keeps the slot's own rectangle; anything else scales it about its centre.
    private static RectangleF ScaleCellBounds(RectangleF bounds, int cellSizePercent)
    {
        float factor = Math.Max(.4f, Math.Min(1.6f, (cellSizePercent > 0 ? cellSizePercent : 100) / 100f));
        if (Math.Abs(factor - 1f) < 0.001f) return bounds;
        float width = bounds.Width * factor;
        float height = bounds.Height * factor;
        return new RectangleF(bounds.Left + (bounds.Width - width) / 2, bounds.Top + (bounds.Height - height) / 2, width, height);
    }

    // The classic wheel keeps the original multi-ring sector look: full sectors,
    // hairline dividers between slots, inner/outer ring outlines, and a filled
    // centre disc. Routing it through the alternative-style renderer made it
    // read - and measure - like a smaller "01" wheel, which is the gap this fills.
    internal void DrawClassicWheel(Graphics graphics, bool includeHighlights, bool highlightsOnly)
    {
		if (highlightsOnly)
		{
			using var hover = new SolidBrush(Color.FromArgb(85, WheelHoverColor));
			using var selected = new SolidBrush(Color.FromArgb(155, WheelAccentColor));
			foreach (var cell in PreparedHitGeometry())
				if (!cell.Hit.IsCenter && cell.Hit.Equals(_selected)) graphics.FillPath(selected, cell.Path);
				else if (!cell.Hit.IsCenter && cell.Hit.Equals(_hover)) graphics.FillPath(hover, cell.Path);
			return;
		}
        RectangleF bounds = GetMenuBounds();
        var rings = _scene.Rings.Where(r => r.Enabled).OrderBy(r => r.Index).ToList();
        if (rings.Count == 0)
        {
            if (!highlightsOnly && DrawForeground) DrawCenter(graphics, bounds, includeHighlights);
            return;
        }
        PointF center = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
        List<RingLayout> layouts = BuildRingLayouts(bounds, rings);
        using Pen ringPen = new Pen(WheelLineColor, 1f);
        using Pen dividerPen = new Pen(WheelLineColor, 0.8f);
        using Brush hoverBrush = new SolidBrush(Color.FromArgb(85, WheelHoverColor));
        using Brush selectedBrush = new SolidBrush(Color.FromArgb(155, WheelAccentColor));
        foreach (RingLayout layout in layouts)
        {
            RingConfig ring = layout.Ring;
            float sweep = 360f / Math.Max(1, ring.Slots);
            if (!highlightsOnly && DrawForeground && DrawGuides) DrawCircle(graphics, ringPen, center, layout.InnerRadius);
            for (int slot = 0; slot < ring.Slots; slot++)
            {
                float start = -90f + slot * sweep;
                bool selected = includeHighlights && _selected.RingIndex == ring.Index && _selected.Slot == slot && ring.Index == _selected.RingIndex;
                bool hover = includeHighlights && _hover.RingIndex == ring.Index && _hover.Slot == slot;
                MenuItemConfig item = ring.Items.FirstOrDefault(it => it.Slot == slot);
                using GraphicsPath path = CreateSlotPath(center, layout.InnerRadius, layout.OuterRadius, start, sweep);
                if (!highlightsOnly && DrawBackdrop)
                {
                    using Brush surface = CreateSurfaceBrush(path.GetBounds());
                    graphics.FillPath(surface, path);
                    DrawClassicCellBackground(graphics, item, path);
                }
                if (selected) FillSlot(graphics, selectedBrush, center, layout.InnerRadius, layout.OuterRadius, start, sweep);
                else if (hover) FillSlot(graphics, hoverBrush, center, layout.InnerRadius, layout.OuterRadius, start, sweep);
                if (!highlightsOnly && DrawForeground && DrawGuides)
                {
                    graphics.DrawLine(dividerPen, PointOnCircle(center, layout.InnerRadius, start), PointOnCircle(center, layout.OuterRadius, start));
                }
                if (!highlightsOnly && DrawForeground && DrawLabels && item != null)
                {
                    // Content sits on the ring centre line, sized so adjacent slots
                    // never overlap - the same .78 factor the CAD wheel uses.
                    float radius = (layout.InnerRadius + layout.OuterRadius) / 2f;
                    float tangent = ring.Slots <= 2 ? layout.Width : 2f * radius * (float)Math.Sin(Math.PI / ring.Slots);
                    float side = Math.Max(1, Math.Min(layout.Width, tangent) * .78f);
                    PointF pt = PointOnCircle(center, radius, start + sweep / 2f);
                    RectangleF rect = new RectangleF(pt.X - side / 2, pt.Y - side / 2, side, side);
                    GraphicsState saved = graphics.Save();
                    graphics.SetClip(path, CombineMode.Intersect);
                    try { DrawStyledContent(graphics, item, rect, ring.Index); }
                    finally { graphics.Restore(saved); }
                }
            }
            if (!highlightsOnly && DrawForeground && DrawGuides) DrawCircle(graphics, ringPen, center, layout.OuterRadius);
        }
        if (!highlightsOnly && DrawForeground && (DrawGuides || DrawLabels)) DrawCenter(graphics, bounds, includeHighlights);
    }

    // A classic sector is allowed to carry the command's custom background
    // image, clipped to the sector and drawn at the menu's display scale.
    private void DrawClassicCellBackground(Graphics graphics, MenuItemConfig item, GraphicsPath clip)
    {
        if (item == null || _backgroundImage == null) return;
        GraphicsState saved = graphics.Save();
        try
        {
            graphics.SetClip(clip, CombineMode.Intersect);
            int alpha = Math.Max(0, Math.Min(100, _scene.BackgroundOpacityPercent));
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix { Matrix33 = alpha / 100f });
            graphics.DrawImage(_backgroundImage, Rectangle.Round(Cover(clip.GetBounds(), _backgroundImage.Size)),
                0, 0, _backgroundImage.Width, _backgroundImage.Height, GraphicsUnit.Pixel, attributes);
        }
        finally { graphics.Restore(saved); }
    }
}



