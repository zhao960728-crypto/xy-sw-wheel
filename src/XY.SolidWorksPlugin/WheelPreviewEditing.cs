using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

// Preview-only editing. Every entry point here requires EnableCellSelection,
// which only the settings preview turns on, so the live gesture wheel never
// mutates the user's layout. The CAD build drives exactly the same operations
// from its own settings window; this is the SolidWorks side of that contract.
internal sealed partial class RadialMenuControl
{
	// The alternative styles are the ones where a cell owns a rectangle of its
	// own: a size, a shape, and - in "free" - a position. Wedge slots and the
	// classic wheel are laid out by the ring geometry instead.
	private bool IsAlternativeStyle =>
		_scene.StyleId == "11" || _scene.StyleId == "06" || _scene.StyleId == "orbit" || _scene.StyleId == "free"
		|| _scene.StyleId == "grid" || _scene.StyleId == "honeycomb";

	internal HashSet<(int Ring, int Slot)> BatchSelection { get; } = new HashSet<(int Ring, int Slot)>();

	internal bool BatchSelectionMode { get; set; }

	// Practice mode freezes editing: the wheel is still drawn and hovered, but
	// nothing here writes into the draft.
	internal bool PracticeMode { get; set; }

	internal bool ShowInsertionPoints { get; set; }

	private bool _resizeMode;
	private int _wheelResizeRemainder;
	private HitResult _wheelResizeTarget = HitResult.None;

	internal bool ResizeMode
	{
		get => _resizeMode;
		set { _resizeMode = value; _wheelResizeRemainder = 0; _wheelResizeTarget = HitResult.None; }
	}

	internal event EventHandler? CellGeometryChanged;

	internal void EndGeometryEditing()
	{
		CancelGeometryDrag();
		ResizeMode = false;
		BatchSelectionMode = false;
		BatchSelection.Clear();
		ShowInsertionPoints = false;
		Capture = false;
		Invalidate();
	}

	// --- insertion points -------------------------------------------------

	internal List<(int Ring, int Slot, PointF Point)> CellInsertionPoints()
	{
		var result = new List<(int Ring, int Slot, PointF Point)>();
		RectangleF bounds = GetMenuBounds();
		var center = new PointF(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
		List<RingConfig> rings = _scene.Rings.Where(r => r.Enabled && r.Slots < 32).OrderBy(r => r.Index).ToList();
		if (!IsAlternativeStyle)
		{
			foreach (RingLayout layout in BuildRingLayouts(bounds, _scene.Rings.Where(r => r.Enabled).OrderBy(r => r.Index).ToList()))
			{
				if (layout.Ring.Slots >= 32) continue;
				for (int slot = 0; slot < layout.Ring.Slots; slot++)
				{
					result.Add((layout.Ring.Index, slot, PointOnCircle(center, (layout.InnerRadius + layout.OuterRadius) / 2,
						-90 + (slot - (_scene.StyleId == "01" ? .5f : 0)) * 360f / layout.Ring.Slots)));
				}
			}
			return result;
		}
		List<StyleCell> cells = BuildStyledCells();
		try
		{
			foreach (RingConfig ring in rings)
			{
				List<StyleCell> row = cells.Where(c => c.Hit.HasSlot && c.Hit.RingIndex == ring.Index).OrderBy(c => c.Hit.Slot).ToList();
				for (int i = 0; i < row.Count; i++)
				{
					RectangleF previous = CellRect(row[(i + row.Count - 1) % row.Count]);
					RectangleF current = CellRect(row[i]);
					var a = new PointF(previous.Left + previous.Width / 2, previous.Top + previous.Height / 2);
					var b = new PointF(current.Left + current.Width / 2, current.Top + current.Height / 2);
					var point = new PointF((a.X + b.X) / 2, (a.Y + b.Y) / 2);
					if (row.Count == 1) point = new PointF(current.Right + 10, b.Y);
					else if (_scene.StyleId == "orbit" || _scene.StyleId == "06")
					{
						double ra = Math.Sqrt((a.X - center.X) * (a.X - center.X) + (a.Y - center.Y) * (a.Y - center.Y));
						double rb = Math.Sqrt((b.X - center.X) * (b.X - center.X) + (b.Y - center.Y) * (b.Y - center.Y));
						if (Math.Abs(ra - rb) < 2)
						{
							double angleA = Math.Atan2(a.Y - center.Y, a.X - center.X) * 180 / Math.PI;
							double angleB = Math.Atan2(b.Y - center.Y, b.X - center.X) * 180 / Math.PI;
							double sweep = (angleB - angleA + 360) % 360;
							point = PointOnCircle(center, (float)((ra + rb) / 2), (float)(angleA + sweep / 2));
						}
					}
					// Do not offer an insertion on top of another cell. The
					// neutral hub is not a cell: in the free styles it covers
					// most of the disc, and cells are meant to sit on it.
					if (cells.Any(c => c.Hit.HasSlot && CellRect(c).Contains(point))) continue;
					// Two gaps can land on the same spot - the wrap-around gap
					// of a grid layout meets the middle one - and one marker
					// there is all the user can act on.
					if (result.Any(r => Math.Abs(r.Point.X - point.X) < 1f && Math.Abs(r.Point.Y - point.Y) < 1f)) continue;
					if (ClientRectangle.Contains(Point.Round(point))) result.Add((ring.Index, row[i].Hit.Slot, point));
				}
			}
		}
		finally { foreach (StyleCell cell in cells) cell.Dispose(); }
		return result;
	}

	internal RadialCellSelectedEventArgs? HitInsertion(Point point)
	{
		float best = 14f * DeviceDpi / 96f;
		double distance = best * best;
		RadialCellSelectedEventArgs? result = null;
		foreach ((int Ring, int Slot, PointF Point) gap in CellInsertionPoints())
		{
			double d = (gap.Point.X - point.X) * (gap.Point.X - point.X) + (gap.Point.Y - point.Y) * (gap.Point.Y - point.Y);
			if (d > distance) continue;
			distance = d;
			result = new RadialCellSelectedEventArgs(gap.Ring, gap.Slot);
		}
		return result;
	}

	internal RadialCellSelectedEventArgs? HitCell(Point point)
	{
		HitResult hit = HitTest(point);
		return hit.HasSlot ? new RadialCellSelectedEventArgs(hit.RingIndex, hit.Slot) : null;
	}

	internal void DrawInsertionPoints(Graphics graphics)
	{
		if (!ShowInsertionPoints) return;
		using var fill = new SolidBrush(Color.FromArgb(0, 134, 153));
		using var pen = new Pen(Color.White, 1.5f);
		foreach ((int Ring, int Slot, PointF Point) gap in CellInsertionPoints())
		{
			float x = gap.Point.X, y = gap.Point.Y;
			graphics.FillEllipse(fill, x - 6, y - 6, 12, 12);
			graphics.DrawLine(pen, x - 3, y, x + 3, y);
			graphics.DrawLine(pen, x, y - 3, x, y + 3);
		}
	}

	// --- cell size --------------------------------------------------------

	// The stored range is the one ConfigStore already normalises to, so a size
	// set here survives a save/load round trip unchanged.
	private static int ClampCellSizePercent(int percent) => Math.Max(40, Math.Min(160, percent));

	internal bool TrySetCellSize(int ring, int slot, int percent)
	{
		if (!IsAlternativeStyle || PracticeMode) return false;
		percent = ClampCellSizePercent(percent);
		RectangleF bounds = GetMenuBounds();
		List<StyleCell> cells = BuildStyledCells();
		try
		{
			StyleCell? cell = cells.FirstOrDefault(c => c.Hit.HasSlot && c.Hit.RingIndex == ring && c.Hit.Slot == slot);
			if (cell == null) return false;
			int current = ClampCellSizePercent(cell.Item?.CellSizePercent ?? 100);
			if (percent == current) return false;
			RectangleF rect = CellRect(cell);
			float factor = percent / (float)current;
			var proposed = new RectangleF(rect.X + rect.Width * (1 - factor) / 2, rect.Y + rect.Height * (1 - factor) / 2,
				rect.Width * factor, rect.Height * factor);
			float gap = bounds.Width * FreeWheelLayout.GapRatio;
			RectangleF limit = bounds;
			limit.Inflate(rect.Width * .02f, rect.Height * .02f);
			if (!limit.Contains(proposed)) return false;
			if (!HasClearance(proposed, Occupied(cells, cell), gap)) return false;
			MenuItemConfig? item = cell.Item ?? EnsureGeometryItem(ring, slot);
			if (item == null) return false;
			item.CellSizePercent = percent;
		}
		finally { foreach (StyleCell cell in cells) cell.Dispose(); }
		Invalidate();
		CellGeometryChanged?.Invoke(this, EventArgs.Empty);
		return true;
	}

	// --- free placement ---------------------------------------------------

	internal bool TryPlaceCell(int ring, int slot, Point clientPoint)
	{
		if (_scene.StyleId != FreeWheelLayout.StyleId || PracticeMode) return false;
		RectangleF bounds = GetMenuBounds();
		List<StyleCell> cells = BuildStyledCells();
		try
		{
			StyleCell? cell = cells.FirstOrDefault(c => c.Hit.HasSlot && c.Hit.RingIndex == ring && c.Hit.Slot == slot);
			if (cell == null) return false;
			RectangleF rect = CellRect(cell);
			var proposed = new RectangleF(clientPoint.X - rect.Width / 2, clientPoint.Y - rect.Height / 2, rect.Width, rect.Height);
			float gap = bounds.Width * FreeWheelLayout.GapRatio;
			if (!bounds.Contains(proposed)) return false;
			if (!HasClearance(proposed, Occupied(cells, cell), gap)) return false;
			if (EnsureGeometryItem(ring, slot) == null) return false;
			double x = (proposed.Left + proposed.Width / 2 - bounds.Left) / bounds.Width - .5;
			double y = (proposed.Top + proposed.Height / 2 - bounds.Top) / bounds.Height - .5;
			if (!FreeWheelLayout.Place(_scene, ring, slot, x, y)) return false;
		}
		finally { foreach (StyleCell cell in cells) cell.Dispose(); }
		Invalidate();
		CellGeometryChanged?.Invoke(this, EventArgs.Empty);
		return true;
	}

	// Rounds every placed cell onto a four-logical-pixel lattice. Each axis
	// moves by at most two pixels, so the overall pattern the user arranged
	// survives instead of being re-packed.
	internal bool NudgeFreeLayout()
	{
		if (_scene.StyleId != FreeWheelLayout.StyleId || PracticeMode) return false;
		RectangleF bounds = GetMenuBounds();
		List<StyleCell> cells = BuildStyledCells();
		bool changed = false;
		try
		{
			List<StyleCell> slots = cells.Where(c => c.Hit.HasSlot).ToList();
			List<RectangleF> occupied = slots.Select(CellRect).ToList();
			float grid = 4f * bounds.Width / Math.Max(1, EffectiveMenuSizePx());
			float gap = bounds.Width * FreeWheelLayout.GapRatio;
			if (grid <= 0) return false;
			for (int i = 0; i < slots.Count; i++)
			{
				StyleCell cell = slots[i];
				if (cell.Item == null || !cell.Item.FreeX.HasValue || !cell.Item.FreeY.HasValue) continue;
				RectangleF rect = occupied[i];
				float x = rect.Left + rect.Width / 2, y = rect.Top + rect.Height / 2;
				float dx = (float)Math.Round((x - bounds.Left) / grid) * grid + bounds.Left - x;
				float dy = (float)Math.Round((y - bounds.Top) / grid) * grid + bounds.Top - y;
				foreach (PointF delta in new[] { new PointF(dx, dy), new PointF(dx, 0), new PointF(0, dy) })
				{
					RectangleF proposed = rect;
					proposed.Offset(delta);
					if (!bounds.Contains(proposed)) continue;
					var others = new List<RectangleF>(occupied);
					others.RemoveAt(i);
					if (!HasClearance(proposed, others, gap)) continue;
					double nx = (proposed.Left + proposed.Width / 2 - bounds.Left) / bounds.Width - .5;
					double ny = (proposed.Top + proposed.Height / 2 - bounds.Top) / bounds.Height - .5;
					if (!FreeWheelLayout.Place(_scene, cell.Hit.RingIndex, cell.Hit.Slot, nx, ny)) continue;
					occupied[i] = proposed;
					changed = true;
					break;
				}
			}
		}
		finally { foreach (StyleCell cell in cells) cell.Dispose(); }
		if (!changed) return false;
		Invalidate();
		CellGeometryChanged?.Invoke(this, EventArgs.Empty);
		return true;
	}

	// --- geometry drag ----------------------------------------------------

	private HitResult _geometryDrag = HitResult.None;
	private bool _geometryResize;
	private Point _geometryStart;
	private RectangleF _geometryBounds;
	private int _geometryStartSize;
	private double? _geometryUndoX, _geometryUndoY;
	private int _geometryUndoSize;

	private bool BeginGeometryDrag(MouseEventArgs e)
	{
		if (!EnableCellSelection || e.Button != MouseButtons.Left || PracticeMode) return false;
		HitResult hit = HitTest(e.Location);
		if (BatchSelectionMode)
		{
			if (hit.HasSlot)
			{
				var key = (hit.RingIndex, hit.Slot);
				if (!BatchSelection.Add(key)) BatchSelection.Remove(key);
			}
			SelectCellAt(e.Location);
			Invalidate();
			return true;
		}
		if (!IsAlternativeStyle || (!ResizeMode && _scene.StyleId != FreeWheelLayout.StyleId)) return false;
		if (ResizeMode && CanFocus) Focus();
		List<StyleCell> cells = BuildStyledCells();
		try
		{
			StyleCell? selected = cells.FirstOrDefault(c => c.Hit.Equals(_selected));
			bool handle = ResizeMode && selected != null && ResizeHandles(CellRect(selected)).Any(r => r.Contains(e.Location));
			if (!handle) SelectCellAt(e.Location);
			StyleCell? cell = cells.FirstOrDefault(c => c.Hit.Equals(handle ? _selected : HitTest(e.Location)));
			if (cell == null) return true;
			_geometryBounds = CellRect(cell);
			_geometryStartSize = ClampCellSizePercent(cell.Item?.CellSizePercent ?? 100);
			if (ResizeMode && !handle) { Invalidate(); return true; }
			_geometryDrag = cell.Hit;
			_geometryResize = handle;
			_geometryStart = e.Location;
			_geometryUndoX = cell.Item?.FreeX;
			_geometryUndoY = cell.Item?.FreeY;
			_geometryUndoSize = _geometryStartSize;
		}
		finally { foreach (StyleCell cell in cells) cell.Dispose(); }
		Capture = true;
		return true;
	}

	private bool MoveGeometryDrag(MouseEventArgs e)
	{
		if (!_geometryDrag.HasSlot) return false;
		if (_geometryResize)
		{
			var center = new PointF(_geometryBounds.Left + _geometryBounds.Width / 2, _geometryBounds.Top + _geometryBounds.Height / 2);
			float side = 2 * Math.Max(Math.Abs(e.X - center.X), Math.Abs(e.Y - center.Y));
			TrySetCellSize(_geometryDrag.RingIndex, _geometryDrag.Slot,
				(int)Math.Round(_geometryStartSize * side / Math.Max(1f, _geometryBounds.Width)));
			return true;
		}
		RectangleF proposed = _geometryBounds;
		proposed.Offset(e.X - _geometryStart.X, e.Y - _geometryStart.Y);
		RectangleF bounds = GetMenuBounds();
		if (!bounds.Contains(proposed)) return true;
		List<StyleCell> cells = BuildStyledCells();
		try
		{
			StyleCell? cell = cells.FirstOrDefault(c => c.Hit.Equals(_geometryDrag));
			if (cell == null) return true;
			if (!HasClearance(proposed, Occupied(cells, cell), bounds.Width * FreeWheelLayout.GapRatio)) return true;
			if (EnsureGeometryItem(_geometryDrag.RingIndex, _geometryDrag.Slot) == null) return true;
			double x = (proposed.Left + proposed.Width / 2 - bounds.Left) / bounds.Width - .5;
			double y = (proposed.Top + proposed.Height / 2 - bounds.Top) / bounds.Height - .5;
			if (!FreeWheelLayout.Place(_scene, _geometryDrag.RingIndex, _geometryDrag.Slot, x, y)) return true;
		}
		finally { foreach (StyleCell cell in cells) cell.Dispose(); }
		Invalidate();
		CellGeometryChanged?.Invoke(this, EventArgs.Empty);
		return true;
	}

	private bool EndGeometryDrag()
	{
		if (!_geometryDrag.HasSlot) return false;
		_geometryDrag = HitResult.None;
		_geometryResize = false;
		Capture = false;
		Invalidate();
		return true;
	}

	private bool CancelGeometryDrag()
	{
		if (!_geometryDrag.HasSlot) return false;
		if (_scene.StyleId == FreeWheelLayout.StyleId && _geometryUndoX.HasValue && _geometryUndoY.HasValue)
			FreeWheelLayout.Place(_scene, _geometryDrag.RingIndex, _geometryDrag.Slot, _geometryUndoX.Value, _geometryUndoY.Value);
		MenuItemConfig? item = FindItem(_geometryDrag);
		if (item != null) item.CellSizePercent = _geometryUndoSize;
		_geometryDrag = HitResult.None;
		_geometryResize = false;
		Capture = false;
		Invalidate();
		CellGeometryChanged?.Invoke(this, EventArgs.Empty);
		return true;
	}

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		if (!EnableCellSelection || !ResizeMode || BatchSelectionMode || PracticeMode || !IsAlternativeStyle
			|| !_selected.HasSlot || !ClientRectangle.Contains(e.Location))
		{
			_wheelResizeRemainder = 0;
			base.OnMouseWheel(e);
			return;
		}
		// Only the preview's explicit resize gesture is consumed; wheel input
		// anywhere else keeps scrolling, and the live wheel never edits data.
		if (e is HandledMouseEventArgs handled) handled.Handled = true;
		if (_geometryDrag.HasSlot) return;
		if (!_wheelResizeTarget.Equals(_selected))
		{
			_wheelResizeTarget = _selected;
			_wheelResizeRemainder = 0;
		}
		_wheelResizeRemainder += e.Delta;
		int steps = _wheelResizeRemainder / SystemInformation.MouseWheelScrollDelta;
		_wheelResizeRemainder %= SystemInformation.MouseWheelScrollDelta;
		if (steps == 0) return;
		MenuItemConfig? item = FindItem(_selected);
		int percent = ClampCellSizePercent((item?.CellSizePercent ?? 100) + steps * 5);
		if (percent == (item?.CellSizePercent ?? 100)) return;
		TrySetCellSize(_selected.RingIndex, _selected.Slot, percent);
	}

	internal void DrawGeometryHandles(Graphics graphics)
	{
		if (!EnableCellSelection || !ResizeMode || !IsAlternativeStyle || BatchSelectionMode || PracticeMode) return;
		List<StyleCell> cells = BuildStyledCells();
		try
		{
			StyleCell? cell = cells.FirstOrDefault(c => c.Hit.Equals(_selected));
			if (cell == null) return;
			using var pen = new Pen(Color.DeepSkyBlue, 1.5f);
			RectangleF b = CellRect(cell);
			graphics.DrawRectangle(pen, b.X, b.Y, b.Width, b.Height);
			foreach (RectangleF handle in ResizeHandles(b))
			{
				graphics.FillRectangle(Brushes.White, handle);
				graphics.DrawRectangle(pen, handle.X, handle.Y, handle.Width, handle.Height);
			}
		}
		finally { foreach (StyleCell cell in cells) cell.Dispose(); }
	}

	private static RectangleF[] ResizeHandles(RectangleF b) => new[]
	{
		new RectangleF(b.Left - 4, b.Top - 4, 8, 8), new RectangleF(b.Right - 4, b.Top - 4, 8, 8),
		new RectangleF(b.Left - 4, b.Bottom - 4, 8, 8), new RectangleF(b.Right - 4, b.Bottom - 4, 8, 8)
	};

	// --- shared geometry helpers -----------------------------------------

	// A cell is drawn by its path; the content rectangle is the caption box
	// inside it. Layout decisions use the visible outline.
	private static RectangleF CellRect(StyleCell cell)
	{
		if (cell.Path.PointCount > 0)
		{
			try { return cell.Path.GetBounds(); }
			catch (OutOfMemoryException) { }
		}
		return cell.Content;
	}

	private static List<RectangleF> Occupied(List<StyleCell> cells, StyleCell? except)
	{
		var result = new List<RectangleF>();
		foreach (StyleCell cell in cells)
		{
			if (cell == except || !cell.Hit.HasSlot) continue;
			result.Add(CellRect(cell));
		}
		return result;
	}

	// A half-gap of slack: normalised coordinates round through single
	// precision, so an exact gap must not become a collision after a reload.
	private static bool HasClearance(RectangleF proposed, List<RectangleF> others, float gap)
	{
		float padding = Math.Max(0f, gap) / 2f;
		RectangleF inflated = proposed;
		inflated.Inflate(padding, padding);
		foreach (RectangleF other in others)
		{
			RectangleF grown = other;
			grown.Inflate(padding, padding);
			if (grown.IntersectsWith(inflated)) return false;
		}
		return true;
	}

	private MenuItemConfig? EnsureGeometryItem(int ring, int slot)
	{
		RingConfig? group = _scene.Rings.FirstOrDefault(r => r.Index == ring);
		if (group == null || slot < 0 || slot >= group.Slots) return null;
		MenuItemConfig? item = group.Items.FirstOrDefault(i => i.Slot == slot);
		if (item == null)
		{
			item = new MenuItemConfig { Slot = slot };
			group.Items.Add(item);
		}
		return item;
	}
}
