using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

internal sealed class RadialForegroundWindow : Form
{
	private static readonly IntPtr HwndTopMost = new IntPtr(-1);

	private static readonly Color TransparentKeyColor = Color.FromArgb(1, 2, 3);
	private bool _showingMenu;

	private readonly RadialMenuControl _control = new RadialMenuControl(TransparentKeyColor)
	{
		Dock = DockStyle.Fill,
		DrawBackdrop = false,
		DrawGuides = false,
		DrawHover = false,
		// This layer draws the live captions, so it is also the layer that can
		// annotate the open centre without making the surface behind it opaque.
		DrawCenterMark = true
	};

	protected override bool ShowWithoutActivation => true;

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams obj = base.CreateParams;
			obj.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
			obj.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
			obj.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
			return obj;
		}
	}

	public RadialForegroundWindow()
	{
		FormBorderStyle = FormBorderStyle.None;
		ShowInTaskbar = false;
		TopMost = true;
		StartPosition = FormStartPosition.Manual;
		BackColor = TransparentKeyColor;
		TransparencyKey = TransparentKeyColor;
		Controls.Add(_control);
	}

	public void ShowMenu(PluginConfig config, SceneMenuConfig scene, Rectangle bounds, bool dragMode)
	{
		if (config?.RadialMenuEnabled != true || scene == null || scene.Enabled == false) { Hide(); return; }
		_showingMenu = true;
		try
		{
		Bounds = bounds;
		Opacity = 1.0;
		_control.SetMenu(config, scene, dragMode, reuseCache: true);
		if (!Visible)
		{
			Show();
		}
		SetWindowPos(Handle, HwndTopMost, Left, Top, Width, Height, 0x0010 | 0x0040);
		UpdateCursor(Cursor.Position);
		}
		finally { _showingMenu = false; }
	}

	protected override void SetVisibleCore(bool value)
	{
		if (value && !_showingMenu) return;
		base.SetVisibleCore(value);
	}

	public void CopyNavigationFrom(RadialMenuControl source) => _control.CopyNavigationFrom(source);

    public void SetMenu(PluginConfig config, SceneMenuConfig scene)
	{
		if (Visible)
		{
			_control.SetMenu(config, scene);
		}
	}

	public void Warmup(PluginConfig config, string preferredSceneKey)
	{
		if (Visible || !SceneResolver.IsEnabled(config, preferredSceneKey))
		{
			return;
		}
		if (config.Menus.TryGetValue(preferredSceneKey, out var scene))
		{
			CreateControl();
			_control.CreateControl();
			// CreateControl skips invisible child handles. Warm the actual native
			// surfaces too, while keeping the popup hidden until the gesture opens.
			_ = Handle;
			_ = _control.Handle;
			int menuSize = RadialMenuWindow.FitMenuSize(EffectiveMenuSize(config, scene), Screen.FromPoint(Cursor.Position).WorkingArea.Size);
			ClientSize = new Size(menuSize, menuSize);
			PerformLayout();
			_control.SetMenu(config, scene, reuseCache: true);
			_control.WarmRenderCache();
		}
	}

	internal int AutoTextLayoutComputationCount => _control.AutoTextLayoutComputationCount;

	public void UpdateCursor(Point cursor)
	{
		if (Visible)
		{
			_control.UpdateCursor(PointToClient(cursor));
		}
	}

	public void EnsureOnTop()
	{
		if (Visible)
		{
			SetWindowPos(Handle, HwndTopMost, Left, Top, Width, Height, 0x0010 | 0x0040);
		}
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

	private static int EffectiveMenuSize(PluginConfig config, SceneMenuConfig scene)
	{
		int size = scene.MenuSizePx > 0 ? scene.MenuSizePx : config.MenuSizePx;
		return Math.Max(260, Math.Min(900, size));
	}
}

