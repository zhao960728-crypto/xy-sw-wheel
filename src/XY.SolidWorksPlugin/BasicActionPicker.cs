using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

internal sealed class BasicActionPicker : PluginToolWindow
{
	private sealed class Item
	{
		public string Value { get; }

		public string Label { get; }

		public Item(string value, string label)
		{
			Value = value;
			Label = label;
		}

		public override string ToString()
		{
			return Label;
		}
	}

	private readonly ListBox _list = new ListBox
	{
		Dock = DockStyle.Fill,
		BorderStyle = BorderStyle.FixedSingle,
		IntegralHeight = false,
		ItemHeight = 38,
		DrawMode = DrawMode.OwnerDrawFixed,
		BackColor = Color.White
	};
	private readonly TextBox _search = new TextBox { Dock = DockStyle.Fill };
	private readonly Label _count = new Label { Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleLeft };
	private readonly List<Item> _items = new List<Item>();

	public string SelectedValue { get; private set; } = "settings";

	public BasicActionPicker()
	{
		Text = "选择轮盘功能";
		base.Width = 440;
		base.Height = 500;
		MinimumSize = new Size(380, 420);
		base.StartPosition = FormStartPosition.CenterParent;
		BackColor = Color.FromArgb(248, 250, 252);
		Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);
		foreach (KeyValuePair<string, string> item in BasicActions.All)
		{
			_items.Add(new Item(item.Key, item.Value));
		}
		_list.DrawItem += DrawItem;
		_search.TextChanged += (_, _) => FilterItems();
		_search.KeyDown += (_, e) =>
		{
			if (e.KeyCode == Keys.Down && _list.Items.Count > 0) { _list.Focus(); e.Handled = true; e.SuppressKeyPress = true; }
		};
		_list.DoubleClick += delegate
		{
			Accept();
		};
		Panel content = new Panel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(14),
			BackColor = Color.FromArgb(248, 250, 252)
		};
		content.Controls.Add(_list);
		content.Controls.Add(_count);
		var filters = new TableLayoutPanel { Dock = DockStyle.Top, Height = 38, ColumnCount = 2 };
		filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		filters.Controls.Add(new Label { Text = "搜索功能", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 2, 10, 0) }, 0, 0);
		filters.Controls.Add(_search, 1, 0);
		content.Controls.Add(filters);
		base.Controls.Add(content);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 54,
			FlowDirection = FlowDirection.RightToLeft,
			Padding = new Padding(10, 8, 10, 8),
			BackColor = Color.FromArgb(248, 250, 252)
		};
		var accept = SettingsButton("确定", Accept, true);
		var cancel = SettingsButton("取消", delegate
		{
			base.DialogResult = DialogResult.Cancel;
		}, false);
		flowLayoutPanel.Controls.Add(accept);
		flowLayoutPanel.Controls.Add(cancel);
		AcceptButton = accept; CancelButton = cancel;
		base.Controls.Add(flowLayoutPanel);
		FilterItems();
		Shown += (_, _) => _search.Focus();
	}

	private void FilterItems()
	{
		string selected = (_list.SelectedItem as Item)?.Value;
		string[] words = _search.Text.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
		_list.BeginUpdate();
		try
		{
			_list.Items.Clear();
			foreach (var item in _items.Where(item => words.All(word => item.Label.IndexOf(word, StringComparison.CurrentCultureIgnoreCase) >= 0 || item.Value.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)))
				_list.Items.Add(item);
			int index = _list.Items.Cast<Item>().ToList().FindIndex(item => item.Value == selected);
			if (_list.Items.Count > 0) _list.SelectedIndex = Math.Max(0, index);
			_count.Text = _list.Items.Count == 0 ? "没有匹配的功能" : _list.Items.Count + " 项功能 · 双击或按 Enter 确认";
		}
		finally { _list.EndUpdate(); }
	}

	private void DrawItem(object sender, DrawItemEventArgs e)
	{
		if (e.Index < 0 || e.Index >= _list.Items.Count) return;
		e.DrawBackground();
		var item = (Item)_list.Items[e.Index];
		RadialCommandIcons.Draw(e.Graphics, new MenuItemConfig { Kind = ActionKinds.Basic, Value = item.Value, Label = item.Label },
			new RectangleF(e.Bounds.Left + 7, e.Bounds.Top + 5, 28, 28), SceneKeys.Blank);
		TextRenderer.DrawText(e.Graphics, item.Label, Font,
			new Rectangle(e.Bounds.Left + 44, e.Bounds.Top, Math.Max(1, e.Bounds.Width - 50), e.Bounds.Height), e.ForeColor,
			TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
		e.DrawFocusRectangle();
	}

	private void Accept()
	{
		if (_list.SelectedItem is Item item)
		{
			SelectedValue = item.Value;
			base.DialogResult = DialogResult.OK;
			Close();
		}
	}

	private static Button SettingsButton(string text, Action action, bool primary)
	{
		Button button = new Button
		{
			Text = text,
			Width = 88,
			Height = 30,
			FlatStyle = FlatStyle.Flat,
			BackColor = primary ? Color.FromArgb(65, 139, 217) : Color.White,
			ForeColor = primary ? Color.White : Color.FromArgb(35, 45, 55),
			Margin = new Padding(4)
		};
		button.FlatAppearance.BorderColor = primary ? Color.FromArgb(65, 139, 217) : Color.FromArgb(210, 216, 222);
		button.Click += delegate
		{
			action();
		};
		return button;
	}
}
