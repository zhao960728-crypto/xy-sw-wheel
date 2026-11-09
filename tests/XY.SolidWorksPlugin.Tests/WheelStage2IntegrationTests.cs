using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class WheelStage2IntegrationTests
{
    [Theory]
    [InlineData("classic", false, false)]
    [InlineData("01", false, false)]
    [InlineData("grid", false, false)]
    [InlineData("honeycomb", true, false)]
    [InlineData("01", false, true)]
    public void ExactNativeArtworkWinsOverAutomaticIconAndKeepsExplicitChoice(string style, bool basic, bool explicitCatalogue) => Sta(() =>
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var field = typeof(RadialCommandIcons).GetField("_resources", flags)!;
        var sync = typeof(RadialCommandIcons).GetField("Sync", flags)!.GetValue(null)!;
        Assert.True(NativeWheelIconMappings.TryGetBasicCommandId("view-front", out int id));
        lock (sync)
        {
            object previous = field.GetValue(null)!;
            using var native = new Bitmap(32, 32);
            using (var graphics = Graphics.FromImage(native)) graphics.Clear(Color.Red);
            try
            {
                field.SetValue(null, new Dictionary<int, Bitmap> { [id] = native });
                var scene = Scene(style, 1);
                var item = scene.Rings[0].Items[0];
                item.Kind = basic ? ActionKinds.Basic : ActionKinds.NativeCommand;
                item.Value = basic ? "view-front" : NativeCommandReference.Encode(id);
                item.Label = "用户改过名字";
                item.IconId = "front";
                if (explicitCatalogue) item.IconData = WheelIconPreference.ExplicitCatalogueData(item.IconId);
                scene.ContentMode = "IconOnly";
                using var wheel = new RadialMenuControl(Color.Fuchsia) { Size = new Size(520, 520) };
                wheel.SetMenu(new PluginConfig(), scene);
                using var bitmap = Render(wheel);
                int originalPixels = Count(bitmap, color => color.R > 240 && color.G < 15 && color.B < 15);
                if (explicitCatalogue) Assert.Equal(0, originalPixels);
                else Assert.True(originalPixels > 100);
                Assert.Equal("front", item.IconId);
                Assert.Equal("用户改过名字", item.Label);
            }
            finally { field.SetValue(null, previous); }
        }
    });

    [Fact]
    public void ExplicitImportedImageRemainsSelectableOverNativeDefault() => Sta(() =>
    {
        string path = Path.Combine(Path.GetTempPath(), "xy-stage2-icon-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (var image = new Bitmap(32, 32))
            { using var graphics = Graphics.FromImage(image); graphics.Clear(Color.Blue); image.Save(path, ImageFormat.Png); }
            var scene = Scene("01", 1); scene.ContentMode = "IconOnly";
            var item = scene.Rings[0].Items[0];
            item.Kind = ActionKinds.NativeCommand; item.Value = NativeCommandReference.Encode(39);
            item.IconId = "front"; item.IconPath = path;
            using var wheel = new RadialMenuControl(Color.Fuchsia) { Size = new Size(520, 520) };
            wheel.SetMenu(new PluginConfig(), scene);
            using var bitmap = Render(wheel);
            Assert.True(Count(bitmap, color => color.B > 240 && color.R < 15 && color.G < 15) > 100);
        }
        finally { File.Delete(path); }
    });

    [Theory]
    [InlineData("grid", 520, false)]
    [InlineData("grid", 360, true)]
    [InlineData("honeycomb", 520, false)]
    [InlineData("honeycomb", 360, true)]
    public void PanelStylesRenderAndSelectEveryOriginalRingSlotWithSafeCentre(string style, int size, bool preview) => Sta(() =>
    {
        var scene = Scene(style, 3, 4);
        using var wheel = new RadialMenuControl(Color.Fuchsia)
        { Size = new Size(size, size), ScaleMenuForPreview = preview, EnableCellSelection = preview };
        wheel.SetMenu(new PluginConfig(), scene);
        using var bitmap = Render(wheel);
        Assert.True(Count(bitmap, color => color.ToArgb() != Color.Fuchsia.ToArgb()) > 1000);
        MenuItemConfig? selected = null; int cancelled = 0;
        wheel.ItemSelected += (_, item) => selected = item;
        wheel.CancelRequested += (_, __) => cancelled++;
        var found = new Dictionary<(int, int), Point>();
        for (int y = 1; y < size; y += 4) for (int x = 1; x < size; x += 4)
        {
            var hit = wheel.HitCell(new Point(x, y));
            if (hit != null) found[(hit.RingIndex, hit.Slot)] = new Point(x, y);
        }
        Assert.Equal(7, found.Count);
        foreach (var ring in scene.Rings) foreach (var item in ring.Items)
        {
            Assert.True(found.TryGetValue((ring.Index, item.Slot), out var point));
            if (preview) wheel.PracticeMode = true;
            selected = null; wheel.CommitAt(point);
            Assert.Same(item, selected);
        }
        selected = null; wheel.CommitAt(new Point(size / 2, size / 2));
        Assert.Null(selected); Assert.Equal(1, cancelled);
        if (style == "honeycomb")
        {
            var cells = (IList)typeof(RadialMenuControl).GetMethod("BuildStyledCells", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(wheel, null)!;
            try
            {
                var first = cells[0]!;
                var path = (GraphicsPath)first.GetType().GetField("Path")!.GetValue(first)!;
                RectangleF rect = path.GetBounds();
                Assert.False(path.IsVisible(rect.Left + 1, rect.Top + 1), "Hexagon corners must not select a rectangular hit target.");
            }
            finally { foreach (IDisposable cell in cells) cell.Dispose(); }
        }
        Capture(bitmap, "stage2-" + style + "-" + size + ".png");
    });

    private static SceneMenuConfig Scene(string style, params int[] counts) => new SceneMenuConfig
    {
        Key = SceneKeys.Part, StyleId = style, MenuSizePx = 520, ContentMode = "IconAndText",
        Rings = counts.Select((count, index) => new RingConfig
        {
            Index = index, Slots = count, Enabled = true, DiameterPx = 520,
            Items = Enumerable.Range(0, count).Select(slot => new MenuItemConfig
            { Slot = slot, Kind = ActionKinds.Hotkey, Value = "Ctrl+" + slot, Label = "功能 " + index + ":" + slot, IconId = "front" }).ToList()
        }).ToList()
    };

    private static Bitmap Render(RadialMenuControl wheel)
    {
        var bitmap = new Bitmap(wheel.Width, wheel.Height);
        wheel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        return bitmap;
    }

    private static int Count(Bitmap bitmap, Func<Color, bool> predicate)
    {
        int count = 0;
        for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++) if (predicate(bitmap.GetPixel(x, y))) count++;
        return count;
    }

    private static void Capture(Bitmap bitmap, string file)
    {
        string? directory = Environment.GetEnvironmentVariable("XY_UI_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); bitmap.Save(Path.Combine(directory, file), ImageFormat.Png);
    }

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(15000));
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
