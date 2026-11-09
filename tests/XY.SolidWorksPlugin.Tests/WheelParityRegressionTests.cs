using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Threading;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class WheelParityRegressionTests
{
    [Fact]
    public void ClassicSectorBothHalvesExecuteThePaintedCommand() => Sta(() =>
    {
        var scene = Scene("classic");
        using var wheel = new RadialMenuControl(Color.Fuchsia) { Size = new Size(520, 520) };
        wheel.SetMenu(new PluginConfig(), scene);
        MenuItemConfig? selected = null;
        wheel.ItemSelected += (_, item) => selected = item;
        for (int slot = 0; slot < 8; slot++)
            foreach (float offset in new[] { 5f, 40f })
            {
                double angle = (-90 + slot * 45 + offset) * Math.PI / 180;
                wheel.CommitAt(new Point(260 + (int)(160 * Math.Cos(angle)), 260 + (int)(160 * Math.Sin(angle))));
                Assert.Same(scene.Rings[0].Items[slot], selected);
            }
    });

    [Theory]
    [InlineData("11")]
    [InlineData("06")]
    [InlineData("orbit")]
    public void CellSizeScalesHitAreaAndContentOnce(string style) => Sta(() =>
    {
        var scene = Scene(style);
        using var wheel = new RadialMenuControl(Color.Fuchsia) { Size = new Size(520, 520) };
        wheel.SetMenu(new PluginConfig(), scene);
        if (style == "06") typeof(RadialMenuControl).GetField("_activeGroup", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(wheel, 0);
        var before = CellBounds(wheel, scene.Rings[0].Items[0]);
        scene.Rings[0].Items[0].CellSizePercent = 150;
        var after = CellBounds(wheel, scene.Rings[0].Items[0]);
        Assert.Equal(before.path.Width * 1.5f, after.path.Width, 2);
        Assert.Equal(before.content.Width * 1.5f, after.content.Width, 2);
        Assert.Equal(before.path.Left + before.path.Width / 2, after.path.Left + after.path.Width / 2, 2);
    });

    [Fact]
    public void MovedSquareRetainsSquareHitCorners() => Sta(() =>
    {
        var scene = Scene("free");
        var item = scene.Rings[0].Items[0];
        item.FreeX = .25; item.FreeY = -.25; item.CellShape = "Square";
        using var wheel = new RadialMenuControl(Color.White) { Size = new Size(520, 520) };
        wheel.SetMenu(new PluginConfig(), scene);
        var bounds = CellBounds(wheel, item).path;
        MenuItemConfig? selected = null;
        wheel.ItemSelected += (_, command) => selected = command;
        wheel.CommitAt(new Point((int)bounds.Left + 2, (int)bounds.Top + 2));
        Assert.Same(item, selected);
    });

    [Fact]
    public void PictureModePreservesSourceColors() => Sta(() =>
    {
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            using (var source = new Bitmap(32, 32))
            { using var g = Graphics.FromImage(source); g.Clear(Color.Red); source.Save(file); }
            var scene = Scene("01");
            using var wheel = new RadialMenuControl(Color.White) { Size = new Size(520, 520) };
            wheel.SetMenu(new PluginConfig(), scene);
            var item = scene.Rings[0].Items[0];
            item.IconPath = file; item.ImageMode = "Image"; item.ContentMode = "IconOnly";
            using var image = new Bitmap(120, 120);
            using var graphics = Graphics.FromImage(image);
            typeof(RadialMenuControl).GetMethod("DrawStyledContent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(wheel,
                new object[] { graphics, item, new RectangleF(10, 10, 100, 100), 0 });
            Color actual = image.GetPixel(60, 60);
            Assert.True(actual.R > 240 && actual.G < 10 && actual.B < 10, actual.ToString());
        }
        finally { File.Delete(file); }
    });

    private static (RectangleF path, RectangleF content) CellBounds(RadialMenuControl wheel, MenuItemConfig item)
    {
        var cells = (IEnumerable)typeof(RadialMenuControl).GetMethod("BuildStyledCells", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(wheel, null);
        var result = (RectangleF.Empty, RectangleF.Empty);
        foreach (var cell in cells)
        {
            var type = cell.GetType();
            if (ReferenceEquals(type.GetField("Item")!.GetValue(cell), item))
                result = (((GraphicsPath)type.GetField("Path")!.GetValue(cell)).GetBounds(), (RectangleF)type.GetField("Content")!.GetValue(cell));
            ((IDisposable)cell).Dispose();
        }
        Assert.False(result.Item1.IsEmpty);
        return result;
    }

    private static SceneMenuConfig Scene(string style)
    {
        var ring = new RingConfig { Index = 0, Slots = 8, DiameterPx = 520, Enabled = true };
        for (int slot = 0; slot < 8; slot++) ring.Items.Add(new MenuItemConfig { Slot = slot, Label = "命令" + slot, Kind = ActionKinds.Hotkey, Value = "Ctrl+" + slot });
        return new SceneMenuConfig { Key = SceneKeys.Part, MenuSizePx = 520, StyleId = style, Rings = new List<RingConfig> { ring } };
    }

    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) throw error;
    }
}
