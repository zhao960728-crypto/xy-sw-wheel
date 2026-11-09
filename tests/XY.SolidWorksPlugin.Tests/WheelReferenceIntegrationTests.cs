using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class WheelReferenceIntegrationTests
{
    [Fact]
    public void PointerMotionReusesGeometryAndLayoutEditsChangeSelection() => Sta(() =>
    {
        var scene = Scene();
        using var wheel = new RadialMenuControl(Color.White) { Size = new Size(520, 520) };
        wheel.SetMenu(new PluginConfig(), scene);
        for (int i = 0; i < 100; i++) wheel.UpdateCursor(new Point(260 + i % 8, 50));
        Assert.Equal(1, wheel.HitGeometryBuildCount);
        MenuItemConfig? selected = null;
        wheel.ItemSelected += (_, item) => selected = item;
        wheel.CommitAt(new Point(260, 50));
        Assert.Same(scene.Rings[0].Items[0], selected);
        scene.Rings[0].Enabled = false;
        selected = null;
        wheel.CommitAt(new Point(260, 50));
        Assert.Null(selected);
        Assert.Equal(2, wheel.HitGeometryBuildCount);
    });

    [Fact]
    public void ForegroundKeepsPreparedFrameUntilVisibleContentChanges() => Sta(() =>
    {
        var config = new PluginConfig(); var scene = Scene();
        using var wheel = new RadialMenuControl(Color.White)
        { Size = new Size(520, 520), DrawHover = false, DrawBackdrop = false, DrawGuides = false, DrawLabels = true };
        wheel.SetMenu(config, scene);
        wheel.WarmRenderCache();
        wheel.SetMenu(config, scene, reuseCache: true);
        for (int i = 0; i < 10; i++) wheel.WarmRenderCache();
        Assert.Equal(1, wheel.StaticLayerRenderCount);
        scene.Rings[0].Items[0].Label = "更新后的功能名称";
        wheel.WarmRenderCache();
        Assert.Equal(2, wheel.StaticLayerRenderCount);
    });

    [Fact]
    public void ImportedArtworkRetainsOriginalColorsInIconMode() => Sta(() =>
    {
        string path = Path.Combine(Path.GetTempPath(), "xy-wheel-color-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (var image = new Bitmap(32, 32))
            { using var graphics = Graphics.FromImage(image); graphics.Clear(Color.Red); image.Save(path, ImageFormat.Png); }
            var scene = Scene(); scene.ContentMode = "IconOnly";
            scene.Rings[0].Items[0].IconPath = path;
            using var wheel = new RadialMenuControl(Color.White) { Size = new Size(520, 520) };
            wheel.SetMenu(new PluginConfig(), scene);
            using var bitmap = new Bitmap(520, 520);
            wheel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            int redPixels = 0;
            for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
            { Color color = bitmap.GetPixel(x, y); if (color.R > 220 && color.G < 30 && color.B < 30) redPixels++; }
            Assert.True(redPixels > 100, "Original artwork was tinted by the wheel's accent color.");
        }
        finally { File.Delete(path); }
    });

    private static SceneMenuConfig Scene() => new SceneMenuConfig
    {
        Key = SceneKeys.Part, StyleId = "01", MenuSizePx = 520,
        Rings = new System.Collections.Generic.List<RingConfig>
        { new RingConfig { Index = 0, Slots = 8, DiameterPx = 520,
            Items = Enumerable.Range(0, 8).Select(slot => new MenuItemConfig
            { Slot = slot, Label = "功能 " + slot, Kind = ActionKinds.Hotkey, Value = "Ctrl+" + slot }).ToList() } }
    };

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(10000), "Wheel integration verification did not complete.");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
