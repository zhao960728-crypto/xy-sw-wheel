using System;
using System.Collections.Generic;
using System.Linq;

namespace XY.SolidWorksPlugin;

internal static class BasicActions
{
	private static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["settings"] = "轮盘设置",
		["watermark-settings"] = "属性水印设置",
		["explode-all"] = "一键爆炸",
			["process-recognition"] = "工序识别",
		["new-part"] = "新建",
		["new-assembly"] = "新建装配体",
		["new-drawing"] = "新建工程图",
		["rebuild"] = "重建",
		["force-rebuild"] = "强制重建",
		["normal-to"] = "视图正向",
		["zoom-fit"] = "适合窗口",
		["clear-selection"] = "清除选择",
		["view-front"] = "前视",
		["view-back"] = "后视",
		["view-left"] = "左视",
		["view-right"] = "右视",
		["view-top"] = "上视",
		["view-bottom"] = "下视",
		["view-isometric"] = "等轴测"
	};

	public static IReadOnlyDictionary<string, string> All => Names.Where(pair => !IsRetired(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

	internal static bool IsRetired(string value) => !ProductFeatureScope.IsAllowedBasicAction(value);

	internal static bool TryGetNativeCommandId(string value, out int commandId)
	{
		string key = (value ?? "").Trim().ToLowerInvariant();
		// One-click explode is a plugin workflow, even though it uses a native icon.
		commandId = -1;
		return key != "explode-all" && NativeWheelIconMappings.TryGetBasicCommandId(key, out commandId);
	}

	public static bool Contains(string? value)
	{
		string text = value?.Trim();
		if (text != null && text.Length > 0)
		{
			return Names.ContainsKey(text);
		}
		return false;
	}

	/// <summary>Resolves either the persisted key or the user-facing name.</summary>
	internal static bool TryResolve(string value, out string key)
	{
		key = "";
		string text = Normalize(value);
		if (text.Length == 0) return false;
		List<KeyValuePair<string, string>> matches = All
			.Where(pair => Normalize(pair.Key) == text || SearchNames(pair.Key).Any(name => Normalize(name) == text))
			.ToList();
		if (matches.Count != 1) return false;
		key = matches[0].Key;
		return true;
	}

	internal static IEnumerable<string> SearchNames(string value)
	{
		yield return Display(value);
		// Keep the old caption searchable without adding another command entry.
		if (string.Equals(value, "explode-all", StringComparison.OrdinalIgnoreCase))
			yield return "一键爆炸全部（自动散开）";
	}

	public static string Display(string value)
	{
		if (!Names.TryGetValue(value, out string value2))
		{
			return value;
		}
		return value2;
	}

	private static string Normalize(string value)
	{
		return string.Join(" ", (value ?? "").Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
	}
}
