using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Automation;

namespace XY.SolidWorksPlugin;

internal static class QinJianFunctionReader
{
	private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
	private const uint MOUSEEVENTF_LEFTUP = 0x0004;

	public static bool TryRead(IntPtr solidWorksWindow, out List<KeyboardShortcutRow> rows, out string message)
	{
		bool catalogRead = QinJianCommandCatalog.TryRead(out List<KeyboardShortcutRow> catalogRows, out string catalogMessage);
		rows = catalogRead ? catalogRows : new List<KeyboardShortcutRow>();
		message = "";
		IntPtr root = solidWorksWindow == IntPtr.Zero ? FindSolidWorksWindow() : solidWorksWindow;
		if (root == IntPtr.Zero)
		{
			if (catalogRead)
			{
				return true;
			}
			message = catalogMessage + Environment.NewLine + "没有找到 SOLIDWORKS 主窗口。请先打开 SOLIDWORKS，并确认琴剑工具栏标签可见。";
			return false;
		}
		try
		{
			AutomationElement rootElement = AutomationElement.FromHandle(root);
			if (rootElement == null)
			{
				message = "无法读取 SOLIDWORKS 窗口控件。";
				return catalogRead;
			}
			List<QinJianTab> tabs = FindQinJianTabs(rootElement);
			if (tabs.Count == 0)
			{
				if (catalogRead)
				{
					return true;
				}
				message = catalogMessage + Environment.NewLine + "没有找到“琴剑或启翔铝型材”插件标签。请先在 SOLIDWORKS 顶部工具栏显示“琴剑SW工具 / 琴剑钣金 / 琴剑型材 / 琴剑小工具 / 启翔铝型材”等标签后再读取。";
				return false;
			}
			Dictionary<string, KeyboardShortcutRow> functions = new Dictionary<string, KeyboardShortcutRow>(StringComparer.OrdinalIgnoreCase);
			foreach (KeyboardShortcutRow row in rows)
			{
				functions[BuildFunctionKey(row.Category, row.Command)] = row;
			}
			foreach (QinJianTab tab in tabs)
			{
				if (!SelectTab(tab.Element))
				{
					continue;
				}
				Thread.Sleep(260);
				rootElement = AutomationElement.FromHandle(root);
				foreach (string commandName in ReadVisibleCommandNames(rootElement, tab))
				{
					string key = BuildFunctionKey(tab.Name, commandName);
					if (!functions.ContainsKey(key))
					{
						functions.Add(key, new KeyboardShortcutRow
						{
						Category = QinJianCommandCatalog.FormatCategory(tab.Name),
							Command = commandName,
							Hotkey = "琴剑功能",
							ActionKind = ActionKinds.QinJian,
							Value = QinJianActionValue.Format(tab.Name, commandName)
						});
					}
				}
			}
			rows = functions.Values
				.OrderBy((KeyboardShortcutRow row) => row.Category, StringComparer.CurrentCultureIgnoreCase)
				.ThenBy((KeyboardShortcutRow row) => row.Command, StringComparer.CurrentCultureIgnoreCase)
				.ToList();
			if (rows.Count > 0)
			{
				return true;
			}
			message = "已经找到琴剑标签，但没有读到按钮中文名。请先点开任意琴剑标签，让插件按钮显示在命令栏上方后再读取。";
			return false;
		}
		catch (Exception ex)
		{
			message = "读取琴剑功能失败: " + ex.Message;
			return catalogRead && rows.Count > 0;
		}
	}

	public static bool TryExecute(IntPtr solidWorksWindow, string actionValue, TimeSpan timeout, CancellationToken cancellationToken, out string message)
	{
		message = "";
		if (!QinJianActionValue.TryParse(actionValue, out string tabName, out string commandName))
		{
			message = "琴剑功能配置无效：" + actionValue;
			return false;
		}
		IntPtr root = solidWorksWindow == IntPtr.Zero ? FindSolidWorksWindow() : solidWorksWindow;
		if (root == IntPtr.Zero)
		{
			message = "没有找到 SOLIDWORKS 主窗口。";
			return false;
		}
		try
		{
			AutomationElement rootElement = AutomationElement.FromHandle(root);
			List<QinJianTab> tabs = FindQinJianTabs(rootElement);
			int tabIndex = FindUniqueTabIndex(tabs.Select(item => item.Name), tabName, false, out bool ambiguous);
			if (tabIndex < 0)
			{
				message = ambiguous ? "琴剑标签页存在多个相同身份，已停止执行：" + tabName : "没有找到琴剑标签页：" + tabName;
				return false;
			}
			QinJianTab tab = tabs[tabIndex];
			if (!SelectTab(tab.Element))
			{
				message = "无法切换到琴剑标签页：" + tabName;
				return false;
			}
			DateTime deadline = DateTime.UtcNow + timeout;
			do
			{
				cancellationToken.ThrowIfCancellationRequested();
				rootElement = AutomationElement.FromHandle(root);
				if (TryClickVisibleCommand(rootElement, tab, commandName))
				{
					return true;
				}
				if (cancellationToken.WaitHandle.WaitOne(60))
				{
					throw new OperationCanceledException(cancellationToken);
				}
			}
			while (DateTime.UtcNow < deadline);
			message = $"已切换到“{tabName}”，但没有找到可点击的功能“{commandName}”。请确认琴剑插件已加载且该功能在当前文档类型中可用。";
			return false;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			message = "执行琴剑功能失败：" + ex.Message;
			return false;
		}
	}

	public static bool TryReadExternalPlugins(IntPtr solidWorksWindow, out List<KeyboardShortcutRow> rows, out string message)
	{
		rows = new List<KeyboardShortcutRow>();
		message = "";
		IntPtr root = solidWorksWindow == IntPtr.Zero ? FindSolidWorksWindow() : solidWorksWindow;
		if (root == IntPtr.Zero) { message = "没有找到 SOLIDWORKS 主窗口。"; return false; }
		try
		{
			AutomationElement rootElement = AutomationElement.FromHandle(root);
			List<QinJianTab> tabs = FindExternalPluginTabs(rootElement);
			foreach (QinJianTab tab in tabs)
			{
				if (!SelectTab(tab.Element)) continue;
				Thread.Sleep(220);
				rootElement = AutomationElement.FromHandle(root);
				foreach (string command in ReadVisibleCommandNames(rootElement, tab))
				{
					rows.Add(new KeyboardShortcutRow { Category = "第三方插件 > " + tab.Name, Command = command, Hotkey = "直接执行", ActionKind = ActionKinds.ExternalPlugin, Value = ExternalPluginActionValue.Format(tab.Name, command) });
				}
			}
			rows = rows.GroupBy(row => row.Category + "\u001f" + row.Command, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).OrderBy(row => row.Category).ThenBy(row => row.Command).ToList();
			Logger.Error("External plugin UI scan: tabs=" + tabs.Count + ", commands=" + rows.Count);
			if (rows.Count > 0) return true;
			message = "已找到外部插件标签，但未从界面按钮读取到命令。请先显示目标插件标签并打开零件后重试。";
			return false;
		}
		catch (Exception ex) { message = "读取外部插件界面失败: " + ex.Message; Logger.Error(message, ex); return false; }
	}

	public static bool TryExecuteExternalPlugin(IntPtr solidWorksWindow, string actionValue, TimeSpan timeout, CancellationToken cancellationToken, out string message)
	{
		message = "";
		if (!ExternalPluginActionValue.TryParse(actionValue, out string tabName, out string commandName)) { message = "外部插件功能配置无效。"; return false; }
		IntPtr root = solidWorksWindow == IntPtr.Zero ? FindSolidWorksWindow() : solidWorksWindow;
		if (root == IntPtr.Zero) { message = "没有找到 SOLIDWORKS 主窗口。"; return false; }
		try
		{
			AutomationElement rootElement = AutomationElement.FromHandle(root);
			List<QinJianTab> tabs = FindExternalPluginTabs(rootElement);
			int tabIndex = FindUniqueTabIndex(tabs.Select(item => item.Name), tabName, true, out bool ambiguous);
			if (tabIndex < 0) { message = ambiguous ? "外部插件标签存在多个相同身份，已停止执行：" + tabName : "没有找到外部插件标签：" + tabName; return false; }
			QinJianTab tab = tabs[tabIndex];
			if (!SelectTab(tab.Element)) { message = "无法切换到外部插件标签：" + tabName; return false; }
			DateTime deadline = DateTime.UtcNow + timeout;
			do { cancellationToken.ThrowIfCancellationRequested(); rootElement = AutomationElement.FromHandle(root); if (TryClickVisibleCommand(rootElement, tab, commandName)) return true; if (cancellationToken.WaitHandle.WaitOne(60)) throw new OperationCanceledException(cancellationToken); } while (DateTime.UtcNow < deadline);
			message = "已切换到插件标签，但没有找到按钮: " + commandName; return false;
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex) { message = ex.Message; return false; }
	}

	internal static string CleanTabNameForTest(string value)
	{
		return CleanTabName(value);
	}

	internal static string CleanCommandNameForTest(string value)
	{
		return CleanCommandName(value);
	}

	internal static bool IsQinJianTabNameForTest(string value)
	{
		return IsQinJianTabName(value);
	}

	private static List<QinJianTab> FindQinJianTabs(AutomationElement rootElement)
	{
		AutomationElementCollection elements = rootElement.FindAll(TreeScope.Descendants, Condition.TrueCondition);
		List<QinJianTab> tabs = new List<QinJianTab>();
		foreach (AutomationElement element in elements)
		{
			if (!IsTabElement(element)) continue;
			string name = CleanTabName(SafeName(element));
			if (!IsQinJianTabName(name) || !TryGetRect(element, out System.Windows.Rect rect) || rect.IsEmpty || rect.Width <= 8 || rect.Height <= 8)
			{
				continue;
			}
			if (tabs.Any((QinJianTab tab) => tab.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && tab.Bounds == rect))
			{
				continue;
			}
			tabs.Add(new QinJianTab(name, element, rect));
		}
		return tabs
			.OrderBy((QinJianTab tab) => tab.Bounds.Top)
			.ThenBy((QinJianTab tab) => tab.Bounds.Left)
			.ToList();
	}

	private static List<QinJianTab> FindExternalPluginTabs(AutomationElement rootElement)
	{
		AutomationElementCollection elements = rootElement.FindAll(TreeScope.Descendants, Condition.TrueCondition);
		List<QinJianTab> tabs = new List<QinJianTab>();
		foreach (AutomationElement element in elements)
		{
			if (!IsTabElement(element)) continue;
			string name = CleanTabName(SafeName(element));
			if (!IsExternalPluginTabName(name) || !TryGetRect(element, out System.Windows.Rect rect) || rect.IsEmpty || rect.Width <= 8 || rect.Height <= 8) continue;
			if (!tabs.Any(tab => tab.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && tab.Bounds == rect)) tabs.Add(new QinJianTab(name, element, rect));
		}
		return tabs.OrderBy(tab => tab.Bounds.Top).ThenBy(tab => tab.Bounds.Left).ToList();
	}

	private static bool IsExternalPluginTabName(string name)
	{
		if (string.IsNullOrWhiteSpace(name)) return false;
		string[] builtIn = { "特征", "草图", "曲面", "钣金", "焊接件", "直接编辑", "标注", "评估", "Features", "Sketch", "Surfaces", "Sheet Metal", "Weldments", "Direct Editing", "Markup", "Evaluate" };
		return !builtIn.Any(item => name.Equals(item, StringComparison.OrdinalIgnoreCase))
			&& !name.Equals("SOLIDWORKS", StringComparison.OrdinalIgnoreCase)
			&& !name.Equals("CommandManager", StringComparison.OrdinalIgnoreCase);
	}

	private static IEnumerable<string> ReadVisibleCommandNames(AutomationElement rootElement, QinJianTab tab)
	{
		if (!TryGetRect(rootElement, out System.Windows.Rect rootRect))
		{
			yield break;
		}
		double top = Math.Max(rootRect.Top + 24, tab.Bounds.Top - 260);
		double bottom = tab.Bounds.Top + 4;
		if (bottom - top < 32)
		{
			top = rootRect.Top + 34;
			bottom = rootRect.Top + 150;
		}
		AutomationElementCollection elements = rootElement.FindAll(TreeScope.Descendants, Condition.TrueCondition);
		HashSet<string> emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (AutomationElement element in elements)
		{
			if (!IsCommandElement(element) || !TryGetRect(element, out System.Windows.Rect rect) || rect.IsEmpty)
			{
				continue;
			}
			if (rect.Width < 12 || rect.Height < 12 || rect.Top < top || rect.Bottom > bottom || rect.Right < rootRect.Left || rect.Left > rootRect.Right)
			{
				continue;
			}
			string name = CleanCommandName(SafeCommandText(element));
			if (!IsUsefulCommandName(name, tab.Name))
			{
				continue;
			}
			if (emitted.Add(name))
			{
				yield return name;
			}
		}
	}

	private static bool TryClickVisibleCommand(AutomationElement rootElement, QinJianTab tab, string commandName)
	{
		if (!tab.Element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object selectionObject) || !(selectionObject is SelectionItemPattern selection))
			throw new InvalidOperationException("无法确认插件标签的选中状态，请重新读取并绑定带完整路径的命令：" + tab.Name);
		if (!selection.Current.IsSelected) return false;
		AutomationElement commandScope = selection.Current.SelectionContainer;
		if (commandScope == null || Automation.Compare(commandScope, rootElement) || commandScope.Current.ControlType != ControlType.Tab)
			throw new InvalidOperationException("无法确认插件标签的命令容器，请重新读取并绑定带完整路径的命令：" + tab.Name);
		if (!TryGetCommandBand(rootElement, tab, out System.Windows.Rect rootRect, out double top, out double bottom))
		{
			return false;
		}
		AutomationElementCollection elements = commandScope.FindAll(TreeScope.Descendants, Condition.TrueCondition);
		List<AutomationElement> matches = new List<AutomationElement>();
		foreach (AutomationElement element in elements)
		{
			if (!IsCommandElement(element) || !TryGetRect(element, out System.Windows.Rect rect) || rect.IsEmpty)
			{
				continue;
			}
			if (rect.Width < 12 || rect.Height < 12 || rect.Top < top || rect.Bottom > bottom || rect.Right < rootRect.Left || rect.Left > rootRect.Right)
			{
				continue;
			}
			if (!CleanCommandName(SafeCommandText(element)).Equals(commandName, StringComparison.CurrentCultureIgnoreCase))
			{
				continue;
			}
			// Multiple visible controls with the same caption cannot be identified
			// by this legacy tab/name reference. Do not click the first one.
			if (!matches.Any(match => Automation.Compare(match, element))) matches.Add(element);
		}
		if (matches.Count > 1) throw new InvalidOperationException($"插件标签“{tab.Name}”内有多个“{commandName}”按钮，已停止执行。请重新绑定能区分路径的命令。");
		if (matches.Count == 0) return false;
		AutomationElement target = matches[0];
		if (!target.Current.IsEnabled || target.Current.IsOffscreen) return false;
		// Once Invoke is attempted its failure is terminal: a second mouse click
		// could invoke the command twice after a provider reports a late error.
		if (target.TryGetCurrentPattern(InvokePattern.Pattern, out object pattern) && pattern is InvokePattern invoke)
		{
			invoke.Invoke();
			return true;
		}
		return TryMouseClick(target);
	}

	private static bool TryGetCommandBand(AutomationElement rootElement, QinJianTab tab, out System.Windows.Rect rootRect, out double top, out double bottom)
	{
		top = 0;
		bottom = 0;
		if (!TryGetRect(rootElement, out rootRect))
		{
			return false;
		}
		top = Math.Max(rootRect.Top + 24, tab.Bounds.Top - 260);
		bottom = tab.Bounds.Top + 4;
		if (bottom - top < 32)
		{
			top = rootRect.Top + 34;
			bottom = rootRect.Top + 150;
		}
		return true;
	}

	private static bool IsCommandElement(AutomationElement element)
	{
		try
		{
			ControlType controlType = element.Current.ControlType;
			return controlType == ControlType.Button
				|| controlType == ControlType.MenuItem
				|| controlType == ControlType.SplitButton
				|| controlType == ControlType.Hyperlink;
		}
		catch
		{
			return false;
		}
	}

	private static bool IsTabElement(AutomationElement element)
	{
		try { return element.Current.ControlType == ControlType.TabItem && !element.Current.IsOffscreen; }
		catch { return false; }
	}

	private static bool IsUsefulCommandName(string name, string tabName)
	{
		if (string.IsNullOrWhiteSpace(name) || name.Length > 40)
		{
			return false;
		}
		if (name.Equals(tabName, StringComparison.OrdinalIgnoreCase) || IsQinJianTabName(name))
		{
			return false;
		}
		string[] blocked =
		{
			"搜索命令",
			"搜索",
			"SOLIDWORKS",
			"CommandManager",
			"菜单栏",
			"工具栏",
			"最小化",
			"最大化",
			"关闭"
		};
		return !blocked.Any((string item) => name.IndexOf(item, StringComparison.OrdinalIgnoreCase) >= 0);
	}

	private static bool SelectTab(AutomationElement element)
	{
		try
		{
			if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object selectionPattern) && selectionPattern is SelectionItemPattern selection)
			{
				selection.Select();
				return true;
			}
		}
		catch
		{
		}
		try
		{
			if (element.TryGetCurrentPattern(InvokePattern.Pattern, out object invokePattern) && invokePattern is InvokePattern invoke)
			{
				invoke.Invoke();
				return true;
			}
		}
		catch
		{
		}
		return TryMouseClick(element);
	}

	private static bool TryMouseClick(AutomationElement element)
	{
		try
		{
			if (element.Current.IsEnabled && !element.Current.IsOffscreen && element.TryGetClickablePoint(out System.Windows.Point point))
			{
				AutomationElement hit = AutomationElement.FromPoint(point);
				for (int depth = 0; hit != null && depth < 12; depth++, hit = TreeWalker.RawViewWalker.GetParent(hit))
				{
					if (!Automation.Compare(hit, element)) continue;
					MouseClick((int)point.X, (int)point.Y);
					return true;
				}
			}
		}
		catch
		{
		}
		return false;
	}

	private static void MouseClick(int x, int y)
	{
		NativeMethods.SetCursorPos(x, y);
		Thread.Sleep(20);
		NativeMethods.mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
		NativeMethods.mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
	}

	private static bool TryGetRect(AutomationElement element, out System.Windows.Rect rect)
	{
		rect = System.Windows.Rect.Empty;
		try
		{
			rect = element.Current.BoundingRectangle;
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static string SafeName(AutomationElement element)
	{
		try
		{
			return element.Current.Name ?? "";
		}
		catch
		{
			return "";
		}
	}

	private static string SafeCommandText(AutomationElement element)
	{
		string name = SafeName(element);
		if (name.Length > 0) return name;
		try
		{
			string help = element.Current.HelpText ?? "";
			if (help.Trim().Length > 0) return help;
			return element.Current.AutomationId ?? "";
		}
		catch
		{
			return "";
		}
	}

	private static string CleanTabName(string value)
	{
		string text = CleanName(value)
			.Replace("选项卡", "")
			.Replace("标签页", "")
			.Replace("已选中", "")
			.Replace("选择", "")
			.Replace("Tab", "")
			.Trim();
		return text;
	}

	private static string CleanCommandName(string value)
	{
		string text = (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
		string[] lines = text.Split(new[] { '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
		if (lines.Length > 0)
		{
			text = lines.Select(CleanName).FirstOrDefault((string line) => line.Length > 0) ?? "";
		}
		else
		{
			text = CleanName(text);
		}
		while (text.EndsWith(".", StringComparison.Ordinal) || text.EndsWith("…", StringComparison.Ordinal) || text.EndsWith("。", StringComparison.Ordinal))
		{
			text = text.Substring(0, text.Length - 1).Trim();
		}
		return text;
	}

	private static string CleanName(string value)
	{
		string text = (value ?? "").Trim().Replace("&", "");
		return string.Join(" ", text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
	}

	private static bool IsQinJianTabName(string value)
	{
		string text = CleanTabName(value);
		return (text.StartsWith("琴剑", StringComparison.CurrentCultureIgnoreCase) && text.Length <= 18)
			|| text.StartsWith("QinJian", StringComparison.OrdinalIgnoreCase)
			|| text.StartsWith("KaiBa", StringComparison.OrdinalIgnoreCase)
			|| text.Equals("启翔铝型材", StringComparison.CurrentCultureIgnoreCase)
			|| text.StartsWith("启翔", StringComparison.CurrentCultureIgnoreCase);
	}

	internal static bool TabNameEqualsForTest(string left, string right)
		=> TabNameEquals(left, right);

	internal static bool PluginTabNamesMatchForTest(string left, string right)
		=> PluginTabNamesMatch(left, right);

	private static bool TabNameEquals(string left, string right)
		=> NormalizeTabName(left).Equals(NormalizeTabName(right), StringComparison.CurrentCultureIgnoreCase);

	// Keep only known localized aliases. Family/substring matching can select
	// a different module (for example Doctor Tools instead of Doctor SheetMetal).
	private static bool PluginTabNamesMatch(string left, string right)
	{
		if (TabNameEquals(left, right)) return true;
		string lhs = NormalizePluginTabName(left);
		string rhs = NormalizePluginTabName(right);
		if (lhs.Length == 0 || rhs.Length == 0) return false;
		string[][] aliases =
		{
			new[] { "doctorsheetmetalplusaddin", "博士钣金" },
			new[] { "dr工具", "doctortools" }
		};
		foreach (string[] group in aliases)
		{
			if (group.Contains(lhs, StringComparer.OrdinalIgnoreCase) && group.Contains(rhs, StringComparer.OrdinalIgnoreCase)) return true;
		}
		return false;
	}

	internal static int FindUniqueTabIndex(IEnumerable<string> names, string expectedName, bool externalPlugin, out bool ambiguous)
	{
		int found = -1, index = 0;
		ambiguous = false;
		foreach (string name in names ?? Enumerable.Empty<string>())
		{
			bool match = externalPlugin ? PluginTabNamesMatch(name, expectedName) : TabNameEquals(name, expectedName);
			if (match)
			{
				if (found >= 0) { ambiguous = true; return -1; }
				found = index;
			}
			index++;
		}
		return found;
	}

	private static string NormalizePluginTabName(string value)
	{
		string text = NormalizeTabName(value);
		return new string(text.Where(char.IsLetterOrDigit).ToArray());
	}

	private static string NormalizeTabName(string value)
	{
		string text = CleanTabName(value);
		if (text.StartsWith("QinJian", StringComparison.OrdinalIgnoreCase)) text = text.Substring("QinJian".Length).Trim();
		if (text.StartsWith("KaiBa", StringComparison.OrdinalIgnoreCase)) text = text.Substring("KaiBa".Length).Trim();
		return string.Join(" ", text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
	}

	private static string BuildFunctionKey(string category, string command)
	{
		string normalizedCategory = (category ?? "").Trim();
		const string prefix = "琴剑系列 > ";
		if (normalizedCategory.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
		{
			normalizedCategory = normalizedCategory.Substring(prefix.Length).Trim();
		}
		return NormalizeTabName(normalizedCategory) + "|" + CleanCommandName(command);
	}

	private static IntPtr FindSolidWorksWindow()
	{
		IntPtr found = IntPtr.Zero;
		NativeMethods.EnumWindows(delegate(IntPtr hwnd, IntPtr _)
		{
			if (!NativeMethods.IsWindowVisible(hwnd))
			{
				return true;
			}
			string title = GetWindowText(hwnd);
			if (title.IndexOf("SOLIDWORKS", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				found = hwnd;
				return false;
			}
			return true;
		}, IntPtr.Zero);
		return found;
	}

	private static string GetWindowText(IntPtr hwnd)
	{
		int length = NativeMethods.GetWindowTextLength(hwnd);
		if (length <= 0)
		{
			return "";
		}
		char[] text = new char[length + 1];
		int read = NativeMethods.GetWindowText(hwnd, text, text.Length);
		return read <= 0 ? "" : new string(text, 0, read);
	}

	private sealed class QinJianTab
	{
		public QinJianTab(string name, AutomationElement element, System.Windows.Rect bounds)
		{
			Name = name;
			Element = element;
			Bounds = bounds;
		}

		public string Name { get; }

		public AutomationElement Element { get; }

		public System.Windows.Rect Bounds { get; }
	}
}

internal static class ExternalPluginActionValue
{
	private const string Prefix = "external-plugin-ui:";

	public static string Format(string tabName, string commandName) => Prefix + Uri.EscapeDataString(tabName ?? "") + "/" + Uri.EscapeDataString(commandName ?? "");

	public static bool TryParse(string value, out string tabName, out string commandName)
	{
		tabName = ""; commandName = ""; string text = (value ?? "").Trim();
		if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
		int separator = text.IndexOf('/', Prefix.Length); if (separator <= Prefix.Length || separator >= text.Length - 1) return false;
		string encodedTab = text.Substring(Prefix.Length, separator - Prefix.Length), encodedCommand = text.Substring(separator + 1);
		if (encodedCommand.Contains("/") || !HasValidEscapes(encodedTab) || !HasValidEscapes(encodedCommand)) return false;
		try
		{
			tabName = Uri.UnescapeDataString(encodedTab).Trim();
			commandName = Uri.UnescapeDataString(encodedCommand).Trim();
			if (tabName.Length > 0 && commandName.Length > 0 && !tabName.Any(char.IsControl) && !commandName.Any(char.IsControl)) return true;
		}
		catch (UriFormatException) { }
		tabName = ""; commandName = "";
		return false;
	}

	private static bool HasValidEscapes(string value)
	{
		for (int index = 0; index < value.Length; index++)
		{
			if (value[index] != '%') continue;
			if (index + 2 >= value.Length || !Uri.IsHexDigit(value[index + 1]) || !Uri.IsHexDigit(value[index + 2])) return false;
			index += 2;
		}
		return true;
	}
}
