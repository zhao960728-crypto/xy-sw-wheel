using System;
using System.Collections.Generic;
using System.Linq;
using XY.SolidWorksPlugin;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public class WheelRingPresetTests
{
	private static SceneMenuConfig Scene(string styleId = "01")
	{
		SceneMenuConfig scene = new SceneMenuConfig { StyleId = styleId, MenuSizePx = 520, CenterRadiusPx = 44 };
		scene.Rings.Add(new RingConfig { Index = 0, Slots = 8 });
		return scene;
	}

	private static void Fill(SceneMenuConfig scene, int ringIndex, int count)
	{
		RingConfig ring = scene.Rings[ringIndex];
		for (int slot = 0; slot < count; slot++)
		{
			ring.Items.Add(new MenuItemConfig { Slot = slot, Kind = ActionKinds.Hotkey, Value = "ctrl+" + slot, Label = "r" + ringIndex + "s" + slot });
		}
	}

	[Fact]
	public void PresetsExistForTheDefaultWheelStyle()
	{
		int[][] presets = WheelRingPresets.ForStyle("01");
		Assert.Equal(3, presets.Length);
		Assert.Equal(new[] { 8, 8 }, presets[0]);
		Assert.Equal(new[] { 8, 8, 16 }, presets[1]);
		Assert.Equal(new[] { 8, 8, 16, 16 }, presets[2]);
	}

	[Fact]
	public void UnknownStyleHasNoPreset()
	{
		Assert.Empty(WheelRingPresets.ForStyle("unknown-layout"));
	}

	[Fact]
	public void ApplyingAPresetCreatesEvenlySpacedRings()
	{
		SceneMenuConfig scene = Scene();

		WheelRingPresets.Apply(scene, new[] { 8, 8, 16 });

		Assert.Equal(3, scene.Rings.Count(ring => ring.Enabled));
		Assert.Equal(new[] { 8, 8, 16 }, scene.Rings.Where(ring => ring.Enabled).Select(ring => ring.Slots).ToArray());
		Assert.Equal(scene.MenuSizePx, scene.Rings.Last().DiameterPx);
		List<int> diameters = scene.Rings.Where(ring => ring.Enabled).Select(ring => ring.DiameterPx).ToList();
		for (int i = 1; i < diameters.Count; i++)
		{
			Assert.True(diameters[i] > diameters[i - 1], "Ring diameters must grow outwards.");
		}
	}

	[Fact]
	public void ShrinkingRingCapacityMovesBindingsToTheUnassignedPool()
	{
		SceneMenuConfig scene = Scene();
		scene.Rings[0].Slots = 16;
		Fill(scene, 0, 16);

		// Recommendations all start at 8 slots on the inner ring, so the eight
		// bindings that no longer fit are pooled rather than dropped.
		WheelRingPresets.Apply(scene, new[] { 8, 8 });

		Assert.Equal(8, scene.Rings[0].Slots);
		Assert.Equal(8, scene.Rings[0].Items.Count);
		Assert.Equal(8, scene.UnassignedItems.Count);
		Assert.All(scene.UnassignedItems, item => Assert.False(string.IsNullOrWhiteSpace(item.Value)));
	}

	[Fact]
	public void HiddenRingsKeepTheirBindings()
	{
		SceneMenuConfig scene = Scene();
		WheelRingPresets.Apply(scene, new[] { 8, 8, 16 });
		Fill(scene, 2, 3);

		WheelRingPresets.Apply(scene, new[] { 8, 8 });

		// Only slots beyond the new capacity are pooled; the rest stay on the ring.
		Assert.Equal(3, scene.Rings[2].Items.Count);
		Assert.Empty(scene.UnassignedItems);
	}

	[Fact]
	public void UnrelatedSlotCountsAreRejected()
	{
		SceneMenuConfig scene = Scene();
		Assert.Throws<ArgumentException>(() => WheelRingPresets.Apply(scene, new[] { 6, 6 }));
	}
}
