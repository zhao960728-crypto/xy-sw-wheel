using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

internal sealed class RadialMenuWindow : Form
{
	private static readonly IntPtr HwndTopMost = new IntPtr(-1);

	private static readonly Color TransparentKeyColor = Color.FromArgb(1, 2, 3);

	private readonly Action<MenuItemConfig> _selected;

	private readonly RadialMenuControl _control = new RadialMenuControl(TransparentKeyColor)
	{
		Dock = DockStyle.Fill,
		SurfaceOpacityAppliedByWindow = true
	};

	private readonly RadialForegroundWindow _foreground = new RadialForegroundWindow();
	private readonly ToolTip _itemTip = new ToolTip { InitialDelay = 400, ReshowDelay = 100, AutoPopDelay = 8000, ShowAlways = true };

	private readonly Timer _dragTimer = new Timer
	{
		Interval = 16
	};

	private PluginConfig _config = new PluginConfig();

	private SceneMenuConfig _scene = new SceneMenuConfig();

	private MouseButtons _triggerButton = MouseButtons.Right;

	private bool _dragMode;
	private bool _showing;
	private string _sceneKey = SceneKeys.Blank;
	private IntPtr _hostWindow;
	private int _previousButtons;
	private Point? _lastCursor;
	private Rectangle _lastCursorBounds;

	protected override bool ShowWithoutActivation => true;

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams obj = base.CreateParams;
			obj.ExStyle |= 134217728;
			return obj;
		}
	}

	public RadialMenuWindow(Action<MenuItemConfig> selected)
	{
		_selected = selected;
		base.FormBorderStyle = FormBorderStyle.None;
		base.ShowInTaskbar = false;
		base.TopMost = true;
		base.StartPosition = FormStartPosition.Manual;
		BackColor = TransparentKeyColor;
		base.TransparencyKey = TransparentKeyColor;
		base.KeyPreview = true;
		_control.DrawLabels = false;
		_control.UseNonActivatingInput();
		base.Controls.Add(_control);
		_control.ItemSelected += delegate(object _, MenuItemConfig item)
		{
			NativeCommandLaunchTrace.Mark("wheel-hide-start", item.Value);
			Hide();
			NativeCommandLaunchTrace.Mark("wheel-hide-ready", item.Value);
			_selected(item);
		};
		_control.CancelRequested += delegate
		{
			Hide();
		};
		_control.HoverTextChanged += text => _itemTip.SetToolTip(_control, text);
        _control.ViewChanged += (_, _) => {
            _dragMode = false; _control.EndDragMode();
            _lastCursor = null;
            _itemTip.SetToolTip(_control, "");
            _foreground.CopyNavigationFrom(_control);
            long style = GetWindowLongPtr(Handle, -20).ToInt64();
            NativeMethods.SetWindowLongPtr(Handle, -20, (IntPtr)(style | 0x08000000L));
            _previousButtons = PressedButtons(); _dragTimer.Start();
        };
		_dragTimer.Tick += delegate
		{
			TickDragCommit();
		};
	}

	public void ShowMenu(PluginConfig config, string sceneKey, Point center, bool dragMode, MouseButtons triggerButton)
	{
		if (!SceneResolver.IsEnabled(config, sceneKey)) { Hide(); return; }
		if (dragMode && !MouseWheelTrigger.IsButtonPressed(triggerButton)) return;
		_showing = true;
		try
		{
		_lastCursor = null;
		_itemTip.SetToolTip(_control, "");
		_config = config;
		_sceneKey = sceneKey;
		_scene = (config.Menus.TryGetValue(sceneKey, out SceneMenuConfig value) ? value : config.Menus["sw.blank"]);
		_dragMode = dragMode;
		_triggerButton = triggerButton;
		int menuSize = FitMenuSize(EffectiveMenuSize(config, _scene), Screen.FromPoint(center).WorkingArea.Size);
		base.Size = new Size(menuSize, menuSize);
		base.Opacity = ComputeSurfaceWindowOpacity(config.OpacityPercent, _scene.SurfaceOpacityPercent);
		base.Location = ClampToScreen(new Point(center.X - base.Width / 2, center.Y - base.Height / 2), base.Size, center);
		_control.SetMenu(config, _scene, dragMode, reuseCache: true);
		// Preparing a cold scene may take longer than the remaining hold.
		if (dragMode && !MouseWheelTrigger.IsButtonPressed(triggerButton)) return;
		long style = GetWindowLongPtr(Handle,-20).ToInt64();
		NativeMethods.SetWindowLongPtr(Handle,-20,(IntPtr)(style | 0x08000000));
		if (!base.Visible)
		{
			Show();
		}
		SetWindowPos(base.Handle, HwndTopMost, base.Left, base.Top, base.Width, base.Height, 80u);
		_foreground.ShowMenu(config, _scene, base.Bounds, dragMode);
		UpdateCursor(Cursor.Position);
		_previousButtons = PressedButtons();
			_dragTimer.Stop();
			_dragTimer.Start();
		}
		finally { _showing = false; if (!Visible) _foreground.Hide(); }
		Logger.Trace($"Overlay shown. Scene={sceneKey}, Drag={dragMode}, Center={center}, Bounds={base.Bounds}");
	}

	public void UpdateCursor(Point cursor)
	{
		if (base.Visible)
		{
			// The mouse hook and the 16 ms release timer can report the same
			// position. Keep release/focus checks active without redoing geometry.
			if (_lastCursor == cursor && _lastCursorBounds == Bounds) return;
			_lastCursor = cursor;
			_lastCursorBounds = Bounds;
			_control.UpdateCursor(PointToClient(cursor));
			_foreground.UpdateCursor(cursor);
		}
	}

	public void CommitCursor(Point cursor)
	{
		Point point;
		if (!base.Visible)
		{
			point = cursor;
			Logger.Trace("Commit ignored because overlay is hidden. Cursor=" + point.ToString());
			return;
		}
		point = cursor;
		Logger.Trace("Commit cursor=" + point.ToString());
		_dragTimer.Stop();
		_dragMode = false;
		_control.CommitAt(PointToClient(cursor));
	}

	public void ReloadConfig()
	{
		if (!SceneResolver.IsEnabled(_config, _sceneKey)) { Hide(); return; }
		if (base.Visible)
		{
			_lastCursor = null;
			_itemTip.SetToolTip(_control, "");
			base.Opacity = ComputeSurfaceWindowOpacity(_config.OpacityPercent, _scene.SurfaceOpacityPercent);
			_control.SetMenu(_config, _scene);
			_foreground.SetMenu(_config, _scene);
		}
	}

	internal void SetHostWindow(IntPtr window) => _hostWindow = window;

	protected override void WndProc(ref Message message)
	{
		if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; } // MA_NOACTIVATE: click still reaches the cell.
		base.WndProc(ref message);
	}

	public void Warmup(PluginConfig config, string preferredSceneKey)
	{
		if (!SceneResolver.IsEnabled(config, preferredSceneKey)) return;
		if (!Visible)
		{
			_foreground.Warmup(config, preferredSceneKey);
			if (config.Menus.TryGetValue(preferredSceneKey, out var scene))
			{
				int size = FitMenuSize(EffectiveMenuSize(config, scene), Screen.FromPoint(Cursor.Position).WorkingArea.Size);
				Size = new Size(size, size);
				PerformLayout();
				_control.SetMenu(config, scene, reuseCache: true);
				_control.WarmRenderCache();
			}
		}
	}

	protected override void SetVisibleCore(bool value)
	{
		// Handle creation, warmup and framework visibility changes are not a
		// wheel trigger. Only an enabled, explicit ShowMenu may reveal a popup.
		if (value && (!_showing || !SceneResolver.IsEnabled(_config, _sceneKey))) return;
		if (!value)
		{
			_lastCursor = null;
			if (!Disposing && !IsDisposed) _itemTip.Hide(_control);
			_control.Capture = false;
			_dragTimer.Stop();
			_dragMode = false;
			_foreground.Hide();
		}
		base.SetVisibleCore(value);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_dragTimer.Dispose();
			_foreground.Dispose();
			_itemTip.Dispose();
		}
		base.Dispose(disposing);
	}

	protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
	{
		if ((keyData & Keys.KeyCode) == Keys.Escape)
		{
			Hide();
			return true;
		}
		return base.ProcessCmdKey(ref msg, keyData);
	}

	protected override void OnDeactivate(EventArgs e)
	{
		base.OnDeactivate(e);
		if (Visible && !_dragMode && !_showing) Hide();
	}

	private static Point ClampToScreen(Point location, Size size, Point point)
	{
		Rectangle workingArea = Screen.FromPoint(point).WorkingArea;
		return new Point(Math.Max(workingArea.Left, Math.Min(location.X, workingArea.Right - size.Width)), Math.Max(workingArea.Top, Math.Min(location.Y, workingArea.Bottom - size.Height)));
	}

	private static int EffectiveMenuSize(PluginConfig config, SceneMenuConfig scene)
	{
		int size = scene.MenuSizePx > 0 ? scene.MenuSizePx : config.MenuSizePx;
		return Math.Max(260, Math.Min(900, size));
	}

	internal static double ComputeSurfaceWindowOpacity(int globalPercent, int surfacePercent) =>
		Math.Max(0.2, Math.Min(1.0, globalPercent / 100.0)) *
		Math.Max(5, Math.Min(100, surfacePercent > 0 ? surfacePercent : 65)) / 100.0;

	internal static int FitMenuSize(int configuredSize, Size available) => Math.Max(1, Math.Min(configuredSize, Math.Min(available.Width, available.Height) - 12));

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
	[DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")]
	private static extern IntPtr GetWindowLongPtr(IntPtr window,int index);

	private void TickDragCommit()
	{
		if (!SceneResolver.IsEnabled(_config, _sceneKey)) { Hide(); return; }
		if (!base.Visible)
		{
			_dragTimer.Stop();
			return;
		}
		Point position = Cursor.Position;
		if (_hostWindow != IntPtr.Zero)
		{
			NativeMethods.GetWindowThreadProcessId(_hostWindow, out uint hostProcess);
			NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out uint foregroundProcess);
			if (hostProcess == 0 || foregroundProcess != hostProcess) { Hide(); return; }
		}
		if (!_dragMode)
		{
			int buttons = PressedButtons();
			IntPtr pointed = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = position.X, Y = position.Y });
			bool overSurface = pointed == Handle || NativeMethods.IsChild(Handle, pointed);
			bool outsidePress = (buttons & ~_previousButtons) != 0 && !overSurface;
			_previousButtons = buttons;
			if (outsidePress) { Hide(); return; }
			UpdateCursor(position); return;
		}
		UpdateCursor(position);
		if (!MouseWheelTrigger.IsButtonPressed(_triggerButton))
		{
			Point point = position;
			Logger.Trace("Drag trigger released by radial timer at " + point.ToString());
			CommitCursor(position);
		}
	}

	private static int PressedButtons() => ((NativeMethods.GetAsyncKeyState(1) & 0x8000) != 0 ? 1 : 0)
		| ((NativeMethods.GetAsyncKeyState(2) & 0x8000) != 0 ? 2 : 0) | ((NativeMethods.GetAsyncKeyState(4) & 0x8000) != 0 ? 4 : 0);
}

