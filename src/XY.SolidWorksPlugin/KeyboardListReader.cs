using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

internal static class KeyboardListReader
{
	public static bool TryRead(out List<KeyboardShortcutRow> rows, out string message)
	{
		rows = new List<KeyboardShortcutRow>();
		message = "";
		string text = null;
		bool flag = false;
		try
		{
			if (Clipboard.ContainsText())
			{
				flag = true;
				text = Clipboard.GetText();
			}
			TryClickCopyList();
			Thread.Sleep(400);
			string text2 = (Clipboard.ContainsText() ? Clipboard.GetText() : "");
			rows = (from r in Parse(text2)
				where !string.IsNullOrWhiteSpace(r.Hotkey)
				select r).ToList();
			if (rows.Count > 0)
			{
				return true;
			}
			message = "没有读到已设置的自定义快捷键。请先打开 SOLIDWORKS“自定义 > 键盘”，点“复制列表”或保持该窗口打开后再读取。";
			return false;
		}
		catch (Exception ex)
		{
			message = ex.Message;
			return false;
		}
		finally
		{
			try
			{
				if (flag && text != null)
				{
					Clipboard.SetText(text);
				}
			}
			catch
			{
			}
		}
	}

	private static List<KeyboardShortcutRow> Parse(string text)
	{
		List<KeyboardShortcutRow> list = new List<KeyboardShortcutRow>();
		string[] array = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = array[i].Trim();
			if (text2.Length == 0)
			{
				continue;
			}
			List<string> list2 = (from p in text2.Split('\t')
				select p.Trim() into p
				where p.Length > 0
				select p).ToList();
			if (list2.Count < 3)
			{
				list2 = (from p in Regex.Split(text2, "\\s{2,}")
					select p.Trim() into p
					where p.Length > 0
					select p).ToList();
			}
			if (list2.Count >= 3 && !IsHeader(list2[0], list2[1], list2[2]))
			{
				string text3 = NormalizeHotkey(list2[2]);
				if (!string.IsNullOrWhiteSpace(text3))
				{
					list.Add(new KeyboardShortcutRow
					{
						Category = Clean(list2[0]),
						Command = Clean(list2[1]),
						Hotkey = text3
					});
				}
			}
		}
		return list;
	}

	private static bool IsHeader(string category, string command, string hotkey)
	{
		if (!category.Contains("类别") && !command.Contains("命令"))
		{
			return hotkey.Contains("快捷");
		}
		return true;
	}

	private static string Clean(string value)
	{
		return Regex.Replace((value ?? "").Trim(), "\\(&?.\\)", "").Replace("&", "").Trim();
	}

	private static string NormalizeHotkey(string value)
	{
		return Regex.Replace(Regex.Replace(Regex.Replace(Regex.Replace((value ?? "").Trim().Replace("控制键", "Ctrl"), "\\s*\\+\\s*", "+"), "\\bCtrl\\s+", "Ctrl+", RegexOptions.IgnoreCase), "\\bAlt\\s+", "Alt+", RegexOptions.IgnoreCase), "\\bShift\\s+", "Shift+", RegexOptions.IgnoreCase).Trim();
	}

	private static void TryClickCopyList()
	{
		IntPtr intPtr = FindTopWindow((string title) => title.Equals("自定义", StringComparison.CurrentCultureIgnoreCase) || title.Equals("Customize", StringComparison.OrdinalIgnoreCase));
		if (intPtr == IntPtr.Zero)
		{
			intPtr = FindTopWindowWithChild((string title) => title.Contains("复制列表") || title.IndexOf("Copy List", StringComparison.OrdinalIgnoreCase) >= 0);
		}
		if (!(intPtr == IntPtr.Zero))
		{
			IntPtr intPtr2 = FindChild(intPtr, (string title) => title.Contains("复制列表") || title.IndexOf("Copy List", StringComparison.OrdinalIgnoreCase) >= 0);
			if (intPtr2 != IntPtr.Zero)
			{
				NativeMethods.PostMessage(intPtr2, 245, IntPtr.Zero, IntPtr.Zero);
			}
		}
	}

	private static IntPtr FindTopWindow(Func<string, bool> predicate)
	{
		IntPtr found = IntPtr.Zero;
		NativeMethods.EnumWindows(delegate(IntPtr hwnd, IntPtr _)
		{
			if (!NativeMethods.IsWindowVisible(hwnd))
			{
				return true;
			}
			if (predicate(GetText(hwnd)))
			{
				found = hwnd;
				return false;
			}
			return true;
		}, IntPtr.Zero);
		return found;
	}

	private static IntPtr FindTopWindowWithChild(Func<string, bool> childPredicate)
	{
		IntPtr found = IntPtr.Zero;
		NativeMethods.EnumWindows(delegate(IntPtr hwnd, IntPtr _)
		{
			if (NativeMethods.IsWindowVisible(hwnd) && FindChild(hwnd, childPredicate) != IntPtr.Zero)
			{
				found = hwnd;
				return false;
			}
			return true;
		}, IntPtr.Zero);
		return found;
	}

	private static IntPtr FindChild(IntPtr parent, Func<string, bool> predicate)
	{
		IntPtr found = IntPtr.Zero;
		NativeMethods.EnumChildWindows(parent, delegate(IntPtr hwnd, IntPtr _)
		{
			if (predicate(GetText(hwnd)))
			{
				found = hwnd;
				return false;
			}
			return true;
		}, IntPtr.Zero);
		return found;
	}

	private static string GetText(IntPtr hwnd)
	{
		int windowTextLength = NativeMethods.GetWindowTextLength(hwnd);
		if (windowTextLength <= 0)
		{
			return "";
		}
		char[] array = new char[windowTextLength + 1];
		int windowText = NativeMethods.GetWindowText(hwnd, array, array.Length);
		if (windowText > 0)
		{
			return new string(array, 0, windowText);
		}
		return "";
	}
}
