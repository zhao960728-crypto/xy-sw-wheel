using System.Collections.Generic;

namespace XY.SolidWorksPlugin;

internal sealed class SceneMenuConfig
{
	// Null preserves pre-switch configurations, including the old sketch fallback.
	public bool? Enabled { get; set; }

	public string StyleId { get; set; } = "01";

	// "Auto" picks icon+text or text-only from the available cell space. Older
	// configurations keep their explicit "IconAndText" value.
	public string ContentMode { get; set; } = "Auto";

	public string IconColor { get; set; } = "Green";

	public string Key { get; set; } = "sw.blank";

	public string BackgroundImagePath { get; set; } = "";

	public int BackgroundOpacityPercent { get; set; } = 65;

	// Opacity of the drawn wheel surface itself, separate from the background.
	public int SurfaceOpacityPercent { get; set; } = 65;

	public int MenuSizePx { get; set; }

	public int CenterRadiusPx { get; set; }

	// Every ring gets the same radial width, whatever the ring count. Turning
	// this off hands the band widths back to the hand-tuned diameters, which is
	// the only way to make one ring deliberately thicker than another.
	public bool EqualRingBands { get; set; } = true;

	public WheelAppearance Appearance { get; set; } = new WheelAppearance();

	// Bindings removed when a ring shrinks. They are kept instead of dropped so
	// the user can put them back without re-picking every command.
	public List<MenuItemConfig> UnassignedItems { get; set; } = new List<MenuItemConfig>();

	public List<RingConfig> Rings { get; set; } = new List<RingConfig>();
}
