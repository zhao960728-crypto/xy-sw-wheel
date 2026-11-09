using System;
using System.Collections.Generic;

namespace XY.SolidWorksPlugin;

internal static class TriggerModes
{
	public const string RightHold = "right-hold";

	public const string MiddleHold = "middle-hold";

	public const string ShiftRightHold = "shift-right-hold";

	public const string AltRightHold = "alt-right-hold";

	public const string Disabled = "disabled";

	private static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["right-hold"] = "右键长按",
		["middle-hold"] = "中键长按",
		["shift-right-hold"] = "Shift + 右键长按",
		["alt-right-hold"] = "Alt + 右键长按"
		,["disabled"] = "不激活轮盘"
	};

	public static IReadOnlyDictionary<string, string> All => Names;

	public static string Normalize(string? value)
	{
		string text = (value ?? "").Trim();
		if (!Names.ContainsKey(text))
		{
			return "right-hold";
		}
		return text;
	}

	public static string Display(string value)
	{
		if (!Names.TryGetValue(value, out string value2))
		{
			return Names["right-hold"];
		}
		return value2;
	}
}
