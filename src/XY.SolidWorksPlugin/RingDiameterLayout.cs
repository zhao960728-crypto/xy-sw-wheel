using System;
using System.Collections.Generic;
using System.Linq;

namespace XY.SolidWorksPlugin;

internal static class RingDiameterLayout
{
	internal const int MinimumDiameterStepPx = 16;

	public static void Normalize(SceneMenuConfig scene)
	{
		if (scene?.Rings == null)
		{
			return;
		}

		var rings = scene.Rings
			.Where(ring => ring != null && ring.Enabled)
			.OrderBy(ring => ring.Index)
			.ToList();
		if (rings.Count == 0)
		{
			return;
		}

		int wheelDiameter = Clamp(scene.MenuSizePx, 260, 900);
		int centerDiameter = EffectiveCenterDiameter(scene, wheelDiameter);

		// Equal bands are the default. A ring that has no diameter yet (a ring
		// just added, or a scene written before diameters existed) forces the
		// same recompute so the wheel never shows a half-migrated layout.
		if (scene.EqualRingBands)
		{
			DistributeEvenly(rings, centerDiameter, wheelDiameter);
			return;
		}

		if (rings.Any(ring => ring.DiameterPx <= 0))
		{
			int totalWeight = Math.Max(1, rings.Sum(ring => Math.Max(1, ring.SizeWeight)));
			int cumulative = 0;
			foreach (RingConfig ring in rings)
			{
				cumulative += Math.Max(1, ring.SizeWeight);
				ring.DiameterPx = centerDiameter
					+ (int)Math.Round((wheelDiameter - centerDiameter) * cumulative / (double)totalWeight);
			}
		}

		int step = Math.Max(1, Math.Min(MinimumDiameterStepPx,
			Math.Max(rings.Count, wheelDiameter - centerDiameter) / rings.Count));
		int previous = centerDiameter;
		for (int index = 0; index < rings.Count; index++)
		{
			int remaining = rings.Count - index - 1;
			int minimum = previous + step;
			int maximum = wheelDiameter - remaining * step;
			rings[index].DiameterPx = index == rings.Count - 1
				? wheelDiameter
				: Clamp(rings[index].DiameterPx, minimum, maximum);
			previous = rings[index].DiameterPx;
		}
	}

	// One band width for every ring: the span between the hub edge and the
	// wheel edge is cut into as many equal parts as there are rings.
	private static void DistributeEvenly(List<RingConfig> rings, int centerDiameter, int wheelDiameter)
	{
		int span = Math.Max(rings.Count, wheelDiameter - centerDiameter);
		for (int index = 0; index < rings.Count; index++)
		{
			rings[index].DiameterPx = centerDiameter
				+ (int)Math.Round(span * (index + 1) / (double)rings.Count);
		}
	}

	public static int EffectiveCenterDiameter(SceneMenuConfig scene, int wheelDiameter, int fallbackCenterRadius = 44)
	{
		return EffectiveCenterRadius(scene, wheelDiameter, fallbackCenterRadius) * 2;
	}

	// Radius of the neutral hub. It is shared by the layout and by the renderer
	// so both cut the wheel from the same inner edge; before they disagreed, the
	// renderer's larger hub silently stole width from the innermost ring.
	// The hub stays at the configured value in every style: a single-ring wheel
	// with a small configured hub keeps the small hub and lets its ring band
	// reach the rim. The cells in that band are arc-limited, so they grow with
	// the band up to a sensible cap and no further - matching the CAD wheel,
	// whose single-ring hub does not balloon to fill the disc.
	public static int EffectiveCenterRadius(SceneMenuConfig scene, int wheelDiameter, int fallbackCenterRadius = 44)
	{
		if (scene == null)
		{
			return Math.Max(20, Math.Min(wheelDiameter / 3, fallbackCenterRadius));
		}

		int configured = Clamp(scene.CenterRadiusPx > 0 ? scene.CenterRadiusPx : fallbackCenterRadius,
			20, wheelDiameter / 3);
		int count = Math.Max(1, scene.Rings?.Count(ring => ring != null && ring.Enabled) ?? 1);
		int maxRadius = Math.Max(8, wheelDiameter / 2 - 8 * count);
		return Math.Max(8, Math.Min(configured, maxRadius));
	}

	public static void SplitOutermostRing(SceneMenuConfig scene, int previousCount)
	{
		if (scene?.Rings == null)
		{
			return;
		}
		var rings = scene.Rings.Where(ring => ring.Enabled).OrderBy(ring => ring.Index).ToList();
		if (rings.Count <= previousCount || rings.Count < 2)
		{
			Normalize(scene);
			return;
		}

		if (scene.EqualRingBands)
		{
			// The new ring has no diameter yet, so the shared recompute gives
			// the ring that used to be the outer edge the same width as well.
			rings[rings.Count - 1].DiameterPx = 0;
			Normalize(scene);
			return;
		}

		Normalize(scene);
		int oldOuterIndex = rings.Count - 2;
		int innerDiameter = oldOuterIndex > 0
			? rings[oldOuterIndex - 1].DiameterPx
			: EffectiveCenterDiameter(scene, Clamp(scene.MenuSizePx, 260, 900));
		rings[oldOuterIndex].DiameterPx = innerDiameter
			+ Math.Max(MinimumDiameterStepPx, (scene.MenuSizePx - innerDiameter) / 2);
		rings[rings.Count - 1].DiameterPx = scene.MenuSizePx;
		Normalize(scene);
	}

	private static int Clamp(int value, int minimum, int maximum)
	{
		return Math.Max(minimum, Math.Min(maximum, value));
	}
}
