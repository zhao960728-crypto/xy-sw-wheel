using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace XY.SolidWorksPlugin;

internal sealed class PropertyWatermarkHost : IDisposable
{
	private readonly Func<IntPtr> _hostHandle;
	private readonly Func<ISldWorks> _getSolidWorks;
	private readonly Func<PropertyWatermarkConfig> _getConfig;
	private readonly Action _saveConfig;
	private readonly Action<Action> _dispatch;
	private PropertyWatermarkWindow _window;
	private readonly WatermarkRefreshQueue _refreshQueue = new WatermarkRefreshQueue();
	private int _refreshPosted;

	public PropertyWatermarkHost(Func<IntPtr> hostHandle, Func<ISldWorks> getSolidWorks, Func<PropertyWatermarkConfig> getConfig, Action saveConfig, Action<Action> dispatch = null)
	{
		_dispatch = dispatch ?? (action => action());
		_hostHandle = hostHandle;
		_getSolidWorks = getSolidWorks;
		_getConfig = getConfig;
		_saveConfig = saveConfig;
	}

	public void Start()
	{
		_dispatch(() =>
		{
			if (_window != null) return;
			_window = new PropertyWatermarkWindow(_hostHandle, _getSolidWorks, _getConfig, _saveConfig);
			// Create the handle on the host STA. Its timer and edit callbacks must
			// never make SOLIDWORKS COM calls from a second apartment.
			_ = _window.Handle;
			_window.RefreshWhenIdle();
		});
	}

	public void Reload()
	{
		var window = _window;
		if (window == null || window.IsDisposed) return;
		_refreshQueue.Request(0);
		PostRefresh(window);
	}

	private void PostRefresh(PropertyWatermarkWindow window)
	{
		if (Interlocked.CompareExchange(ref _refreshPosted, 1, 0) != 0) return;
		try
		{
			window.BeginInvoke((Action)(() =>
			{
				try
				{
					if (ReferenceEquals(_window, window) && !window.IsDisposed && _refreshQueue.TryBegin(0, out long token))
					{
						window.RefreshWhenIdle();
						_refreshQueue.Complete(token);
					}
				}
				finally
				{
					Interlocked.Exchange(ref _refreshPosted, 0);
					if (ReferenceEquals(_window, window) && !window.IsDisposed && _refreshQueue.Pending) PostRefresh(window);
				}
			}));
		}
		catch (InvalidOperationException) { Interlocked.Exchange(ref _refreshPosted, 0); }
	}

	public void Dispose()
	{
		_dispatch(() =>
		{
			var window = _window;
			_window = null;
			_refreshQueue.Cancel();
			window?.Dispose();
		});
	}
}

internal sealed class PropertyWatermarkWindow : Form
{
	private const int WmNchittest = 0x84;
	private const int WmEnterSizeMove = 0x0231;
	private const int WmExitSizeMove = 0x0232;
	private const int WmNcLButtonDown = 0x00A1;
	private const int HtTransparent = -1;
	private const int HtCaption = 2;
	private const int HtClient = 1;
	private readonly Func<IntPtr> _hostHandle;
	private readonly Func<ISldWorks> _getSolidWorks;
	private readonly Func<PropertyWatermarkConfig> _getConfig;
	private readonly Action _saveConfig;
	// Selection/property polling runs on the host STA between input messages.
	// No global idle requirement: selecting components remains responsive.
	private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 500 };
	private readonly Label _caption = new Label { AutoSize = true, UseMnemonic = false, Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(30, 41, 59) };
	private readonly Label _content = new Label { AutoSize = true, UseMnemonic = false, Font = new Font("Microsoft YaHei UI", 9f), ForeColor = Color.FromArgb(51, 65, 85) };
	private readonly Button _toggle = new Button { FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 9f), TabStop = false, Text = "−", Width = 22, Height = 22, Cursor = Cursors.Hand };
	private readonly List<PropertyWatermarkField> _visibleFields = new List<PropertyWatermarkField>();
	private string _lastText = "";
	private string _lastAppearance = "";
	private int _lastLayoutWidth;
	private IntPtr _attachedHost;
	private bool _positioning;
	private bool _dragging;
	private bool _expanded = true;
	private string _displayedConfiguration = "";
	private IModelDoc2 _displayedDocument;
	private bool _editing;
	private PluginToolWindow _editDialog;
	private bool _refreshing;
	private string _lastRefreshError;
	private readonly ToolTip _details = new ToolTip { AutoPopDelay = 15000, InitialDelay = 500 };

	public PropertyWatermarkWindow(Func<IntPtr> hostHandle, Func<ISldWorks> getSolidWorks, Func<PropertyWatermarkConfig> getConfig, Action saveConfig)
	{
		_hostHandle = hostHandle;
		_getSolidWorks = getSolidWorks;
		_getConfig = getConfig;
		_saveConfig = saveConfig;
		FormBorderStyle = FormBorderStyle.None;
		ShowInTaskbar = false;
		StartPosition = FormStartPosition.Manual;
		BackColor = Color.White;
		Padding = new Padding(10, 8, 10, 8);
		Controls.Add(_caption);
		Controls.Add(_content);
		Controls.Add(_toggle);
		_toggle.FlatAppearance.BorderSize = 0;
		_toggle.Click += (_, __) => ToggleExpanded();
		_details.SetToolTip(_toggle, "收起 / 展开属性；双击属性值可编辑，原生属性只读。");
		MouseDown += BeginMove;
		_caption.MouseDown += BeginMove;
		_content.MouseDoubleClick += EditProperty;
		_timer.Tick += (_, __) => RefreshWhenIdle();
		_timer.Start();
	}

	internal void RefreshWhenIdle()
	{
		if (_dragging) return;
		try { RefreshNow(); _lastRefreshError=null; }
		catch (Exception ex)
		{
			ClearDisplay();
			string key=ex.GetType().FullName+"|"+ex.Message;
			if(key!=_lastRefreshError) { _lastRefreshError=key; Logger.Error("Watermark refresh failed",ex); }
		}
	}

	protected override bool ShowWithoutActivation => true;

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams cp = base.CreateParams;
		cp.ExStyle |= 0x08000000 | 0x00000080; // no activation, tool window
			return cp;
		}
	}

	protected override void WndProc(ref Message m)
	{
		if (m.Msg == WmNchittest)
		{
			Point screen = new Point((short)(m.LParam.ToInt64() & 0xffff), (short)((m.LParam.ToInt64() >> 16) & 0xffff));
			Point client = PointToClient(screen);
			if (_toggle.Bounds.Contains(client) || (_expanded && _content.Bounds.Contains(client))) { m.Result = (IntPtr)HtClient; return; }
			m.Result = (IntPtr)(_getConfig().MoveEnabled ? HtCaption : HtTransparent);
			return;
		}
		if (m.Msg == WmEnterSizeMove) _dragging = true;
		if (m.Msg == WmExitSizeMove) { _dragging = false; SaveMovedPosition(); }
		base.WndProc(ref m);
	}

	public void RefreshNow()
	{
		if (_refreshing || IsDisposed) return;
		_refreshing = true;
		try
		{
			PropertyWatermarkConfig config = _getConfig();
			IntPtr host = _hostHandle();
			if (config == null || !config.Enabled || host == IntPtr.Zero || !NativeMethods.IsWindowVisible(host) || NativeMethods.IsIconic(host)) { ClearDisplay(); return; }
			IModelDoc2 document = ResolveWatermarkDocument(config, out string configuration);
			if (config == null || !config.Enabled || !IsSupportedDocument(document) || host == IntPtr.Zero || !NativeMethods.IsWindowVisible(host) || NativeMethods.IsIconic(host))
			{
				ClearDisplay();
				return;
			}
			if (_editing)
			{
				if (!Equals(document, _displayedDocument) || configuration != _displayedConfiguration) ClearDisplay();
				return;
			}
			AttachToHost(host);
			_displayedConfiguration = configuration;
			string text = BuildText(config, document);
			_displayedDocument = document;
			string appearance = AppearanceKey(config);
			if (!string.Equals(text, _lastText, StringComparison.Ordinal) || !string.Equals(appearance, _lastAppearance, StringComparison.Ordinal)
				|| _lastLayoutWidth != AvailableWidth(host))
			{
				ApplyText(text, config);
				_lastText = text;
				_lastAppearance = appearance;
			}
			if (!_dragging && NativeMethods.GetWindowRect(host, out NativeMethods.RECT rect))
			{
				_positioning = true;
				Point desired = config.PositionMode == "custom" ? new Point(rect.Left + config.OffsetX, rect.Top + config.OffsetY) : new Point(rect.Left + config.OffsetX, rect.Bottom - Height - config.OffsetY);
				try { Location = new Point(Math.Max(rect.Left, Math.Min(desired.X, rect.Right - Width)), Math.Max(rect.Top, Math.Min(desired.Y, rect.Bottom - Height))); }
				finally { _positioning = false; }
			}
			if (!Visible) Show();
		}
		finally { _refreshing = false; }
	}

	private void ClearDisplay()
	{
		if (_editDialog != null && !_editDialog.IsDisposed)
		{
			_editDialog.DialogResult = DialogResult.Cancel;
			_editDialog.Close();
		}
		Hide();
		_displayedDocument = null;
		_displayedConfiguration = "";
		_lastText = "";
		_lastAppearance = "";
		_lastLayoutWidth = 0;
		_visibleFields.Clear();
		_caption.Text = "";
		_content.Text = "";
		_details.SetToolTip(_content, "");
		_details.SetToolTip(_caption, "");
	}

	private void BeginMove(object sender, MouseEventArgs e)
	{
		if (!_getConfig().MoveEnabled || e.Button != MouseButtons.Left) return;
		_dragging = true;
		NativeMethods.ReleaseCapture();
		try { NativeMethods.SendMessage(Handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero); }
		finally { _dragging = false; }
	}

	private static string AppearanceKey(PropertyWatermarkConfig config)
	{
		return config.FontName + "|" + config.FontSize + "|" + config.TextColor + "|" + config.OpacityPercent + "|" + config.DisplayScalePercent;
	}

	protected override void OnLocationChanged(EventArgs e)
	{
		base.OnLocationChanged(e);
		// Persist once when the drag ends, not on every mouse movement.
	}

	private void SaveMovedPosition()
	{
		if (!_getConfig().MoveEnabled || _positioning || !NativeMethods.GetWindowRect(_hostHandle(), out NativeMethods.RECT rect)) return;
		PropertyWatermarkConfig config = _getConfig();
		config.PositionMode = "custom"; config.OffsetX = Left - rect.Left; config.OffsetY = Top - rect.Top;
		try { _saveConfig(); } catch (Exception ex) { Logger.Error("Save property watermark position failed", ex); }
	}

	private void AttachToHost(IntPtr host)
	{
		if (_attachedHost == host || !IsHandleCreated) return;
		NativeMethods.SetWindowLongPtr(Handle, -8, host); // GWL_HWNDPARENT: make this a SOLIDWORKS-owned floating window.
		_attachedHost = host;
	}

	private void ApplyText(string text, PropertyWatermarkConfig config)
	{
		float scale = config.DisplayScalePercent / 100f;
		string[] parts = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
		_caption.Text = parts.Length > 0 ? parts[0] : "当前模型";
		_content.Text = parts.Length > 1 ? string.Join(System.Environment.NewLine, parts, 1, parts.Length - 1) : "";
		{
			ReplaceOwnedFont(_content, new Font(config.FontName, config.FontSize * scale, FontStyle.Regular));
			ReplaceOwnedFont(_caption, new Font(config.FontName, (config.FontSize + 1f) * scale, FontStyle.Bold));
			Color color = ColorTranslator.FromHtml(config.TextColor);
			_caption.ForeColor = color;
			_content.ForeColor = Color.FromArgb(Math.Max(0, color.R - 15), Math.Max(0, color.G - 15), Math.Max(0, color.B - 15));
		}
		int insetX = Math.Max(6, (int)(10 * config.DisplayScalePercent / 100f));
		int insetY = Math.Max(5, (int)(8 * config.DisplayScalePercent / 100f));
		int gap = Math.Max(4, (int)(7 * config.DisplayScalePercent / 100f));
		int availableWidth = AvailableWidth(_hostHandle());
		_lastLayoutWidth = availableWidth;
		_caption.Text = FitLine(_caption.Text, _caption.Font, availableWidth - _toggle.Width - gap);
		for (int i = 1; i < parts.Length; i++) parts[i] = FitLine(parts[i], _content.Font, availableWidth);
		_content.Text = parts.Length > 1 ? string.Join(System.Environment.NewLine, parts, 1, parts.Length - 1) : "";
		_caption.Location = new Point(insetX, insetY);
		_toggle.Text = _expanded ? "−" : "+";
		ReplaceOwnedFont(_toggle, new Font(config.FontName, Math.Max(8f, config.FontSize * scale), FontStyle.Bold));
		_toggle.Location = new Point(_caption.Right + gap, insetY - 1);
		_content.Visible = _expanded;
		_content.Location = new Point(insetX, _caption.Bottom + gap);
		int contentWidth = _expanded ? _content.Width : 0;
		int width = Math.Max(_caption.Width + gap + _toggle.Width, contentWidth) + insetX * 2;
		int height = _expanded ? _content.Bottom + insetY : Math.Max(_caption.Bottom, _toggle.Bottom) + insetY;
		Size = new Size(width, height);
		Opacity = config.OpacityPercent / 100d;
	}

	private static int AvailableWidth(IntPtr host)
	{
		int availableWidth = Math.Max(160, Screen.FromHandle(host).WorkingArea.Width / 2);
		if (NativeMethods.GetWindowRect(host, out NativeMethods.RECT rect)) availableWidth = Math.Min(availableWidth, Math.Max(160, rect.Right - rect.Left - 60));
		return availableWidth;
	}
	private static void ReplaceOwnedFont(Control control, Font replacement)
	{
		Font previous = control.Font;
		// WinForms keeps the original instance when the new font has equal values.
		// Disposing that original after assignment invalidates future layout/paint.
		if (previous.Equals(replacement)) { replacement.Dispose(); return; }
		try { control.Font = replacement; }
		catch { replacement.Dispose(); throw; }
		previous.Dispose();
	}

	internal static string FitLine(string text, Font font, int width)
	{
		if (TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPrefix).Width <= width) return text;
		int low = 0, high = text.Length;
		while (low < high)
		{
			int middle = (low + high + 1) / 2;
			if (TextRenderer.MeasureText(text.Substring(0, middle) + "…", font, Size.Empty, TextFormatFlags.NoPrefix).Width <= width) low = middle; else high = middle - 1;
		}
		return text.Substring(0, low) + "…";
	}

	private string BuildText(PropertyWatermarkConfig config, IModelDoc2 doc)
	{
		if (doc == null) return "未打开模型";
		_visibleFields.Clear();
		string title = (doc.GetTitle() ?? "当前模型").Trim();
		string result = title + (string.IsNullOrWhiteSpace(_displayedConfiguration) ? "" : " · " + _displayedConfiguration);
		var details = new List<string>();
		foreach (PropertyWatermarkField field in config.Fields)
		{
			if (!field.Enabled) continue;
			WatermarkPropertyAccess.Value property = WatermarkPropertyAccess.Read(doc, _displayedConfiguration, field.Source, field.PropertyName);
			string value = property.Text;
			value = (value ?? "").Replace("\r", " ").Replace("\n", " ");
			result += System.Environment.NewLine + field.DisplayName + "    " + (string.IsNullOrWhiteSpace(value) ? "—" : value);
			_visibleFields.Add(field);
			details.Add(field.DisplayName + "：" + value + "\n" + (field.Source == "原生属性" ? "模型计算属性（只读）" : property.Origin));
		}
		_details.SetToolTip(_content, string.Join("\n\n", details));
		_details.SetToolTip(_caption, title + " / " + _displayedConfiguration);
		return result;
	}

	private IModelDoc2 ResolveWatermarkDocument(PropertyWatermarkConfig config, out string configuration)
	{
		return WatermarkPropertyAccess.ResolveDocument(_getSolidWorks(), config?.ReadSelectedPartProperties == true, out configuration);
	}

	private void ToggleExpanded()
	{
		_expanded = !_expanded;
		ApplyText(_lastText, _getConfig());
		RefreshNow();
	}

	private async void EditProperty(object sender, MouseEventArgs e)
	{
		if (_editing || e.Button != MouseButtons.Left) return;
		string visibleConfiguration = _displayedConfiguration;
		IModelDoc2 doc = ResolveWatermarkDocument(_getConfig(), out string currentConfiguration);
		if (!Equals(doc, _displayedDocument) || visibleConfiguration != currentConfiguration) { RefreshNow(); return; }
		if (doc == null || _visibleFields.Count == 0) return;
		int lineHeight = Math.Max(1, TextRenderer.MeasureText("A", _content.Font).Height);
		int index = Math.Min(_visibleFields.Count - 1, Math.Max(0, e.Y / lineHeight));
		PropertyWatermarkField field = _visibleFields[index];
		if (string.IsNullOrWhiteSpace(field.PropertyName)) return;
		if (field.Source == "原生属性")
		{
			ToolNotice.Show(this, "原生属性由当前模型控制。请在模型中修改后，水印会自动刷新。", "原生属性", MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}
		WatermarkPropertyAccess.Value target;
		try { target = WatermarkPropertyAccess.Read(doc, _displayedConfiguration, field.Source, field.PropertyName); }
		catch (Exception ex) { Logger.Error("Read watermark property for editing failed", ex); ToolNotice.Show(this, "属性读取失败：" + ex.Message, "属性水印"); return; }
		string currentValue = target.Text;
		string configuration = currentConfiguration;
		string value;
		_editing = true;
		try { value = await InputValue(field.DisplayName + "（" + target.Origin + "）", currentValue); }
		finally { _editing = false; }
		if (IsDisposed) return;
		if (value == null || value == currentValue) { RefreshWhenIdle(); return; }
		if (!Equals(doc, ResolveWatermarkDocument(_getConfig(), out currentConfiguration))) { RefreshWhenIdle(); return; }
		try
		{
			if (configuration != currentConfiguration || doc.IsOpenedReadOnly())
			{
				ToolNotice.Show(this, "模型配置已切换或文档为只读，请重新打开属性编辑。", "属性水印");
				return;
			}
			WatermarkPropertyAccess.Write(doc, target, value);
			doc.ForceRebuild3(false);
			RefreshNow();
		}
		catch (Exception ex)
		{
			Logger.Error("Write property watermark value failed", ex);
			RefreshWhenIdle();
			ToolNotice.Show(this, "写入属性失败：" + ex.Message, "属性水印", MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
	}

	private async System.Threading.Tasks.Task<string> InputValue(string displayName, string currentValue)
	{
		string value = currentValue ?? "";
		using PluginToolWindow dialog = new PluginToolWindow { Text = "修改属性：" + displayName, ClientSize = new Size(360, 118), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, Font = Font };
		_editDialog = dialog;
		TextBox input = new TextBox { Text = value, Dock = DockStyle.Top, Margin = new Padding(14, 6, 14, 0) };
		Button ok = new Button { Text = "确认写入", DialogResult = DialogResult.OK, AutoSize = true };
		Button cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
		FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 4, 12, 4) };
		actions.Controls.Add(cancel); actions.Controls.Add(ok);
		dialog.Controls.Add(actions); dialog.Controls.Add(new Label { Text = "输入“" + displayName + "”的新值：", Dock = DockStyle.Top, Height = 30, Padding = new Padding(14, 10, 0, 0) }); dialog.Controls.Add(input); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
		try
		{
			if (await dialog.ShowModelessAsync(this) != DialogResult.OK) return null;
			return input.Text;
		}
		finally { _editDialog = null; }
	}

	private static bool IsSupportedDocument(IModelDoc2 document)
	{
		if (document == null) return false;
		int type = document.GetType();
		return type == 1 || type == 2; // SOLIDWORKS: part or assembly.
	}

	private static string ReadProperty(IModelDoc2 doc, string source, string name)
	{
		return PropertyIo.Read(doc, source, name);
	}

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern bool GetLastInputInfo(ref LastInputInfo info);

	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
	private struct LastInputInfo
	{
		public uint Size;
		public uint Tick;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing) { _timer.Dispose(); _details.Dispose(); _content.Font.Dispose(); _caption.Font.Dispose(); _toggle.Font.Dispose(); }
		base.Dispose(disposing);
	}
}
