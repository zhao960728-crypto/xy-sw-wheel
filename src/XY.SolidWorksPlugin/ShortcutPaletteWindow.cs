using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

internal sealed class ShortcutPaletteWindow : PluginToolWindow
{
	private const int MaximumRows = 9;
	private readonly Action<KeyboardShortcutRow> _execute;
	private readonly TextBox _input = new TextBox { Dock = DockStyle.Fill, Height = 30, ReadOnly = false, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Microsoft YaHei", 10f) };
	private readonly CheckBox _shortcutMode = new CheckBox { Appearance = Appearance.Button, Text = "按键筛选 (F2)", Dock = DockStyle.Right, Width = 108, TextAlign = ContentAlignment.MiddleCenter };
	private readonly ListView _list = new ListView { Activation = ItemActivation.OneClick, Dock = DockStyle.Fill, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, HideSelection = false, View = View.Details };
	private readonly Label _status = new Label { Dock = DockStyle.Bottom, Height = 26, ForeColor = Color.FromArgb(97, 113, 140), Padding = new Padding(6, 4, 0, 0) };
	private readonly ToolTip _tip = new ToolTip();
	private List<KeyboardShortcutRow> _rows = new List<KeyboardShortcutRow>();
	private List<string> _tokens = new List<string>();
	private bool _explicitSelection;
	private int _matchCount;

	public ShortcutPaletteWindow(Action<KeyboardShortcutRow> execute)
	{
		_execute = execute;
		FormBorderStyle = FormBorderStyle.None;
		ShowInTaskbar = false;
		TopMost = true;
		KeyPreview = true;
		Opacity = 0.96;
		Size = new Size(650, 58);
		BackColor = Canvas;
		_input.BackColor = Color.White;
		_input.ForeColor = Ink;
		_input.AccessibleName = "快捷键候选功能输入";
		_input.AccessibleDescription = "输入功能名并回车执行；也可以使用 Ctrl、Shift、Alt 组合键筛选";
		_input.KeyDown += delegate(object _, KeyEventArgs e)
		{
			// Plain letters/numbers belong to the function-name editor. Modifier
			// combinations and navigation keys retain the original shortcut mode.
			if (IsNameEditingKey(e.KeyData)) return;
			e.Handled = HandleKey(e.KeyData); e.SuppressKeyPress = e.Handled;
		};
		_input.TextChanged += delegate
		{
			if (_tokens.Count == 0) RefreshCandidates();
		};
		_tip.SetToolTip(_input, "输入功能名；唯一结果按 Enter 执行，同名结果请先选择来源");
		_shortcutMode.CheckedChanged += (_, _) =>
		{
			_tokens.Clear(); _input.ReadOnly = _shortcutMode.Checked; _input.Text = ""; RefreshCandidates(); _input.Focus();
		};
		_list.BackColor = Color.White;
		_list.ForeColor = Ink;
		_list.Font = new Font("Microsoft YaHei", 9f);
		_list.Columns.Add("快捷键", 112);
		_list.Columns.Add("功能", 210);
		_list.Columns.Add("来源", 315);
		_list.ShowItemToolTips = true;
		_list.ItemActivate += delegate { ExecuteSelected(explicitChoice: true); };
		_list.KeyDown += delegate(object _, KeyEventArgs e) { e.Handled = HandleKey(e.KeyData); e.SuppressKeyPress = e.Handled; };
		Controls.Add(_list);
		Controls.Add(_status);
		var inputRow = new Panel { Dock = DockStyle.Top, Height = 32 };
		inputRow.Controls.Add(_input); inputRow.Controls.Add(_shortcutMode);
		Controls.Add(inputRow);
	}

	public void ShowPalette(IEnumerable<KeyboardShortcutRow> rows, Point anchor)
	{
		_rows = (rows ?? Array.Empty<KeyboardShortcutRow>()).Where(ShortcutPaletteMatcher.IsNameCandidate).ToList();
		_tokens.Clear();
		_shortcutMode.Checked = false;
		_input.Text = "";
		RefreshCandidates();
		Location = Clamp(new Point(anchor.X + 18, anchor.Y + 14));
		if (!Visible) Show();
		Activate();
		_input.Focus();
	}

	protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
	{
		if ((keyData & Keys.KeyCode) == Keys.F2) { _shortcutMode.Checked = !_shortcutMode.Checked; return true; }
		// ProcessCmdKey precedes TextBox.KeyDown. Let the textbox and IME own
		// ordinary text here, otherwise the first Latin key becomes a shortcut.
		if (_input.ContainsFocus && IsNameEditingKey(keyData))
		{
			if (_tokens.Count > 0 && (keyData & Keys.KeyCode) != Keys.Back)
			{ _tokens.Clear(); _input.Text = ""; RefreshCandidates(); }
			return base.ProcessCmdKey(ref msg, keyData);
		}
		return HandleKey(keyData) || base.ProcessCmdKey(ref msg, keyData);
	}

	protected override void OnDeactivate(EventArgs e)
	{
		base.OnDeactivate(e);
		if (IsDisposed || !IsHandleCreated) return;
		BeginInvoke((Action)delegate
		{
			if (Visible && !ContainsFocus)
			{
				Hide();
			}
		});
	}

	private bool HandleKey(Keys keyData)
	{
		Keys key = keyData & Keys.KeyCode;
		if (key == Keys.Escape) { Hide(); return true; }
		if (key == Keys.Up || key == Keys.Down) { MoveSelection(key == Keys.Down ? 1 : -1); return true; }
		if (key == Keys.Enter) { ExecuteSelected(explicitChoice: false); return true; }
		if (!_shortcutMode.Checked) return false;
		if (key == Keys.Back)
		{
			if (_tokens.Count == 0) return false;
			_tokens.Clear(); _input.Text = ""; RefreshCandidates(); return true;
		}
		IReadOnlyList<string> tokens = ShortcutPaletteMatcher.TokensFromKeyData(keyData);
		if (tokens.Count == 0) return false;
		_tokens = tokens.ToList();
		_input.Text = "";
		RefreshCandidates();
		return true;
	}

	internal bool IsNameEditingKey(Keys keyData)
	{
		Keys key = keyData & Keys.KeyCode;
		return !_shortcutMode.Checked && key != Keys.Enter && key != Keys.Escape && key != Keys.Up && key != Keys.Down;
	}

	private void RefreshCandidates()
	{
		IReadOnlyList<KeyboardShortcutRow> matches = _tokens.Count > 0
			? ShortcutPaletteMatcher.Match(_rows, _tokens)
			: ShortcutPaletteMatcher.MatchByName(_rows, _input.Text);
		_matchCount = matches.Count;
		_explicitSelection = false;
		_list.BeginUpdate();
		try
		{
			_list.Items.Clear();
			if (_tokens.Count > 0)
			{
				_input.Text = string.Join("+", _tokens);
			}
			else if (_input.Text.Length == 0)
			{
				_input.Text = "";
			}
			foreach (KeyboardShortcutRow row in matches.Take(MaximumRows))
			{
				ListViewItem item = new ListViewItem(string.IsNullOrWhiteSpace(row.Hotkey) ? "直接执行" : row.Hotkey) { Tag = row };
				item.SubItems.Add(row.Command);
				item.SubItems.Add(ShortcutPaletteMatcher.SourceLabel(row));
				item.ToolTipText = row.Command + "\r\n" + ShortcutPaletteMatcher.SourceLabel(row);
				_list.Items.Add(item);
			}
			if (matches.Count == 1) _list.Items[0].Selected = true;
		}
		finally { _list.EndUpdate(); }
		_status.Text = matches.Count == 0 ? (_input.Text.Length == 0 ? (_shortcutMode.Checked ? "请按组合键筛选候选 · F2 返回功能名输入" : "输入功能名 · F2 切换按键筛选") : "没有匹配的功能")
			: matches.Count == 1 ? "1 项功能 · Enter 执行" : matches.Count + " 项功能 · 请选择来源，再按 Enter 或单击执行";
		if (matches.Count > MaximumRows) _status.Text += " · 继续输入可缩小范围";
		_list.Visible = _list.Items.Count > 0;
		Height = 32 + _status.Height + 2 + (_list.Items.Count == 0 ? 0 : 24 + _list.Items.Count * 26);
		if (Visible) Location = Clamp(Location);
	}

	private void MoveSelection(int offset)
	{
		if (_list.Items.Count == 0) return;
		int current = _list.SelectedIndices.Count == 0 ? (offset > 0 ? -1 : _list.Items.Count) : _list.SelectedIndices[0];
		int next = Math.Max(0, Math.Min(_list.Items.Count - 1, current + offset));
		if (current >= 0 && current < _list.Items.Count) _list.Items[current].Selected = false;
		_list.Items[next].Selected = true;
		_list.Items[next].Focused = true;
		_list.EnsureVisible(next);
		_explicitSelection = true;
	}

	private void ExecuteSelected(bool explicitChoice)
	{
		if (_matchCount > 1 && !explicitChoice && !_explicitSelection)
		{ _status.Text = "匹配到多个功能，请用 ↑↓ 或单击选择所需来源。"; return; }
		if (_list.SelectedItems.Count == 0 || !(_list.SelectedItems[0].Tag is KeyboardShortcutRow row)) return;
		if (!ShortcutPaletteMatcher.TryGetAction(row, out string kind, out _)) { _status.Text = "功能身份已失效，请重新读取命令列表。"; return; }
		if (kind == ActionKinds.Hotkey && ShortcutChangeDetector.HasConflict(_rows, row.Hotkey, out _))
		{ _status.Text = "该快捷键对应多个命令，请先在 SOLIDWORKS 中消除快捷键冲突。"; return; }
		Hide();
		_execute(row);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing) _tip.Dispose();
		base.Dispose(disposing);
	}

	private Point Clamp(Point location)
	{
		Rectangle area = Screen.FromPoint(location).WorkingArea;
		return new Point(Math.Max(area.Left, Math.Min(location.X, area.Right - Width)), Math.Max(area.Top, Math.Min(location.Y, area.Bottom - Height)));
	}
}
