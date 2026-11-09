using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace XY.SolidWorksPlugin;

// The retained wheel, watermark and process artwork from the supplied source,
// adapted as independent XY assets with no dependency on the reference add-in.
internal static class UnifiedWheelArtwork
{
    private static readonly Dictionary<string, Bitmap> Images = new Dictionary<string, Bitmap>(StringComparer.Ordinal);
    internal static bool TryDraw(Graphics graphics, string action, RectangleF bounds)
    {
        if (action != "wheel" && action != "settings" && action != "watermark-settings" && action != "process-recognition" && action != "open-model-path" && action != "explode-all") return false;
        lock (Images)
        {
            int size = Math.Max(16, Math.Min(256, (int)Math.Round(Math.Min(bounds.Width, bounds.Height))));
            string key = action + "/" + size;
            if (!Images.TryGetValue(key, out var image)) Images[key] = image = Render(action, size);
            WheelIconDrawing.Draw(graphics, image, bounds);
        }
        return true;
    }

    internal static Bitmap Render(string action, int size)
    {
        if (size <= 0 || size > 1024) throw new ArgumentOutOfRangeException(nameof(size));
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.ScaleTransform(size / 32f, size / 32f);
        if (action == "watermark-settings")
        {
            using var pen = Pen(Color.FromArgb(28, 116, 211), 2);
            using var faint = new SolidBrush(Color.FromArgb(228, 240, 253));
            graphics.FillRectangle(faint, 4, 5, 24, 22); graphics.DrawRectangle(pen, 4, 5, 24, 22);
            graphics.DrawLine(pen, 8, 10, 23, 10); graphics.DrawLine(pen, 8, 16, 19, 16); graphics.DrawLine(pen, 8, 22, 23, 22);
        }
        else if (action == "wheel")
        {
            using var pen = Pen(Color.FromArgb(0, 102, 255), 2);
            graphics.DrawEllipse(pen, 3.2f, 3.2f, 25.6f, 25.6f);
            graphics.DrawEllipse(pen, 12.16f, 12.16f, 7.68f, 7.68f);
            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.PI / 4;
                graphics.DrawLine(pen, 16 + (float)Math.Cos(angle) * 3.84f, 16 + (float)Math.Sin(angle) * 3.84f,
                    16 + (float)Math.Cos(angle) * 12.8f, 16 + (float)Math.Sin(angle) * 12.8f);
            }
        }
        else
        {
            graphics.ScaleTransform(32f / 24, 32f / 24);
            using var pen = Pen(Color.FromArgb(0, 102, 255), 1.7f);
            if (action == "explode-all")
            {
                graphics.DrawRectangle(pen, 9, 9, 6, 6);
                graphics.DrawRectangle(pen, 1, 1, 5, 5); graphics.DrawLine(pen, 6, 6, 9, 9);
                graphics.DrawRectangle(pen, 18, 1, 5, 5); graphics.DrawLine(pen, 18, 6, 15, 9);
                graphics.DrawRectangle(pen, 1, 18, 5, 5); graphics.DrawLine(pen, 6, 18, 9, 15);
                graphics.DrawRectangle(pen, 18, 18, 5, 5); graphics.DrawLine(pen, 18, 18, 15, 15);
            }
            else if (action == "process-recognition")
            {
                for (int y = 5; y <= 19; y += 7) { graphics.DrawRectangle(pen, 2, y - 1, 2, 2); graphics.DrawLine(pen, 8, y, 22, y); }
            }
            else if (action == "open-model-path")
            {
                graphics.DrawLines(pen, new[] { new Point(2, 20), new Point(22, 20), new Point(22, 7), new Point(11, 7), new Point(8, 4), new Point(2, 4), new Point(2, 20) });
            }
            else
            {
                graphics.DrawEllipse(pen, 6, 6, 12, 12); graphics.DrawEllipse(pen, 10, 10, 4, 4);
                for (int i = 0; i < 8; i++)
                {
                    double angle = i * Math.PI / 4;
                    graphics.DrawLine(pen, (float)(12 + 7 * Math.Cos(angle)), (float)(12 + 7 * Math.Sin(angle)),
                        (float)(12 + 10 * Math.Cos(angle)), (float)(12 + 10 * Math.Sin(angle)));
                }
            }
        }
        return bitmap;
    }

    private static Pen Pen(Color color, float width) => new Pen(color, width)
    { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };

    internal static void Clear()
    {
        lock (Images) { foreach (var image in Images.Values) image.Dispose(); Images.Clear(); }
    }
}
