using System;
using System.Drawing;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

internal sealed class BackupContentDialog : PluginToolWindow
{
	private readonly CheckBox _config = new CheckBox
	{
		Text = "轮盘配置",
		AutoSize = true,
		Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold)
	};

	private readonly CheckBox _shortcuts = new CheckBox
	{
		Text = "SOLIDWORKS 自定义快捷键",
		AutoSize = true,
		Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold)
	};

	public bool IncludeConfig => _config.Checked && _config.Enabled;

	public bool IncludeShortcuts => _shortcuts.Checked && _shortcuts.Enabled;

	public BackupContentDialog(bool exporting, bool configAvailable = true, bool shortcutsAvailable = true)
	{
		Text = exporting ? "选择导出内容" : "选择导入内容";
		Width = 540;
		Height = 330;
		MinimumSize = new Size(500, 310);
		StartPosition = FormStartPosition.CenterParent;
		FormBorderStyle = FormBorderStyle.FixedDialog;
		MaximizeBox = false;
		MinimizeBox = false;
		ShowInTaskbar = false;
		BackColor = Color.FromArgb(248, 250, 252);
		Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);

		_config.Enabled = configAvailable;
		_config.Checked = configAvailable;
		_shortcuts.Enabled = shortcutsAvailable;
		_shortcuts.Checked = shortcutsAvailable;

		TableLayoutPanel layout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(20, 16, 20, 14),
			ColumnCount = 1,
			RowCount = 4,
			BackColor = BackColor
		};
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78f));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78f));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		layout.Controls.Add(new Label
		{
			Text = exporting ? "请选择需要写入备份包的内容" : "请选择需要从备份包恢复的内容",
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			ForeColor = Color.FromArgb(38, 54, 70),
			Font = new Font(Font, FontStyle.Bold)
		}, 0, 0);
		layout.Controls.Add(OptionPanel(
			_config,
			exporting ? "保存所有轮盘页面、组合命令、外观设置和背景资源。" : "覆盖当前轮盘页面、组合命令、外观设置和背景资源。",
			configAvailable), 0, 1);
		layout.Controls.Add(OptionPanel(
			_shortcuts,
			exporting ? "保存当前用户在 SOLIDWORKS 中自行设置的键盘快捷键。" : "后台仅恢复键盘快捷键，不导入其他 SW 设置；导入前自动生成回滚文件。",
			shortcutsAvailable), 0, 2);

		FlowLayoutPanel actions = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.RightToLeft,
			WrapContents = false,
			Padding = new Padding(0, 12, 0, 0)
		};
		Button confirm = Button("确定", primary: true);
		confirm.Click += delegate
		{
			if (!IncludeConfig && !IncludeShortcuts)
			{
				ToolNotice.Show(this, "请至少勾选一项内容。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}
			DialogResult = DialogResult.OK;
		};
		Button cancel = Button("取消", primary: false);
		cancel.DialogResult = DialogResult.Cancel;
		actions.Controls.Add(confirm);
		actions.Controls.Add(cancel);
		layout.Controls.Add(actions, 0, 3);
		Controls.Add(layout);
		AcceptButton = confirm;
		CancelButton = cancel;
	}

	private static Control OptionPanel(CheckBox option, string description, bool available)
	{
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			Margin = new Padding(0, 4, 0, 4),
			Padding = new Padding(14, 10, 14, 8),
			BackColor = Color.White,
			BorderStyle = BorderStyle.FixedSingle
		};
		option.Dock = DockStyle.Top;
		option.Height = 28;
		option.ForeColor = available ? Color.FromArgb(38, 54, 70) : Color.FromArgb(145, 152, 160);
		Label detail = new Label
		{
			Text = available ? description : "当前备份文件不包含此内容。",
			Dock = DockStyle.Fill,
			Padding = new Padding(24, 4, 0, 0),
			ForeColor = available ? Color.FromArgb(86, 98, 110) : Color.FromArgb(145, 152, 160)
		};
		panel.Controls.Add(detail);
		panel.Controls.Add(option);
		return panel;
	}

	private static Button Button(string text, bool primary)
	{
		Button button = new Button
		{
			Text = text,
			Width = 92,
			Height = 32,
			Margin = new Padding(8, 0, 0, 0),
			FlatStyle = FlatStyle.Flat,
			BackColor = primary ? Color.FromArgb(65, 139, 217) : Color.White,
			ForeColor = primary ? Color.White : Color.FromArgb(38, 54, 70)
		};
		button.FlatAppearance.BorderColor = primary ? Color.FromArgb(65, 139, 217) : Color.FromArgb(205, 213, 221);
		return button;
	}
}
