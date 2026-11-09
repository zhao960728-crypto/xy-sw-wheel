using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

// Only the visible page owns bitmaps. Selecting a library icon stores a normal
// PNG through the caller, preserving backup and older-version support.
internal sealed class WheelIconLibraryDialog : Form
{
    private const int PageSize = 48;
    private readonly TextBox _search = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _category = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly FlowLayoutPanel _grid = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(4) };
    private readonly Label _status = new() { AutoSize = true, Margin = new Padding(8, 10, 8, 0) };
    private readonly Button _previous = new() { Text = "上一页", AutoSize = true };
    private readonly Button _next = new() { Text = "下一页", AutoSize = true };
    private int _page;
    public string SelectedIconId { get; private set; }

    public WheelIconLibraryDialog()
    {
        Text = "轮盘图标库";
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(900, 660);
        MinimumSize = new Size(540, 400);
        ShowInTaskbar = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(244, 248, 246);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);
        var filters = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, Padding = new Padding(0, 0, 0, 8) };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        filters.Controls.Add(new Label { Text = "搜索", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        filters.Controls.Add(_search, 1, 0);
        filters.Controls.Add(new Label { Text = "分类", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);
        filters.Controls.Add(_category, 3, 0);
        root.Controls.Add(filters, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        footer.Controls.Add(_previous); footer.Controls.Add(_next); footer.Controls.Add(_status); footer.Controls.Add(cancel);
        root.Controls.Add(footer, 0, 2);
        CancelButton = cancel;
        _category.Items.Add("全部分类");
        _category.Items.AddRange(WheelIconCatalog.Entries.Select(x => x.Category).Distinct().Cast<object>().ToArray());
        _category.SelectedIndex = 0;
        _search.TextChanged += (_, _) => { _page = 0; RefreshPage(); };
        _category.SelectedIndexChanged += (_, _) => { _page = 0; RefreshPage(); };
        _previous.Click += (_, _) => { _page--; RefreshPage(); };
        _next.Click += (_, _) => { _page++; RefreshPage(); };
        PerformAutoScale();
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var available = new Size(Math.Max(1, area.Width - 24), Math.Max(1, area.Height - 24));
        MinimumSize = new Size(Math.Min(MinimumSize.Width, available.Width), Math.Min(MinimumSize.Height, available.Height));
        Size = new Size(Math.Min(Width, available.Width), Math.Min(Height, available.Height));
        RefreshPage();
    }

    private void ClearPage()
    {
        foreach (var button in _grid.Controls.OfType<Button>().ToArray())
        {
            var image = button.Image;
            button.Image = null;
            button.Dispose();
            image?.Dispose();
        }
    }

    private void RefreshPage()
    {
        var query = _search.Text.Trim();
        var category = _category.SelectedIndex <= 0 ? null : _category.SelectedItem.ToString();
        var matches = WheelIconCatalog.Entries.Where(x => (category == null || x.Category == category) &&
            (query.Length == 0 || (x.Name + " " + x.Keywords + " " + x.Id).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
        var pages = Math.Max(1, (matches.Length + PageSize - 1) / PageSize);
        _page = Math.Max(0, Math.Min(_page, pages - 1));
        _grid.SuspendLayout();
        try
        {
            ClearPage();
            _grid.AutoScrollPosition = Point.Empty;
            foreach (var entry in matches.Skip(_page * PageSize).Take(PageSize))
            {
                var scale = DeviceDpi / 96f;
                var button = new Button
                {
                    Text = entry.Name, AccessibleName = entry.Category + " · " + entry.Name,
                    Size = new Size((int)(124 * scale), (int)(94 * scale)),
                    Margin = new Padding((int)(4 * scale)), BackColor = Color.White,
                    TextImageRelation = TextImageRelation.ImageAboveText,
                    Image = WheelIconCatalog.Render(entry.Id, (int)(44 * scale)),
                    FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(38, 65, 52),
                    UseVisualStyleBackColor = false
                };
                button.FlatAppearance.BorderColor = Color.FromArgb(205, 223, 213);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(234, 242, 237);
                button.FlatAppearance.MouseDownBackColor = Color.FromArgb(236, 229, 246);
                button.Click += (_, _) => { SelectedIconId = entry.Id; DialogResult = DialogResult.OK; Close(); };
                _grid.Controls.Add(button);
            }
            _previous.Enabled = _page > 0;
            _next.Enabled = _page + 1 < pages;
            _status.Text = matches.Length == 0 ? "没有匹配的图标" : $"{matches.Length} 个图标 · {_page + 1} / {pages} 页";
        }
        finally { _grid.ResumeLayout(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ClearPage();
        base.Dispose(disposing);
    }
}
