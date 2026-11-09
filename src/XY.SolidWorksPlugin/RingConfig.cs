using System.Collections.Generic;

namespace XY.SolidWorksPlugin;

internal sealed class RingConfig
{
	internal const int MaximumRingCount = 8;

	public int Index { get; set; }

	public string Name { get; set; } = "";

	// Shrinks or grows this ring's band against the diameter the layout computed.
	public int NodeInnerSizePercent { get; set; } = 100;
	public int NodeOuterSizePercent { get; set; } = 100;

	public int Slots { get; set; } = 8;

	public int SizeWeight { get; set; } = 1;

	// Outer diameter of this concentric ring, in wheel pixels. Zero means a
	// legacy size-weight value that still needs to be migrated.
	public int DiameterPx { get; set; }

	public bool Enabled { get; set; } = true;

	public List<MenuItemConfig> Items { get; set; } = new List<MenuItemConfig>();
}
