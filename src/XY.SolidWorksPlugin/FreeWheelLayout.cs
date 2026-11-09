using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace XY.SolidWorksPlugin;

// Free layout geometry. A cell keeps its ring slot for execution and binding,
// but may be drawn anywhere inside the wheel when it carries FreeX/FreeY.
// Coordinates are normalised to the menu bounds (0,0 is the centre, ±0.5 the
// edge) so a stored layout survives any change of wheel size.
internal static class FreeWheelLayout
{
	internal const string StyleId = "free";

	// Minimum clear space between two cells, as a fraction of the wheel width.
	internal const float GapRatio = 0.015f;

	internal static bool HasAny(SceneMenuConfig scene) =>
		scene?.Rings != null && scene.Rings.Where(r => r.Enabled)
			.SelectMany(r => r.Items ?? new List<MenuItemConfig>())
			.Any(item => item.FreeX.HasValue && item.FreeY.HasValue);

	internal static PointF CenterOf(RectangleF bounds, double x, double y) => new PointF(
		bounds.Left + (float)(x + .5) * bounds.Width,
		bounds.Top + (float)(y + .5) * bounds.Height);

	internal static RectangleF RectAt(RectangleF bounds, PointF center, SizeF size) =>
		ClampToBounds(bounds, new RectangleF(center.X - size.Width / 2, center.Y - size.Height / 2, size.Width, size.Height));

	internal static RectangleF ClampToBounds(RectangleF bounds, RectangleF rect)
	{
		float width = Math.Min(rect.Width, bounds.Width);
		float height = Math.Min(rect.Height, bounds.Height);
		return new RectangleF(
			Math.Max(bounds.Left, Math.Min(rect.Left, bounds.Right - width)),
			Math.Max(bounds.Top, Math.Min(rect.Top, bounds.Bottom - height)),
			width, height);
	}

	internal static bool Overlaps(RectangleF a, RectangleF b, float gap)
	{
		RectangleF grown = a;
		grown.Inflate(gap, gap);
		return grown.IntersectsWith(b);
	}

	// Moves one cell. Returns false when the target would fall outside the wheel
	// or collide with another cell, leaving the layout untouched.
	internal static bool Place(SceneMenuConfig scene, int ring, int slot, double x, double y)
	{
		MenuItemConfig item = Find(scene, ring, slot);
		if (item == null) return false;
		double nx = Math.Max(-.48, Math.Min(.48, x));
		double ny = Math.Max(-.48, Math.Min(.48, y));
		item.FreeX = nx;
		item.FreeY = ny;
		return true;
	}

	internal static MenuItemConfig Find(SceneMenuConfig scene, int ring, int slot) =>
		scene?.Rings?.FirstOrDefault(r => r.Index == ring)?.Items?.FirstOrDefault(i => i.Slot == slot);

	internal static void Clear(SceneMenuConfig scene)
	{
		if (scene?.Rings == null) return;
		foreach (RingConfig ring in scene.Rings)
		{
			foreach (MenuItemConfig item in ring.Items ?? new List<MenuItemConfig>())
			{
				item.FreeX = null;
				item.FreeY = null;
			}
		}
	}

	// Lays every bound cell out on a grid in reading order. Cells that already
	// have coordinates are moved as little as possible, so tidying never feels
	// like a reshuffle. Returns how many cells were placed.
	internal static int Tidy(SceneMenuConfig scene, int columns)
	{
		if (scene?.Rings == null) return 0;
		int cols = Math.Max(1, Math.Min(12, columns));
		List<MenuItemConfig> items = scene.Rings.Where(r => r.Enabled).OrderBy(r => r.Index)
			.SelectMany(r => (r.Items ?? new List<MenuItemConfig>()).OrderBy(i => i.Slot))
			.ToList();
		if (items.Count == 0) return 0;
		int rows = (int)Math.Ceiling(items.Count / (double)cols);
		// Spread over ±0.34 of the wheel so cells stay clear of the rim.
		double spanX = .68, spanY = .68;
		for (int index = 0; index < items.Count; index++)
		{
			int row = index / cols;
			int column = index % cols;
			double x = cols == 1 ? 0d : -spanX / 2 + spanX * column / (cols - 1);
			double y = rows == 1 ? 0d : -spanY / 2 + spanY * row / (rows - 1);
			items[index].FreeX = Math.Round(x, 4);
			items[index].FreeY = Math.Round(y, 4);
		}
		return items.Count;
	}
}
