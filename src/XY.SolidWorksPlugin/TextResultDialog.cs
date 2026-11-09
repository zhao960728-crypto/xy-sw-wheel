using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

internal sealed class TextResultDialog : PluginToolWindow
{
	public TextResultDialog(string title, string text, string filePath)
	{
		Text = title;
		Width = 760;
		Height = 560;
		MinimumSize = new Size(620, 440);
		StartPosition = FormStartPosition.CenterParent;
		BackColor = Color.FromArgb(248, 250, 252);
		Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);

		TextBox result = new TextBox
		{
			Dock = DockStyle.Fill,
			Multiline = true,
			ReadOnly = true,
			ScrollBars = ScrollBars.Both,
			WordWrap = false,
			BorderStyle = BorderStyle.FixedSingle,
			BackColor = Color.White,
			Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular),
			Text = text ?? ""
		};
		Panel content = new Panel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(16),
			BackColor = BackColor
		};
		content.Controls.Add(result);
		Controls.Add(content);

		FlowLayoutPanel actions = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 56,
			FlowDirection = FlowDirection.RightToLeft,
			WrapContents = false,
			Padding = new Padding(12, 10, 12, 10),
			BackColor = BackColor
		};
		Button close = MakeButton("关闭", primary: true);
		close.Click += delegate { Close(); };
		actions.Controls.Add(close);
		if (!string.IsNullOrWhiteSpace(filePath))
		{
			Button openFile = MakeButton("打开 TXT", primary: false);
			openFile.Click += delegate { OpenPath(filePath); };
			Button openFolder = MakeButton("打开所在目录", primary: false, width: 112);
			openFolder.Click += delegate { OpenPath(Path.GetDirectoryName(filePath)); };
			actions.Controls.Add(openFile);
			actions.Controls.Add(openFolder);
		}
		Controls.Add(actions);
		AcceptButton = close;
	}

	private static Button MakeButton(string text, bool primary, int width = 92)
	{
		Button button = new Button
		{
			Text = text,
			Width = width,
			Height = 32,
			Margin = new Padding(8, 0, 0, 0),
			FlatStyle = FlatStyle.Flat,
			BackColor = primary ? Color.FromArgb(65, 139, 217) : Color.White,
			ForeColor = primary ? Color.White : Color.FromArgb(38, 54, 70)
		};
		button.FlatAppearance.BorderColor = primary ? Color.FromArgb(65, 139, 217) : Color.FromArgb(205, 213, 221);
		return button;
	}

	private static void OpenPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) && !Directory.Exists(path))
		{
			return;
		}
		Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
	}
}
