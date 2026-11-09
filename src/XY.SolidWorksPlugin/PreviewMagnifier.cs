using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

// A paint-only lens over the settings preview. It never captures the mouse and
// never touches hit testing, so it cannot interfere with dragging, resizing or
// dropping a cell - the very gestures it is meant to help with.
internal sealed partial class RadialMenuControl
{
    private Point _magnifierPointer;
    private bool _magnifierPointerActive;
    private bool _magnifierMouseDown;
    private int _magnifierCorner = -1;

    internal bool PreviewMagnifierEnabled { get; set; }

    internal Rectangle PreviewMagnifierBounds { get; private set; }

    private void DrawPreviewMagnifier(Graphics graphics)
    {
        PreviewMagnifierBounds = Rectangle.Empty;
        if (!PreviewMagnifierEnabled || !ScaleMenuForPreview || !EnableCellSelection) return;
        if (!_magnifierPointerActive || _magnifierMouseDown || !_hover.HasSlot) return;
        // Every editing gesture draws its own overlay; the lens would sit on
        // top of the insertion markers, the resize handles or a cell being
        // dragged, and in the free styles a cell is already as large as the
        // user made it.
        if (ShowInsertionPoints || BatchSelectionMode || ResizeMode || _cellDragArmed) return;
        if (_scene.StyleId == FreeWheelLayout.StyleId) return;
        float dpi = Math.Max(1f, DeviceDpi / 96f);
        int margin = Math.Max(4, (int)(8 * dpi));
        int width = Math.Min((int)(220 * dpi), ClientSize.Width - margin * 2);
        int height = Math.Min((int)(190 * dpi), ClientSize.Height - margin * 2);
        if (width < 60 || height < 60) return;
        // The lens sits in the corner the pointer is furthest from, so it never
        // covers the cell being inspected.
        int x = _magnifierPointer.X < ClientSize.Width / 2 ? ClientSize.Width - width - margin : margin;
        int y = _magnifierPointer.Y < ClientSize.Height / 2 ? ClientSize.Height - height - margin : margin;
        var bounds = new Rectangle(x, y, width, height);
        PreviewMagnifierBounds = bounds;
        using var background = new SolidBrush(Color.FromArgb(247, 250, 252));
        using var border = new Pen(WheelAccentColor, Math.Max(1f, dpi));
        graphics.FillRectangle(background, bounds);
        graphics.DrawRectangle(border, bounds);
        using var headingFont = new Font(Font.FontFamily, Math.Max(9f, 12f * dpi), FontStyle.Regular, GraphicsUnit.Pixel);
        int headingHeight = Math.Min(height / 3, (int)(28 * dpi));
        TextRenderer.DrawText(graphics, $"第 {_hover.RingIndex + 1} 圈 · 第 {_hover.Slot + 1} 格", headingFont,
            new Rectangle(x + margin, y + 3, width - margin * 2, headingHeight), WheelAccentColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        var content = new RectangleF(x + margin, y + headingHeight, width - margin * 2, height - headingHeight - margin);
        if (content.Width <= 2 || content.Height <= 2) return;
        // A detached copy keeps the lens from writing anything back, and its
        // explicit size is what makes the text read at lens scale instead of
        // inheriting the preview's smaller one.
        MenuItemConfig? item = FindItem(_hover);
        float scale = GetMenuBounds().Width / Math.Max(1f, EffectiveMenuSizePx());
        var enlarged = item == null
            ? new MenuItemConfig { Label = "空格", ContentMode = "TextOnly" }
            : new MenuItemConfig
            {
                Label = item.Label, IconId = item.IconId, IconPath = item.IconPath, IconData = item.IconData,
                ImageMode = item.ImageMode, IconRatioPercent = item.IconRatioPercent, Kind = item.Kind, Value = item.Value,
                FontFamily = item.FontFamily, FontName = item.FontName, FontBold = item.FontBold,
                ContentMode = "IconAndText",
                FontSizePx = (int)Math.Round(Math.Max(10f, content.Height / 6f) / Math.Max(.05f, scale))
            };
        if (item == null) enlarged.FontSizePx = (int)Math.Round(Math.Max(10f, content.Height / 6f) / Math.Max(.05f, scale));
        var state = graphics.Save();
        graphics.SetClip(content, CombineMode.Intersect);
        DrawStyledContent(graphics, enlarged, content, _hover.RingIndex);
        graphics.Restore(state);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _magnifierPointerActive = false;
        if (PreviewMagnifierEnabled) Invalidate();
    }
}
