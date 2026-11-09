using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swcommands;

namespace XY.SolidWorksPlugin;

/// <summary>Reads commands exposed by currently loaded third-party CommandManager tabs.</summary>
internal static class ExternalPluginCommandCatalog
{
	private static readonly HashSet<string> ExcludedAddinGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"{8D97B869-0F3D-4F13-901A-0A63D9D13A12}"
	};

	public static bool TryRead(ISldWorks solidWorks, ICommandManager commandManager, out List<KeyboardShortcutRow> rows, out string message)
	{
		TryReadLive(solidWorks, commandManager, out rows, out message);
		IModelDoc2 document = solidWorks?.ActiveDoc as IModelDoc2;
		int liveCount = rows.Count;
		// Installed command metadata and versioned mappings are still useful
		// before a document is opened.  Returning here used to leave the command
		// library empty in a blank SOLIDWORKS session, even though the plug-ins
		// were registered and their public commands could be read.
		int documentType = document?.GetType() ?? 1;
		try
		{
			ThirdPartyPluginFunctionCatalog.TryRead(out List<KeyboardShortcutRow> installed, out string _);
			var registeredNames = RegisteredPluginTabs().Values.Select(info => info.DisplayName).Distinct().ToArray();
			installed.AddRange(ToolbarExternalCommandCatalog.KnownCatalog(documentType).Where(row => registeredNames.Any(name => PluginNamesMatch(name, ExternalNamedCommandReference.PluginName(row.Category)))));
			foreach (var row in installed.Where(item => !ToolbarExternalCommandCatalog.IsRemovedCommand(-1, item?.Command)))
			{
				if (rows.Any(existing => ExternalMenuCommandCatalog.CleanName(existing.Command) == ExternalMenuCommandCatalog.CleanName(row.Command) && existing.Category == row.Category)) continue;
				row.Hotkey = "已安装 · 执行前匹配";
				row.ActionKind = ActionKinds.ExternalPlugin;
				// Static Doctor mappings can be replayed through the validated
				// command-id path.  Reflection/manifest-only commands have no
				// reliable ID; retain a UI identity so they can still be read and
				// clicked after the matching plugin tab is shown.
				if (document != null && ToolbarExternalCommandCatalog.TryResolveMappedCommand(documentType, row.Category, row.Command, out _))
				{
					row.Value = ExternalNamedCommandReference.Encode(documentType, row.Category, row.Command);
				}
				else
				{
					string pluginTab = ExternalNamedCommandReference.PluginName(row.Category);
					row.Hotkey = "已安装 · 界面执行";
					row.Value = ExternalPluginActionValue.Format(pluginTab, row.Command);
				}
				rows.Add(row);
			}
				rows = DistinctRows(rows);
			message = $"当前环境命令 {liveCount} 项，含已安装功能共 {rows.Count} 项。未加载功能在执行前重新匹配，保持插件显示设置。";
		}
		catch (Exception ex) { Logger.Error("Installed command metadata scan failed", ex); message += " 部分已安装功能未能读取：" + ex.Message; }
		return rows.Count > 0;
	}

	internal static bool TryReadLive(ISldWorks solidWorks, ICommandManager commandManager, out List<KeyboardShortcutRow> rows, out string message)
		=> ReadLive(solidWorks, commandManager, null, out rows, out message);

	internal static bool TryReadLivePath(ISldWorks solidWorks, ICommandManager commandManager, string category, out List<KeyboardShortcutRow> rows, out string message)
		=> ReadLive(solidWorks, commandManager, category, out rows, out message);

	private static bool ReadLive(ISldWorks solidWorks, ICommandManager commandManager, string category, out List<KeyboardShortcutRow> rows, out string message)
	{
		rows = new List<KeyboardShortcutRow>();
		if (solidWorks == null) { message = "未连接到 SOLIDWORKS。"; return false; }
		IModelDoc2 document = solidWorks.ActiveDoc as IModelDoc2;
		if (document == null) { message = "请先打开零件、装配体或工程图，再读取外部插件命令。"; return false; }

		try
		{
			ToolbarExternalCommandCatalog.TryReadForPath(solidWorks, document, category, out List<KeyboardShortcutRow> toolbarRows, out string _);
			rows.AddRange(toolbarRows);
			int documentType = document.GetType();
				foreach (var entry in ExternalMenuCommandCatalog.Read(solidWorks).Where(item => !ToolbarExternalCommandCatalog.IsRemovedCommand(item.Id, item.Name)))
				{
					string menuCategory = "第三方插件 > " + entry.Path;
					if (!MatchesCategory(category, menuCategory)) continue;
					rows.Add(new KeyboardShortcutRow { Category = menuCategory, Command = entry.Name,
						Hotkey = entry.Enabled ? "直接执行" : "需满足文档/选择条件", ActionKind = ActionKinds.ExternalPlugin,
						Value = ExternalToolbarCommandReference.Encode(document.GetType(), entry.Id, entry.Name, menuCategory) });
				}
			Dictionary<string, PluginInfo> pluginTabs = RegisteredPluginTabs();
			Dictionary<string, ICommandTab> commandTabs = ReadCommandTabs(document, commandManager, documentType, pluginTabs.Keys, category);
			foreach (KeyValuePair<string, ICommandTab> entry in commandTabs)
			{
				string tabName = entry.Key;
				if (tabName.IndexOf("XY", StringComparison.OrdinalIgnoreCase) >= 0) continue;
				if (!TryResolvePluginTab(pluginTabs, tabName, out PluginInfo plugin))
				{
					if (IsBuiltInSolidWorksTab(tabName)) continue;
					plugin = new PluginInfo { DisplayName = tabName };
				}
				ICommandTab tab = entry.Value;
				foreach (object boxValue in Enumerate(tab.CommandTabBoxes()))
				{
					if (!(boxValue is ICommandTabBox box)) continue;
					box.GetCommands(out object commandIds, out object _);
					foreach (object idValue in Enumerate(commandIds))
					{
						if (!TryCommandId(idValue, out int commandId) || ToolbarExternalCommandCatalog.IsRemovedCommand(commandId)) continue;
						string parent; string name = CleanName(solidWorks.GetMenuStrings(commandId, documentType, out parent));
						if (name.Length == 0 || ToolbarExternalCommandCatalog.IsRemovedCommand(commandId, name)) continue;
						rows.Add(new KeyboardShortcutRow { Category = "第三方插件 > " + tabName, Command = name, Hotkey = "直接执行", ActionKind = ActionKinds.ExternalPlugin, Value = ExternalToolbarCommandReference.Encode(documentType, commandId, name, "第三方插件 > " + tabName) });
					}
				}
			}
				rows = DistinctRows(rows);
			Logger.Trace($"External plugin command scan: tabs={commandTabs.Count}, registeredNames={pluginTabs.Count}, commands={rows.Count}");
			message = rows.Count == 0 ? "当前环境未发现已加载插件的命令。请先加载插件，并在适用的零件、装配体或工程图中读取。" : $"已从菜单、工具栏（含隐藏按钮）及标签页读取 {rows.Count} 个外部插件命令。";
			return rows.Count > 0;
		}
		catch (Exception ex)
		{
			Logger.Error("Read external plugin CommandManager commands skipped", ex);
			message = rows.Count > 0 ? $"已读取 {rows.Count} 个外部插件命令。" : ex.Message;
			return rows.Count > 0;
		}
	}

	internal static List<KeyboardShortcutRow> DistinctRows(IEnumerable<KeyboardShortcutRow> rows)
	{
		return (rows ?? Enumerable.Empty<KeyboardShortcutRow>())
			.Where(row => row != null)
			.GroupBy(row => (row.Category ?? "") + "\u001f" + (row.Command ?? "") + "\u001f" + (row.Value ?? ""), StringComparer.OrdinalIgnoreCase)
			.Select(group => group.First())
			.OrderBy(row => row.Category, StringComparer.CurrentCultureIgnoreCase)
			.ThenBy(row => row.Command, StringComparer.CurrentCultureIgnoreCase)
			.ToList();
	}

	/// <summary>Resolve only the persisted full path in the current document.</summary>
	internal static bool TryResolveCurrent(ISldWorks solidWorks, int documentType, string category, string name, out KeyboardShortcutRow row, out string message)
	{
		row = null;
		message = "";
		if (solidWorks == null) { message = "未连接到 SOLIDWORKS。"; return false; }
		if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(name)) { message = "插件功能缺少完整路径或名称，请重新绑定。"; return false; }
		try
		{
			IModelDoc2 document = solidWorks.ActiveDoc as IModelDoc2;
			if (document == null || document.GetType() != documentType)
			{ message = "该插件功能不适用于当前文档环境，未改投其他环境中的同名功能。"; return false; }
			TryReadLivePath(solidWorks, null, category, out List<KeyboardShortcutRow> live, out string readMessage);
			if (!Equals(document, solidWorks.ActiveDoc as IModelDoc2)) { message = "核验插件路径期间活动文档已变化，未改投新文档。"; return false; }
			if (TryResolveLiveRows(live, documentType, category, name, out row)) return true;
			message = "当前会话未找到唯一路径“" + category + " > " + name + "”，请加载原插件后重新读取绑定；未执行其他同名功能。";
			if (live.Count == 0 && !string.IsNullOrWhiteSpace(readMessage)) message += " " + readMessage;
			return false;
		}
		catch (Exception ex) { message = "核验插件功能路径失败：" + ex.Message; return false; }
	}

	internal static bool PathsEqual(string left, string right)
	{
		string Normalize(string path) => string.Join(" > ", ExternalNamedCommandReference.PluginName(path)
			.Split(new[] { '>' }, StringSplitOptions.RemoveEmptyEntries).Select(ExternalMenuCommandCatalog.CleanName));
		string a = Normalize(left), b = Normalize(right);
		return a.Length > 0 && b.Length > 0 && a.Equals(b, StringComparison.OrdinalIgnoreCase);
	}

	internal static bool MatchesCategory(string filter, string actual)
		=> filter == null || PathsEqual(filter, actual);

	internal static bool TryResolveLiveRows(IEnumerable<KeyboardShortcutRow> live, int documentType, string category, string name, out KeyboardShortcutRow row)
	{
		row = null;
		string wanted = ExternalMenuCommandCatalog.CleanName(name);
		if (wanted.Length == 0 || string.IsNullOrWhiteSpace(category) || ToolbarExternalCommandCatalog.IsRemovedCommand(-1, name)) return false;
		var matches = (live ?? Enumerable.Empty<KeyboardShortcutRow>()).Where(item => item != null
			&& PathsEqual(item.Category, category)
			&& ExternalMenuCommandCatalog.CleanName(item.Command).Equals(wanted, StringComparison.OrdinalIgnoreCase)
			&& ExternalToolbarCommandReference.TryDecode(item.Value, out int type, out int id) && type == documentType && id > 0
			&& PathsEqual(ExternalToolbarCommandReference.Path(item.Value), category)
			&& ExternalMenuCommandCatalog.CleanName(ExternalToolbarCommandReference.Name(item.Value)).Equals(wanted, StringComparison.OrdinalIgnoreCase))
			.GroupBy(item => item.Value, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
		if (matches.Count != 1) return false;
		row = matches[0];
		return true;
	}

	internal static bool TryExecuteCurrent(ISldWorks solidWorks, int documentType, string category, string name, out string message)
	{
		if (!TryResolveCurrent(solidWorks, documentType, category, name, out KeyboardShortcutRow row, out message)) return false;
		if (!ExternalToolbarCommandReference.TryDecode(row.Value, out _, out int commandId)) return false;
		return ToolbarExternalCommandCatalog.TryExecute(solidWorks, commandId, out message, row.Command, row.Category, currentIdentityVerified: true);
	}

	internal static bool TryExecuteUiReference(ISldWorks host, string tabName, string name, out bool handled, out string message)
	{
		handled = false; message = "";
		IModelDoc2 document = host?.ActiveDoc as IModelDoc2;
		if (document == null) return false;
		string category = "第三方插件 > " + tabName;
		TryReadLivePath(host, null, category, out var live, out _);
		string caption = ExternalMenuCommandCatalog.CleanName(name);
		if (!live.Any(row => PathsEqual(row.Category, category)
			&& ExternalMenuCommandCatalog.CleanName(row.Command).Equals(caption, StringComparison.OrdinalIgnoreCase))) return false;
		handled = true;
		if (!Equals(document, host.ActiveDoc as IModelDoc2)) { message = "查找插件功能期间文档已变化，未执行。"; return false; }
		if (!TryResolveLiveRows(live, document.GetType(), category, name, out var match))
		{ message = "当前插件路径中存在不唯一或失效的命令身份，未执行其他同名功能。"; return false; }
		if (!ExternalToolbarCommandReference.TryDecode(match.Value, out _, out int commandId)) return false;
		return ToolbarExternalCommandCatalog.TryExecute(host, commandId, out message, match.Command, match.Category, currentIdentityVerified: true);
	}

	private static bool TryResolvePluginTab(string pluginName, out PluginInfo plugin)
	{
		plugin = null;
		if (string.IsNullOrWhiteSpace(pluginName)) return false;
		Dictionary<string, PluginInfo> tabs = RegisteredPluginTabs();
		foreach (KeyValuePair<string, PluginInfo> pair in tabs)
			if (PluginNamesMatch(pair.Key, pluginName)) { plugin = pair.Value; return true; }
		return false;
	}

	// The mapping CSV preserves the command's user-facing family (for example
	// “博士钣金”), while the registry usually contains an English COM title
	// (“DoctorSheetMetal_Plus”).  Treat those names as the same add-in family so
	// installed commands are not silently dropped from the reader or replay path.
	internal static bool PluginNamesMatchForTest(string registeredName, string requestedName)
		=> PluginNamesMatch(registeredName, requestedName);

	private static bool PluginNamesMatch(string registeredName, string requestedName)
	{
		string registered = NormalizePluginName(registeredName);
		string requested = NormalizePluginName(requestedName);
		if (registered.Length == 0 || requested.Length == 0) return false;
		if (PluginFamilyMatch(registered, requested, new[] { "博士", "doctor", "drsheetmetal", "drtools", "dr工具" })) return true;
		if (PluginFamilyMatch(registered, requested, new[] { "琴剑", "qinjian", "kaiba" })) return true;
		if (PluginFamilyMatch(registered, requested, new[] { "嘉立创", "ican", "jlc" })) return true;
		if (PluginFamilyMatch(registered, requested, new[] { "启翔", "朗乐", "铝型材", "profile" })) return true;
		return registered.Equals(requested, StringComparison.OrdinalIgnoreCase)
			|| registered.IndexOf(requested, StringComparison.OrdinalIgnoreCase) >= 0
			|| requested.IndexOf(registered, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool PluginFamilyMatch(string registered, string requested, IEnumerable<string> markers)
	{
		string[] family = (markers ?? Array.Empty<string>()).Select(NormalizePluginName).Where(value => value.Length > 0).ToArray();
		bool registeredInFamily = family.Any(marker => registered.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
		bool requestedInFamily = family.Any(marker => requested.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
		return registeredInFamily && requestedInFamily;
	}

	private static string NormalizePluginName(string value)
		=> new string((value ?? "").Where(char.IsLetterOrDigit).ToArray());

	private sealed class PluginInfo
	{
		public string Guid { get; set; } = "";
		public string DisplayName { get; set; } = "";
	}

	private static Dictionary<string, PluginInfo> RegisteredPluginTabs()
	{
		Dictionary<string, PluginInfo> result = new Dictionary<string, PluginInfo>(StringComparer.OrdinalIgnoreCase);
		var seenGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		// SOLIDWORKS add-ins can be registered per-user or per-machine and in
		// either registry view.  Looking only at the process-default view made
		// installed Doctor/QinJian commands disappear from the reader on a
		// 32/64-bit mismatch.
		foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
		foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
		{
			try
			{
				using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
				using RegistryKey addins = baseKey.OpenSubKey("SOFTWARE\\SolidWorks\\Addins");
				if (addins == null) continue;
				foreach (string guid in addins.GetSubKeyNames())
				{
					if (ExcludedAddinGuids.Contains(guid) || seenGuids.Contains(guid)) continue;
					using RegistryKey addin = addins.OpenSubKey(guid);
					string title = Convert.ToString(addin?.GetValue("Title"))?.Trim() ?? "";
					string description = Convert.ToString(addin?.GetValue("Description"))?.Trim() ?? "";
					string display = description.Length > 0 ? description : title;
					if (display.Length == 0) continue;
					seenGuids.Add(guid);
					PluginInfo info = new PluginInfo { Guid = guid, DisplayName = display };
					if (title.Length > 0) result[title] = info;
					if (description.Length > 0) result[description] = info;
				}
			}
			catch (Exception ex) { Logger.Trace("Read SolidWorks add-in registry view skipped: " + hive + "/" + view + ": " + ex.Message); }
		}
		return result;
	}

	private static bool TryResolvePluginTab(IReadOnlyDictionary<string, PluginInfo> tabs, string tabName, out PluginInfo plugin)
	{
		plugin = null;
		foreach (KeyValuePair<string, PluginInfo> pair in tabs)
			if (PluginNamesMatch(pair.Key, tabName)) { plugin = pair.Value; return true; }
		return false;
	}

	private static bool IsBuiltInSolidWorksTab(string tabName)
	{
		string name = (tabName ?? "").Trim();
		return new HashSet<string>(StringComparer.CurrentCultureIgnoreCase)
		{
			"特征", "草图", "曲面", "钣金", "焊接件", "直接编辑", "标注", "评估", "SOLIDWORKS 插件", "SOLIDWORKS Add-Ins",
			"Features", "Sketch", "Surfaces", "Sheet Metal", "Weldments", "Direct Editing", "Markup", "Evaluate"
		}.Contains(name);
	}

	private static Dictionary<string, ICommandTab> ReadCommandTabs(IModelDoc2 document, ICommandManager commandManager, int documentType, IEnumerable<string> registeredPluginNames, string category = null)
	{
		Dictionary<string, ICommandTab> result = new Dictionary<string, ICommandTab>(StringComparer.CurrentCultureIgnoreCase);
		if (commandManager == null) return result;
		Action<string, ICommandTab> add = (name, tab) =>
		{
			if (!string.IsNullOrWhiteSpace(name) && tab != null && MatchesCategory(category, "第三方插件 > " + name)) result[name.Trim()] = tab;
		};
		try
		{
			foreach (object value in Enumerate(commandManager.CommandTabs(documentType)))
			{
				if (value is ICommandTab tab) add(tab.Name, tab);
			}
		}
		catch (Exception ex) { Logger.Trace("Read external plugin CommandManager tabs skipped: " + ex.Message); }
		try
		{
			foreach (object value in Enumerate(document.Extension.GetCommandTabs()))
			{
				string name = Convert.ToString(value)?.Trim();
				if (string.IsNullOrWhiteSpace(name) || !MatchesCategory(category, "第三方插件 > " + name)) continue;
				ICommandTab tab = commandManager.GetCommandTab(documentType, name) as ICommandTab;
				add(name, tab);
			}
		}
		catch (Exception ex) { Logger.Trace("Read external plugin document tabs skipped: " + ex.Message); }
		foreach (string name in registeredPluginNames ?? Array.Empty<string>())
		{
			if (string.IsNullOrWhiteSpace(name) || result.ContainsKey(name) || !MatchesCategory(category, "第三方插件 > " + name)) continue;
			try { add(name, commandManager.GetCommandTab(documentType, name) as ICommandTab); }
			catch (Exception ex) { Logger.Trace("Read external plugin registered tab skipped: " + ex.Message); }
		}
		return result;
	}

	private static IEnumerable<object> Enumerate(object value) => value is IEnumerable values ? values.Cast<object>() : Array.Empty<object>();
	private static bool TryCommandId(object value, out int commandId) { try { commandId = Convert.ToInt32(value); return commandId >= 0; } catch { commandId = -1; return false; } }
	private static string CleanName(string value) => (value ?? "").Replace("&", "").Replace("...", "").Trim();
}

/// <summary>Persists the plug-in identity and readable command metadata with the current-session command id.</summary>
internal static class ExternalPluginCommandReference
{
	private const string Prefix = "sw-external-plugin:";

	public static string Encode(string pluginGuid, string tabName, string commandName, int commandId)
	{
		return Prefix + Uri.EscapeDataString(pluginGuid ?? "") + "/" + Uri.EscapeDataString(tabName ?? "") + "/" + Uri.EscapeDataString(commandName ?? "") + "/" + commandId;
	}

	public static bool TryDecodeId(string value, out int commandId)
	{
		commandId = -1;
		string text = (value ?? "").Trim();
		if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
		int separator = text.LastIndexOf('/');
		return separator >= Prefix.Length && int.TryParse(text.Substring(separator + 1), out commandId) && commandId >= 0;
	}
}
