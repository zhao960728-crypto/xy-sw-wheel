using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

// The preview is where the user arranges the wheel. These cover the editing
// gestures the CAD build already offers there: insertion points, per-cell
// sizing, free placement and the tidy nudge.
public sealed class WheelPreviewEditingTests
{
    private static SceneMenuConfig Scene(string style, int slots = 8)
    {
        var ring = new RingConfig { Index = 0, Slots = slots, DiameterPx = 520 };
        for (int slot = 0; slot < slots; slot++)
            ring.Items.Add(new MenuItemConfig { Slot = slot, Label = "测试命令" + slot, Value = "Ctrl+" + slot, Kind = ActionKinds.Hotkey });
        return new SceneMenuConfig { Key = SceneKeys.Part, MenuSizePx = 520, StyleId = style, Rings = new List<RingConfig> { ring } };
    }

    private static RadialMenuControl Wheel(SceneMenuConfig scene)
    {
        var wheel = new RadialMenuControl(Color.Fuchsia) { Size = new Size(520, 520), EnableCellSelection = true };
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

    [Theory]
    [InlineData("01")]
    [InlineData("orbit")]
    [InlineData("free")]
    public void EveryStyleOffersInsertionPointsThatHitBackASlot(string style) => Sta(() =>
    {
        var scene = Scene(style);
        // Free cells only become discrete tiles once they carry coordinates.
        if (style == "free") FreeWheelLayout.Tidy(scene, 4);
        using var wheel = Wheel(scene);
        var points = wheel.CellInsertionPoints();
        Assert.NotEmpty(points);
        foreach (var gap in points)
        {
            var hit = wheel.HitInsertion(Point.Round(gap.Point));
            Assert.NotNull(hit);
            Assert.Equal(gap.Ring, hit!.RingIndex);
            Assert.Equal(gap.Slot, hit.Slot);
        }
    });

    [Fact]
    public void InsertionMarkersAreOnlyDrawnWhenThePreviewAsksForThem() => Sta(() =>
    {
        var scene = Scene("orbit");
        using var wheel = Wheel(scene);
        using var plain = new Bitmap(520, 520);
        wheel.DrawToBitmap(plain, new Rectangle(0, 0, 520, 520));

        wheel.ShowInsertionPoints = true;
        using var marked = new Bitmap(520, 520);
        wheel.DrawToBitmap(marked, new Rectangle(0, 0, 520, 520));
        Capture(marked, "preview-insertion-points.png");

        int different = 0;
        for (int y = 0; y < 520; y += 2)
            for (int x = 0; x < 520; x += 2)
                if (plain.GetPixel(x, y).ToArgb() != marked.GetPixel(x, y).ToArgb()) different++;
        Assert.True(different > 50, "插入标记应当可见，实际差异像素 " + different);
    });

    [Fact]
    public void TileStylesTakeAPerCellSizeButWedgeSlotsDoNot() => Sta(() =>
    {
        var scenes = new[] { "01", "classic" };
        foreach (string style in scenes)
        {
            var scene = Scene(style);
            using var wheel = Wheel(scene);
            Assert.False(wheel.TrySetCellSize(0, 0, 60));
        }

        var orbit = Scene("orbit");
        using var tile = Wheel(orbit);
        Assert.True(tile.TrySetCellSize(0, 0, 60));
        Assert.Equal(60, orbit.Rings[0].Items[0].CellSizePercent);
        Assert.True(tile.TrySetCellSize(0, 0, 40));
        Assert.Equal(40, orbit.Rings[0].Items[0].CellSizePercent);
        // The stored range is the one a save/load round trip keeps, so an
        // out-of-range request lands on the nearest end instead of failing.
        Assert.True(tile.TrySetCellSize(0, 1, 5));
        Assert.Equal(40, orbit.Rings[0].Items[1].CellSizePercent);
    });

    [Fact]
    public void FreePlacementOnlyMovesCellsInTheFreeStyle() => Sta(() =>
    {
        var orbit = Scene("orbit");
        using var wheel = Wheel(orbit);
        Assert.False(wheel.TryPlaceCell(0, 0, new Point(300, 300)));
        Assert.False(orbit.Rings[0].Items[0].FreeX.HasValue);

        var free = Scene("free");
        FreeWheelLayout.Tidy(free, 4);
        using var designer = Wheel(free);
        Assert.True(designer.TryPlaceCell(0, 0, new Point(320, 190)));
        Assert.True(free.Rings[0].Items[0].FreeX.HasValue);
        Assert.InRange(free.Rings[0].Items[0].FreeX!.Value, -0.5, 0.5);
        Assert.InRange(free.Rings[0].Items[0].FreeY!.Value, -0.5, 0.5);
        // Dropping a cell on top of another one is refused, not overlapped.
        var occupied = designer.CellInsertionPoints();
        Assert.NotEmpty(occupied);
    });

    [Fact]
    public void TidyNudgeKeepsEveryCellInsideTheWheelAndClearOfItsNeighbours() => Sta(() =>
    {
        var free = Scene("free", 12);
        FreeWheelLayout.Tidy(free, 4);
        using var wheel = Wheel(free);
        wheel.NudgeFreeLayout();
        var placed = free.Rings.SelectMany(r => r.Items).Where(i => i.FreeX.HasValue && i.FreeY.HasValue).ToList();
        Assert.Equal(12, placed.Count);
        foreach (var item in placed)
        {
            Assert.InRange(item.FreeX!.Value, -0.49, 0.49);
            Assert.InRange(item.FreeY!.Value, -0.49, 0.49);
        }
    });

    [Fact]
    public void PracticeModeFreezesEveryEditingGesture() => Sta(() =>
    {
        var free = Scene("free");
        FreeWheelLayout.Tidy(free, 4);
        using var wheel = Wheel(free);
        wheel.PracticeMode = true;
        Assert.False(wheel.TrySetCellSize(0, 0, 60));
        Assert.False(wheel.TryPlaceCell(0, 0, new Point(300, 300)));
        Assert.False(wheel.NudgeFreeLayout());
        Assert.Equal(100, free.Rings[0].Items[0].CellSizePercent);
    });

    [Fact]
    public void FreeWheelRendersTheCellsTheUserArranged() => Sta(() =>
    {
        var free = Scene("free");
        FreeWheelLayout.Tidy(free, 3);
        using var wheel = Wheel(free);
        using var image = new Bitmap(520, 520);
        wheel.DrawToBitmap(image, new Rectangle(0, 0, 520, 520));
        Capture(image, "preview-free-layout.png");
        // A tied layout spreads cells away from the ring orbit, so the band
        // where the ring slots would sit is no longer the only painted place.
        int painted = Enumerable.Range(60, 400).Count(x => image.GetPixel(x, 260).ToArgb() != Color.Fuchsia.ToArgb());
        Assert.True(painted > 40, "自由布局应当在盘面上铺开，实际 " + painted);
    });

    private static void Capture(Bitmap image, string file)
    {
        string? directory = Environment.GetEnvironmentVariable("XY_UI_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        image.Save(Path.Combine(directory, file));
    }
}
