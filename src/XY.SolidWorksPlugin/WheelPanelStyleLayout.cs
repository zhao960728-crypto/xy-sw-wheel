using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace XY.SolidWorksPlugin;

internal sealed partial class RadialMenuControl
{
    // Adapted from the reference panel geometry: rectangular rows and staggered
    // point-up hexagons share the same cells for paint, selection and preview.
    // Ring/slot identities remain unchanged when the display is rearranged.
    private List<StyleCell> BuildPanelStyleCells(RectangleF bounds, List<RingConfig> rings, bool honeycomb)
    {
        var bindings = rings.SelectMany(ring => Enumerable.Range(0, Math.Max(0, ring.Slots))
            .Select(slot => new { Ring = ring, Slot = slot, Item = ring.Items.FirstOrDefault(item => item.Slot == slot) })).ToList();
        int count = bindings.Count + 1;
        int columns = Math.Max(1, Math.Min(count, (int)Math.Ceiling(Math.Sqrt(count * (honeycomb ? 1f : .85f)))));
        const float height = 80f, gap = 8f;
        float width = honeycomb ? height * (float)Math.Sqrt(3) / 2f : 104f;
        float pitchX = width + gap, pitchY = (honeycomb ? height * .75f : height) + gap;
        var positions = Enumerable.Range(0, count).Select(index => new PointF(
            (index % columns + (honeycomb ? index / columns % 2 * .5f : 0)) * pitchX,
            index / columns * pitchY)).ToArray();
        float middleX = (positions.Min(point => point.X) + positions.Max(point => point.X)) / 2f;
        float middleY = (positions.Min(point => point.Y) + positions.Max(point => point.Y)) / 2f;
        int cancelIndex = Enumerable.Range(0, count).OrderBy(index =>
            Math.Pow(positions[index].X - middleX, 2) + Math.Pow(positions[index].Y - middleY, 2)).First();
        PointF origin = positions[cancelIndex];
        float extentX = positions.Max(point => Math.Abs(point.X - origin.X)) + width / 2f;
        float extentY = positions.Max(point => Math.Abs(point.Y - origin.Y)) + height / 2f;
        float scale = Math.Min(bounds.Width * .46f / extentX, bounds.Height * .46f / extentY);
        var center = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
        var cells = new List<StyleCell>();
        int at = 0;
        for (int index = 0; index < count; index++)
        {
            bool cancel = index == cancelIndex;
            var binding = cancel ? null : bindings[at++];
            var point = positions[index];
            var rect = new RectangleF(center.X + (point.X - origin.X - width / 2f) * scale,
                center.Y + (point.Y - origin.Y - height / 2f) * scale, width * scale, height * scale);
            var cell = new StyleCell
            {
                Hit = cancel ? HitResult.Center : new HitResult(binding!.Ring.Index, binding.Slot, false),
                Item = binding?.Item, Content = rect, Label = cancel ? "取消" : ""
            };
            AddCellShape(cell.Path, rect, honeycomb ? "Hexagon" : "Rounded");
            cell.Content.Inflate(-rect.Width * (honeycomb ? .16f : .07f), -rect.Height * (honeycomb ? .16f : .07f));
            cells.Add(cell);
        }
        ApplyCellSizes(cells);
        ApplyFreePositions(cells, bounds);
        return cells;
    }
}
