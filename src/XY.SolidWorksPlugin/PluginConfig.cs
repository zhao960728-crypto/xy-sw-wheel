using System;
using System.Collections.Generic;

namespace XY.SolidWorksPlugin;

internal sealed class PluginConfig
{
	internal Dictionary<string, object> UnknownSettings { get; set; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

	public string TriggerMode { get; set; } = "right-hold";

	public int HoldDelayMs { get; set; } = 450;

	public int MenuSizePx { get; set; } = 520;

	public int CenterRadiusPx { get; set; } = 44;

	public int FontSizePx { get; set; } = 14;

	public string FontName { get; set; } = "SimSun";

	public bool FontBold { get; set; }

	public string TextEffect { get; set; } = TextEffects.None;

	public string TextColor { get; set; } = "#1E2935";

	public int OpacityPercent { get; set; } = 94;

	public bool RadialMenuEnabled { get; set; } = true;

	public bool ShortcutPaletteEnabled { get; set; } = true;

	public bool WheelCommandTabEnabled { get; set; } = true;

	public string WheelCommandTabName { get; set; } = "XY轮盘";

	public List<UserMacroCommand> UserMacros { get; set; } = new List<UserMacroCommand>();

	public bool ExplodeEnabled { get; set; } = true;

	public bool SketchWheelEnabled { get; set; }

	public bool GlobalComboAdvanceEnabled { get; set; }

	public string ComboAdvanceKey { get; set; } = "Ctrl";

	public PropertyWatermarkConfig PropertyWatermark { get; set; } = new PropertyWatermarkConfig();

	public ProcessRecognitionConfig ProcessRecognition { get; set; } = new ProcessRecognitionConfig();

	public Dictionary<string, SceneMenuConfig> Menus { get; set; } = new Dictionary<string, SceneMenuConfig>(StringComparer.OrdinalIgnoreCase);
}
