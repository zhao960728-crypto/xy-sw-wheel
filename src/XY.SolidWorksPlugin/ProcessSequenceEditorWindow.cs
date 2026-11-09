using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

internal sealed class ProcessSequenceEditorWindow : PluginToolWindow
{
    private readonly DataGridView _grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = Color.White };
    private readonly string _separator;
    private readonly PictureBox _previewImage = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
    private readonly Label _previewStatus = new Label { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(8), TextAlign = ContentAlignment.MiddleCenter, Text = "暂无模型预览。" };
    private readonly Func<ProcessModelPreview> _loadPreview;
    private ProcessModelPreview _preview;
    internal string Sequence { get; private set; }

    internal ProcessSequenceEditorWindow(string partName, string sequence, string separator)
        : this(partName, sequence, separator, null, false, null, "应用到草稿") { }

    internal ProcessSequenceEditorWindow(string partName, string sequence, string separator, IEnumerable<string> candidates = null, bool useDropdown = false, Func<ProcessModelPreview> preview = null, string confirmText = "应用到草稿")
    {
        Text = "编辑零件工序 — " + partName;
        Width = 860; Height = 560; MinimumSize = new Size(700, 420);
        _loadPreview = preview;
        _separator = string.IsNullOrEmpty(separator) ? "-" : separator;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Order", HeaderText = "序号", Width = 60, ReadOnly = true });
        string[] suggestions = (candidates ?? Enumerable.Empty<string>()).Concat(Split(sequence, separator)).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();
        _grid.Columns.Add(new DataGridViewColumn(useDropdown ? (DataGridViewCell)new ProcessNameComboCell { Candidates = suggestions } : new DataGridViewTextBoxCell())
        { Name = "Process", HeaderText = useDropdown ? "工序（可下拉选择或输入）" : "工序（双击编辑）", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        ProcessSequenceGrid.Attach(_grid, Renumber);
        foreach (string name in Split(sequence, _separator)) _grid.Rows.Add(_grid.Rows.Count + 1, name);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8), WrapContents = true };
        AddButton(footer, "选中后插入工序", () =>
        {
            int index = ProcessSequenceGrid.InsertAfterSelection(_grid, 0, "");
            if (index >= 0) { Renumber(); _grid.BeginEdit(true); }
        });
        AddButton(footer, "删除选中", () =>
        {
            if (_grid.CurrentRow == null || !_grid.EndEdit()) return;
            _grid.Rows.Remove(_grid.CurrentRow); Renumber();
        });
        AddButton(footer, confirmText, () =>
        {
            if (!_grid.EndEdit()) return;
            string[] names = _grid.Rows.Cast<DataGridViewRow>().Select(row => Convert.ToString(row.Cells["Process"].Value).Trim()).ToArray();
            if (names.Any(name => name.Length == 0 || name.Contains(_separator) || name.Contains(">") || name.Contains("→")))
            { ToolNotice.Show(this, "请每行填写一道工序，名称不能留空或包含工序分隔符。", Text); return; }
            if (names.Distinct(StringComparer.CurrentCultureIgnoreCase).Count() != names.Length)
            { ToolNotice.Show(this, "工序名称有重复，请区分不同步骤的名称后再应用。", Text); return; }
            Sequence = string.Join(_separator, names);
            DialogResult = DialogResult.OK; Close();
        });
        AddButton(footer, "取消", () => { DialogResult = DialogResult.Cancel; Close(); });
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(10, 0, 10, 0) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(_grid, 0, 0);
        var previewPanel = new GroupBox { Text = "模型预览", Dock = DockStyle.Fill, Padding = new Padding(8), Margin = new Padding(10, 0, 0, 0) };
        previewPanel.Controls.Add(_previewImage); previewPanel.Controls.Add(_previewStatus);
        body.Controls.Add(previewPanel, 1, 0);
        Controls.Add(body);
        Controls.Add(new Label { Dock = DockStyle.Top, Height = 58, Padding = new Padding(10), Text = "双击逐道编辑工序；按住工序行拖动换序。" + (useDropdown ? "可从下拉列表选择，也可输入自定义工序。" : "") + "\r\n" + (confirmText == "应用到草稿" ? "返回后确认写入模型；取消不改变工序。" : "保存时仅提交当前零件；取消不改变工序。") });
        Controls.Add(footer);
        Shown += (_, __) => LoadPreview();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (!_grid.IsDisposed) _grid.CancelEdit();
            _previewImage.Image = null;
            _preview?.Dispose(); _preview = null;
        }
        base.Dispose(disposing);
    }

    private void LoadPreview()
    {
        if (_loadPreview == null) { _previewStatus.Text = "未提供模型预览；工序仍可编辑。"; return; }
        try
        {
            _previewImage.Image = null;
            _preview?.Dispose();
            _preview = _loadPreview();
            _previewImage.Image = _preview?.Image;
            _previewStatus.Text = _preview?.Description ?? "暂时无法显示模型预览。";
        }
        catch (Exception ex)
        {
            Logger.Trace("Load process editor preview failed: " + ex.Message);
            _previewStatus.Text = "模型预览读取失败；工序仍可编辑。";
        }
    }

    internal static string[] Split(string sequence, string separator) => (sequence ?? "").Split(new[] { string.IsNullOrEmpty(separator) ? "-" : separator, ">", "→" }, StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()).Where(value => value.Length > 0).ToArray();
    private void Renumber() { for (int i = 0; i < _grid.Rows.Count; i++) _grid.Rows[i].Cells["Order"].Value = i + 1; }
    private static void AddButton(FlowLayoutPanel panel, string text, Action action)
    { var button = new Button { Text = text, AutoSize = true, Height = 32 }; button.Click += (_, __) => action(); panel.Controls.Add(button); }
}
