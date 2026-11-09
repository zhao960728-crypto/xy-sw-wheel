using System;
using System.Collections.Generic;
using System.Linq;

namespace XY.SolidWorksPlugin;

internal sealed class ShortcutChangeResult
{
	public string Location { get; set; } = "";
	public string Command { get; set; } = "";
	public string ConfiguredHotkey { get; set; } = "";
	public string SolidWorksHotkey { get; set; } = "";
	public string Status { get; set; } = "";
}

internal sealed class ShortcutConflictResult
{
	public string Hotkey { get; set; } = "";
	public IReadOnlyList<string> Commands { get; set; } = Array.Empty<string>();
}

internal static class ShortcutChangeDetector
{
	public static IReadOnlyList<ShortcutChangeResult> Compare(PluginConfig config, IEnumerable<KeyboardShortcutRow> currentRows)
	{
		List<KeyboardShortcutRow> rows = ValidRows(currentRows);
		Dictionary<string, KeyboardShortcutRow> byCommand = UniqueRowsByCommand(rows);
		Dictionary<string, KeyboardShortcutRow> byHotkey = rows
			.GroupBy(row => NormalizeHotkey(row.Hotkey), StringComparer.OrdinalIgnoreCase)
			.Where(group => group.Key.Length > 0)
			.ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
		List<ShortcutChangeResult> results = new List<ShortcutChangeResult>();
		foreach (ShortcutUsage usage in CollectUsages(config))
		{
			string commandKey = NormalizeCommand(usage.Command);
			string hotkeyKey = NormalizeHotkey(usage.Hotkey);
			if (hotkeyKey.Length == 0)
			{
				continue;
			}
			if (commandKey.Length > 0 && byCommand.TryGetValue(commandKey, out KeyboardShortcutRow commandRow))
			{
				if (!string.Equals(NormalizeHotkey(commandRow.Hotkey), hotkeyKey, StringComparison.OrdinalIgnoreCase))
				{
					results.Add(Result(usage, commandRow.Hotkey, "SOLIDWORKS 快捷键已修改"));
				}
				continue;
			}
			if (byHotkey.TryGetValue(hotkeyKey, out KeyboardShortcutRow hotkeyRow))
			{
				results.Add(Result(usage, hotkeyRow.Command + " / " + hotkeyRow.Hotkey, "原快捷键现在指向其他命令"));
			}
			else
			{
				results.Add(Result(usage, "", "SOLIDWORKS 列表未找到该命令"));
			}
		}
		return results;
	}

	public static IReadOnlyList<ShortcutConflictResult> FindConflicts(IEnumerable<KeyboardShortcutRow> currentRows)
	{
		return ValidRows(currentRows)
			.Where(row => NormalizeHotkey(row.Hotkey).Length > 0)
			.GroupBy(row => NormalizeHotkey(row.Hotkey), StringComparer.OrdinalIgnoreCase)
			.Select(group => new
			{
				Hotkey = group.First().Hotkey ?? "",
				Commands = group
					.GroupBy(row => NormalizeCommand(row.Command), StringComparer.OrdinalIgnoreCase)
					.Where(commandGroup => commandGroup.Key.Length > 0)
					.Select(commandGroup => commandGroup.First().Command ?? "")
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList()
			})
			.Where(item => item.Commands.Count > 1)
			.Select(item => new ShortcutConflictResult { Hotkey = item.Hotkey, Commands = item.Commands })
			.ToList();
	}

	public static bool HasConflict(IEnumerable<KeyboardShortcutRow> currentRows, string hotkey, out ShortcutConflictResult conflict)
	{
		string key = NormalizeHotkey(hotkey);
		conflict = FindConflicts(currentRows)
			.FirstOrDefault(item => string.Equals(NormalizeHotkey(item.Hotkey), key, StringComparison.OrdinalIgnoreCase));
		return conflict != null;
	}

	public static int Synchronize(PluginConfig config, IEnumerable<KeyboardShortcutRow> currentRows)
	{
		List<KeyboardShortcutRow> rows = ValidRows(currentRows);
		HashSet<string> conflictedHotkeys = new HashSet<string>(
			FindConflicts(rows).Select(conflict => NormalizeHotkey(conflict.Hotkey)),
			StringComparer.OrdinalIgnoreCase);
		Dictionary<string, KeyboardShortcutRow> byCommand = UniqueRowsByCommand(rows)
			.Where(pair => !conflictedHotkeys.Contains(NormalizeHotkey(pair.Value.Hotkey)))
			.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
		int updated = 0;
		foreach (MenuItemConfig item in CollectItems(config))
		{
			if (IsHotkey(item.Kind))
			{
				updated += SynchronizeValue(item.Label, item.Value, value => item.Value = value, byCommand);
			}
			foreach (ComboCommandConfig command in item.ComboCommands ?? Enumerable.Empty<ComboCommandConfig>())
			{
				if (command != null && IsHotkey(command.Kind))
				{
					updated += SynchronizeValue(command.Label, command.Value, value => command.Value = value, byCommand);
				}
			}
		}
		return updated;
	}

	private static int SynchronizeValue(string command, string configuredHotkey, Action<string> assign, IReadOnlyDictionary<string, KeyboardShortcutRow> byCommand)
	{
		string commandKey = NormalizeCommand(command);
		if (commandKey.Length == 0 || !byCommand.TryGetValue(commandKey, out KeyboardShortcutRow row) || string.IsNullOrWhiteSpace(row.Hotkey))
		{
			return 0;
		}
		if (string.Equals(NormalizeHotkey(configuredHotkey), NormalizeHotkey(row.Hotkey), StringComparison.OrdinalIgnoreCase))
		{
			return 0;
		}
		assign(row.Hotkey.Trim());
		return 1;
	}

	private static Dictionary<string, KeyboardShortcutRow> UniqueRowsByCommand(IEnumerable<KeyboardShortcutRow> rows)
	{
		return rows
			.GroupBy(row => NormalizeCommand(row.Command), StringComparer.OrdinalIgnoreCase)
			.Where(group => group.Key.Length > 0)
			.Where(group => group.Select(row => NormalizeHotkey(row.Hotkey)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
			.ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
	}

	private static List<KeyboardShortcutRow> ValidRows(IEnumerable<KeyboardShortcutRow> rows)
	{
		return (rows ?? Enumerable.Empty<KeyboardShortcutRow>()).Where(row => row != null).ToList();
	}

	private static ShortcutChangeResult Result(ShortcutUsage usage, string solidWorksHotkey, string status)
	{
		return new ShortcutChangeResult
		{
			Location = usage.Location,
			Command = usage.Command,
			ConfiguredHotkey = usage.Hotkey,
			SolidWorksHotkey = solidWorksHotkey ?? "",
			Status = status
		};
	}

	private static IEnumerable<MenuItemConfig> CollectItems(PluginConfig config)
	{
		if (config?.Menus == null)
		{
			yield break;
		}
		foreach (SceneMenuConfig scene in config.Menus.Values)
		{
			foreach (RingConfig ring in scene?.Rings ?? Enumerable.Empty<RingConfig>())
			{
				foreach (MenuItemConfig item in ring?.Items ?? Enumerable.Empty<MenuItemConfig>())
				{
					if (item != null)
					{
						yield return item;
					}
				}
			}
		}
	}

	private static IEnumerable<ShortcutUsage> CollectUsages(PluginConfig config)
	{
		if (config?.Menus == null)
		{
			yield break;
		}
		foreach (KeyValuePair<string, SceneMenuConfig> scenePair in config.Menus)
		{
			foreach (RingConfig ring in scenePair.Value?.Rings?.OrderBy(value => value.Index) ?? Enumerable.Empty<RingConfig>())
			{
				foreach (MenuItemConfig item in ring?.Items?.OrderBy(value => value.Slot) ?? Enumerable.Empty<MenuItemConfig>())
				{
					string location = $"{SceneKeys.Display(scenePair.Key)} 第 {ring.Index + 1} 圈 / 第 {item.Slot + 1} 格";
					if (IsHotkey(item.Kind))
					{
						yield return new ShortcutUsage(location, item.Label, item.Value);
					}
					for (int index = 0; index < (item.ComboCommands?.Count ?? 0); index++)
					{
						ComboCommandConfig command = item.ComboCommands[index];
						if (command != null && IsHotkey(command.Kind))
						{
							yield return new ShortcutUsage(location + $" 组合第 {index + 1} 步", command.Label, command.Value);
						}
					}
				}
			}
		}
	}

	private static bool IsHotkey(string kind)
	{
		string text = (kind ?? "").Trim();
		return text.Length == 0 || text.Equals(ActionKinds.Hotkey, StringComparison.OrdinalIgnoreCase);
	}

	internal static string NormalizeCommandForTest(string value) => NormalizeCommand(value);
	internal static string NormalizeHotkeyForTest(string value) => NormalizeHotkey(value);

	private static string NormalizeCommand(string value)
	{
		string text = (value ?? "").Trim();
		while (text.EndsWith(".", StringComparison.Ordinal) || text.EndsWith("。", StringComparison.Ordinal) || text.EndsWith("…", StringComparison.Ordinal))
		{
			text = text.Substring(0, text.Length - 1).Trim();
		}
		return new string(text.Where(ch => !char.IsWhiteSpace(ch) && !char.IsPunctuation(ch) && ch != '。' && ch != '…').ToArray());
	}

	private static string NormalizeHotkey(string value)
	{
		return new string((value ?? "").Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToUpperInvariant();
	}

	private readonly struct ShortcutUsage
	{
		public ShortcutUsage(string location, string command, string hotkey)
		{
			Location = location ?? "";
			Command = string.IsNullOrWhiteSpace(command) ? (hotkey ?? "") : command;
			Hotkey = hotkey ?? "";
		}

		public string Location { get; }
		public string Command { get; }
		public string Hotkey { get; }
	}
}
