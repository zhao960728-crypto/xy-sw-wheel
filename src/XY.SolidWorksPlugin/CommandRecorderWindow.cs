using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

// Records the commands a user performs in SOLIDWORKS and turns them into an
// ordered step sequence. Any wheel cell can hold the result, so a single
// function and a combo share the same recording and the same executor.
internal sealed class CommandRecorderWindow : PluginToolWindow
{
	private readonly CommandRecordingSession _session = new CommandRecordingSession { Paused = true };
	private readonly ListView _list = new ListView
	{
		Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
		HideSelection = false, GridLines = false, ShowItemToolTips = true
	};
	private readonly Label _status = new Label { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(8) };
	private readonly CheckBox _recordGaps = new CheckBox { Text = "记录操作间隔", AutoSize = true, Checked = false };
	private readonly Button _pause = new Button { Text = "开始录制", AutoSize = true };
	private readonly Button _reRecord = new Button { Text = "重录此步", AutoSize = true };
	private readonly List<Button> _editActions = new List<Button>();
	private bool _started;
	private int _selectAfterChange = -2;

	public List<ComboCommandConfig> Recorded { get; private set; } = new List<ComboCommandConfig>();

	public CommandRecorderWindow(IEnumerable<ComboCommandConfig> initial = null)
	{
		foreach (ComboCommandConfig command in initial ?? Array.Empty<ComboCommandConfig>()) _session.Insert(_session.Commands.Count, command);
		Text = "录制步骤序列";
		ClientSize = new Size(720, 460);
		MinimumSize = new Size(560, 360);
		BuildList();

		Controls.Add(_list);
		var instructions = new Label
		{
			Text = "点“开始录制”后在 SOLIDWORKS 中依次执行功能，新命令追加到末尾。模型选择和参数仍由你操作。"
				+ "可暂停整理步骤、重录选中命令；取消重录保留原步骤。缩放、保存、撤销等不录入。",
			Dock = DockStyle.Top, Height = 56, Padding = new Padding(10)
		};
		void ResizeInstructions() => instructions.Height = instructions.GetPreferredSize(new Size(ClientSize.Width, 0)).Height;
		Controls.Add(instructions);
		ClientSizeChanged += (_, __) => ResizeInstructions();
		instructions.FontChanged += (_, __) => ResizeInstructions();
		ResizeInstructions();
		Controls.Add(_status);
		Controls.Add(BuildActions());

		_recordGaps.CheckedChanged += (_, __) => _session.RecordDelays = _recordGaps.Checked;
		_session.Changed += () =>
		{
			if (!IsDisposed && !Disposing && IsHandleCreated)
			{
				try { BeginInvoke((Action)OnSessionChanged); }
				catch (InvalidOperationException) { }
			}
		};
		Shown += (_, __) => RefreshList();
	}

	private void BuildList()
	{
		_list.Columns.Add("步骤", 50);
		_list.Columns.Add("命令", 240);
		_list.Columns.Add("类型", 90);
		_list.Columns.Add("衔接", 70);
		_list.Columns.Add("等待", 120);
		_list.Columns.Add("失败时", 90);
		_list.DoubleClick += (_, __) => EditStep();
		_list.KeyDown += (_, e) =>
		{
			if (e.KeyCode == Keys.Delete) { RemoveSelected(); e.SuppressKeyPress = true; }
			else if (e.Control && e.KeyCode == Keys.C && _list.SelectedIndex() >= 0)
			{
				ComboCommandConfig command = _session.Commands[_list.SelectedIndex()];
				try { Clipboard.SetText(command.Kind + ": " + command.Value); }
				catch (System.Runtime.InteropServices.ExternalException) { _status.Text = "剪贴板正忙，请稍后重试。"; }
				e.SuppressKeyPress = true;
			}
			else if (e.Alt && e.KeyCode == Keys.Up) { MoveSelected(-1); e.SuppressKeyPress = true; }
			else if (e.Alt && e.KeyCode == Keys.Down) { MoveSelected(1); e.SuppressKeyPress = true; }
		};
	}

	private Control BuildActions()
	{
		var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(6) };
		actions.Controls.Add(_recordGaps);
		actions.Controls.Add(_pause);
		AddEditButton(actions, "上移", () => MoveSelected(-1));
		AddEditButton(actions, "下移", () => MoveSelected(1));
		AddEditButton(actions, "插入延时", InsertDelay);
		AddEditButton(actions, "插入等待确认", InsertManualConfirm);
		AddEditButton(actions, "步骤属性", EditStep);
		actions.Controls.Add(_reRecord);
		_reRecord.Click += (_, __) => ReRecordStep();
		AddEditButton(actions, "删除选中", RemoveSelected);
		actions.Controls.Add(MakeButton("结束并导入", Finish));
		actions.Controls.Add(MakeButton("放弃录制", () => { DialogResult = DialogResult.Cancel; Close(); }));
		_pause.Click += (_, __) => TogglePause();
		return actions;
	}

	private void AddEditButton(Control actions, string text, Action click)
	{
		Button button = MakeButton(text, click);
		_editActions.Add(button);
		actions.Controls.Add(button);
	}

	private Button MakeButton(string text, Action click)
	{
		var button = new Button { Text = text, AutoSize = true };
		button.Click += (_, __) => click();
		return button;
	}

	private void TogglePause()
	{
		if (_session.IsReplacing) { _session.CancelReplace(); RefreshList(); return; }
		if (!_started)
		{
			try { _session.Start(); _started = true; }
			catch (InvalidOperationException ex) { _status.Text = ex.Message; return; }
		}
		_session.Paused = !_session.Paused;
		RefreshList();
	}

	private void MoveSelected(int delta)
	{
		if (_session.IsReplacing) return;
		int index = _list.SelectedIndex();
		int target = index + delta;
		if (index < 0 || target < 0 || target >= _session.Commands.Count) return;
		PauseForEditing();
		_selectAfterChange = target;
		_session.Move(index, delta);
	}

	private void RemoveSelected()
	{
		if (_session.IsReplacing) return;
		int index = _list.SelectedIndex();
		if (index < 0) return;
		PauseForEditing();
		_selectAfterChange = Math.Min(index, _session.Commands.Count - 2);
		_session.RemoveAt(index);
	}

	private void InsertDelay()
	{
		if (_session.IsReplacing) return;
		PauseForEditing();
		using var dialog = new StepValueDialog("插入延时", "等待毫秒数：", "800");
		if (dialog.ShowDialog(this) != DialogResult.OK) return;
		if (!int.TryParse(dialog.ValueText, out int delayMs) || delayMs < 0)
		{
			_status.Text = "延时请输入 0 至 600000 的整数毫秒数。";
			return;
		}
		delayMs = Math.Min(600000, delayMs);
		int target = InsertIndex();
		_selectAfterChange = target;
		_session.Insert(target, new ComboCommandConfig
		{
			Label = "延时 " + delayMs + " 毫秒",
			Kind = ActionKinds.Delay,
			Value = delayMs.ToString(),
			WaitMode = ComboWaitModes.Delay,
			DelayMs = delayMs
		});
	}

	private void InsertManualConfirm()
	{
		if (_session.IsReplacing) return;
		PauseForEditing();
		int target = InsertIndex();
		_selectAfterChange = target;
		_session.Insert(target, new ComboCommandConfig
		{
			Label = "等待手动确认",
			Kind = ActionKinds.Delay,
			Value = "0",
			WaitMode = ComboWaitModes.ManualConfirm
		});
	}

	private void ReRecordStep()
	{
		if (_session.IsReplacing) { _session.CancelReplace(); RefreshList(); return; }
		int index = _list.SelectedIndex();
		if (index < 0) { _status.Text = "请先选择要重录的步骤。"; return; }
		if (!_started)
		{
			try { _session.Start(); _started = true; }
			catch (InvalidOperationException ex) { _status.Text = ex.Message; return; }
		}
		_session.BeginReplace(index);
		RefreshList();
	}

	private int InsertIndex() => _list.SelectedIndex() < 0 ? _session.Commands.Count : _list.SelectedIndex() + 1;

	private void PauseForEditing()
	{
		_session.Paused = true;
		RefreshList();
	}

	private void EditStep()
	{
		if (_session.IsReplacing) return;
		int index = _list.SelectedIndex();
		if (index < 0) { _status.Text = "请先选择要设置的步骤。"; return; }
		PauseForEditing();
		ComboCommandConfig command = _session.Commands[index].Clone();
		if (!EditStepProperties(this, command)) return;
		_session.Replace(index, command);
	}

	internal static bool EditStepProperties(IWin32Window owner, ComboCommandConfig command)
	{
		using var dialog = new StepPropertiesDialog(command);
		return dialog.ShowDialog(owner) == DialogResult.OK;
	}

	private void Finish()
	{
		if (_session.IsReplacing)
		{
			_status.Text = "正在重录此步，请先点击目标命令或“取消重录”。原步骤仍保留。";
			return;
		}
		if (_session.Commands.Count == 0)
		{
			_status.Text = "尚未录到可识别的命令。";
			return;
		}
		Recorded = _session.Commands.Select(command => command.Clone()).ToList();
		DialogResult = DialogResult.OK;
		Close();
	}

	private void OnSessionChanged()
	{
		if (IsDisposed || Disposing) return;
		RefreshList();
	}

	private void RefreshList()
	{
		int selected = _selectAfterChange >= -1 ? _selectAfterChange : _list.SelectedIndex();
		_selectAfterChange = -2;
		_list.BeginUpdate();
		try
		{
			_list.Items.Clear();
			for (int i = 0; i < _session.Commands.Count; i++)
			{
				ComboCommandConfig command = _session.Commands[i];
				_list.Items.Add(new ListViewItem(new[]
				{
					(i + 1).ToString(),
					string.IsNullOrWhiteSpace(command.Label) ? command.Value : command.Label,
					StepKindText(command.Kind),
					command.RequireAdvanceKey ? (string.IsNullOrWhiteSpace(command.AdvanceKey) ? "跟随" : command.AdvanceKey) : "自动",
					WaitText(command),
					ErrorText(command)
				}) { ToolTipText = command.Kind + ": " + command.Value + "\nCtrl+C 复制命令标识" });
			}
		}
		finally
		{
			_list.EndUpdate();
		}
		if (selected >= 0 && selected < _list.Items.Count)
		{
			_list.Items[selected].Selected = true;
			_list.Items[selected].Focused = true;
			_list.Items[selected].EnsureVisible();
		}
		_pause.Text = !_started ? "开始录制" : _session.Paused ? "继续录制" : "暂停录制";
		_reRecord.Text = _session.IsReplacing ? "取消重录" : "重录此步";
		foreach (Button button in _editActions) button.Enabled = !_session.IsReplacing;
		_status.Text = (_session.IsReplacing ? "重录第 " + (_session.ReplacingIndex + 1) + " 步 · 请点击一次目标命令"
			: !_started ? "未开始 · 请点“开始录制”" : _session.Paused ? "已暂停" : "正在录制")
			+ " · " + _session.Commands.Count + " 步"
			+ " · 未识别 " + _session.Skipped
			+ " · 已过滤 " + _session.Filtered
			+ " · 重复通知 " + _session.Merged
			+ (_session.LastIssue.Length == 0 ? "" : " · " + _session.LastIssue);
	}

	internal static string StepKindText(string kind) => kind switch
	{
		ActionKinds.NativeCommand => "原生命令",
		ActionKinds.Hotkey => "快捷键",
		ActionKinds.Basic => "基础动作",
		ActionKinds.UiClick => "界面点击",
		ActionKinds.QinJian => "外部功能",
		ActionKinds.ExternalPlugin => "外部功能",
		ActionKinds.JiaLiChuang => "外部功能",
		ActionKinds.Delay => "等待",
		ActionKinds.ShortcutPalette => "快捷键盘",
		_ => "命令"
	};

	private static string WaitText(ComboCommandConfig command) => command.WaitMode switch
	{
		ComboWaitModes.Delay => "延时 " + command.DelayMs + " ms",
		ComboWaitModes.WindowClosed => "等待窗口关闭",
		ComboWaitModes.PropertyPane => "等待属性面板",
		ComboWaitModes.ManualConfirm => "等待手动确认",
		_ => command.AutoConfirm ? "自动确定" : "等待手动完成"
	};

	private static string ErrorText(ComboCommandConfig command) => command.OnError switch
	{
		ComboErrorPolicies.Skip => "跳过",
		ComboErrorPolicies.Retry => "重试 " + command.RetryCount + " 次",
		_ => "停止"
	};

	protected override void Dispose(bool disposing)
	{
		if (disposing) _session.Dispose();
		base.Dispose(disposing);
	}

	private sealed class StepValueDialog : Form
	{
		private readonly TextBox _value = new TextBox { Dock = DockStyle.Fill };
		public string ValueText => _value.Text.Trim();

		public StepValueDialog(string title, string prompt, string initial)
		{
			Text = title;
			Font = new Font("Microsoft YaHei UI", 9f);
			StartPosition = FormStartPosition.CenterParent;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MinimizeBox = false;
			MaximizeBox = false;
			ClientSize = new Size(340, 130);
			var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, RowCount = 2 };
			body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			body.Controls.Add(new Label { Text = prompt, AutoSize = true }, 0, 0);
			_value.Text = initial;
			body.Controls.Add(_value, 1, 0);
			var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
			var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, AutoSize = true };
			footer.Controls.Add(ok);
			footer.Controls.Add(new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true });
			body.Controls.Add(footer, 0, 1);
			body.SetColumnSpan(footer, 2);
			Controls.Add(body);
			AcceptButton = ok;
		}
	}

	private sealed class StepPropertiesDialog : Form
	{
		private readonly ComboCommandConfig _command;
		private readonly TextBox _label = new TextBox { Dock = DockStyle.Fill };
		private readonly CheckBox _autoConfirm = new CheckBox { Text = "自动确定（不等手动完成）", AutoSize = true };
		private readonly CheckBox _requireAdvance = new CheckBox { Text = "完成后需按衔接键", AutoSize = true };
		private readonly CheckBox _saveSelection = new CheckBox { Text = "执行前保存选择", AutoSize = true };
		private readonly CheckBox _restoreSelection = new CheckBox { Text = "完成后恢复选择", AutoSize = true };
		private readonly ComboBox _advanceKey = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
		private readonly ComboBox _waitMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
		private readonly ComboBox _onError = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
		private readonly NumericUpDown _delay = new NumericUpDown { Maximum = 600000, Minimum = 0, Dock = DockStyle.Fill };
		private readonly NumericUpDown _timeout = new NumericUpDown { Maximum = 600000, Minimum = 0, Dock = DockStyle.Fill };
		private readonly NumericUpDown _retry = new NumericUpDown { Maximum = 5, Minimum = 0, Dock = DockStyle.Fill };

		public StepPropertiesDialog(ComboCommandConfig command)
		{
			_command = command;
			Text = "步骤属性";
			Font = new Font("Microsoft YaHei UI", 9f);
			StartPosition = FormStartPosition.CenterParent;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MinimizeBox = false;
			MaximizeBox = false;
			AutoScaleMode = AutoScaleMode.Dpi;
			ClientSize = new Size(500, 460);

			_advanceKey.Items.AddRange(new object[] { "跟随整体", "Ctrl", "Shift", "Alt", "Space", "Enter", "Tab", "Esc" });
			_waitMode.Items.AddRange(new object[] { "自动等待完成", "固定延时", "等待窗口关闭", "等待属性面板", "等待手动确认" });
			_onError.Items.AddRange(new object[] { "停止", "跳过继续", "重试" });

			_autoConfirm.Checked = command.AutoConfirm;
			_requireAdvance.Checked = command.RequireAdvanceKey;
			_saveSelection.Checked = command.SaveSelectionBefore;
			_restoreSelection.Checked = command.RestoreSelectionAfter;
			_advanceKey.SelectedIndex = Math.Max(0, Array.IndexOf(KeyValues(), command.AdvanceKey ?? ""));
			_waitMode.SelectedIndex = WaitIndex(command.WaitMode);
			_onError.SelectedIndex = command.OnError == ComboErrorPolicies.Skip ? 1
				: command.OnError == ComboErrorPolicies.Retry ? 2 : 0;
			int delayMs = command.Kind == ActionKinds.Delay && int.TryParse(command.Value, out int parsedDelay) ? parsedDelay : command.DelayMs;
			_delay.Value = Math.Max(_delay.Minimum, Math.Min(_delay.Maximum, delayMs));
			_timeout.Value = Math.Max(_timeout.Minimum, Math.Min(_timeout.Maximum, command.TimeoutMs));
			_retry.Value = Math.Max(_retry.Minimum, Math.Min(_retry.Maximum, command.RetryCount));
			_autoConfirm.CheckedChanged += (_, __) => { if (_autoConfirm.Checked) _requireAdvance.Checked = false; };
			_requireAdvance.CheckedChanged += (_, __) => { if (_requireAdvance.Checked) _autoConfirm.Checked = false; };

			var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, AutoSize = true };
			body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			int row = 0;
			_label.Text = command.Label ?? "";
			AddRow(body, "步骤名称", _label, ref row);
			AddRow(body, "命令标识", new TextBox
			{
				Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, Height = 46,
				ScrollBars = ScrollBars.Vertical, Text = command.Kind + ": " + command.Value
			}, ref row);
			body.Controls.Add(_autoConfirm, 0, row++); body.SetColumnSpan(_autoConfirm, 2);
			body.Controls.Add(_requireAdvance, 0, row++); body.SetColumnSpan(_requireAdvance, 2);
			body.Controls.Add(_saveSelection, 0, row++); body.SetColumnSpan(_saveSelection, 2);
			body.Controls.Add(_restoreSelection, 0, row++); body.SetColumnSpan(_restoreSelection, 2);
			AddRow(body, "衔接键", _advanceKey, ref row);
			AddRow(body, "等待方式", _waitMode, ref row);
			AddRow(body, "延时毫秒", _delay, ref row);
			AddRow(body, "超时毫秒（0 不限）", _timeout, ref row);
			AddRow(body, "失败时", _onError, ref row);
			AddRow(body, "重试次数", _retry, ref row);

			var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(14) };
			var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, AutoSize = true };
			footer.Controls.Add(ok);
			footer.Controls.Add(new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true });
			Controls.Add(body);
			Controls.Add(footer);
			AcceptButton = ok;
		}

		private static void AddRow(TableLayoutPanel body, string label, Control control, ref int row)
		{
			body.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 8, 12, 4) }, 0, row);
			body.Controls.Add(control, 1, row);
			row++;
		}

		private static string[] KeyValues() => new[] { "", "Ctrl", "Shift", "Alt", "Space", "Enter", "Tab", "Esc" };

		private static int WaitIndex(string mode) => mode switch
		{
			ComboWaitModes.Delay => 1,
			ComboWaitModes.WindowClosed => 2,
			ComboWaitModes.PropertyPane => 3,
			ComboWaitModes.ManualConfirm => 4,
			_ => 0
		};

		protected override void OnFormClosed(FormClosedEventArgs e)
		{
			base.OnFormClosed(e);
			if (DialogResult != DialogResult.OK) return;
			_command.Label = _label.Text.Trim();
			_command.AutoConfirm = _autoConfirm.Checked;
			_command.RequireAdvanceKey = _requireAdvance.Checked;
			_command.SaveSelectionBefore = _saveSelection.Checked;
			// Restoring without capturing would restore an unrelated selection.
			_command.RestoreSelectionAfter = _restoreSelection.Checked && _saveSelection.Checked;
			_command.AdvanceKey = KeyValues()[Math.Max(0, _advanceKey.SelectedIndex)];
			_command.WaitMode = WaitIndexToMode(_waitMode.SelectedIndex);
			_command.DelayMs = (int)_delay.Value;
			if (_command.Kind == ActionKinds.Delay) _command.Value = _command.DelayMs.ToString();
			_command.TimeoutMs = (int)_timeout.Value;
			_command.OnError = _onError.SelectedIndex == 1 ? ComboErrorPolicies.Skip
				: _onError.SelectedIndex == 2 ? ComboErrorPolicies.Retry : ComboErrorPolicies.Stop;
			_command.RetryCount = (int)_retry.Value;
		}

		private static string WaitIndexToMode(int index) => index switch
		{
			1 => ComboWaitModes.Delay,
			2 => ComboWaitModes.WindowClosed,
			3 => ComboWaitModes.PropertyPane,
			4 => ComboWaitModes.ManualConfirm,
			_ => ComboWaitModes.None
		};
	}
}

internal static class RecorderListExtensions
{
	public static int SelectedIndex(this ListView list)
	{
		if (list.SelectedIndices.Count == 0) return -1;
		return list.SelectedIndices[0];
	}
}
