using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swcommands;

namespace XY.SolidWorksPlugin;

/// <summary>Reads named commands exposed by legacy SOLIDWORKS ToolbarWindow32 controls.</summary>
internal static class ToolbarExternalCommandCatalog
{
	private const string MappingResourceName = "XY.SolidWorksPlugin.ExternalCommandMappings.csv";
	private static readonly Lazy<Dictionary<int, Dictionary<int, MappedCommand>>> IconMappings = new Lazy<Dictionary<int, Dictionary<int, MappedCommand>>>(() => Enumerable.Range(1,3).ToDictionary(type => type, LoadMappedCommands));
	private const int TbButtonCount = 0x0418;
	private const int TbGetButton = 0x0417;
	private const int TbGetItemRect = 0x041D;
	private const int TbGetTooltips = 0x0423;
	private const int TtmGetTextW = 0x0438;
	private const int WmLeftButtonDown = 0x0201;
	private const int WmLeftButtonUp = 0x0202;
	private const int MouseKeyLeftButton = 0x0001;
	private const byte TbStateHidden = 0x08;

	[StructLayout(LayoutKind.Explicit, Size = 32)]
	private struct ToolbarButton
	{
		[FieldOffset(0)] public int ImageIndex;
		[FieldOffset(4)] public int CommandId;
		[FieldOffset(8)] public byte State;
		[FieldOffset(9)] public byte Style;
	}

	[DllImport("comctl32.dll")] private static extern IntPtr ImageList_GetIcon(IntPtr images, int index, uint flags);
	[DllImport("comctl32.dll")] private static extern int ImageList_GetImageCount(IntPtr images);
	[DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
	[DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
	[DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out Accessibility.IAccessible accessible);

	internal static Dictionary<int, Bitmap> ReadIcons(ISldWorks host, IEnumerable<int> requestedNativeIds, ISet<int> requestedRawIds, ISet<string> requestedNames, out Dictionary<string, int> names, out Dictionary<int, int> nativeIds)
	{
		var result = new Dictionary<int, Bitmap>();
		var commandNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		names = commandNames;
		var nativeCommands = new Dictionary<int, int>();
		nativeIds = nativeCommands;
		int documentType = (host.ActiveDoc as IModelDoc2)?.GetType() ?? 1;
		var neededNative = requestedNativeIds.Distinct().ToArray();
		IntPtr root = GetMainWindow(host);
		GetWindowThreadProcessId(root, out uint process);
		// TB_GETBUTTON passes a local pointer and HIMAGELIST is process-local.
		if (root == IntPtr.Zero || process != (uint)System.Diagnostics.Process.GetCurrentProcess().Id) return result;
		var menuNames = ExternalMenuCommandCatalog.Read(host).GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First().Name);
		var requestedIds = new HashSet<int>(requestedRawIds);
		// Named references do not carry an ID, and a CommandManager tab may not
		// publish its buttons through the main menu. Use the versioned mapping as
		// an icon hint while still verifying the actual toolbar button below.
		foreach (KeyValuePair<int, MappedCommand> mapped in LoadMappedCommands(documentType))
			if (requestedNames.Contains(RadialCommandIcons.NameKey(mapped.Value.Name))) requestedIds.Add(mapped.Key);
		foreach (var group in menuNames.GroupBy(pair => RadialCommandIcons.NameKey(pair.Value),StringComparer.OrdinalIgnoreCase))
		{
			if (!requestedNames.Contains(group.Key)) continue;
			var ids=group.Where(pair => !IsRemovedCommand(pair.Key, pair.Value)).Select(pair=>pair.Key).Distinct().Take(2).ToArray();
			commandNames[group.Key]=ids.Length==1?ids[0]:-1;
			if(ids.Length==1) requestedIds.Add(ids[0]);
		}
		foreach (int apiId in neededNative)
		{
			string parent;
			string name = RadialCommandIcons.NameKey(host.GetMenuStrings(apiId,documentType,out parent));
			if (name.Length == 0) continue;
			var matches = menuNames.Where(pair => RadialCommandIcons.NameKey(pair.Value).Equals(name,StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Key).Distinct().Take(2).ToArray();
			if (matches.Length == 1) {nativeCommands[apiId]=matches[0];requestedIds.Add(matches[0]);}
		}
		NativeMethods.EnumChildWindows(root, (toolbar, _) =>
		{
			if (!ClassName(toolbar).Equals("ToolbarWindow32", StringComparison.Ordinal)) return true;
			Guid iid = typeof(Accessibility.IAccessible).GUID;
			Accessibility.IAccessible accessible = null;
			bool accessibleRead = false;
			int buttonIndex = -1;
			try
			{
			foreach (var button in ReadButtons(toolbar))
			{
				buttonIndex++;
				if (!requestedIds.Contains(button.CommandId) || IsRemovedCommand(button.CommandId)) continue;
				if (result.Count >= 2048 || button.CommandId <= 0 || button.ImageIndex < 0 || (button.Style & 1) != 0 || result.ContainsKey(button.CommandId)) continue;
				int listIndex = (button.ImageIndex >> 16) & 0xffff;
				int imageIndex = button.ImageIndex & 0xffff;
				IntPtr images = NativeMethods.SendMessage(toolbar, 0x0431, (IntPtr)listIndex, IntPtr.Zero); // TB_GETIMAGELIST
				if (images == IntPtr.Zero || imageIndex >= ImageList_GetImageCount(images)) continue;
				IntPtr handle = ImageList_GetIcon(images, imageIndex, 1); // ILD_TRANSPARENT; caller owns this HICON
				if (handle == IntPtr.Zero) continue;
				try
				{
					using var icon = Icon.FromHandle(handle); using var bitmap = icon.ToBitmap(); result.Add(button.CommandId, WheelIconDrawing.CopySource(bitmap));
					string commandName = menuNames.TryGetValue(button.CommandId, out var menuName) ? menuName : "";
					if (commandName.Length == 0 && !accessibleRead) {accessibleRead=true;AccessibleObjectFromWindow(toolbar,0xfffffffc,ref iid,out accessible);}
					if (commandName.Length == 0 && accessible != null)
						try { commandName = CleanName(accessible.get_accName(buttonIndex + 1)); } catch (COMException) { }
					if (commandName.Length == 0) commandName = ReadTooltipName(toolbar, button.CommandId);
					// Raw toolbar IDs are not API command IDs. Passing them to
					// GetMenuStrings both misidentifies commands and stalls painting.
					string name = RadialCommandIcons.NameKey(commandName);
					if (name.Length > 0)
					{
						if (commandNames.TryGetValue(name, out int existing) && existing != button.CommandId) commandNames[name] = -1;
						else commandNames[name] = button.CommandId;
					}
				}
				finally { DestroyIcon(handle); }
			}
			}
			finally { if (accessible != null && Marshal.IsComObject(accessible)) Marshal.ReleaseComObject(accessible); }
			return true;
		}, IntPtr.Zero);
		return result;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct TooltipInfo
	{
		public uint Size;
		public uint Flags;
		public IntPtr Window;
		public UIntPtr Id;
		public NativeMethods.RECT Bounds;
		public IntPtr Instance;
		public IntPtr Text;
		public IntPtr Parameter;
		public IntPtr Reserved;
	}

	private sealed class MappedCommand
	{
		public string Category { get; set; } = "";
		public string Name { get; set; } = "";
	}

	public static bool TryRead(ISldWorks solidWorks, IModelDoc2 document, out List<KeyboardShortcutRow> rows, out string message)
		=> TryReadForPath(solidWorks, document, null, out rows, out message);

	internal static bool TryReadForPath(ISldWorks solidWorks, IModelDoc2 document, string category, out List<KeyboardShortcutRow> rows, out string message)
	{
		List<KeyboardShortcutRow> collectedRows = new List<KeyboardShortcutRow>();
		rows = collectedRows;
		message = "";
		if (solidWorks == null || document == null) return false;
		IntPtr root = GetMainWindow(solidWorks);
		if (root == IntPtr.Zero) return false;
		int documentType = document.GetType();
		HashSet<string> emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		NativeMethods.EnumChildWindows(root, delegate(IntPtr toolbar, IntPtr _)
		{
			if (!ClassName(toolbar).Equals("ToolbarWindow32", StringComparison.Ordinal)) return true;
			string toolbarName = WindowText(toolbar);
			if (toolbarName.Length == 0) toolbarName = "未命名工具栏";
			// Names and tooltips can invoke foreign COM providers. Reject unrelated
			// toolbars by their full current path before inspecting any button.
			if (!ExternalPluginCommandCatalog.MatchesCategory(category, "第三方插件 > " + toolbarName)) return true;
			foreach (ToolbarButton button in ReadButtons(toolbar))
			{
				// Third-party user-command IDs are not required to be above a
				// numeric threshold.  Exclude only known SOLIDWORKS command IDs;
				// this keeps add-ins whose IDs happen to be below 10,000 readable.
				if (button.CommandId <= 0 || Enum.IsDefined(typeof(swCommands_e), button.CommandId) || IsRemovedCommand(button.CommandId)) continue;
				string name = ReadCommandName(solidWorks, toolbar, button.CommandId, documentType);
				if (IsRemovedCommand(button.CommandId, name)) continue;
				// Persist the toolbar path actually exposed by this session. A
				// versioned numeric mapping is useful for catalogue/icon hints, but
				// cannot prove that a reused command ID still belongs to that plugin.
				if (name.Length == 0 || toolbarName == "未命名工具栏") continue;
				string commandCategory = toolbarName;
                string key = commandCategory + "\u001f" + button.CommandId + "\u001f" + name;
				if (!emitted.Add(key)) continue;
				collectedRows.Add(new KeyboardShortcutRow
				{
					Category = "第三方插件 > " + commandCategory,
					Command = name,
					Hotkey = (button.State & 0x04) != 0 ? "直接执行" : "需满足文档/选择条件",
					ActionKind = ActionKinds.ExternalPlugin,
					Value = ExternalToolbarCommandReference.Encode(documentType, button.CommandId, name, "第三方插件 > " + commandCategory)
				});
			}
			return true;
		}, IntPtr.Zero);
		rows = collectedRows.OrderBy(row => row.Category, StringComparer.CurrentCultureIgnoreCase).ThenBy(row => row.Command, StringComparer.CurrentCultureIgnoreCase).ToList();
		message = rows.Count > 0 ? $"已读取 {rows.Count} 个当前文档环境可用的外部插件命令。" : "工具栏中没有读到带名称的外部插件命令。";
		if (category == null) Logger.Info($"External toolbar command scan: documentType={documentType}, namedCommands={rows.Count}");
		return rows.Count > 0;
	}

	public static bool TryExecute(ISldWorks solidWorks, int commandId, out string message, string expectedName = "", string expectedPath = "", bool currentIdentityVerified = false)
	{
		message = "";
		if (solidWorks == null) { message = "SOLIDWORKS 未连接。"; return false; }
		if (string.IsNullOrWhiteSpace(expectedName) || string.IsNullOrWhiteSpace(expectedPath))
		{ message = "该外部插件命令缺少完整路径，请重新读取绑定；未按旧编号猜测执行。"; return false; }
		if (!currentIdentityVerified)
		{
			int type = (solidWorks.ActiveDoc as IModelDoc2)?.GetType() ?? 0;
			if (!ExternalPluginCommandCatalog.TryResolveCurrent(solidWorks, type, expectedPath, expectedName, out KeyboardShortcutRow resolved, out message)
				|| !ExternalToolbarCommandReference.TryDecode(resolved.Value, out _, out commandId)) return false;
		}
		if (IsRemovedCommand(commandId, expectedName))
		{
			message = "底孔压铆独立入口已移除，请使用批量底孔／工艺孔窗口。";
			return false;
		}
		IntPtr root = GetMainWindow(solidWorks);
		if (root == IntPtr.Zero) { message = "没有找到 SOLIDWORKS 主窗口。"; return false; }
		IntPtr commandToolbar = IntPtr.Zero;
		int commandIndex = -1;
		NativeMethods.EnumChildWindows(root, delegate(IntPtr toolbar, IntPtr _)
		{
			if (!ClassName(toolbar).Equals("ToolbarWindow32", StringComparison.Ordinal)) return true;
			int index = FindButtonIndex(toolbar, commandId);
			if (index < 0) return true;
			commandToolbar = toolbar;
			commandIndex = index;
			return false;
		}, IntPtr.Zero);
		if (commandToolbar == IntPtr.Zero || commandIndex < 0)
		{
			var entries = ExternalMenuCommandCatalog.Read(solidWorks).Where(entry => entry.Id == commandId).ToList();
			if (entries.Count > 0)
			{
				var exact = entries.Where(entry => ExternalPluginCommandCatalog.PathsEqual(entry.Path, expectedPath)
					&& ExternalMenuCommandCatalog.CleanName(entry.Name).Equals(ExternalMenuCommandCatalog.CleanName(expectedName), StringComparison.OrdinalIgnoreCase)).ToList();
				if (exact.Count != 1 || !exact[0].Enabled)
				{ message = "插件菜单路径不唯一、已变化或当前不可用，未执行其他同名功能。"; return false; }
				return NativeMethods.PostMessage(root, 0x0111, (IntPtr)commandId, IntPtr.Zero);
			}
			// This invocation already resolved an actual current CommandManager
			// row by full path and name. Hidden buttons may use that verified ID;
			// an arbitrary numeric range or static installed mapping is no proof.
			if (commandId > 0)
			{
				bool posted = NativeMethods.PostMessage(root, 0x0111, (IntPtr)commandId, IntPtr.Zero);
				if (!posted) message = "外部插件已核验路径的命令消息发送失败。";
				return posted;
			}
			message = "当前会话没有找到可用的对应插件命令，请重新读取；未发送命令。";
			return false;
		}
		ToolbarButton current = ReadButtons(commandToolbar).FirstOrDefault(button => button.CommandId == commandId);
		if ((current.State & 0x04) == 0)
		{
			message = "该插件命令在当前文档或选择状态下不可用。";
			return false;
		}
		string actualName = ReadCommandName(solidWorks, commandToolbar, commandId, ((IModelDoc2)solidWorks.ActiveDoc).GetType());
		if (expectedName.Length > 0 && ExternalMenuCommandCatalog.CleanName(actualName) != ExternalMenuCommandCatalog.CleanName(expectedName))
		{
			message = "插件命令编号已变化，请重新读取并选择该功能；未执行不匹配的命令。";
			return false;
		}
		if (!NativeMethods.PostMessage(root, 0x0111, (IntPtr)commandId, IntPtr.Zero))
		{
			message = "外部插件工具栏按钮消息发送失败。";
			return false;
		}
		Logger.Info($"External toolbar command queued: commandId={commandId}, toolbar={WindowText(commandToolbar)}, index={commandIndex}");
		return true;
	}

	private static int FindButtonIndex(IntPtr toolbar, int commandId)
	{
		int count = unchecked((int)NativeMethods.SendMessage(toolbar, TbButtonCount, IntPtr.Zero, IntPtr.Zero).ToInt64());
		if (count <= 0 || count > 4096) return -1;
		IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ToolbarButton)));
		try
		{
			for (int index = 0; index < count; index++)
			{
				if (NativeMethods.SendMessage(toolbar, TbGetButton, (IntPtr)index, buffer) == IntPtr.Zero) continue;
				ToolbarButton button = (ToolbarButton)Marshal.PtrToStructure(buffer, typeof(ToolbarButton));
				if (button.CommandId == commandId) return index;
			}
		}
		finally { Marshal.FreeHGlobal(buffer); }
		return -1;
	}

	private static bool TryReadButtonRect(IntPtr toolbar, int index, out NativeMethods.RECT rect)
	{
		rect = default;
		IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeMethods.RECT)));
		try
		{
			if (NativeMethods.SendMessage(toolbar, TbGetItemRect, (IntPtr)index, buffer) == IntPtr.Zero) return false;
			rect = (NativeMethods.RECT)Marshal.PtrToStructure(buffer, typeof(NativeMethods.RECT));
			return rect.Right > rect.Left && rect.Bottom > rect.Top;
		}
		finally { Marshal.FreeHGlobal(buffer); }
	}

	private static IntPtr PackPoint(int x, int y)
	{
		return (IntPtr)unchecked((int)(((y & 0xFFFF) << 16) | (x & 0xFFFF)));
	}

	internal static IntPtr PackPointForTest(int x, int y) => PackPoint(x, y);

	private static IEnumerable<ToolbarButton> ReadButtons(IntPtr toolbar)
	{
		int count = unchecked((int)NativeMethods.SendMessage(toolbar, TbButtonCount, IntPtr.Zero, IntPtr.Zero).ToInt64());
		if (count <= 0 || count > 4096) yield break;
		IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ToolbarButton)));
		try
		{
			for (int index = 0; index < count; index++)
			{
				if (NativeMethods.SendMessage(toolbar, TbGetButton, (IntPtr)index, buffer) == IntPtr.Zero) continue;
				yield return (ToolbarButton)Marshal.PtrToStructure(buffer, typeof(ToolbarButton));
			}
		}
		finally { Marshal.FreeHGlobal(buffer); }
	}

	private static string ReadCommandName(ISldWorks solidWorks, IntPtr toolbar, int commandId, int documentType)
	{
		// Prefer the tooltip of the button currently visible in the selected
		// plugin tab. GetMenuStrings can resolve a reused command ID to another
		// add-in, which would record the wrong function name.
		string tooltipName = ReadTooltipName(toolbar, commandId);
		if (tooltipName.Length > 0) return tooltipName;
		try
		{
			string parent;
			string menuName = CleanName(solidWorks.GetMenuStrings(commandId, documentType, out parent));
			if (menuName.Length > 0) return menuName;
		}
		catch { }
		return "";
	}

	private static string ReadTooltipName(IntPtr toolbar, int commandId)
	{
		IntPtr tooltip = NativeMethods.SendMessage(toolbar, TbGetTooltips, IntPtr.Zero, IntPtr.Zero);
		if (tooltip == IntPtr.Zero) return "";
		IntPtr textBuffer = Marshal.AllocHGlobal(2048);
		IntPtr infoBuffer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TooltipInfo)));
		try
		{
			for (int offset = 0; offset < 2048; offset++) Marshal.WriteByte(textBuffer, offset, 0);
			TooltipInfo info = new TooltipInfo
			{
				Size = (uint)Marshal.SizeOf(typeof(TooltipInfo)),
				Window = toolbar,
				Id = (UIntPtr)(uint)commandId,
				Text = textBuffer
			};
			Marshal.StructureToPtr(info, infoBuffer, false);
			NativeMethods.SendMessage(tooltip, TtmGetTextW, IntPtr.Zero, infoBuffer);
			return CleanName(Marshal.PtrToStringUni(textBuffer));
		}
		catch { return ""; }
		finally
		{
			Marshal.FreeHGlobal(infoBuffer);
			Marshal.FreeHGlobal(textBuffer);
		}
	}

	private static string CleanName(string value)
	{
		return (value ?? "").Replace("&", "").Replace("...", "").Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(item => item.Trim()).FirstOrDefault(item => item.Length > 0) ?? "";
	}

	private static Dictionary<int, MappedCommand> LoadMappedCommands(int documentType)
	{
		Dictionary<int, MappedCommand> result = new Dictionary<int, MappedCommand>();
		try
		{
			using Stream stream = typeof(ToolbarExternalCommandCatalog).Assembly.GetManifestResourceStream(MappingResourceName);
			if (stream == null) return result;
			using StreamReader reader = new StreamReader(stream, Encoding.UTF8, true);
			reader.ReadLine();
			string line;
			while ((line = reader.ReadLine()) != null)
			{
				List<string> fields = ParseCsvLine(line);
				if (fields.Count < 4 || !int.TryParse(fields[0], out int mappedDocumentType) || mappedDocumentType != documentType || !int.TryParse(fields[2], out int commandId)) continue;
				string name = CleanName(fields[3]);
				if (name.Length == 0) continue;
				result[commandId] = new MappedCommand { Category = fields[1].Trim(), Name = name };
			}
		}
		catch (Exception ex) { Logger.Trace("Read embedded external command mapping skipped: " + ex.Message); }
		return result;
	}

	private static List<string> ParseCsvLine(string line)
	{
		List<string> fields = new List<string>();
		StringBuilder value = new StringBuilder();
		bool quoted = false;
		for (int index = 0; index < (line ?? "").Length; index++)
		{
			char current = line[index];
			if (current == '"')
			{
				if (quoted && index + 1 < line.Length && line[index + 1] == '"') { value.Append('"'); index++; }
				else quoted = !quoted;
			}
			else if (current == ',' && !quoted) { fields.Add(value.ToString()); value.Clear(); }
			else value.Append(current);
		}
		fields.Add(value.ToString());
		return fields;
	}

	internal static bool TryResolveMappedNameForTest(int documentType, int commandId, out string name)
		=> TryResolveMappedName(documentType, commandId, out name);

	// The former Doctor sheet-metal bottom-hole press-rivet command is retired.
	// Keep the filter at the shared external-command boundary so old mappings,
	// saved wheel entries, live toolbar scans and recorder events cannot bring it
	// back through a different discovery path.  The generic batch hole/process
	// window remains available and is intentionally unaffected.
	internal static bool IsRemovedCommand(int commandId, string name = "")
	{
		if (commandId == 41770) return true;
		return RadialCommandIcons.NameKey(name).Equals("压铆拉铆底孔", StringComparison.CurrentCultureIgnoreCase);
	}

	internal static bool TryResolveMappedName(int documentType, int commandId, out string name)
	{
		MappedCommand mapped = null;
		bool found = IconMappings.Value.TryGetValue(documentType,out var commands) && commands.TryGetValue(commandId, out mapped);
		name = found ? mapped.Name : "";
		return found;
	}

	internal static bool TryResolveMappedCommand(int documentType, string category, string name, out int commandId)
	{
		commandId = -1;
		string wantedCategory = ExternalNamedCommandReference.PluginName(category);
		string wantedName = ExternalMenuCommandCatalog.CleanName(name);
		if (wantedName.Length == 0) return false;
		foreach (KeyValuePair<int, MappedCommand> pair in LoadMappedCommands(documentType))
		{
			if (!ExternalMenuCommandCatalog.CleanName(pair.Value.Name).Equals(wantedName, StringComparison.CurrentCultureIgnoreCase)) continue;
			if (wantedCategory.Length > 0 && !pair.Value.Category.Equals(wantedCategory, StringComparison.CurrentCultureIgnoreCase)
				&& pair.Value.Category.IndexOf(wantedCategory, StringComparison.CurrentCultureIgnoreCase) < 0
				&& wantedCategory.IndexOf(pair.Value.Category, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
			commandId = pair.Key;
			return true;
		}
		return false;
	}

	internal static bool TryResolveMappedCategory(int documentType, int commandId, out string category)
	{
		category = "";
		if (!IconMappings.Value.TryGetValue(documentType, out Dictionary<int, MappedCommand> commands)
			|| !commands.TryGetValue(commandId, out MappedCommand mapped)) return false;
		category = mapped.Category;
		return category.Length > 0;
	}

	// Resolve the identity reported by a live CommandManager click.  Add-ins
	// often expose a command only while their tab is visible, so the static
	// mapping is not sufficient for recording or replaying that click.
	internal static bool TryResolveLiveIdentity(ISldWorks solidWorks, int documentType, int commandId, out string name, out string category)
	{
		name = category = "";
		if (solidWorks == null || commandId <= 0 || IsRemovedCommand(commandId)) return false;
		try
		{
			if ((solidWorks.ActiveDoc as IModelDoc2)?.GetType() != documentType) return false;
			IntPtr root = GetMainWindow(solidWorks);
			if (root == IntPtr.Zero) return false;
			string clickedName = "", clickedPath = "";
			NativeMethods.GetWindowThreadProcessId(root, out uint rootProcess);
			if (NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
			{
				IntPtr hovered = NativeMethods.WindowFromPoint(cursor);
				NativeMethods.GetWindowThreadProcessId(hovered, out uint hoveredProcess);
				if (hoveredProcess == rootProcess)
				for (int depth = 0; hovered != IntPtr.Zero && depth < 8; depth++, hovered = NativeMethods.GetParent(hovered))
				{
					if (!ClassName(hovered).Equals("ToolbarWindow32", StringComparison.Ordinal)) continue;
					if (!ReadButtons(hovered).Any(button => button.CommandId == commandId)) continue;
					clickedName = ReadCommandName(solidWorks, hovered, commandId, documentType);
					clickedPath = WindowText(hovered);
					break;
				}
			}
			var menu = ExternalMenuCommandCatalog.Read(solidWorks).Where(entry => entry.Id == commandId && !string.IsNullOrWhiteSpace(entry.Path))
				.GroupBy(entry => entry.Path + "\0" + ExternalMenuCommandCatalog.CleanName(entry.Name), StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
			if (menu.Count == 1)
			{
				if (clickedName.Length > 0 && !ExternalMenuCommandCatalog.CleanName(clickedName).Equals(ExternalMenuCommandCatalog.CleanName(menu[0].Name), StringComparison.OrdinalIgnoreCase)) return false;
				name = ExternalMenuCommandCatalog.CleanName(menu[0].Name); category = menu[0].Path;
				return name.Length > 0;
			}
			if (menu.Count > 1) return false;
			if (clickedName.Length > 0 && clickedPath.Length > 0)
			{ name = clickedName; category = clickedPath; return true; }
			var identities = new Dictionary<string, KeyValuePair<string, string>>(StringComparer.OrdinalIgnoreCase);
			NativeMethods.EnumChildWindows(root, (toolbar, _) =>
			{
				if (!ClassName(toolbar).Equals("ToolbarWindow32", StringComparison.Ordinal) || !ReadButtons(toolbar).Any(button => button.CommandId == commandId)) return true;
				string actualName = ReadCommandName(solidWorks, toolbar, commandId, documentType), path = WindowText(toolbar);
				if (actualName.Length > 0 && path.Length > 0) identities[path + "\0" + actualName] = new KeyValuePair<string, string>(path, actualName);
				return true;
			}, IntPtr.Zero);
			if (identities.Count != 1) return false;
			var identity = identities.Values.Single(); name = identity.Value; category = identity.Key;
			return true;
		}
		catch (Exception ex) { Logger.Trace("Resolve exact live external command identity failed: " + ex.Message); return false; }
	}

	internal static IEnumerable<KeyboardShortcutRow> KnownCatalog(int documentType) => LoadMappedCommands(documentType)
		.Select(pair => new KeyboardShortcutRow
		{
			Category = "第三方插件 > " + pair.Value.Category,
			Command = pair.Value.Name,
			Hotkey = "已安装 · 执行前匹配",
			ActionKind = ActionKinds.ExternalPlugin,
			Value = ExternalNamedCommandReference.Encode(documentType, "第三方插件 > " + pair.Value.Category, pair.Value.Name)
		});

	private static IntPtr GetMainWindow(ISldWorks solidWorks)
	{
		try { return (IntPtr)((IFrame)solidWorks.IFrameObject()).GetHWndx64(); }
		catch { return IntPtr.Zero; }
	}

	private static string ClassName(IntPtr window)
	{
		char[] value = new char[128];
		int length = NativeMethods.GetClassName(window, value, value.Length);
		return length > 0 ? new string(value, 0, length) : "";
	}

	private static string WindowText(IntPtr window)
	{
		int size = Math.Max(2, NativeMethods.GetWindowTextLength(window) + 2);
		char[] value = new char[size];
		int length = NativeMethods.GetWindowText(window, value, value.Length);
		return length > 0 ? new string(value, 0, length).Trim() : "";
	}
}

internal static class ExternalToolbarCommandReference
{
	private const string Prefix = "sw-external-toolbar:";

	public static string Encode(int documentType, int commandId) => Prefix + documentType + "/" + commandId;
	public static string Encode(int documentType, int commandId, string name) => Encode(documentType, commandId) + "/" + Uri.EscapeDataString(name ?? "");
	public static string Encode(int documentType, int commandId, string name, string path) => Encode(documentType, commandId, name) + "/" + Uri.EscapeDataString(path ?? "");
	public static string Path(string value)
	{
		if (!TryDecode(value, out _, out _)) return "";
		string[] fields = value.Trim().Substring(Prefix.Length).Split('/');
		try { return fields.Length == 4 ? Uri.UnescapeDataString(fields[3]).Trim() : ""; } catch { return ""; }
	}
	public static string Name(string value)
	{
		if (!TryDecode(value, out _, out _)) return "";
		string[] fields = value.Trim().Substring(Prefix.Length).Split('/');
		try { return fields.Length >= 3 ? Uri.UnescapeDataString(fields[2]) : ""; } catch { return ""; }
	}

	public static bool TryDecode(string value, out int documentType, out int commandId)
	{
		documentType = 0;
		commandId = -1;
		string text = (value ?? "").Trim();
		if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
		string[] fields = text.Substring(Prefix.Length).Split('/');
		return (fields.Length == 2 || fields.Length == 3 || fields.Length == 4) && int.TryParse(fields[0], out documentType) && documentType >= 1 && documentType <= 3 && int.TryParse(fields[1], out commandId) && commandId >= 0;
	}
}
