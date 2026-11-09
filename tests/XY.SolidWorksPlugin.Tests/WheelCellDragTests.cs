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

// The preview is where the user arranges the wheel, so a cell has to move the
// way any other Windows drag does: press, move, release. These pin that down
// plus the lens that helps read a small cell.
public sealed class WheelCellDragTests
{
    private static SceneMenuConfig Scene(string style, int slots = 8)
    {
        var ring = new RingConfig { Index = 0, Slots = slots, DiameterPx = 520 };
        for (int slot = 0; slot < slots; slot++)
            ring.Items.Add(new MenuItemConfig { Slot = slot, Label = "命令" + slot, Value = "Ctrl+" + slot, Kind = ActionKinds.Hotkey });
        return new SceneMenuConfig { Key = SceneKeys.Part, MenuSizePx = 520, StyleId = style, Rings = new List<RingConfig> { ring } };
    }

    private static RadialMenuControl Wheel(SceneMenuConfig scene, bool preview = false)
    {
        var wheel = new RadialMenuControl(Color.Fuchsia)
        { Size = new Size(520, 520), EnableCellSelection = true, ScaleMenuForPreview = preview };
        wheel.SetMenu(new PluginConfig(), scene);
        return wheel;
    }

    private static void Sta(Action work)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { work(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw failure;
    }

    private static void Call(object target, string name, params object[] args)
    {
        MethodInfo? method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(target, args);
    }

    private static Point FindSlot(RadialMenuControl wheel, int slot)
    {
        for (int y = 6; y < 520; y += 4)
            for (int x = 6; x < 520; x += 4)
            {
                var hit = wheel.HitCell(new Point(x, y));
                if (hit != null && hit.RingIndex == 0 && hit.Slot == slot) return new Point(x, y);
            }
        throw new InvalidOperationException("预览里找不到第 " + slot + " 格");
    }

    [Theory]
    [InlineData("01")]
    [InlineData("orbit")]
    public void PressingAndMovingACellSwapsItWithoutALongPress(string style) => Sta(() =>
    {
        var scene = Scene(style);
        using var wheel = Wheel(scene);
        RadialCellsSwappedEventArgs? swapped = null;
        wheel.CellsSwapped += (_, e) => swapped = e;
        Point from = FindSlot(wheel, 0), to = FindSlot(wheel, 3);
        Call(wheel, "BeginCellDrag", from);
        // A plain drag: the button stays down and the pointer leaves the cell.
        Call(wheel, "UpdateCellDrag", new MouseEventArgs(MouseButtons.Left, 1, to.X, to.Y, 0));
        Call(wheel, "EndCellDrag", to);
        Assert.NotNull(swapped);
        Assert.Equal(0, swapped!.SourceSlot);
        Assert.Equal(3, swapped.TargetSlot);
    });

    [Fact]
    public void MovingWithoutTheButtonDownDoesNotSwapAnything() => Sta(() =>
    {
        var scene = Scene("01");
        using var wheel = Wheel(scene);
        int swaps = 0;
        wheel.CellsSwapped += (_, _) => swaps++;
        Point from = FindSlot(wheel, 0), to = FindSlot(wheel, 3);
        Call(wheel, "BeginCellDrag", from);
        Call(wheel, "UpdateCellDrag", new MouseEventArgs(MouseButtons.None, 0, to.X, to.Y, 0));
        Call(wheel, "EndCellDrag", to);
        Assert.Equal(0, swaps);
    });

    [Fact]
    public void AClickThatBarelyMovesStaysASelection() => Sta(() =>
    {
        var scene = Scene("01");
        using var wheel = Wheel(scene);
        int swaps = 0;
        wheel.CellsSwapped += (_, _) => swaps++;
        Point from = FindSlot(wheel, 2);
        Call(wheel, "BeginCellDrag", from);
        Call(wheel, "UpdateCellDrag", new MouseEventArgs(MouseButtons.Left, 1, from.X + 2, from.Y + 1, 0));
        Call(wheel, "EndCellDrag", new Point(from.X + 2, from.Y + 1));
        Assert.Equal(0, swaps);
    });

    [Fact]
    public void TheDragShowsItsTargetWhileTheButtonIsStillDown() => Sta(() =>
    {
        var scene = Scene("01");
        using var wheel = Wheel(scene);
        Point from = FindSlot(wheel, 0), to = FindSlot(wheel, 4);
        Call(wheel, "BeginCellDrag", from);
        Call(wheel, "UpdateCellDrag", new MouseEventArgs(MouseButtons.Left, 1, to.X, to.Y, 0));
        using var dragged = new Bitmap(520, 520);
        wheel.DrawToBitmap(dragged, new Rectangle(0, 0, 520, 520));
        Capture(dragged, "preview-cell-drag.png");
        Call(wheel, "EndCellDrag", to);
        using var idle = new Bitmap(520, 520);
        wheel.DrawToBitmap(idle, new Rectangle(0, 0, 520, 520));
        int different = 0;
        for (int y = 0; y < 520; y += 2)
            for (int x = 0; x < 520; x += 2)
                if (dragged.GetPixel(x, y).ToArgb() != idle.GetPixel(x, y).ToArgb()) different++;
        Assert.True(different > 50, "拖动过程应当画出来源与目标提示，实际差异像素 " + different);
    });

    [Fact]
    public void TheLensOnlyAppearsWhenThePreviewAsksForIt() => Sta(() =>
    {
        var scene = Scene("01");
        using var wheel = Wheel(scene, preview: true);
        Point point = FindSlot(wheel, 1);
        wheel.UpdateCursor(point);
        Call(wheel, "TrackMagnifierPointer", point);
        using var plain = new Bitmap(520, 520);
        wheel.DrawToBitmap(plain, new Rectangle(0, 0, 520, 520));
        Assert.True(wheel.PreviewMagnifierBounds.IsEmpty, "默认不应绘制放大镜");

        wheel.PreviewMagnifierEnabled = true;
        using var lens = new Bitmap(520, 520);
        wheel.DrawToBitmap(lens, new Rectangle(0, 0, 520, 520));
        Capture(lens, "preview-magnifier.png");
        Assert.False(wheel.PreviewMagnifierBounds.IsEmpty, "开启后应当在预览角落画出放大镜");
        Rectangle bounds = wheel.PreviewMagnifierBounds;
        Assert.True(bounds.Width > 60 && bounds.Height > 60);
        Assert.True(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= 520 && bounds.Bottom <= 520, "放大镜越出预览范围: " + bounds);
    });

    [Fact]
    public void TheLensStaysOutOfTheWayWhileACellIsBeingEdited() => Sta(() =>
    {
        var scene = Scene("01");
        using var wheel = Wheel(scene, preview: true);
        Point point = FindSlot(wheel, 1);
        wheel.UpdateCursor(point);
        Call(wheel, "TrackMagnifierPointer", point);
        wheel.PreviewMagnifierEnabled = true;
        wheel.ResizeMode = true;
        using var resizing = new Bitmap(520, 520);
        wheel.DrawToBitmap(resizing, new Rectangle(0, 0, 520, 520));
        Assert.True(wheel.PreviewMagnifierBounds.IsEmpty, "调大小时不应弹出放大镜");
    });

    private static void Capture(Bitmap image, string file)
    {
        string? directory = Environment.GetEnvironmentVariable("XY_UI_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        image.Save(Path.Combine(directory, file));
    }
}
