using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

// Reserve enough room for control text at the current DPI and UI font. Nothing
// here changes a wheel setting, a stored value or the form's own size: it only
// fits controls and their grid cells to the text they display.
internal sealed partial class SettingsWindow
{
	private bool _responsiveLayoutReady;
	private bool _responsiveLayoutRunning;
	private bool _responsiveLayoutQueued;
	private readonly HashSet<Control> _responsiveObserved = new HashSet<Control>();
	private readonly Dictionary<ColumnStyle, float> _responsiveColumns = new Dictionary<ColumnStyle, float>();
	private readonly Dictionary<RowStyle, float> _responsiveRows = new Dictionary<RowStyle, float>();
	private readonly Dictionary<ComboBox, string> _responsiveComboWidths = new Dictionary<ComboBox, string>();
	private readonly Dictionary<Control, Size> _responsiveMinimums = new Dictionary<Control, Size>();

	// A larger UI font and a higher DPI are separate inputs; both must be honoured.
	private int ResponsiveDpi => Math.Max(DeviceDpi, (int)Math.Ceiling(96f * Font.SizeInPoints / 9f));

	private void InitializeResponsiveSettingsLayout()
	{
		if (_responsiveLayoutReady) return;
		_responsiveLayoutReady = true;
		Layout += (_, _) => QueueResponsiveSettingsLayout();
		FontChanged += (_, _) => QueueResponsiveSettingsLayout();
		DpiChanged += (_, _) => QueueResponsiveSettingsLayout();
		Shown += (_, _) => ApplyResponsiveSettingsLayout();
		ApplyResponsiveSettingsLayout();
	}

	private void QueueResponsiveSettingsLayout()
	{
		if (!_responsiveLayoutReady || _responsiveLayoutRunning || _responsiveLayoutQueued) return;
		if (!IsHandleCreated || IsDisposed) return;
		_responsiveLayoutQueued = true;
		try
		{
			BeginInvoke(new Action(() =>
			{
				_responsiveLayoutQueued = false;
				if (!IsDisposed) ApplyResponsiveSettingsLayout();
			}));
		}
		catch (Exception ex)
		{
			Logger.Trace("Responsive layout scheduling failed: " + ex.Message);
			_responsiveLayoutQueued = false;
		}
	}

	private void ApplyResponsiveSettingsLayout()
	{
		if (!_responsiveLayoutReady || _responsiveLayoutRunning || IsDisposed) return;
		_responsiveLayoutRunning = true;
		List<Control> controls;
		try { controls = Descendants(this).ToList(); }
		catch { _responsiveLayoutRunning = false; return; }

		Control[] containers = controls.Where(c => c.HasChildren).Prepend((Control)this).ToArray();
		foreach (Control container in containers)
		{
			try { container.SuspendLayout(); }
			catch { /* A control disposed mid-walk must not stop the pass. */ }
		}
		try
		{
			var live = new HashSet<Control>(controls);
			_responsiveObserved.RemoveWhere(c => c.IsDisposed || !live.Contains(c));
			foreach (Control old in _responsiveMinimums.Keys.Where(c => !live.Contains(c)).ToArray()) _responsiveMinimums.Remove(old);
			foreach (ComboBox old in _responsiveComboWidths.Keys.Where(c => !live.Contains(c)).ToArray()) _responsiveComboWidths.Remove(old);
			int unit = ScaleLogicalForDpi(4);
			foreach (Control control in controls)
			{
				if (control.IsDisposed) continue;
				if (_responsiveObserved.Add(control))
				{
					control.FontChanged += (_, _) => QueueResponsiveSettingsLayout();
					control.TextChanged += (_, _) => QueueResponsiveSettingsLayout();
					if (control is TabPage || control is FlowLayoutPanel)
						control.SizeChanged += (_, _) => QueueResponsiveSettingsLayout();
					if (control is TabControl tabs) tabs.SelectedIndexChanged += (_, _) => QueueResponsiveSettingsLayout();
					if (control is ComboBox observedCombo) observedCombo.DropDown += (_, _) =>
					{
						_responsiveComboWidths.Remove(observedCombo);
						ApplyResponsiveSettingsLayout();
					};
				}
				switch (control)
				{
					case Button button: FitButton(button, unit); break;
					case ComboBox combo: FitComboBox(combo, unit); break;
					case NumericUpDown number: FitNumeric(number, unit); break;
					case CheckBox check when check.AutoSize: FitCheckOrLabel(check, check.Text, unit * 5); break;
					case Label label when label.AutoSize: FitAutoSizeLabel(label, unit); break;
				}
			}
			FitGridColumns(controls);
		}
		finally
		{
			// Commit leaves first: per-control metric changes would otherwise
			// trigger a complete layout for every control touched.
			for (int i = containers.Length - 1; i >= 0; i--)
			{
				try { containers[i].ResumeLayout(true); }
				catch { /* ignore */ }
			}
			_responsiveLayoutRunning = false;
		}
	}

	private void FitButton(Button button, int unit)
	{
		Size text = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.SingleLine);
		int height = Math.Max(ScaleLogicalForDpi(30), text.Height + button.Padding.Vertical + unit * 2);
		var minimum = new Size(text.Width + button.Padding.Horizontal + unit * 2, height);
		// Buttons inside a flow row keep their designed width when the caption
		// already fits, so toolbars do not grow on their own.
		SetResponsiveMinimum(button, minimum);
	}

	private void FitComboBox(ComboBox combo, int unit)
	{
		string key = combo.Font.Name + "|" + combo.Font.SizeInPoints + "|" + combo.Font.Style + "|" + combo.Items.Count + "|" + combo.Width + "|" + combo.Text + "|" + ResponsiveDpi;
		if (!_responsiveComboWidths.TryGetValue(combo, out string cached) || cached != key)
		{
			_responsiveComboWidths[combo] = key;
			int longest = 0;
			foreach (object item in combo.Items)
			{
				int width = TextRenderer.MeasureText(combo.GetItemText(item), combo.Font).Width;
				if (width > longest) longest = width;
			}
			int current = TextRenderer.MeasureText(combo.Text, combo.Font).Width;
			combo.DropDownWidth = Math.Max(combo.Width, Math.Min(ScaleLogicalForDpi(720), Math.Max(longest, current) + unit * 7));
		}
	}

	private void FitNumeric(NumericUpDown number, int unit)
	{
		string format = (number.ThousandsSeparator ? "N" : "F") + number.DecimalPlaces;
		int width = new[] { number.Minimum, number.Maximum, number.Value }
			.Select(value => TextRenderer.MeasureText(number.Hexadecimal ? ((long)value).ToString("X") : value.ToString(format), number.Font).Width).Max() + unit * 8;
		if (number.MaximumSize.Width > 0 && number.MaximumSize.Width < width) number.MaximumSize = new Size(width, number.MaximumSize.Height);
		SetResponsiveMinimum(number, new Size(width, number.PreferredHeight));
	}

	private void FitCheckOrLabel(Control control, string text, int extra)
	{
		Size measured = TextRenderer.MeasureText(text, control.Font);
		var minimum = new Size(measured.Width + extra, measured.Height + ScaleLogicalForDpi(4));
		SetResponsiveMinimum(control, minimum);
	}

	private void SetResponsiveMinimum(Control control, Size measured)
	{
		if (!_responsiveMinimums.TryGetValue(control, out Size original))
			_responsiveMinimums[control] = original = control.MinimumSize;
		var minimum = new Size(Math.Max(original.Width, measured.Width), Math.Max(original.Height, measured.Height));
		if (control.MinimumSize != minimum) control.MinimumSize = minimum;
	}

	private void FitAutoSizeLabel(Label label, int unit)
	{
		int available = label.Parent == null ? label.Width : label.Parent.ClientSize.Width - label.Parent.Padding.Horizontal - label.Margin.Horizontal;
		if (label.Parent is TableLayoutPanel table && table.GetColumnSpan(label) == 1)
		{
			int column = table.GetColumn(label);
			int[] widths = table.GetColumnWidths();
			if (column >= 0 && column < widths.Length) available = Math.Max(1, widths[column] - label.Margin.Horizontal);
		}
		if (available > 0) label.MaximumSize = new Size(Math.Max(available, unit * 4), 0);
	}

	private void FitGridColumns(IEnumerable<Control> controls)
	{
		float baseline = Math.Max(96f, DeviceDpi) / 96f;
		foreach (TableLayoutPanel table in controls.OfType<TableLayoutPanel>().Reverse())
		{
			if (table.IsDisposed) continue;
			for (int column = 0; column < table.ColumnStyles.Count; column++)
			{
				ColumnStyle style = table.ColumnStyles[column];
				if (style.SizeType != SizeType.Absolute || style.Width <= 0) continue;
				if (!_responsiveColumns.TryGetValue(style, out float logical)) { logical = style.Width / baseline; _responsiveColumns[style] = logical; }
				int need = (int)Math.Ceiling(logical * ResponsiveDpi / 96f);
				foreach (Control child in table.Controls)
				{
					if (child.IsDisposed || table.GetColumn(child) != column || table.GetColumnSpan(child) != 1) continue;
					need = Math.Max(need, child.MinimumSize.Width + child.Margin.Horizontal);
					if (child is Label || child is CheckBox)
						need = Math.Max(need, TextRenderer.MeasureText(child.Text, child.Font).Width + child.Padding.Horizontal + child.Margin.Horizontal + ScaleLogicalForDpi(child is CheckBox ? 20 : 4));
				}
				if ((int)style.Width != need) style.Width = need;
			}
			for (int row = 0; row < table.RowStyles.Count; row++)
			{
				RowStyle style = table.RowStyles[row];
				if (style.SizeType != SizeType.Absolute || style.Height <= 0) continue;
				if (!_responsiveRows.TryGetValue(style, out float logical)) { logical = style.Height / baseline; _responsiveRows[style] = logical; }
				int need = table.Controls.Cast<Control>()
					.Where(c => !c.IsDisposed && table.GetRow(c) == row && table.GetRowSpan(c) == 1)
					.Select(c => Math.Max(c.MinimumSize.Height, c is Panel && !c.AutoSize ? 0 : c.GetPreferredSize(new Size(Math.Max(1, c.Width), 0)).Height) + c.Margin.Vertical)
					.DefaultIfEmpty(0).Max();
				need = Math.Max(need, (int)Math.Ceiling(logical * ResponsiveDpi / 96f));
				if ((int)style.Height != need) style.Height = need;
			}
		}
	}

	private int ScaleLogicalForDpi(int value) => (int)Math.Ceiling(value * ResponsiveDpi / 96f);

	private static IEnumerable<Control> Descendants(Control root)
	{
		foreach (Control child in root.Controls)
		{
			yield return child;
			foreach (Control nested in Descendants(child)) yield return nested;
		}
	}
}
