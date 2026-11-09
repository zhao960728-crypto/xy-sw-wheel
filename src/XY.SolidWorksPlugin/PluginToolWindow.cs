using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

// Only our ordinary tool dialogs inherit this class. Transparent wheel and
// watermark surfaces keep their user-selected fonts, colors and hit testing.
internal class PluginToolWindow : Form
{
	internal static readonly Color Canvas = Color.FromArgb(245, 248, 246);
	internal static readonly Color Ink = Color.FromArgb(34, 52, 46);
	internal static readonly Color Accent = Color.FromArgb(20, 117, 96);
	internal static readonly Color Border = Color.FromArgb(215, 226, 219);
	private static readonly ConditionalWeakTable<Control, object> Preserved = new ConditionalWeakTable<Control, object>();
	private readonly HashSet<Control> _styled = new HashSet<Control>();
	private readonly HashSet<TabControl> _tabs = new HashSet<TabControl>();
	private readonly Dictionary<Button, Tuple<Color, Color>> _buttonColors = new Dictionary<Button, Tuple<Color, Color>>();
	private readonly Font _bodyFont = new Font("Microsoft YaHei UI", 9f);
	private readonly Font _headingFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
	public PluginToolWindow()
	{
		Font = _bodyFont; BackColor = Canvas; ForeColor = Ink;
		SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
	}
	internal void ShowModeless(Form owner = null, Action<DialogResult> completed = null)
	{
		if (Visible) { Activate(); return; }
		DialogResult = DialogResult.None;
		if (completed != null) FormClosed += (_, __) =>
		{
			if (owner != null && owner.IsDisposed) return;
			try { completed(DialogResult); }
			catch (Exception ex) { Logger.Error("Tool window completion failed", ex); }
		};
		if (owner != null) Show(owner);
		else
		{
			using var process = System.Diagnostics.Process.GetCurrentProcess();
			IntPtr host = string.Equals(process.ProcessName, "SLDWORKS", StringComparison.OrdinalIgnoreCase) ? process.MainWindowHandle : IntPtr.Zero;
			if (host == IntPtr.Zero) Show(); else Show(new HostWindow(host));
		}
		Activate();
		KeepOnScreen();
	}
	internal void KeepOnScreen()
	{
		if (!TopLevel || WindowState != FormWindowState.Normal) return;
		Rectangle area = Screen.FromControl(Owner ?? this).WorkingArea;
		Rectangle bounds = FitWindow(Bounds, area);
		MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
		Bounds = bounds;
	}
	internal static Rectangle FitWindow(Rectangle bounds, Rectangle area)
	{
		int width = Math.Min(bounds.Width, area.Width), height = Math.Min(bounds.Height, area.Height);
		return new Rectangle(Math.Max(area.Left, Math.Min(bounds.Left, area.Right - width)),
			Math.Max(area.Top, Math.Min(bounds.Top, area.Bottom - height)), width, height);
	}
	protected override void OnShown(EventArgs e) { base.OnShown(e); KeepOnScreen(); }
	private sealed class HostWindow : IWin32Window
	{
		public IntPtr Handle { get; }
		public HostWindow(IntPtr handle) { Handle = handle; }
	}
	internal Task<DialogResult> ShowModelessAsync(Form owner)
	{
		TaskCompletionSource<DialogResult> completion = new TaskCompletionSource<DialogResult>();
		FormClosed += (_, __) => completion.TrySetResult(owner?.IsDisposed == true ? DialogResult.Cancel : DialogResult);
		Disposed += (_, __) => completion.TrySetResult(DialogResult.Cancel);
		ShowModeless(owner);
		return completion.Task;
	}
	private void CloseOnResult(object sender, EventArgs e)
	{
		if (Modal || IsDisposed || !IsHandleCreated) return;
		// Defer until the button's validation and save handlers have finished.
		BeginInvoke((Action)(() => { if (!IsDisposed && DialogResult != DialogResult.None) Close(); }));
	}
	internal static void PreserveAppearance(Control control) { if (control != null) Preserved.GetValue(control, _ => new object()); }
	protected override void OnLoad(EventArgs e)
	{
		SuspendLayout();
		try { BackColor = Canvas; ApplyVisualStyle(this); }
		finally { ResumeLayout(true); }
		base.OnLoad(e);
	}
	internal void ApplyVisualStyle(Control control)
	{
		if (control == null || control.IsDisposed || Preserved.TryGetValue(control, out _) || !_styled.Add(control)) return;
		control.ControlAdded += ChildAdded;
		control.ControlRemoved += ChildRemoved;
		control.Disposed += ChildDisposed;
		if (control is Button button) StyleButton(button);
		else if (control is DataGridView grid) StyleGrid(grid);
		else if (control is TextBoxBase text)
		{
			text.BorderStyle = BorderStyle.FixedSingle; text.BackColor = text.ReadOnly ? Color.FromArgb(249, 251, 250) : Color.White; text.ForeColor = Ink;
		}
		else if (control is ComboBox combo) { combo.FlatStyle = FlatStyle.Flat; combo.BackColor = Color.White; combo.ForeColor = Ink; }
		else if (control is NumericUpDown number) { number.BorderStyle = BorderStyle.FixedSingle; number.BackColor = Color.White; number.ForeColor = Ink; }
		else if (control is ListView list) { list.BorderStyle = BorderStyle.None; list.BackColor = Color.White; list.ForeColor = Ink; }
		else if (control is ListBox box) { box.BorderStyle = BorderStyle.FixedSingle; box.BackColor = Color.White; box.ForeColor = Ink; }
		else if (control is TreeView tree) { tree.BorderStyle = BorderStyle.None; tree.BackColor = Color.White; tree.ForeColor = Ink; tree.LineColor = Border; }
		else if (control is TabControl tabs && tabs.DrawMode == TabDrawMode.Normal)
		{
			tabs.DrawMode = TabDrawMode.OwnerDrawFixed; tabs.ItemSize = new Size(tabs.ItemSize.Width, Math.Max(tabs.ItemSize.Height, ScalePx(32)));
			tabs.Padding = new Point(14, 5); tabs.DrawItem += DrawTab; _tabs.Add(tabs);
		}
		else if (control is Panel || control is GroupBox || control is TabPage)
		{
			if (control.BackColor.GetBrightness() > 0.8f && control.BackColor.GetSaturation() < 0.2f)
				control.BackColor = control is TabPage || control.BackColor.ToArgb() == Color.White.ToArgb() ? Color.White : Canvas;
		}
		// Explicit warning, success, swatch and preview colors carry meaning.
		if (control is Label && control.ForeColor.ToArgb() == SystemColors.ControlText.ToArgb()) control.ForeColor = Ink;
		foreach (Control child in control.Controls) ApplyVisualStyle(child);
	}
	private int ScalePx(int pixels) => (int)Math.Round(pixels * DeviceDpi / 96d);
	private void ChildAdded(object sender, ControlEventArgs e) => ApplyVisualStyle(e.Control);
	private void ChildRemoved(object sender, ControlEventArgs e) => Detach(e.Control);
	private void ChildDisposed(object sender, EventArgs e) { if (sender is Control control) Detach(control); }
	private void Detach(Control control)
	{
		if (!_styled.Remove(control)) return;
		control.ControlAdded -= ChildAdded; control.ControlRemoved -= ChildRemoved; control.Disposed -= ChildDisposed;
		if (control is Button button)
		{
			button.Click -= CloseOnResult;
			button.EnabledChanged -= ButtonEnabledChanged;
			if (!button.IsDisposed && _buttonColors.TryGetValue(button, out var colors)) { button.BackColor = colors.Item1; button.ForeColor = colors.Item2; }
			_buttonColors.Remove(button);
		}
		if (control is TabControl tabs && _tabs.Remove(tabs)) { tabs.DrawItem -= DrawTab; if (!tabs.IsDisposed) tabs.DrawMode = TabDrawMode.Normal; }
		foreach (Control child in control.Controls) Detach(child);
	}
	private void ButtonEnabledChanged(object sender, EventArgs e) => UpdateButtonState((Button)sender);
	private void UpdateButtonState(Button button)
	{
		if (!_buttonColors.TryGetValue(button, out var colors)) return;
		button.BackColor = button.Enabled ? colors.Item1 : Color.FromArgb(238, 243, 240);
		button.ForeColor = button.Enabled ? colors.Item2 : Color.FromArgb(116, 131, 123);
		button.FlatAppearance.BorderColor = button.Enabled && colors.Item1 == Accent ? Accent : Border;
	}
	private void StyleButton(Button button)
	{
		button.Click += CloseOnResult;
		bool primary = ReferenceEquals(AcceptButton, button) || button.DialogResult == DialogResult.OK
			|| (button.ForeColor.ToArgb() == Color.White.ToArgb() && button.BackColor.B > button.BackColor.R + 30 && button.BackColor.G > button.BackColor.R + 20)
			|| button.Text == "保存设置";
		bool destructive = button.Text.StartsWith("删除", StringComparison.Ordinal);
		button.UseVisualStyleBackColor = false; button.FlatStyle = FlatStyle.Flat;
		button.BackColor = primary ? Accent : Color.White;
		button.ForeColor = primary ? Color.White : destructive ? Color.FromArgb(173, 47, 47) : Ink;
		button.FlatAppearance.BorderColor = primary ? Accent : Border;
		button.FlatAppearance.BorderSize = 1;
		button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(16, 94, 77) : Color.FromArgb(233, 243, 237);
		button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(12, 75, 62) : Color.FromArgb(216, 235, 225);
		_buttonColors[button] = Tuple.Create(button.BackColor, button.ForeColor);
		button.EnabledChanged += ButtonEnabledChanged; UpdateButtonState(button);
		if (button.AutoSize)
		{
			button.MinimumSize = new Size(button.MinimumSize.Width, Math.Max(button.MinimumSize.Height, ScalePx(32)));
			button.Padding = new Padding(Math.Max(button.Padding.Left, ScalePx(6)), button.Padding.Top, Math.Max(button.Padding.Right, ScalePx(6)), button.Padding.Bottom);
		}
	}
	private void StyleGrid(DataGridView grid)
	{
		grid.BackgroundColor = Color.White; grid.BorderStyle = BorderStyle.None; grid.GridColor = Border;
		grid.EnableHeadersVisualStyles = false; grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
		grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
		grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(232, 241, 236);
		grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink; grid.ColumnHeadersDefaultCellStyle.Font = _headingFont;
		grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(232, 241, 236);
		grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Ink;
		grid.ColumnHeadersHeight = Math.Max(grid.ColumnHeadersHeight, ScalePx(34));
		grid.DefaultCellStyle.BackColor = Color.White; grid.DefaultCellStyle.ForeColor = Ink;
		grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(212, 234, 224); grid.DefaultCellStyle.SelectionForeColor = Ink;
		grid.DefaultCellStyle.Padding = new Padding(ScalePx(6), ScalePx(3), ScalePx(6), ScalePx(3));
		grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 250, 248);
		grid.RowTemplate.Height = Math.Max(grid.RowTemplate.Height, ScalePx(28));
		foreach (DataGridViewRow row in grid.Rows) row.MinimumHeight = Math.Max(row.MinimumHeight, ScalePx(28));
		// Do not touch row/column/cell styles: they include hole-color swatches,
		// validation highlights, locked BOM columns and the user's font samples.
	}
	private void DrawTab(object sender, DrawItemEventArgs e)
	{
		var tabs = (TabControl)sender; bool selected = e.Index == tabs.SelectedIndex;
		using var background = new SolidBrush(selected ? Color.White : Canvas); e.Graphics.FillRectangle(background, e.Bounds);
		if (selected) { using var accent = new SolidBrush(Accent); e.Graphics.FillRectangle(accent, e.Bounds.Left + 8, e.Bounds.Bottom - ScalePx(3), e.Bounds.Width - 16, ScalePx(3)); }
		TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, selected ? _headingFont : _bodyFont, e.Bounds, selected ? Accent : Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
		if (selected && tabs.Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -4, -4));
	}
	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			foreach (Control control in _styled) { control.ControlAdded -= ChildAdded; control.ControlRemoved -= ChildRemoved; control.Disposed -= ChildDisposed; if (control is TabControl tabs) tabs.DrawItem -= DrawTab; if (control is Button button) button.EnabledChanged -= ButtonEnabledChanged; }
			_styled.Clear();
			_tabs.Clear(); _buttonColors.Clear();
		}
		base.Dispose(disposing);
		if (disposing) { _bodyFont.Dispose(); _headingFont.Dispose(); }
	}
}
