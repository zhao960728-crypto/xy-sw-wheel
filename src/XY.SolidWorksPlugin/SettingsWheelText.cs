using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;
internal sealed partial class SettingsWindow
{
    private void EditWheelTextRules()
    {
        var selected = FindSelectedCommandRow();
        if (!IsOccupiedCommandRow(selected)) { ToolNotice.Show(this, "请先选择已设置命令的格子。", DisplayName(), MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var state = ReadComboRowState(selected.Tag);
        using var dialog = new Form { Text = "文字设置与应用范围", Font = Font, AutoScaleMode = AutoScaleMode.Dpi,
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(490, 410), ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false };
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(18), AutoScroll = true };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var scope = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        scope.Items.AddRange(new object[] { "当前格", "表格中所选格", "当前场景全部已配置格" }); scope.SelectedIndex = 0;
        var face = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill, Text = Convert.ToString(selected.Cells["FontName"].Value) };
        using (var fonts = new System.Drawing.Text.InstalledFontCollection()) face.Items.AddRange(fonts.Families.Select(f => f.Name).Cast<object>().ToArray());
        var size = new NumericUpDown { Minimum = 0, Maximum = 72, Dock = DockStyle.Fill, Value = Math.Min(72, ParseFontSize(Convert.ToString(selected.Cells["FontSize"].Value))) };
        var bold = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        bold.Items.AddRange(new object[] { "继承默认", "常规", "粗体" }); bold.SelectedIndex = !state.CellBold.HasValue ? 0 : state.CellBold.Value ? 2 : 1;
        var effect = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        effect.Items.Add("继承默认"); effect.Items.AddRange(TextEffects.All.Select(x => (object)x.Value).ToArray());
        effect.SelectedIndex = string.IsNullOrEmpty(state.CellEffect) ? 0 : Math.Max(0, TextEffects.All.ToList().FindIndex(x => x.Key == state.CellEffect) + 1);
        var color = new Button { Text = "选择颜色", AutoSize = true, BackColor = ParseColorOrDefault(Convert.ToString(selected.Cells["TextColor"].Value), Color.FromArgb(30, 41, 53)) };
        string colorText = Convert.ToString(selected.Cells["TextColor"].Value) ?? "";
        color.Click += (_, _) => { using var picker = new ColorDialog { FullOpen = true, Color = color.BackColor }; if (picker.ShowDialog(dialog) == DialogResult.OK) { color.BackColor = picker.Color; colorText = ColorTranslator.ToHtml(picker.Color); } };
        var colorRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var inheritColor = new Button { Text = "继承默认", AutoSize = true };
        inheritColor.Click += (_, _) => { colorText = ""; color.BackColor = ParseColorOrDefault(_config.TextColor, Color.FromArgb(30, 41, 53)); };
        colorRow.Controls.Add(color); colorRow.Controls.Add(inheritColor);
        body.Controls.Add(new Label { Text = "应用范围", AutoSize = true }, 0, 0); body.Controls.Add(scope, 1, 0);
        string[] names = { "字体（空为继承）", "字号（0 为自动适配）", "字重", "文字效果", "文字颜色" };
        Control[] inputs = { face, size, bold, effect, colorRow };
        var enabled = new CheckBox[inputs.Length];
        for (int i = 0; i < inputs.Length; i++) {
            enabled[i] = new CheckBox { Text = names[i], AutoSize = true, Checked = true, Margin = new Padding(3, 10, 12, 10) };
            var input = inputs[i]; enabled[i].CheckedChanged += (sender, _) => input.Enabled = ((CheckBox)sender).Checked;
            body.Controls.Add(enabled[i], 0, i + 1); body.Controls.Add(input, 1, i + 1);
        }
        var hint = new Label { AutoSize = true, MaximumSize = new Size(440, 0), Text = "只应用勾选的项目。单格设置优先于默认；自动字号按可用空间适配，长文字可悬停查看。" };
        body.Controls.Add(hint, 0, 6); body.SetColumnSpan(hint, 2);
        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = "应用", AutoSize = true, DialogResult = DialogResult.OK };
        footer.Controls.Add(ok); footer.Controls.Add(new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel });
        body.Controls.Add(footer, 0, 7); body.SetColumnSpan(footer, 2); dialog.Controls.Add(body); dialog.AcceptButton = ok;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var targets = scope.SelectedIndex == 2 ? OccupiedCommandRows().ToArray() : scope.SelectedIndex == 1 ? SelectedCommandRows().Where(IsOccupiedCommandRow).ToArray() : new[] { selected };
        _loadingScene = true;
        try { foreach (var row in targets) {
            var item = ReadComboRowState(row.Tag);
            if (enabled[0].Checked) row.Cells["FontName"].Value = face.Text.Trim();
            if (enabled[1].Checked) row.Cells["FontSize"].Value = size.Value == 0 ? "" : size.Value.ToString();
            if (enabled[2].Checked) item.CellBold = bold.SelectedIndex == 0 ? (bool?)null : bold.SelectedIndex == 2;
            if (enabled[3].Checked) item.CellEffect = effect.SelectedIndex == 0 ? "" : TextEffects.All[effect.SelectedIndex - 1].Key;
            if (enabled[4].Checked) row.Cells["TextColor"].Value = colorText;
            row.Tag = item;
        } } finally { _loadingScene = false; }
        LoadSelectedCellFontAppearance(); RefreshPreview();
    }
}
