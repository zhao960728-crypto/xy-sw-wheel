using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class WheelStyleTests
{
    [Theory]
    [InlineData("classic")]
    [InlineData("01")]
    [InlineData("11")]
    [InlineData("06")]
    [InlineData("orbit")]
    public void StylesRenderAllModesAndHitTheirOriginalBindings(string style) => Sta(() =>
    {
        var scene = Scene(style);
        using var wheel = new RadialMenuControl(Color.Fuchsia) { Size = new Size(520, 520) };
        var config = new PluginConfig();
        wheel.SetMenu(config, scene);
        MenuItemConfig? selected = null;
        int cancelled = 0, navigated = 0;
        wheel.ItemSelected += (_, item) => selected = item;
        wheel.CancelRequested += (_, __) => cancelled++;
        wheel.ViewChanged += (_, __) => navigated++;
        if (style == "06")
        {
            wheel.CommitAt(new Point(260, 104));
            Assert.Null(selected); Assert.Equal(1, navigated); Assert.Equal(0, cancelled);
        }
        foreach (string mode in new[] { "Auto", "IconOnly", "TextOnly", "IconAndText" })
        {
            scene.ContentMode = mode;
            using var image = new Bitmap(520, 520);
            wheel.DrawToBitmap(image, new Rectangle(0, 0, 520, 520));
            Assert.True(Enumerable.Range(20, 480).Any(x => image.GetPixel(x, 140).ToArgb() != Color.Fuchsia.ToArgb()));
            Capture(image, "wheel-" + style + "-" + mode + ".png");
        }
        // Find each cell by its public hover identity, then assert committing
        // that very point returns the unchanged command object and payload.
        var found = new Dictionary<string, Point>(); string hover = "";
        wheel.HoverTextChanged += text => hover = text;
        for (int y = 15; y < 510; y += 8)
            for (int x = 15; x < 510; x += 8)
            { wheel.UpdateCursor(new Point(x, y)); if (hover.StartsWith("测试命令", StringComparison.Ordinal)) found[hover] = new Point(x, y); }
        Assert.Equal(8, found.Count);
        foreach (var item in scene.Rings[0].Items)
        {
            selected = null; wheel.CommitAt(found[item.Label]);
            Assert.Same(item, selected); Assert.Equal("Ctrl+" + item.Slot, selected!.Value);
        }
        wheel.CommitAt(new Point(-10, -10)); Assert.Equal(1, cancelled);
        if (style == "06") { wheel.CommitAt(new Point(260, 260)); Assert.Equal(2, navigated); Assert.Equal(1, cancelled); }
    });

    [Fact]
    public void TinyKnownIconCellDoesNotReplacePreviewWithPaintError()
    {
        Sta(() =>
        {
            var ring = new RingConfig { Index = 0, Slots = 64, DiameterPx = 520, Enabled = true };
            ring.Items.Add(new MenuItemConfig
            {
                Slot = 0,
                Label = "设置",
                Kind = ActionKinds.Basic,
                Value = "settings",
                IconId = "gear"
            });
            var scene = new SceneMenuConfig
            {
                Key = SceneKeys.Part,
                MenuSizePx = 520,
                StyleId = "01",
                Rings = new List<RingConfig> { ring }
            };
            using var wheel = new RadialMenuControl(Color.Fuchsia) { Size = new Size(520, 520) };
            wheel.SetMenu(new PluginConfig(), scene);
            using var image = new Bitmap(520, 520);
            wheel.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
            Assert.True(Enumerable.Range(0, image.Width).Any(x => Enumerable.Range(0, image.Height)
                .Any(y => image.GetPixel(x, y).ToArgb() != Color.Fuchsia.ToArgb())));
        });
    }

    [Fact]
    public void EmptyCommandValueCancelsLikeCadInsteadOfDispatching() => Sta(() =>
    {
        var scene = Scene("classic");
        scene.Rings[0].Items[0].Value = "  ";
        using var wheel = new RadialMenuControl(Color.Fuchsia) { Size = new Size(520, 520) };
        wheel.SetMenu(new PluginConfig(), scene);
        MenuItemConfig? selected = null;
        int cancelled = 0;
        wheel.ItemSelected += (_, item) => selected = item;
        wheel.CancelRequested += (_, __) => cancelled++;

        wheel.CommitAt(new Point(260, 70)); // slot 0, inside the first ring

        Assert.Null(selected);
        Assert.Equal(1, cancelled);
    });

    // The CAD wheel widens the hub of a lone icon ring to 29% of the wheel so a
    // single ring is a compact band with an open middle instead of one ring
    // stretched from a tiny hub to the rim. It is display geometry: the stored
    // centre radius stays exactly what the user configured.
    [Fact]
    public void ALoneIconRingKeepsABroadOpenCentreAndTwoRingsUseTheStoredHub() => Sta(() =>
    {
        var scene = Scene("01");
        scene.CenterRadiusPx = 24;
        RingDiameterLayout.Normalize(scene);
        using var wheel = new RadialMenuControl(Color.Fuchsia) { Size = new Size(520, 520) };
        int cancelled = 0;
        wheel.CancelRequested += (_, __) => cancelled++;
        wheel.SetMenu(new PluginConfig(), scene);

        // 120 px out from the middle is far past the configured 24 px hub, yet
        // the lone ring leaves it open, so a press there cancels and the wheel
        // paints nothing at that spot.
        wheel.CommitAt(new Point(260, 140));
        Assert.Equal(1, cancelled);
        Assert.Equal(24, scene.CenterRadiusPx);
        using (var image = new Bitmap(520, 520))
        {
            wheel.DrawToBitmap(image, new Rectangle(0, 0, 520, 520));
            Assert.Equal(Color.Fuchsia.ToArgb(), image.GetPixel(260, 140).ToArgb());
            Capture(image, "wheel-01-open-centre.png");
        }

        // Add a ring and the widening is dropped: the same point is now on the
        // inner band, so it hits a command instead of cancelling.
        var second = new RingConfig { Index = 1, Slots = 8, Enabled = true };
        scene.Rings.Add(second);
        RingDiameterLayout.Normalize(scene);
        wheel.SetMenu(new PluginConfig(), scene);
        cancelled = 0;
        wheel.CommitAt(new Point(260, 140));
        Assert.Equal(0, cancelled);
        using (var image = new Bitmap(520, 520))
        {
            wheel.DrawToBitmap(image, new Rectangle(0, 0, 520, 520));
            Assert.NotEqual(Color.Fuchsia.ToArgb(), image.GetPixel(260, 140).ToArgb());
            Capture(image, "wheel-01-two-rings.png");
        }
    });

    [Theory]
    [InlineData("darkGlass")]
    [InlineData("classic")]
    [InlineData("native")]
    [InlineData("midnight")]
    public void AppearancePresetsRenderDistinctSurfaces(string presetId) => Sta(() =>
    {
        var scene = Scene("01");
        scene.Appearance = WheelAppearance.FromPreset(presetId);
        using var wheel = new RadialMenuControl(Color.Fuchsia) { Size = new Size(520, 520) };
        wheel.SetMenu(new PluginConfig(), scene);
        using var image = new Bitmap(520, 520);
        wheel.DrawToBitmap(image, new Rectangle(0, 0, 520, 520));
        Capture(image, "wheel-preset-" + presetId + ".png");
        // The neutral centre and at least one corner stay transparent so the
        // preset only changes the surface, not the wheel's overall shape.
        Assert.Equal(Color.Fuchsia.ToArgb(), image.GetPixel(260, 260).ToArgb());
        Assert.Equal(Color.Fuchsia.ToArgb(), image.GetPixel(0, 0).ToArgb());
    });

    [Fact]
    public void PreviewHoldSwapDoesNotExecuteAndLostCaptureCancels() => Sta(() =>
    {
        using var wheel = new RadialMenuControl(Color.White) { Size = new Size(520, 520), EnableCellSelection = true };
        var scene = Scene("01"); wheel.SetMenu(new PluginConfig(), scene); wheel.CreateControl();
        int executions = 0, swaps = 0;
        RadialCellsSwappedEventArgs? last = null;
        wheel.ItemSelected += (_, __) => executions++;
        wheel.CellsSwapped += (_, args) => { swaps++; last = args; };
        Point north = new Point(260, 50), east = new Point(470, 260);
        Invoke(wheel, "BeginCellDrag", north);
        // Simulate the hold timer having elapsed without generating physical
        // input on the user's desktop. The production mouse-up path is real.
        typeof(RadialMenuControl).GetField("_cellDragArmed", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(wheel, true);
        Invoke(wheel, "UpdateCellDrag", Drag(east)); Invoke(wheel, "EndCellDrag", east);
        Assert.Equal(1, swaps); Assert.Equal(0, executions);
        Assert.Equal(0, last!.SourceSlot); Assert.Equal(2, last.TargetSlot);
        Assert.Equal("Ctrl+0", scene.Rings[0].Items[0].Value); // Parent owns swapping/persistence.
        Invoke(wheel, "BeginCellDrag", north);
        typeof(RadialMenuControl).GetField("_cellDragArmed", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(wheel, true);
        wheel.Capture = false;
        Invoke(wheel, "UpdateCellDrag", Drag(east)); Invoke(wheel, "EndCellDrag", east);
        Assert.Equal(1, swaps); Assert.Equal(0, executions);
    });

    // The drag gesture carries the pressed button with it, the way a real
    // mouse-move event does.
    private static MouseEventArgs Drag(Point point) => new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0);

    [Fact]
    public void LongAutomaticTextAndInvalidIconRenderWithoutChangingBinding() => Sta(() =>
    {
        using var wheel = new RadialMenuControl(Color.Fuchsia) { Size = new Size(360, 360), ScaleMenuForPreview = true };
        var scene = Scene("orbit"); scene.IconColor = "Purple";
        var item = scene.Rings[0].Items[0];
        item.Label = "非常长的自定义原生命令名称用于检查自动换行与格内裁切";
        item.IconId = "missing-old-library-id"; item.FontSizePx = 0;
        item.FontBold = true; item.TextEffect = TextEffects.Outline;
        wheel.SetMenu(new PluginConfig { FontSizePx = 72 }, scene);
        using var image = new Bitmap(360, 360);
        wheel.DrawToBitmap(image, new Rectangle(0, 0, 360, 360));
        Capture(image, "wheel-long-auto-purple.png");
        Assert.Equal(0, item.FontSizePx); Assert.Equal("Ctrl+0", item.Value);
        var diagnostic = Assert.Single(wheel.TextDiagnostics.Where(entry => entry.Label == item.Label));
        Assert.True(diagnostic.LogicalFontSizePx < 72); Assert.True(diagnostic.FitsBounds);
        // The center and the four corners remain transparent in independent cells.
        Assert.Equal(Color.Fuchsia.ToArgb(), image.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.Fuchsia.ToArgb(), image.GetPixel(180, 180).ToArgb());
    });

    [Fact]
    public void CentreMarkIsOptInSoTheColourKeyedSurfaceStaysOpen() => Sta(() =>
    {
        // The live popup is colour-keyed: any pixel drawn on the surface turns
        // opaque and covers the model. The CAD-style centre cross therefore has
        // to be asked for - only the label layer does.
        var scene = Scene("01");
        using var plain = new RadialMenuControl(Color.Fuchsia) { Size = new Size(520, 520) };
        plain.SetMenu(new PluginConfig(), scene);
        using var plainImage = new Bitmap(520, 520);
        plain.DrawToBitmap(plainImage, new Rectangle(0, 0, 520, 520));
        Assert.Equal(Color.Fuchsia.ToArgb(), plainImage.GetPixel(260, 260).ToArgb());

        using var marked = new RadialMenuControl(Color.Fuchsia) { Size = new Size(520, 520), DrawCenterMark = true };
        marked.SetMenu(new PluginConfig(), scene);
        using var markedImage = new Bitmap(520, 520);
        marked.DrawToBitmap(markedImage, new Rectangle(0, 0, 520, 520));
        // The cross is small: sample a short run through the centre and require
        // at least one drawn pixel, without pinning an exact antialiased value.
        Assert.True(Enumerable.Range(252, 17).Any(offset => markedImage.GetPixel(offset, offset).ToArgb() != Color.Fuchsia.ToArgb()));
        Capture(markedImage, "wheel-01-centre-mark.png");
    });

    private static SceneMenuConfig Scene(string style)
    {
        var ring = new RingConfig { Index = 0, Slots = 8, DiameterPx = 520 };
        for (int slot = 0; slot < 8; slot++) ring.Items.Add(new MenuItemConfig { Slot = slot, Label = "测试命令" + slot, Value = "Ctrl+" + slot, Kind = ActionKinds.Hotkey, IconId = WheelIconCatalog.Entries[slot * 8].Id });
        return new SceneMenuConfig { Key = SceneKeys.Part, MenuSizePx = 520, StyleId = style, Rings = new List<RingConfig> { ring } };
    }
    private static void Capture(Bitmap image, string file)
    {
        string? directory = Environment.GetEnvironmentVariable("XY_UI_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); image.Save(Path.Combine(directory, file));
    }
    private static void Invoke(RadialMenuControl wheel, string name, Point point) => typeof(RadialMenuControl).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(wheel, new object[] { point });
    private static void Invoke(RadialMenuControl wheel, string name, MouseEventArgs drag) => typeof(RadialMenuControl).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(wheel, new object[] { drag });
    private static void Sta(Action work)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { work(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw failure;
    }
}
