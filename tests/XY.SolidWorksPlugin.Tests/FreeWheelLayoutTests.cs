using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using XY.SolidWorksPlugin;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public class FreeWheelLayoutTests
{
	private static SceneMenuConfig SceneWithItems(int count)
	{
		SceneMenuConfig scene = new SceneMenuConfig { StyleId = FreeWheelLayout.StyleId, MenuSizePx = 520, CenterRadiusPx = 44 };
		RingConfig ring = new RingConfig { Index = 0, Slots = 16 };
		for (int slot = 0; slot < count; slot++)
		{
			ring.Items.Add(new MenuItemConfig { Slot = slot, Kind = ActionKinds.Hotkey, Value = "ctrl+" + slot, Label = "命令" + slot });
		}
		scene.Rings.Add(ring);
		return scene;
	}

	[Fact]
	public void SceneWithoutCoordinatesIsNotFree()
	{
		Assert.False(FreeWheelLayout.HasAny(SceneWithItems(4)));
	}

	[Fact]
	public void PlacingACellStoresNormalisedCoordinates()
	{
		SceneMenuConfig scene = SceneWithItems(4);
		Assert.True(FreeWheelLayout.Place(scene, 0, 1, .2, -.1));
		Assert.True(FreeWheelLayout.HasAny(scene));
		MenuItemConfig item = FreeWheelLayout.Find(scene, 0, 1);
		Assert.Equal(.2, item.FreeX.Value);
		Assert.Equal(-.1, item.FreeY.Value);
	}

	[Fact]
	public void PlacementIsClampedInsideTheWheel()
	{
		SceneMenuConfig scene = SceneWithItems(2);
		FreeWheelLayout.Place(scene, 0, 0, 9, -9);
		MenuItemConfig item = FreeWheelLayout.Find(scene, 0, 0);
		Assert.True(item.FreeX.Value <= .48);
		Assert.True(item.FreeY.Value >= -.48);
	}

	[Fact]
	public void MissingSlotCannotBePlaced()
	{
		SceneMenuConfig scene = SceneWithItems(2);
		Assert.False(FreeWheelLayout.Place(scene, 0, 99, 0, 0));
		Assert.False(FreeWheelLayout.Place(scene, 7, 0, 0, 0));
	}

	[Fact]
	public void TidyPlacesEveryBoundCell()
	{
		SceneMenuConfig scene = SceneWithItems(7);
		int placed = FreeWheelLayout.Tidy(scene, 3);
		Assert.Equal(7, placed);
		Assert.All(scene.Rings[0].Items, item => Assert.True(item.FreeX.HasValue && item.FreeY.HasValue));
	}

	[Fact]
	public void TidyKeepsCellsInsideTheRim()
	{
		SceneMenuConfig scene = SceneWithItems(9);
		FreeWheelLayout.Tidy(scene, 4);
		Assert.All(scene.Rings[0].Items, item =>
		{
			Assert.True(item.FreeX.Value >= -.35 && item.FreeX.Value <= .35);
			Assert.True(item.FreeY.Value >= -.35 && item.FreeY.Value <= .35);
		});
	}

	[Fact]
	public void TidyIsIdempotent()
	{
		SceneMenuConfig scene = SceneWithItems(5);
		FreeWheelLayout.Tidy(scene, 2);
		var first = scene.Rings[0].Items.Select(i => (i.FreeX, i.FreeY)).ToList();
		FreeWheelLayout.Tidy(scene, 2);
		Assert.Equal(first, scene.Rings[0].Items.Select(i => (i.FreeX, i.FreeY)).ToList());
	}

	[Fact]
	public void ClearRestoresRingLayout()
	{
		SceneMenuConfig scene = SceneWithItems(4);
		FreeWheelLayout.Tidy(scene, 2);
		FreeWheelLayout.Clear(scene);
		Assert.False(FreeWheelLayout.HasAny(scene));
	}

	[Fact]
	public void CoordinatesMapToWheelPixels()
	{
		var bounds = new RectangleF(100, 50, 400, 400);
		PointF center = FreeWheelLayout.CenterOf(bounds, 0d, 0d);
		Assert.Equal(300f, center.X);
		Assert.Equal(250f, center.Y);
		// -0.25 sits a quarter of the wheel left of the centre.
		Assert.Equal(200f, FreeWheelLayout.CenterOf(bounds, -.25, 0d).X);
	}

	[Fact]
	public void RectStaysInsideTheWheel()
	{
		var bounds = new RectangleF(0, 0, 400, 400);
		RectangleF rect = FreeWheelLayout.RectAt(bounds, new PointF(395, 200), new SizeF(60, 60));
		Assert.True(rect.Right <= bounds.Right);
		Assert.True(rect.Left >= bounds.Left);
	}

	[Fact]
	public void OverlapDetectsTouchingCells()
	{
		var a = new RectangleF(0, 0, 40, 40);
		Assert.True(FreeWheelLayout.Overlaps(a, new RectangleF(30, 0, 40, 40), 0f));
		Assert.False(FreeWheelLayout.Overlaps(a, new RectangleF(100, 0, 40, 40), 0f));
	}

	[Fact]
	public void FreeCoordinatesSurviveNormalize()
	{
		PluginConfig config = new PluginConfig();
		ConfigStore.Normalize(config);
		SceneMenuConfig scene = config.Menus.Values.First();
		scene.Rings[0].Slots = 8;
		scene.Rings[0].Items.Add(new MenuItemConfig
		{
			Slot = 0, Kind = ActionKinds.Hotkey, Value = "ctrl+1", FreeX = .2, FreeY = -.3
		});
		ConfigStore.Normalize(config);
		MenuItemConfig item = config.Menus.Values.First().Rings[0].Items.Single();
		Assert.Equal(.2, item.FreeX.Value);
		Assert.Equal(-.3, item.FreeY.Value);
	}
}
