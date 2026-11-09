using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swcommands;

namespace XY.SolidWorksPlugin;

internal static class SolidWorksNativeCommandCatalog
{
	private static readonly Regex AcceleratorPattern = new Regex(@"\(&[^)]?\)?", RegexOptions.Compiled);
	private static readonly HashSet<int> NativeCommandIds = new HashSet<int>(
		Enum.GetValues(typeof(swCommands_e)).Cast<swCommands_e>().Select(value => (int)value).Where(value => value >= 0));
	// Bind the cache to the actual host proxy, never to a process name, caption
	// or static menu map. A different RCW/proxy conservatively starts cold.
	private static readonly ConditionalWeakTable<ISldWorks, LegacyHostCache> LegacyCaches = new ConditionalWeakTable<ISldWorks, LegacyHostCache>();

	private sealed class LegacyHostCache
	{
		public LegacyHostCache() { }
		internal readonly object Sync = new object();
		internal readonly Dictionary<int, Dictionary<string, int>> Contexts = new Dictionary<int, Dictionary<string, int>>();
	}

	private static readonly HashSet<string> SketchMenus = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase)
	{
		"草图绘制实体", "草图工具", "草图设置", "草图油墨", "尺寸", "关系", "快速捕捉", "样条曲线工具",
		"2D 到 3D", "对齐", "块", "笔工具", "Sketch Entities", "Sketch Tools", "Sketch Settings",
		"Dimensions", "Relations", "Quick Snaps", "Spline Tools", "2D to 3D", "Align", "Blocks"
	};

	private static readonly HashSet<string> FeatureMenus = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase)
	{
		"特征", "孔", "参考几何体", "曲线", "阵列/镜向", "直接编辑", "Features", "Hole",
		"Reference Geometry", "Curves", "Pattern/Mirror", "Direct Editing"
	};

	private static readonly HashSet<string> DoctorSheetMetalShortcutCommands = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase)
	{
		"属性处理", "删特征", "删除特征", "快速钣金", "门板切口", "快速工程图", "快速拆图",
		"设计树材质", "系数纠错", "统计数量", "自动出图", "STEP格式互换", "散件组装配",
		"设定固定面", "设置为正视图", "删除圆角"
	};


	public static bool TryRead(ISldWorks solidWorks, out List<KeyboardShortcutRow> rows, out string message)
	{
		return TryRead(solidWorks, null, out rows, out message);
	}

	public static bool TryRead(ISldWorks solidWorks, ICommandManager commandManager, out List<KeyboardShortcutRow> rows, out string message)
	{
		rows = new List<KeyboardShortcutRow>();
		message = "";
		if (solidWorks == null)
		{
			message = "没有连接到 SOLIDWORKS。";
			return false;
		}

		try
		{
			IModelDoc2 document = solidWorks.ActiveDoc as IModelDoc2;
			rows = ReadRows(solidWorks, document, commandManager);
			if (rows.Count == 0)
			{
				message = "当前环境没有读到可识别的 SOLIDWORKS 原生命令。可打开零件、装配体或工程图后重新读取。";
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			LegacyCaches.Remove(solidWorks);
			Logger.Error("Read SOLIDWORKS native command catalog failed", ex);
			message = ex.Message;
			return false;
		}
	}

	public static bool TryExecute(ISldWorks solidWorks, string value, out string message)
	{
		message = "";
		if (solidWorks == null)
		{
			message = "没有连接到 SOLIDWORKS。";
			return false;
		}

		bool externalToolbar = ExternalToolbarCommandReference.TryDecode(value, out int expectedDocumentType, out int commandId);
		if (externalToolbar)
		{
			IModelDoc2 document = solidWorks.ActiveDoc as IModelDoc2;
			if (document == null)
			{
				message = "请先打开零件、装配体或工程图后再执行外部插件命令。";
				return false;
			}
			if (document.GetType() != expectedDocumentType)
			{
				message = "该外部插件命令不适用于当前文档类型，请在原文档环境重新绑定。";
				return false;
			}
			string expectedPath = ExternalToolbarCommandReference.Path(value);
			string expectedName = ExternalToolbarCommandReference.Name(value);
			if (expectedPath.Length == 0 || expectedName.Length == 0)
			{
				message = "该旧版外部插件命令未保存完整插件路径，请重新读取并绑定；原配置保留，未执行猜测命令。";
				return false;
			}
			return ExternalPluginCommandCatalog.TryExecuteCurrent(solidWorks, expectedDocumentType, expectedPath, expectedName, out message);
		}
		else if (ExternalPluginCommandReference.TryDecodeId(value, out commandId))
		{
			message = "该外部插件命令来自旧版无环境映射，请重新读取外部插件后再设置。";
			return false;
		}
		else if (!NativeCommandReference.TryDecodeId(value, out commandId))
		{
			if (!TryResolveLegacyReference(solidWorks, value, out commandId))
			{
				message = "该 SOLIDWORKS 原生命令来自旧版读取结果，请重新读取 SW 命令后再设置。";
				return false;
			}
		}

		try
		{
			if (!NativeCommandIds.Contains(commandId))
			{
				message = "该标识不是当前 SOLIDWORKS API 可确认的原生命令，请重新读取；第三方工具栏命令须从外部插件入口设置。";
				return false;
			}
			NativeMenuDispatchResult menuResult = NativeVerifiedMenuRoute.TryExecute(solidWorks, commandId, out message);
			if (menuResult == NativeMenuDispatchResult.Submitted) return true;
			if (menuResult == NativeMenuDispatchResult.Rejected) return false;
			NativeCommandLaunchTrace.Mark("native-api-fallback", NativeCommandReference.Encode(commandId));
			if (solidWorks.RunCommand(commandId, ""))
			{
				return true;
			}
			message = "SOLIDWORKS 当前状态不能执行该命令。请确认文档类型、选择对象和草图编辑状态符合命令要求。";
			return false;
		}
		catch (Exception ex)
		{
			message = ex.Message;
			return false;
		}
	}

	public static void ClassifyShortcutRows(ISldWorks solidWorks, IEnumerable<KeyboardShortcutRow> rows)
	{
		if (rows == null)
		{
			return;
		}

		Dictionary<string, string> nativeCommands = ReadNativeCommandCategories(solidWorks);
		Dictionary<string, string> knownPluginCommands = ReadKnownPluginCommands();
		foreach (KeyboardShortcutRow row in rows.Where(item => item != null))
		{
			string originalCategory = NormalizeText(row.Category);
			string commandKey = CommandKey(row.Command);
			if (commandKey.Length == 0)
			{
				continue;
			}
			row.Category = ClassifyShortcutCategory(originalCategory, row.Command, nativeCommands, knownPluginCommands);
		}
	}

	internal static List<KeyboardShortcutRow> ExcludeSeparatelyReadPluginShortcuts(IEnumerable<KeyboardShortcutRow> rows)
	{
		return (rows ?? Enumerable.Empty<KeyboardShortcutRow>())
			.Where(row => row != null && !IsSeparatelyReadPluginShortcutCategory(row.Category))
			.ToList();
	}

	internal static string ClassifyShortcutCategory(string originalCategory, string command,
		IReadOnlyDictionary<string, string> nativeCommands,
		IReadOnlyDictionary<string, string> knownPluginCommands)
	{
		string original = NormalizeText(originalCategory);
		string key = CommandKey(command);
		if (LooksLikePluginCategory(original))
		{
			return "插件快捷键 > " + TrimCategoryPrefix(original);
		}
		if (IsDoctorSheetMetalShortcut(command))
		{
			return "插件快捷键 > 博士钣金";
		}
		if (key.Length > 0 && knownPluginCommands != null && knownPluginCommands.TryGetValue(key, out string pluginCategory))
		{
			return "插件快捷键 > " + TrimCategoryPrefix(pluginCategory);
		}
		if (key.Length > 0 && nativeCommands != null && nativeCommands.TryGetValue(key, out string nativeCategory))
		{
			return nativeCategory.Replace("SOLIDWORKS > ", "SOLIDWORKS 快捷键 > ");
		}
		string fallback = original.Length == 0 ? "未分类" : original;
		return "其他快捷键 > " + TrimCategoryPrefix(fallback);
	}


	internal static List<KeyboardShortcutRow> BuildRows(string category, IEnumerable<string> commandNames)
	{
		string normalizedCategory = NormalizeText(category);
		return (commandNames ?? Enumerable.Empty<string>())
			.Select(NormalizeText)
			.Where(name => name.Length > 0)
			.Distinct(StringComparer.CurrentCultureIgnoreCase)
			.Select(name => CreateRow(normalizedCategory, name, -1))
			.ToList();
	}

	internal static string BuildCategory(string parentMenu)
	{
		string parent = NormalizeText(parentMenu);
		if (SketchMenus.Contains(parent) || parent.IndexOf("草图", StringComparison.CurrentCultureIgnoreCase) >= 0)
		{
			return "SOLIDWORKS > 草图 > " + parent;
		}
		if (FeatureMenus.Contains(parent))
		{
			return "SOLIDWORKS > 特征 > " + parent;
		}
		if (parent.IndexOf("钣金", StringComparison.CurrentCultureIgnoreCase) >= 0 || parent.IndexOf("Sheet Metal", StringComparison.CurrentCultureIgnoreCase) >= 0)
		{
			return "SOLIDWORKS > 钣金 > " + parent;
		}
		if (ContainsAny(parent, "装配体", "零部件", "配合", "Assembly", "Component", "Mate"))
		{
			return "SOLIDWORKS > 装配体 > " + parent;
		}
		if (ContainsAny(parent, "工程图", "图纸", "注解", "表格", "视图布局", "Drawing", "Sheet", "Annotation", "Table"))
		{
			return "SOLIDWORKS > 工程图 > " + parent;
		}
		if (ContainsAny(parent, "显示", "隐藏/显示", "修改", "视图", "Display", "Hide/Show", "View"))
		{
			return "SOLIDWORKS > 视图 > " + parent;
		}
		if (ContainsAny(parent, "评估", "Evaluate"))
		{
			return "SOLIDWORKS > 评估 > " + parent;
		}
		return "SOLIDWORKS > 菜单 > " + (parent.Length == 0 ? "其他" : parent);
	}

	private static List<KeyboardShortcutRow> ReadRows(ISldWorks solidWorks, IModelDoc2 document, ICommandManager commandManager = null)
		=> ReadRows(solidWorks, document, commandManager, out _);

	private static List<KeyboardShortcutRow> ReadRows(ISldWorks solidWorks, IModelDoc2 document, ICommandManager commandManager, out bool complete)
	{
		int documentType = document == null ? 0 : document.GetType();
		complete = true;
		Dictionary<int, KeyboardShortcutRow> commands = new Dictionary<int, KeyboardShortcutRow>();
		foreach (int commandId in NativeCommandIds.OrderBy(value => value))
		{
			if (commandId < 0)
			{
				continue;
			}
			string parentMenu;
			string menuText;
			try
			{
				menuText = solidWorks.GetMenuStrings(commandId, documentType, out parentMenu);
			}
			catch
			{
				complete = false;
				continue;
			}
			KeyboardShortcutRow row = BuildNativeRow(commandId, menuText, parentMenu);
			if (row != null) commands[commandId] = row;
		}
		complete &= ReadCommandManagerRows(solidWorks, document, commandManager, documentType, commands);
		try
		{
			IModelDoc2 current = solidWorks.ActiveDoc as IModelDoc2;
			complete &= Equals(document, current) && (current == null ? 0 : current.GetType()) == documentType;
		}
		catch { complete = false; }
		List<KeyboardShortcutRow> rows = OrderDistinctRows(commands.Values);
		PublishLegacySnapshot(solidWorks, documentType, complete ? rows : null);
		return rows;
	}

	internal static List<KeyboardShortcutRow> OrderDistinctRows(IEnumerable<KeyboardShortcutRow> rows)
	{
		// Display names are not command identities: distinct native IDs may share a menu label.
		return rows.Where(row => row != null)
			.GroupBy(row => row.Value, StringComparer.OrdinalIgnoreCase)
			.Select(group => group.First())
			.OrderBy(row => row.Category, StringComparer.CurrentCultureIgnoreCase)
			.ThenBy(row => row.Command, StringComparer.CurrentCultureIgnoreCase)
			.ThenBy(row => NativeCommandId(row.Value))
			.ToList();
	}

	internal static KeyboardShortcutRow BuildNativeRow(int commandId, string menuText, string parentMenu, string tabName = null)
	{
		// CommandManager also contains add-in/runtime toolbar IDs; these are not RunCommand IDs.
		if (!NativeCommandIds.Contains(commandId)) return null;
		string commandName = ParseCommandName(menuText);
		string parent = NormalizeText(parentMenu);
		// A curated translation alone is not evidence that this command exists in the current context.
		if (commandName.Length == 0 && parent.Length == 0) return null;
		if (commandName.Length == 0) commandName = KnownChineseCommandName(commandId);
		if (commandName.Length == 0) return null;
		string category = parent.Length > 0 ? BuildCategory(parent) : KnownCommandCategory(commandId);
		if (category.Length == 0) category = BuildCategory(tabName);
		return CreateRow(category, commandName, commandId);
	}

	private static int NativeCommandId(string value)
	{
		return NativeCommandReference.TryDecodeId(value, out int commandId) ? commandId : int.MaxValue;
	}

	private static bool ReadCommandManagerRows(ISldWorks solidWorks, IModelDoc2 document, ICommandManager commandManager,
		int documentType, IDictionary<int, KeyboardShortcutRow> commands)
	{
		if (commandManager == null || document == null)
		{
			return true;
		}
		bool complete = true;
		Dictionary<string, ICommandTab> tabs = new Dictionary<string, ICommandTab>(StringComparer.CurrentCultureIgnoreCase);
		try
		{
			foreach (object value in EnumerateArray(commandManager.CommandTabs(documentType)))
			{
				if (value is ICommandTab tab)
				{
					string name = NormalizeText(tab.Name);
					if (name.Length > 0)
					{
						tabs[name] = tab;
					}
				}
			}
		}
		catch (Exception ex)
		{
			complete = false;
			Logger.Trace("Read CommandManager tab collection skipped: " + ex.Message);
		}

		try
		{
			object names = document.Extension.GetCommandTabs();
			foreach (object value in EnumerateArray(names))
			{
				string name = NormalizeText(Convert.ToString(value));
				if (name.Length == 0 || tabs.ContainsKey(name))
				{
					continue;
				}
				CommandTab commandTab = commandManager.GetCommandTab(documentType, name);
				if (commandTab is ICommandTab tab)
				{
					tabs[name] = tab;
				}
			}
		}
		catch (Exception ex)
		{
			complete = false;
			Logger.Trace("Read CommandManager tab names skipped: " + ex.Message);
		}

		foreach (KeyValuePair<string, ICommandTab> tabEntry in tabs)
		{
			try
			{
				foreach (object boxValue in EnumerateArray(tabEntry.Value.CommandTabBoxes()))
				{
					if (!(boxValue is ICommandTabBox box))
					{
						continue;
					}
					box.GetCommands(out object commandIds, out object _);
					foreach (object idValue in EnumerateArray(commandIds))
					{
						if (!TryConvertCommandId(idValue, out int commandId) || commandId < 0)
						{
							continue;
						}
						// Only enrich an already API-resolved native command; never promote a toolbar ID.
						if (!commands.ContainsKey(commandId)) continue;
						KeyboardShortcutRow candidate;
						try
						{
							string menuText = solidWorks.GetMenuStrings(commandId, documentType, out string parentMenu);
							candidate = BuildNativeRow(commandId, menuText, parentMenu, tabEntry.Key);
						}
						catch { complete = false; continue; }
						if (candidate == null) continue;
						if (!commands.TryGetValue(commandId, out KeyboardShortcutRow existing)
							|| IsMoreSpecificCategory(candidate.Category, existing.Category))
						{
							commands[commandId] = candidate;
						}
					}
				}
			}
			catch (Exception ex)
			{
				complete = false;
				Logger.Trace("Read CommandManager tab skipped: " + tabEntry.Key + "; " + ex.Message);
			}
		}
		return complete;
	}

	private static string KnownChineseCommandName(int commandId)
	{
		string name = Enum.GetName(typeof(swCommands_e), commandId) ?? "";
		switch (name)
		{
			case "swCommands_LinearPattern":
			case "swCommands_Feat_Linear_Pattern":
				return "线性阵列";
			case "swCommands_CircularPattern":
			case "swCommands_Feat_Cir_Pattern":
				return "圆周阵列";
			case "swCommands_InsertFeatureMoveFace":
				return "移动面";
			case "swCommands_MoveCopyBodies":
				return "移动/复制实体";
			case "swCommands_DeleteFace":
				return "删除面";
			case "swCommands_DeleteSolidSurface":
				return "删除/保留实体";
			case "swCommands_ReplaceFace":
				return "替换面";
			case "swCommands_Fillet":
				return "圆角";
			case "swCommands_Chamfer":
				return "倒角";
			case "swCommands_InsertSplitFeat":
				return "分割";
			case "swCommands_Combine":
				return "组合";
			default:
				return "";
		}
	}

	private static string KnownCommandCategory(int commandId)
	{
		string name = Enum.GetName(typeof(swCommands_e), commandId) ?? "";
		switch (name)
		{
			case "swCommands_LinearPattern":
			case "swCommands_Feat_Linear_Pattern":
			case "swCommands_CircularPattern":
			case "swCommands_Feat_Cir_Pattern":
				return "SOLIDWORKS > 特征 > 阵列/镜向";
			case "swCommands_InsertFeatureMoveFace":
			case "swCommands_MoveCopyBodies":
			case "swCommands_DeleteFace":
			case "swCommands_DeleteSolidSurface":
			case "swCommands_ReplaceFace":
			case "swCommands_Fillet":
			case "swCommands_Chamfer":
			case "swCommands_InsertSplitFeat":
			case "swCommands_Combine":
				return "SOLIDWORKS > 特征 > 直接编辑";
			default:
				return "";
		}
	}

	private static bool IsMoreSpecificCategory(string candidate, string existing)
	{
		if ((existing ?? "").IndexOf(" > 菜单 > ", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return true;
		}
		if ((candidate ?? "").IndexOf("直接编辑", StringComparison.CurrentCultureIgnoreCase) >= 0
			&& (existing ?? "").IndexOf("直接编辑", StringComparison.CurrentCultureIgnoreCase) < 0)
		{
			return true;
		}
		return (candidate ?? "").Count(ch => ch == '>') > (existing ?? "").Count(ch => ch == '>');
	}

	private static bool TryConvertCommandId(object value, out int commandId)
	{
		commandId = -1;
		try
		{
			commandId = Convert.ToInt32(value);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static IEnumerable<object> EnumerateArray(object value)
	{
		if (value is Array array)
		{
			foreach (object item in array)
			{
				yield return item;
			}
		}
		else if (value != null)
		{
			yield return value;
		}
	}

	private static Dictionary<string, string> ReadNativeCommandCategories(ISldWorks solidWorks)
	{
		Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
		IModelDoc2 document = solidWorks?.ActiveDoc as IModelDoc2;
		if (document == null)
		{
			return result;
		}
		foreach (KeyboardShortcutRow row in ReadRows(solidWorks, document))
		{
			string key = CommandKey(row.Command);
			if (key.Length > 0 && !result.ContainsKey(key))
			{
				result[key] = row.Category;
			}
		}
		return result;
	}

	private static bool TryResolveLegacyReference(ISldWorks solidWorks, string value, out int commandId)
	{
		commandId = -1;
		if (!NativeCommandReference.TryDecode(value, out string category, out string command))
		{
			return false;
		}
		try
		{
			IModelDoc2 document = solidWorks.ActiveDoc as IModelDoc2;
			if (document == null) return false;
			int documentType = document.GetType();
			LegacyHostCache cache = LegacyCaches.GetOrCreateValue(solidWorks);
			string key = LegacyKey(category, command);
			lock (cache.Sync)
				if (cache.Contexts.TryGetValue(documentType, out Dictionary<string, int> snapshot) && snapshot.TryGetValue(key, out commandId)) return true;
			// Misses and ambiguous/failed reads stay cold so a newly available
			// command can recover without restarting SOLIDWORKS.
			List<KeyboardShortcutRow> rows = ReadRows(solidWorks, document, null, out bool complete);
			return complete && TryResolveLegacyRows(rows, category, command, out commandId);
		}
		catch (Exception ex)
		{
			LegacyCaches.Remove(solidWorks);
			Logger.Trace("Resolve legacy native identity deferred: " + ex.Message);
			return false;
		}
	}

	private static string LegacyCategory(string value)
	{
		string category = NormalizeText(value);
		return category.StartsWith("SOLIDWORKS > ", StringComparison.OrdinalIgnoreCase) ? category : "SOLIDWORKS > " + category;
	}

	private static string LegacyKey(string category, string command) => LegacyCategory(category) + "\u001f" + CommandKey(command);

	private static void PublishLegacySnapshot(ISldWorks solidWorks, int documentType, IEnumerable<KeyboardShortcutRow> rows)
	{
		LegacyHostCache cache = LegacyCaches.GetOrCreateValue(solidWorks);
		Dictionary<string, int> snapshot = null;
		if (rows != null)
		{
			snapshot = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
			foreach (var group in rows.GroupBy(row => LegacyKey(row.Category, row.Command), StringComparer.CurrentCultureIgnoreCase))
			{
				int[] ids = group.Select(row => NativeCommandId(row.Value)).Where(NativeCommandIds.Contains).Distinct().Take(2).ToArray();
				if (ids.Length == 1) snapshot[group.Key] = ids[0];
			}
		}
		lock (cache.Sync)
		{
			if (snapshot == null) cache.Contexts.Remove(documentType);
			else cache.Contexts[documentType] = snapshot;
		}
	}

	internal static bool TryResolveLegacyRows(IEnumerable<KeyboardShortcutRow> rows, string category, string command, out int commandId)
	{
		commandId = -1;
		List<KeyboardShortcutRow> matches = rows.Where(row => row != null
			&& CommandKey(row.Command).Equals(CommandKey(command), StringComparison.CurrentCultureIgnoreCase)).ToList();
		string normalizedCategory = LegacyCategory(category);
		List<KeyboardShortcutRow> exact = matches.Where(row => NormalizeText(row.Category)
			.Equals(normalizedCategory, StringComparison.CurrentCultureIgnoreCase)).ToList();
		// A saved category is part of the command path, even when only one
		// same-named native command remains elsewhere in the current catalog.
		matches = exact;
		int[] ids = matches.Select(row => NativeCommandId(row.Value)).Where(id => id != int.MaxValue).Distinct().ToArray();
		if (ids.Length != 1) return false;
		commandId = ids[0];
		return true;
	}

	private static Dictionary<string, string> ReadKnownPluginCommands()
	{
		Dictionary<string, HashSet<string>> candidates = new Dictionary<string, HashSet<string>>(StringComparer.CurrentCultureIgnoreCase);
		// Doctor's command IDs are exposed by the add-in toolbar rather than the
		// SOLIDWORKS native menu.  Include the embedded per-document mapping in
		// shortcut classification so every readable Doctor function keeps its
		// external identity instead of falling into “其它快捷键”.
		foreach (int documentType in new[] { 1, 2, 3 })
		{
			foreach (KeyboardShortcutRow row in ToolbarExternalCommandCatalog.KnownCatalog(documentType))
			{
				if (row != null && ((row.Category ?? "").IndexOf("博士", StringComparison.CurrentCultureIgnoreCase) >= 0
					|| (row.Category ?? "").IndexOf("Dr工具", StringComparison.CurrentCultureIgnoreCase) >= 0))
				{
					AddPluginCandidate(candidates, row.Command, row.Category);
				}
			}
		}
		if (QinJianCommandCatalog.TryRead(out List<KeyboardShortcutRow> qinJian, out _))
		{
			foreach (KeyboardShortcutRow row in qinJian)
			{
				AddPluginCandidate(candidates, row.Command, row.Category ?? "琴剑");
			}
		}
		if (JiaLiChuangFunctionCatalog.TryRead(out List<KeyboardShortcutRow> jiaLiChuang, out _))
		{
			foreach (KeyboardShortcutRow row in jiaLiChuang)
			{
				AddPluginCandidate(candidates, row.Command, row.Category ?? "嘉立创 Ican");
			}
		}
		return candidates
			.Where(item => item.Value.Count == 1)
			.ToDictionary(item => item.Key, item => item.Value.Single(), StringComparer.CurrentCultureIgnoreCase);
	}

	private static void AddPluginCandidate(IDictionary<string, HashSet<string>> candidates, string command, string category)
	{
		string key = CommandKey(command);
		string normalizedCategory = TrimCategoryPrefix(category);
		if (key.Length == 0 || normalizedCategory.Length == 0)
		{
			return;
		}
		if (!candidates.TryGetValue(key, out HashSet<string> categories))
		{
			categories = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
			candidates[key] = categories;
		}
		categories.Add(normalizedCategory);
	}

	private static bool LooksLikePluginCategory(string category)
	{
		string[] markers = { "琴剑", "博士", "嘉立创", "Ican", "开拔网", "盛兴", "启翔", "朗乐", "Dr工具", "防护栏", "方通" };
		return markers.Any(marker => category.IndexOf(marker, StringComparison.CurrentCultureIgnoreCase) >= 0);
	}

	private static bool IsSeparatelyReadPluginShortcutCategory(string category)
	{
		string value = TrimCategoryPrefix(category);
		return value.StartsWith("琴剑", StringComparison.CurrentCultureIgnoreCase)
			|| value.IndexOf("嘉立创", StringComparison.CurrentCultureIgnoreCase) >= 0
			|| value.IndexOf("Ican", StringComparison.CurrentCultureIgnoreCase) >= 0;
	}

	private static bool IsDoctorSheetMetalShortcut(string command)
	{
		return DoctorSheetMetalShortcutCommands.Contains(CommandKey(command));
	}

	private static string TrimCategoryPrefix(string category)
	{
		string value = NormalizeText(category);
		string[] prefixes = { "插件快捷键 > ", "其他快捷键 > ", "SOLIDWORKS 快捷键 > " };
		foreach (string prefix in prefixes)
		{
			if (value.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
			{
				return value.Substring(prefix.Length).Trim();
			}
		}
		return value;
	}

	private static KeyboardShortcutRow CreateRow(string category, string command, int commandId)
	{
		return new KeyboardShortcutRow
		{
			Category = category.StartsWith("SOLIDWORKS > ", StringComparison.OrdinalIgnoreCase) ? category : "SOLIDWORKS > " + category,
			Command = command,
			Hotkey = "已识别，执行时校验",
			ActionKind = ActionKinds.NativeCommand,
			Value = commandId >= 0 ? NativeCommandReference.Encode(commandId) : NativeCommandReference.Encode(category, command)
		};
	}

	private static string ParseCommandName(string menuText)
	{
		string text = menuText ?? "";
		int shortcut = text.IndexOf('\t');
		if (shortcut >= 0)
		{
			text = text.Substring(0, shortcut);
		}
		return NormalizeText(text).TrimEnd('.', '…').Trim();
	}

	private static string CommandKey(string value)
	{
		return NormalizeText(value).TrimEnd('.', '…').Trim();
	}

	private static string NormalizeText(string value)
	{
		string text = (value ?? "").Trim();
		text = AcceleratorPattern.Replace(text, "");
		// Windows menus escape a literal ampersand as &&; preserve it in the real command name.
		text = Regex.Replace(text, @"&&|&", match => match.Value.Length == 2 ? "&" : "");
		text = Regex.Replace(text, @"\s*\([A-Za-z0-9]\)$", "");
		return Regex.Replace(text, @"\s+", " ").Trim();
	}

	private static bool ContainsAny(string value, params string[] candidates)
	{
		return candidates.Any(candidate => value.IndexOf(candidate, StringComparison.CurrentCultureIgnoreCase) >= 0);
	}
}

internal static class NativeCommandReference
{
	private const string LegacyPrefix = "sw-native:";
	private const string IdPrefix = "sw-native-id:";

	public static string Encode(int commandId)
	{
		return IdPrefix + commandId;
	}

	public static bool TryDecodeId(string value, out int commandId)
	{
		commandId = -1;
		string text = (value ?? "").Trim();
		return text.StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase)
			&& int.TryParse(text.Substring(IdPrefix.Length), out commandId)
			&& commandId >= 0;
	}

	public static string Encode(string category, string command)
	{
		return LegacyPrefix + Uri.EscapeDataString((category ?? "").Trim()) + "/" + Uri.EscapeDataString((command ?? "").Trim());
	}

	public static bool TryDecode(string value, out string category, out string command)
	{
		category = "";
		command = "";
		string text = (value ?? "").Trim();
		if (!text.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		string payload = text.Substring(LegacyPrefix.Length);
		int separator = payload.IndexOf('/');
		if (separator <= 0 || separator >= payload.Length - 1)
		{
			return false;
		}
		category = Uri.UnescapeDataString(payload.Substring(0, separator)).Trim();
		command = Uri.UnescapeDataString(payload.Substring(separator + 1)).Trim();
		return category.Length > 0 && command.Length > 0;
	}
}
