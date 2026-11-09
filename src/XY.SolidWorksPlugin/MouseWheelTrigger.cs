using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

internal sealed class MouseWheelTrigger : IDisposable
{
	private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

	private struct MouseInfo
	{
		public NativePoint Point;

		public IntPtr Window;

		public uint HitTestCode;

		public IntPtr ExtraInfo;
	}

	private struct NativePoint
	{
		public int X;

		public int Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct KeyboardMessage
	{
		public IntPtr Window;
		public uint Message;
		public IntPtr Key;
		public IntPtr Flags;
		public uint Time;
		public NativePoint Point;
		public uint Private;
	}

	private enum TriggerButton
	{
		Right,
		Middle
	}

	private const int WhMouse = 7;

	private const int WmMouseMove = 512;

	private const int WmMButtonDown = 519;

	private const int WmMButtonUp = 520;

	private const int WmRButtonDown = 516;

	private const int WmRButtonUp = 517;

	private const int VkShift = 16;

	private const int VkMenu = 18;

	private static readonly IntPtr ReplayMarker = new IntPtr(1481196109);

	private readonly Func<string> _triggerModeProvider;

	private readonly Func<int> _holdDelayProvider;

	private readonly Func<IntPtr> _solidWorksHwndProvider;

	private readonly Action _prepare;

	private readonly Action<Point, MouseButtons> _show;

	private readonly Action<Point> _commit;
	private readonly Func<MouseButtons, bool> _buttonPressed;
	private readonly Func<Point, bool> _isInHost;
	private readonly Action<MouseButtons> _replay;
	private readonly Func<bool>? _cancelMenu;

	private readonly LowLevelMouseProc _proc;
	private readonly LowLevelMouseProc _keyboardProc;

	private readonly object _stateSync = new object();

	private IntPtr _hook;
	private IntPtr _keyboardHook;
	private bool _escapeConsumed;
	private bool _gestureCancelled;
	private IntPtr _solidWorksWindow;

	private uint _solidWorksProcessId;

	private volatile bool _tracking;

	private volatile bool _active;

	private volatile bool _disposed;

	private Point _start;

	private Point _current;

	private System.Windows.Forms.Timer? _timer;

	private TriggerButton _triggerButton;

	private int _gestureId;

	public MouseWheelTrigger(Func<string> triggerModeProvider, Func<int> holdDelayProvider, Func<IntPtr> solidWorksHwndProvider, Action prepare, Action<Point, MouseButtons> show, Action<Point> commit,
		Func<MouseButtons, bool>? buttonPressed = null, Func<Point, bool>? isInHost = null, Action<MouseButtons>? replay = null, Func<bool>? cancelMenu = null)
	{
		_triggerModeProvider = triggerModeProvider;
		_holdDelayProvider = holdDelayProvider;
		_solidWorksHwndProvider = solidWorksHwndProvider;
		_prepare = prepare;
		_show = show;
		_commit = commit;
		_buttonPressed = buttonPressed ?? IsButtonPressed;
		_isInHost = isInHost ?? IsInSolidWorks;
		_replay = replay ?? (button => ReplayClick(button == MouseButtons.Middle ? TriggerButton.Middle : TriggerButton.Right));
		_cancelMenu = cancelMenu;
		_proc = HookCallback;
		_keyboardProc = KeyboardMessageCallback;
	}

	public void Start()
	{
		if (_disposed || _hook != IntPtr.Zero) return;
		IntPtr intPtr = _solidWorksHwndProvider();
		_solidWorksWindow = intPtr;
		uint processId;
		uint num = ((!(intPtr == IntPtr.Zero)) ? NativeMethods.GetWindowThreadProcessId(intPtr, out processId) : 0u);
		if (num == 0)
		{
			Logger.Error("Mouse hook install failed. SOLIDWORKS thread was not found; another add-in or window state may be interfering.");
			return;
		}
		_hook = SetWindowsHookEx(7, _proc, IntPtr.Zero, num);
		if (_hook == IntPtr.Zero)
		{
			Logger.Error("Mouse hook install failed. Win32Error=" + Marshal.GetLastWin32Error());
		}
		else
		{
			if (_cancelMenu != null)
			{
				// The popup never takes input focus. Consume its Escape on the host
				// UI thread so cancelling the wheel cannot also cancel a native tool.
				_keyboardHook = SetWindowsHookEx(3, _keyboardProc, IntPtr.Zero, num); // WH_GETMESSAGE
				if (_keyboardHook == IntPtr.Zero)
				{
					int error = Marshal.GetLastWin32Error();
					UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero;
					throw new System.ComponentModel.Win32Exception(error, "Wheel Escape hook could not be installed.");
				}
			}
			Logger.Info("SolidWorks thread mouse hook installed. ThreadId=" + num);
		}
	}

	public void Dispose()
	{
		lock (_stateSync)
		{
			_disposed = true;
			_tracking = false;
			_active = false;
			_gestureId++;
			_timer?.Dispose();
			_timer = null;
		}
		if (_hook != IntPtr.Zero)
		{
			UnhookWindowsHookEx(_hook);
			_hook = IntPtr.Zero;
		}
		if (_keyboardHook != IntPtr.Zero)
		{
			UnhookWindowsHookEx(_keyboardHook); _keyboardHook = IntPtr.Zero;
		}
	}

	private IntPtr KeyboardMessageCallback(int nCode, IntPtr wParam, IntPtr lParam)
	{
		// Never consume PeekMessage(PM_NOREMOVE) previews. Null only a removed
		// Escape belonging to this wheel, before native accelerators see it.
		if (nCode == 0 && !_disposed && wParam == (IntPtr)1)
		{
			var message = Marshal.PtrToStructure<KeyboardMessage>(lParam);
			if (message.Key == (IntPtr)27
				&& (message.Message == 0x0100 || message.Message == 0x0101 || message.Message == 0x0104 || message.Message == 0x0105)
				&& TryCancelEscape(message.Message == 0x0101 || message.Message == 0x0105))
			{
				message.Message = 0; // WM_NULL, preserving the rest of the message.
				Marshal.StructureToPtr(message, lParam, false);
			}
		}
		return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
	}

	private bool TryCancelEscape(bool released)
	{
		if (released)
		{
			if (!_escapeConsumed) return false;
			_escapeConsumed = false; return true;
		}
		if (_escapeConsumed) return true;
		bool cancelled = _cancelMenu?.Invoke() == true;
		lock (_stateSync)
		{
			if (_tracking)
			{
				_timer?.Dispose(); _timer = null;
				_gestureCancelled = true; _gestureId++;
				// Keep tracking until button-up, swallowing the cancelled hold's
				// release without replaying a click or committing a stale selection.
			}
		}
		if (!cancelled) return false;
		_escapeConsumed = true; return true;
	}

	private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
	{
		// HC_NOREMOVE is only a PeekMessage preview, not another button event.
		if (nCode != 0 || _disposed)
		{
			return CallNextHookEx(_hook, nCode, wParam, lParam);
		}
		MouseInfo mouseInfo = Marshal.PtrToStructure<MouseInfo>(lParam);
		if (mouseInfo.ExtraInfo == ReplayMarker)
		{
			return CallNextHookEx(_hook, nCode, wParam, lParam);
		}
		int num = wParam.ToInt32();
		if (!_tracking && !IsAnyTriggerDown(num))
		{
			return CallNextHookEx(_hook, nCode, wParam, lParam);
		}
		if (_tracking && num != 512 && !IsAnyTriggerUp(num) && !IsAnyTriggerDown(num))
		{
			return CallNextHookEx(_hook, nCode, wParam, lParam);
		}
		Point point = new Point(mouseInfo.Point.X, mouseInfo.Point.Y);
		if (TryGetTriggerButton(num, out var button))
		{
			if (!_isInHost(point))
			{
				return CallNextHookEx(_hook, nCode, wParam, lParam);
			}
			lock (_stateSync)
			{
				if (_tracking)
				{
					_timer?.Dispose();
					_timer = null;
					_tracking = false;
					_active = false;
				}
				int gestureId = ++_gestureId;
				_tracking = true;
				_active = false;
				_gestureCancelled = false;
				_triggerButton = button;
				_start = point;
				_current = point;
				_timer?.Dispose();
				// Serialize activation with the host's mouse messages on its UI thread.
				_timer = new System.Windows.Forms.Timer { Interval = NormalizeHoldDelay(_holdDelayProvider()) };
				_timer.Tick += (_, __) => Activate(gestureId);
				_timer.Start();
			}
			Logger.Trace("Hold tracking started at " + point);
			return (IntPtr)1;
		}
		if (!_tracking)
		{
			return CallNextHookEx(_hook, nCode, wParam, lParam);
		}
		if (num == 512)
		{
			lock (_stateSync)
			{
				if (_tracking)
				{
					_current = point;
				}
			}
			return CallNextHookEx(_hook, nCode, wParam, lParam);
		}
		if (IsTriggerUp(num, _triggerButton))
		{
			bool active;
			bool cancelled;
			lock (_stateSync)
			{
				_timer?.Dispose();
				_timer = null;
				active = _active;
				cancelled = _gestureCancelled;
				_tracking = false;
				_active = false;
				_gestureId++;
			}
			if (cancelled) return (IntPtr)1;
			if (!active)
			{
				// Do not move the cursor back or inject a click into another application.
				if (_isInHost(Cursor.Position)) _replay(ToMouseButtons(_triggerButton));
			}
			else
			{
				Logger.Trace("Hold released in SolidWorks hook at " + point);
				_commit(point);
			}
			return (IntPtr)1;
		}
		return CallNextHookEx(_hook, nCode, wParam, lParam);
	}

	private bool TryGetTriggerButton(int message, out TriggerButton button)
	{
		button = TriggerButton.Right;
		string text = TriggerModes.Normalize(_triggerModeProvider());
		if (text == TriggerModes.Disabled)
		{
			return false;
		}
		if (message == 519 && text == "middle-hold")
		{
			button = TriggerButton.Middle;
			return true;
		}
		if (message != 516)
		{
			return false;
		}
		if (text == "shift-right-hold")
		{
			return IsKeyDown(16);
		}
		if (text == "alt-right-hold")
		{
			return IsKeyDown(18);
		}
		return text == "right-hold";
	}

	private static bool IsAnyTriggerDown(int message)
	{
		if (message == 516 || message == 519)
		{
			return true;
		}
		return false;
	}

	private static bool IsAnyTriggerUp(int message)
	{
		if (message == 517 || message == 520)
		{
			return true;
		}
		return false;
	}

	private static bool IsTriggerUp(int message, TriggerButton button)
	{
		if (button != TriggerButton.Middle)
		{
			return message == 517;
		}
		return message == 520;
	}

	private static bool IsKeyDown(int virtualKey)
	{
		return (GetKeyState(virtualKey) & 0x8000) != 0;
	}

	private void Activate(int gestureId)
	{
		Point start;
		Point current;
		TriggerButton triggerButton;
		lock (_stateSync)
		{
			if (!_tracking || _active || _disposed || gestureId != _gestureId)
			{
				return;
			}
			_timer?.Dispose();
			_timer = null;
			start = _start;
			current = _current;
			triggerButton = _triggerButton;
		}
		// A busy host can process the timer after the physical button was released.
		// Keep pending short-click state until its queued button-up can replay it.
		if (!CanActivate(gestureId, triggerButton)) return;
		try
		{
			_prepare();
			if (!CanActivate(gestureId, triggerButton)) return;
			_active = true;
			Logger.Trace("Hold activated. Start=" + start + " Current=" + current);
			_show(start, ToMouseButtons(triggerButton));
		}
		catch (Exception ex)
		{
			_active = false;
			Logger.Error("Radial trigger activation failed", ex);
		}
	}

	private bool CanActivate(int gestureId, TriggerButton button)
	{
		return !_disposed && _tracking && !_gestureCancelled && gestureId == _gestureId
			&& _buttonPressed(ToMouseButtons(button)) && _isInHost(Cursor.Position)
			&& TryGetTriggerButton(button == TriggerButton.Middle ? WmMButtonDown : WmRButtonDown, out var configured)
			&& configured == button;
	}

	internal static bool IsButtonPressed(MouseButtons button)
	{
		int key = button == MouseButtons.Middle ? 4 : (GetSystemMetrics(23) != 0 ? 1 : 2);
		return (GetAsyncKeyState(key) & 0x8000) != 0;
	}

	// Same hold range as the integrated reference trigger; it stays distinct
	// from the 16 ms pointer refresh used while the wheel is already visible.
	internal static int NormalizeHoldDelay(int milliseconds) => Math.Max(150, Math.Min(2000, milliseconds));

	private static MouseButtons ToMouseButtons(TriggerButton button)
	{
		if (button != TriggerButton.Middle)
		{
			return MouseButtons.Right;
		}
		return MouseButtons.Middle;
	}

	private bool IsInSolidWorks(Point point)
	{
		// Never enter SOLIDWORKS COM from a mouse-hook callback. The host window is
		// stable for the lifetime of this add-in instance and was captured at Start().
		IntPtr intPtr = _solidWorksWindow;
		if (intPtr == IntPtr.Zero || !NativeMethods.GetWindowRect(intPtr, out var rect))
		{
			return false;
		}
		if (point.X < rect.Left || point.X >= rect.Right || point.Y < rect.Top + 90 || point.Y >= rect.Bottom)
		{
			return false;
		}
		IntPtr foregroundWindow = NativeMethods.GetForegroundWindow();
		if (!IsSameProcessWindow(foregroundWindow, intPtr))
		{
			return false;
		}
		IntPtr intPtr2 = NativeMethods.WindowFromPoint(new NativeMethods.POINT
		{
			X = point.X,
			Y = point.Y
		});
		if (intPtr2 != IntPtr.Zero)
		{
			return IsSameProcessWindow(intPtr2, intPtr);
		}
		return false;
	}

	private bool IsSameProcessWindow(IntPtr candidate, IntPtr solidWorksHwnd)
	{
		if (candidate == IntPtr.Zero)
		{
			return false;
		}
		NativeMethods.GetWindowThreadProcessId(solidWorksHwnd, out var processId);
		if (processId == 0)
		{
			return false;
		}
		_solidWorksProcessId = processId;
		NativeMethods.GetWindowThreadProcessId(candidate, out var processId2);
		return processId2 == _solidWorksProcessId;
	}

	private void ReplayClick(TriggerButton button)
	{
		UIntPtr marker = new UIntPtr(unchecked((ulong)ReplayMarker.ToInt64()));
		if (button == TriggerButton.Middle)
		{
			NativeMethods.mouse_event(32u, 0u, 0u, 0u, marker);
			NativeMethods.mouse_event(64u, 0u, 0u, 0u, marker);
		}
		else
		{
			NativeMethods.mouse_event(8u, 0u, 0u, 0u, marker);
			NativeMethods.mouse_event(16u, 0u, 0u, 0u, marker);
		}
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool UnhookWindowsHookEx(IntPtr hhk);

	[DllImport("user32.dll")]
	private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	private static extern short GetKeyState(int nVirtKey);

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int nVirtKey);

	[DllImport("user32.dll")]
	private static extern int GetSystemMetrics(int index);
}
