using System;
using System.Collections.Generic;
using System.Linq;

namespace XY.SolidWorksPlugin;

// Recommended ring layouts. They keep every ring evenly spaced and move bindings
// that no longer fit into the unassigned pool instead of dropping them.
internal static class WheelRingPresets
{
	internal static int[][] ForStyle(string style) => IsPresetStyle(style)
		? new[] { new[] { 8, 8 }, new[] { 8, 8, 16 }, new[] { 8, 8, 16, 16 } }
		: Array.Empty<int[]>();

	internal static bool IsPresetStyle(string style)
	{
		return WheelLayoutStyles.Find(style).SupportsRingPresets;
	}

	internal static void Apply(SceneMenuConfig scene, int[] slots)
	{
		if (scene == null || slots == null || !ForStyle(scene.StyleId).Any(preset => preset.SequenceEqual(slots)))
		{
			throw new ArgumentException("请选择当前轮盘的推荐设置。");
		}
		while (scene.Rings.Count < slots.Length)
		{
			scene.Rings.Add(new RingConfig { Index = scene.Rings.Count });
		}
		for (int index = 0; index < scene.Rings.Count; index++)
		{
			RingConfig ring = scene.Rings[index];
			ring.Index = index;
			ring.Enabled = index < slots.Length;
			if (!ring.Enabled) continue;
			ring.Slots = slots[index];
			// A confirmed recommendation defines a fresh, evenly spaced layout.
			// Hidden outer rings keep their commands, icons and appearance.
			int diameter = Clamp(scene.MenuSizePx > 0 ? scene.MenuSizePx : 520, 260, 900);
			int centerDiameter = Clamp(scene.CenterRadiusPx > 0 ? scene.CenterRadiusPx : 44, 20, diameter / 3) * 2;
			ring.DiameterPx = centerDiameter
				+ (int)Math.Round((diameter - centerDiameter) * (index + 1) / (double)slots.Length);
			scene.UnassignedItems.AddRange(ring.Items.Where(item => item.Slot >= ring.Slots));
			ring.Items.RemoveAll(item => item.Slot >= ring.Slots);
		}
		RingDiameterLayout.Normalize(scene);
	}

	private static int Clamp(int value, int minimum, int maximum) => Math.Max(minimum, Math.Min(maximum, value));
}
