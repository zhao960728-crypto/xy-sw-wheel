using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace XY.SolidWorksPlugin;

internal static class WheelIconDrawing
{
    // Keep decoded pixels independent of the file/resource stream. Resampling
    // belongs to the final destination, not to fixed-size intermediate caches.
    internal static Bitmap CopySource(Image image)
    {
        float scale = Math.Min(1f, 256f / Math.Max(image.Width, image.Height));
        var copy = new Bitmap(Math.Max(1, (int)Math.Round(image.Width * scale)),
            Math.Max(1, (int)Math.Round(image.Height * scale)), PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(copy);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        Draw(graphics, image, new RectangleF(0, 0, copy.Width, copy.Height));
        return copy;
    }

    internal static void Draw(Graphics graphics, Image image, RectangleF bounds, ImageAttributes attributes = null)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var target = new Rectangle((int)Math.Round(bounds.X), (int)Math.Round(bounds.Y),
            Math.Max(1, (int)Math.Round(bounds.Width)), Math.Max(1, (int)Math.Round(bounds.Height)));
        var state = graphics.Save();
        try
        {
            // Explicit pixel source units also ignore PNG print-DPI metadata.
            // DrawImageUnscaled can otherwise crop a 72-DPI host resource.
            graphics.InterpolationMode = image.Width == target.Width && image.Height == target.Height
                ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            using var edge = attributes == null ? new ImageAttributes() : null;
            var effective = attributes ?? edge;
            effective.SetWrapMode(WrapMode.TileFlipXY);
            graphics.DrawImage(image, target, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, effective);
        }
        finally { graphics.Restore(state); }
    }
}
