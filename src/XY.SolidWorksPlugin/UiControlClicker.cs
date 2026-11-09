using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Automation;

namespace XY.SolidWorksPlugin;

internal static class UiControlClicker
{
	private const int BM_CLICK = 0x00F5;
	private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
	private const uint MOUSEEVENTF_LEFTUP = 0x0004;

	public static bool ClickByText(IntPtr root, string targetText, TimeSpan timeout, out string message)
	{
		return ClickByText(root, targetText, timeout, CancellationToken.None, out message);
	}

	public static bool ClickByText(IntPtr root, string targetText, TimeSpan timeout, CancellationToken cancellationToken, out string message)
	{
		return ClickAnyByText(root, SplitTargets(targetText), timeout, cancellationToken, out message);
	}

	internal static bool ClickUniqueByText(IntPtr root, string targetText, TimeSpan timeout, CancellationToken cancellationToken, out string message)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var targets = BuildTargets(SplitTargets(targetText));
		if (root == IntPtr.Zero || targets.Count == 0) { message = "点击目标窗口或名称为空。"; return false; }
		var clock = Stopwatch.StartNew();
		do
		{
			cancellationToken.ThrowIfCancellationRequested();
			var native = new HashSet<IntPtr>();
			NativeMethods.EnumChildWindows(root, (window, _) =>
			{
				if (NativeMethods.IsWindowVisible(window) && NativeMethods.IsWindowEnabled(window)
					&& GetClassName(window).IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0
					&& targets.Contains(Normalize(GetText(window)), StringComparer.OrdinalIgnoreCase)) native.Add(window);
				return true;
			}, IntPtr.Zero);
			if (native.Count > 1) { message = "当前窗口有多个同名按钮，路径不唯一，未点击。"; return false; }
			if (native.Count == 1) { cancellationToken.ThrowIfCancellationRequested(); Click(native.Single()); message = ""; return true; }
			try
			{
				var element = AutomationElement.FromHandle(root);
				var matches = new List<AutomationElement>();
				foreach (AutomationElement candidate in element.FindAll(TreeScope.Descendants, Condition.TrueCondition))
				{
					var state = candidate.Current;
					if (!state.IsEnabled || state.IsOffscreen || !targets.Contains(Normalize(state.Name), StringComparer.OrdinalIgnoreCase)) continue;
					if (state.ControlType == ControlType.Button || state.ControlType == ControlType.MenuItem || state.ControlType == ControlType.SplitButton || state.ControlType == ControlType.Hyperlink) matches.Add(candidate);
				}
				if (matches.Count > 1) { message = "当前窗口有多个同名界面功能，路径不唯一，未点击。"; return false; }
				if (matches.Count == 1)
				{
					if (!matches[0].TryGetCurrentPattern(InvokePattern.Pattern, out object pattern) || !(pattern is InvokePattern invoke))
					{ message = "唯一目标没有可靠的调用接口，未猜测鼠标位置。"; return false; }
					cancellationToken.ThrowIfCancellationRequested();
					invoke.Invoke(); message = ""; return true;
				}
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex) { message = "核验或调用界面路径失败，未尝试第二条激发路径：" + ex.Message; return false; }
			if (cancellationToken.WaitHandle.WaitOne(50)) throw new OperationCanceledException(cancellationToken);
		} while (clock.Elapsed < timeout);
		message = "当前窗口没有找到唯一、可用的准确按钮名称：" + targetText;
		return false;
	}

	public static bool ContainsByText(IntPtr root, string targetText)
	{
		List<string> targets = BuildTargets(SplitTargets(targetText));
		if (targets.Count == 0)
		{
			return false;
		}
		foreach (IntPtr searchRoot in SearchRoots(root))
		{
			if (FindBestWindow(searchRoot, targets) != IntPtr.Zero)
			{
				return true;
			}
			string rootText = Normalize(GetText(searchRoot));
			if (rootText.Length > 0 && MatchScore(rootText, targets) > 0)
			{
				return true;
			}
			if (TryFindAutomation(searchRoot, targets))
			{
				return true;
			}
		}
		return false;
	}

	public static bool ContainsByTextNative(IntPtr root, string targetText)
	{
		List<string> targets = BuildTargets(SplitTargets(targetText));
		if (targets.Count == 0)
		{
			return false;
		}
		foreach (IntPtr searchRoot in SearchRoots(root))
		{
			if (FindBestWindow(searchRoot, targets) != IntPtr.Zero)
			{
				return true;
			}
			string rootText = Normalize(GetText(searchRoot));
			if (rootText.Length > 0 && MatchScore(rootText, targets) > 0)
			{
				return true;
			}
		}
		return false;
	}

	public static bool ContainsByTextInRoot(IntPtr root, string targetText)
	{
		List<string> targets = BuildTargets(SplitTargets(targetText));
		if (root == IntPtr.Zero || targets.Count == 0)
		{
			return false;
		}
		if (FindBestWindow(root, targets) != IntPtr.Zero)
		{
			return true;
		}
		string rootText = Normalize(GetText(root));
		return rootText.Length > 0 && MatchScore(rootText, targets) > 0
			|| TryFindAutomation(root, targets);
	}

	public static IntPtr FindByTextNative(IntPtr root, string targetText)
	{
		List<string> targets = BuildTargets(SplitTargets(targetText));
		if (targets.Count == 0)
		{
			return IntPtr.Zero;
		}
		return root == IntPtr.Zero ? IntPtr.Zero : FindBestWindow(root, targets);
	}

	public static void ClickNativeControl(IntPtr control)
	{
		if (control != IntPtr.Zero)
		{
			Click(control);
		}
	}

	public static bool ClickAnyByText(IntPtr root, IEnumerable<string> targetTexts, TimeSpan timeout, out string message)
	{
		return ClickAnyByText(root, targetTexts, timeout, CancellationToken.None, out message);
	}

	public static bool ClickAnyByText(IntPtr root, IEnumerable<string> targetTexts, TimeSpan timeout, CancellationToken cancellationToken, out string message)
	{
		cancellationToken.ThrowIfCancellationRequested();
		List<string> targets = BuildTargets(targetTexts);
		if (targets.Count == 0)
		{
			message = "点击目标为空。";
			return false;
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		do
		{
			cancellationToken.ThrowIfCancellationRequested();
			foreach (IntPtr searchRoot in SearchRoots(root))
			{
				IntPtr found = FindBestWindow(searchRoot, targets);
				if (found != IntPtr.Zero)
				{
					Click(found);
					message = "";
					return true;
				}
				if (TryClickAutomation(searchRoot, targets))
				{
					message = "";
					return true;
				}
			}
			if (cancellationToken.WaitHandle.WaitOne(50))
			{
				throw new OperationCanceledException(cancellationToken);
			}
		}
		while (stopwatch.Elapsed < timeout);
		message = "没有在 SOLIDWORKS 窗口内找到按钮/控件：" + string.Join(" / ", targets);
		return false;
	}

	private static List<string> BuildTargets(IEnumerable<string> targetTexts)
	{
		return targetTexts
			.Select(Normalize)
			.Where((string target) => target.Length > 0)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static IReadOnlyList<IntPtr> SearchRoots(IntPtr root)
	{
		List<IntPtr> roots = new List<IntPtr>();
		if (root != IntPtr.Zero)
		{
			roots.Add(root);
		}
		if (root != IntPtr.Zero)
		{
			NativeMethods.GetWindowThreadProcessId(root, out uint rootProcessId);
			if (rootProcessId != 0)
			{
				NativeMethods.EnumWindows(delegate(IntPtr hwnd, IntPtr _)
				{
					NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
					if (processId == rootProcessId && NativeMethods.IsWindowVisible(hwnd))
					{
						roots.Add(hwnd);
					}
					return true;
				}, IntPtr.Zero);
			}
		}
		return roots.Where((IntPtr hwnd) => hwnd != IntPtr.Zero).Distinct().ToList();
	}

	private static bool TryFindAutomation(IntPtr root, IReadOnlyList<string> targets)
	{
		try
		{
			AutomationElement rootElement = AutomationElement.FromHandle(root);
			if (rootElement == null)
			{
				return false;
			}
			AutomationElementCollection elements = rootElement.FindAll(TreeScope.Descendants, Condition.TrueCondition);
			foreach (AutomationElement element in elements)
			{
				string name = Normalize(SafeCurrentName(element));
				if (name.Length > 0 && MatchScore(element, name, targets) > 0)
				{
					return true;
				}
			}
		}
		catch
		{
		}
		return false;
	}

	private static bool TryClickAutomation(IntPtr root, IReadOnlyList<string> targets)
	{
		try
		{
			AutomationElement rootElement = AutomationElement.FromHandle(root);
			if (rootElement == null)
			{
				return false;
			}
			AutomationElementCollection elements = rootElement.FindAll(TreeScope.Descendants, Condition.TrueCondition);
			List<AutomationMatch> matches = new List<AutomationMatch>();
			foreach (AutomationElement element in elements)
			{
				string name = Normalize(SafeCurrentName(element));
				if (name.Length == 0)
				{
					continue;
				}
				int score = MatchScore(element, name, targets);
				if (score > 0)
				{
					matches.Add(new AutomationMatch(element, score));
				}
			}
			foreach (AutomationMatch match in matches.OrderByDescending((AutomationMatch item) => item.Score))
			{
				if (TryInvoke(match.Element) || TryMouseClick(match.Element))
				{
					return true;
				}
			}
		}
		catch
		{
		}
		return false;
	}

	private static string SafeCurrentName(AutomationElement element)
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

	private static bool TryInvoke(AutomationElement element)
	{
		try
		{
			if (element.TryGetCurrentPattern(InvokePattern.Pattern, out object pattern) && pattern is InvokePattern invokePattern)
			{
				invokePattern.Invoke();
				return true;
			}
		}
		catch
		{
		}
		return false;
	}

	private static bool TryMouseClick(AutomationElement element)
	{
		try
		{
			if (element.TryGetClickablePoint(out System.Windows.Point point))
			{
				MouseClick((int)point.X, (int)point.Y);
				return true;
			}
			System.Windows.Rect rect = element.Current.BoundingRectangle;
			if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0)
			{
				MouseClick((int)(rect.Left + rect.Width / 2), (int)(rect.Top + rect.Height / 2));
				return true;
			}
		}
		catch
		{
		}
		return false;
	}

	private static IntPtr FindBestWindow(IntPtr root, IReadOnlyList<string> targets)
	{
		List<WindowMatch> matches = new List<WindowMatch>();
		NativeMethods.EnumChildWindows(root, delegate(IntPtr hwnd, IntPtr _)
		{
			if (!NativeMethods.IsWindowVisible(hwnd) || !NativeMethods.IsWindowEnabled(hwnd))
			{
				return true;
			}
			string text = Normalize(GetText(hwnd));
			if (text.Length == 0)
			{
				return true;
			}
			int score = MatchScore(text, targets);
			if (score > 0)
			{
				if (GetClassName(hwnd).IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					score += 80;
				}
				matches.Add(new WindowMatch(hwnd, score));
			}
			return true;
		}, IntPtr.Zero);
		return matches.OrderByDescending((WindowMatch item) => item.Score).Select((WindowMatch item) => item.Handle).FirstOrDefault();
	}

	private static void Click(IntPtr hwnd)
	{
		if (GetClassName(hwnd).IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			NativeMethods.SendMessage(hwnd, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
			return;
		}
		if (NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
		{
			int x = rect.Left + Math.Max(1, (rect.Right - rect.Left) / 2);
			int y = rect.Top + Math.Max(1, (rect.Bottom - rect.Top) / 2);
			MouseClick(x, y);
		}
	}

	private static void MouseClick(int x, int y)
	{
		NativeMethods.SetCursorPos(x, y);
		Thread.Sleep(20);
		NativeMethods.mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
		NativeMethods.mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
	}

	private static int MatchScore(AutomationElement element, string text, IReadOnlyList<string> targets)
	{
		int score = MatchScore(text, targets);
		if (score <= 0)
		{
			return 0;
		}
		try
		{
			ControlType controlType = element.Current.ControlType;
			if (controlType == ControlType.Button || controlType == ControlType.MenuItem || controlType == ControlType.Hyperlink)
			{
				score += 100;
			}
			else if (controlType == ControlType.TabItem)
			{
				score += 20;
			}
		}
		catch
		{
		}
		return score;
	}

	private static int MatchScore(string text, IReadOnlyList<string> targets)
	{
		foreach (string target in targets)
		{
			if (text.Equals(target, StringComparison.OrdinalIgnoreCase))
			{
				return 100;
			}
			if (text.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return 70;
			}
			if (target.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return 40;
			}
		}
		return 0;
	}

	private static string GetClassName(IntPtr hwnd)
	{
		char[] buffer = new char[256];
		int read = NativeMethods.GetClassName(hwnd, buffer, buffer.Length);
		return read <= 0 ? "" : new string(buffer, 0, read);
	}

	private static string GetText(IntPtr hwnd)
	{
		int length = NativeMethods.GetWindowTextLength(hwnd);
		if (length <= 0)
		{
			return "";
		}
		char[] buffer = new char[length + 1];
		int read = NativeMethods.GetWindowText(hwnd, buffer, buffer.Length);
		return read <= 0 ? "" : new string(buffer, 0, read);
	}

	private static string Normalize(string value)
	{
		string text = (value ?? "").Trim().Replace("&", "");
		return new string(text.Where((char c) => !char.IsWhiteSpace(c) && c != ':' && c != '：').ToArray());
	}

	private static IEnumerable<string> SplitTargets(string value)
	{
		return (value ?? "").Split(new[] { '|', '；', ';', '/', '，', ',', '、' }, StringSplitOptions.RemoveEmptyEntries);
	}

	private sealed class AutomationMatch
	{
		public AutomationMatch(AutomationElement element, int score)
		{
			Element = element;
			Score = score;
		}

		public AutomationElement Element { get; }

		public int Score { get; }
	}

	private readonly struct WindowMatch
	{
		public WindowMatch(IntPtr handle, int score)
		{
			Handle = handle;
			Score = score;
		}

		public IntPtr Handle { get; }

		public int Score { get; }
	}
}
