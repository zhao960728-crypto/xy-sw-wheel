using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace XY.SolidWorksPlugin;

internal sealed class CommandCaptureWindow : PluginToolWindow
{
    private readonly CommandRecordingSession _session = new CommandRecordingSession { CaptureSingle = true };
	private readonly Func<ISldWorks> _getSolidWorks;
	private readonly Func<ICommandManager> _getCommandManager;
	private readonly ComboBox _choices = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
	private readonly Button _refreshChoices = new Button { Text = "读取可选命令", AutoSize = true };
	private readonly Button _record = new Button { Text = "开始录制", AutoSize = true };
	private readonly List<KeyboardShortcutRow> _availableChoices = new List<KeyboardShortcutRow>();
    private readonly Label _status = new Label { Dock = DockStyle.Fill, Padding = new Padding(14), AutoSize = false };
    private readonly Button _accept = new Button { Text = "绑定到当前轮盘格", AutoSize = true, Enabled = false };
    private readonly TextBox _name = new TextBox { Dock = DockStyle.Fill, Enabled = false };
    private readonly Timer _timeout = new Timer { Interval = 60000 };
	private bool _recording;
    public ComboCommandConfig Captured { get; private set; }

	public CommandCaptureWindow(Func<ISldWorks> getSolidWorks = null, Func<ICommandManager> getCommandManager = null)
    {
		_getSolidWorks = getSolidWorks ?? (() => null);
		_getCommandManager = getCommandManager ?? (() => null);
        Text = "其他插件调用命令设置";
        ClientSize = new Size(560, 360);
        MinimumSize = new Size(500, 340);
        Controls.Add(_status);
        Controls.Add(new Label
        {
            Text = "先点击“开始录制”，再选择下拉命令或点击 SOLIDWORKS/插件目标功能。点击会正常执行该功能，请使用合适的测试文档。\n读取完成后核对名称和 ID，再绑定；不会自动重放命令。",
            Dock = DockStyle.Top, Height = 72, Padding = new Padding(14)
        });
		var picker = new TableLayoutPanel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(14, 4, 14, 4), ColumnCount = 3 };
		picker.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
		picker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		picker.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
		picker.Controls.Add(new Label { Text = "选择已读取命令", AutoSize = true, Margin = new Padding(0, 5, 6, 0) }, 0, 0);
		picker.Controls.Add(_choices, 1, 0);
		picker.Controls.Add(_refreshChoices, 2, 0);
		Controls.Add(picker);
		_refreshChoices.Click += (_, __) => LoadChoices();
		_record.Click += (_, __) => StartCapture();
		_choices.SelectedIndexChanged += (_, __) =>
		{
			int index = _choices.SelectedIndex;
			if (index >= 0 && index < _availableChoices.Count) ApplyChoice(_availableChoices[index]);
		};
		var recordPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(14, 4, 14, 4), WrapContents = false };
		recordPanel.Controls.Add(_record);
		recordPanel.Controls.Add(new Label { Text = "开始后才可选择功能", AutoSize = true, Margin = new Padding(8, 7, 0, 0) });
		Controls.Add(recordPanel);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(10), FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "取消", AutoSize = true };
        cancel.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };
        var retry = new Button { Text = "重新获取", AutoSize = true };
        retry.Click += (_, __) => Arm();
        _accept.Click += (_, __) =>
        {
            if (Captured == null) return;
            if (string.IsNullOrWhiteSpace(_name.Text)) { _status.Text = "请填写功能名称。命令 ID 与来源不会随名称改变。"; return; }
            Captured.Label = _name.Text.Trim();
            DialogResult = DialogResult.OK; Close();
        };
        var naming = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(14, 4, 14, 4), ColumnCount = 2 };
        naming.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        naming.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        naming.Controls.Add(new Label { Text = "自定义功能名称", AutoSize = true, Margin = new Padding(0, 5, 6, 0) }, 0, 0);
        naming.Controls.Add(_name, 1, 0);
        Controls.Add(naming);
		actions.Controls.Add(cancel); actions.Controls.Add(_accept); actions.Controls.Add(retry);
        Controls.Add(actions);
        _session.Changed += SessionChanged;
        _timeout.Tick += (_, __) =>
        {
			_timeout.Stop(); _session.Dispose(); _recording = false; _record.Enabled = true; _record.Text = "重新录制";
			_choices.Enabled = false;
            _status.Text = "未取得可识别的命令。可点击“重新获取”再试；未改动轮盘配置。";
        };
		Shown += (_, __) => { LoadChoices(); Arm(); };
    }

	private void LoadChoices()
	{
		ISldWorks solidWorks = null;
		try { solidWorks = _getSolidWorks(); } catch (Exception ex) { _status.Text = "读取 SOLIDWORKS 连接失败：" + ex.Message; return; }
		if (solidWorks == null)
		{
			_status.Text = "当前未连接 SOLIDWORKS。可以继续使用下方手动点击获取。";
			return;
		}
		try
		{
			if (!ExternalPluginCommandCatalog.TryRead(solidWorks, _getCommandManager(), out List<KeyboardShortcutRow> rows, out string message))
			{
				_status.Text = SanitizeExternalMessage(message) + " 可以点击 SOLIDWORKS/外部功能按钮手动获取。";
				return;
			}
			_availableChoices.Clear();
			_availableChoices.AddRange(ExternalPluginCommandCatalog.DistinctRows(rows.Where(row => row != null && string.Equals(row.ActionKind, ActionKinds.ExternalPlugin, StringComparison.OrdinalIgnoreCase))));
			_choices.BeginUpdate();
			try
			{
				_choices.Items.Clear();
				foreach (KeyboardShortcutRow row in _availableChoices) _choices.Items.Add(ChoiceText(row));
			}
			finally { _choices.EndUpdate(); }
			_status.Text = _availableChoices.Count == 0
				? SanitizeExternalMessage(message) + " 可以点击 SOLIDWORKS/外部功能按钮手动获取。"
				: "已读取 " + _availableChoices.Count + " 个外部功能命令；请选择目标后绑定。也可继续手动点击获取。";
		}
		catch (Exception ex)
		{
			Logger.Error("Read selectable external commands failed", ex);
			_status.Text = "读取外部功能命令失败：" + ex.Message + " 可以继续手动点击获取。";
		}
	}

	private static string ChoiceText(KeyboardShortcutRow row)
	{
		string category = string.IsNullOrWhiteSpace(row.Category) ? "外部功能" : row.Category.Trim();
		string name = (row.Command ?? "").Trim();
		string identity = "";
		if (ExternalToolbarCommandReference.TryDecode(row.Value, out int documentType, out int commandId))
			identity = " [ID " + commandId + ", 文档 " + documentType + "]";
		else if (ExternalNamedCommandReference.TryDecode(row.Value, out documentType, out _, out _))
			identity = " [完整路径，文档 " + documentType + "]";
		return category + " / " + name + identity;
	}

	private static string SanitizeExternalMessage(string message)
		=> (message ?? "").Replace("琴剑", "外部功能").Replace("嘉立创", "扩展功能").Replace("第三方插件", "外部功能").Replace("博士", "外部工具").Replace("Doctor", "外部工具").Replace("doctor", "外部工具");

	private void ApplyChoice(KeyboardShortcutRow row)
	{
		if (row == null || string.IsNullOrWhiteSpace(row.Value)) return;
		if (!_recording)
		{
			_status.Text = "请先点击“开始录制”，再选择目标功能。";
			return;
		}
		_timeout.Stop(); _session.Dispose();
		_recording = false; _record.Enabled = true; _record.Text = "重新录制";
		_choices.Enabled = false;
		while (_session.Commands.Count > 0) _session.RemoveAt(0);
		Captured = new ComboCommandConfig { Label = row.Command, Kind = ActionKinds.ExternalPlugin, Value = row.Value, RequireAdvanceKey = false };
		_name.Text = row.Command ?? ""; _name.Enabled = true; _accept.Enabled = true;
		_status.Text = ChoiceText(row) + "\n已选择；点击“绑定到当前轮盘格”保存。执行时仍会校验当前插件和命令身份。";
	}

    private void Arm()
    {
        _timeout.Stop(); _session.Dispose();
        while (_session.Commands.Count > 0) _session.RemoveAt(0);
		_recording = false; _record.Enabled = true; _record.Text = "开始录制";
		Captured = null; _accept.Enabled = false; _name.Enabled = false; _name.Text = ""; _choices.SelectedIndex = -1; _choices.Enabled = false; _session.Paused = true;
		_status.Text = "请先点击“开始录制”，再选择下拉命令或点击 SOLIDWORKS/插件功能。";
	}

	private void StartCapture()
	{
		Arm();
        try
        {
			_session.Paused = false;
            _session.Start();
			_recording = true; _record.Enabled = false; _record.Text = "录制中…";
			_choices.Enabled = true;
            _status.Text = "等待点击目标功能按钮（60 秒内）…";
            _timeout.Start();
        }
        catch (InvalidOperationException ex) { _session.Paused = true; _status.Text = ex.Message; }
    }

    private void SessionChanged()
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        BeginInvoke((Action)(() =>
        {
            if (IsDisposed || !_recording) return;
            var command = _session.Commands.FirstOrDefault();
            if (command == null)
            {
                if (!string.IsNullOrWhiteSpace(_session.LastIssue)) _status.Text = _session.LastIssue + "\n继续等待目标功能（本次未绑定）。";
                return;
            }
            _timeout.Stop(); _session.Dispose();
			_recording = false; _record.Enabled = true; _record.Text = "重新录制";
			_choices.Enabled = false;
            Captured = command.Clone();
            Captured.RequireAdvanceKey = false;
			string source;
            if (NativeCommandReference.TryDecodeId(command.Value, out int nativeId))
                source = "SOLIDWORKS 原生命令    ID：" + nativeId;
            else if (ExternalToolbarCommandReference.TryDecode(command.Value, out int documentType, out int commandId))
                source = "外部/工具栏命令    ID：" + commandId + "    文档类型：" + documentType;
			else if (ExternalNamedCommandReference.TryDecode(command.Value, out documentType, out string category, out string namedCommand))
				source = "外部插件命令    " + category + " / " + namedCommand + "    文档类型：" + documentType;
            else { Captured = null; _status.Text = "无法确认命令身份，请重新获取。"; return; }
            _status.Text = command.Label + "\n" + source + "\n已识别；执行时仍会校验当前文档与命令身份。";
            _name.Text = command.Label; _name.Enabled = true;
            _accept.Enabled = true;
        }));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _session.Changed -= SessionChanged;
            _session.Dispose(); _timeout.Dispose();
        }
        base.Dispose(disposing);
    }
}
