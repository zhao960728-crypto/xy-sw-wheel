using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class WheelSurfaceCompositionTests
{
    [Theory]
    [InlineData("classic")]
    [InlineData("01")]
    [InlineData("11")]
    [InlineData("06")]
    [InlineData("orbit")]
    public void PopupSurfaceDoesNotMixItsPaletteWithTheTransparentKey(string style) => Sta(() =>
    {
        var scene = Scene(style);
        using var wheel = new RadialMenuControl(Color.FromArgb(1, 2, 3))
        { Size = new Size(520, 520), DrawLabels = false, SurfaceOpacityAppliedByWindow = true };
        wheel.SetMenu(new PluginConfig(), scene);
        using var bitmap = new Bitmap(520, 520);
        wheel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        // Fully interior surface pixels retain the selected palette. The old
        // renderer turned white into grey before Windows composited the popup.
        int whitePixels = 0;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).ToArgb() == Color.White.ToArgb()) whitePixels++;
        Assert.True(whitePixels > 1000, "Surface was tinted by the transparency key.");
        Assert.Equal(Color.FromArgb(1, 2, 3).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(65, scene.SurfaceOpacityPercent);
        string? capture = Environment.GetEnvironmentVariable("XY_UI_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(capture))
        {
            Directory.CreateDirectory(capture);
            bitmap.Save(Path.Combine(capture, "popup-surface-" + style + ".png"));
            wheel.DrawLabels = true;
            wheel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(capture, "popup-labelled-" + style + ".png"));
        }
    });

    [Theory]
    [InlineData(90, 65, .585)]
    [InlineData(100, 100, 1)]
    [InlineData(100, 0, .65)]
    [InlineData(100, 5, .05)]
    public void WindowCompositesSavedOpacityOverTheModel(int global, int surface, double expected) =>
        Assert.Equal(expected, RadialMenuWindow.ComputeSurfaceWindowOpacity(global, surface), 5);

    [Fact]
    public void ShadowStaysBehindTheOpaqueIconBand() => Sta(() =>
    {
        var scene = Scene("01");
        scene.SurfaceOpacityPercent = 100;
        scene.Appearance.ShadowPercent = 70;
        using var wheel = new RadialMenuControl(Color.White)
        { Size = new Size(520, 520), EnableCellSelection = true, DrawLabels = false };
        wheel.SetMenu(new PluginConfig(), scene);
        using var bitmap = new Bitmap(520, 520);
        wheel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(260, 50).ToArgb());
    });

    private static SceneMenuConfig Scene(string style) => new SceneMenuConfig
    {
        Key = SceneKeys.Part, MenuSizePx = 520, StyleId = style,
        SurfaceOpacityPercent = 65, Appearance = WheelAppearance.Classic(),
        Rings = new System.Collections.Generic.List<RingConfig>
        { new RingConfig { Index = 0, Slots = 8, DiameterPx = 520, Enabled = true,
            Items = Enumerable.Range(0, 8).Select(slot => new MenuItemConfig
            { Slot = slot, Label = "命令" + slot, Kind = ActionKinds.Hotkey, Value = "Ctrl+" + slot }).ToList() } }
    };

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw failure;
    }
}
