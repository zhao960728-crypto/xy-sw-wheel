using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;
namespace XY.SolidWorksPlugin;

internal sealed class ProcessRuleEditorWindow : PluginToolWindow
{
    private readonly Dictionary<string, TextBox> _fields = new Dictionary<string, TextBox>();
    private readonly CheckBox _enabled = new CheckBox { Text = "启用此规则", AutoSize = true };
    private readonly ComboBox _action = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _weld = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Label _status = new Label { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(6) };
    private readonly ProcessRecognitionConfig _config;
    public ProcessRecognitionRule Result { get; private set; }
    public ProcessRuleEditorWindow(ProcessRecognitionRule rule, ProcessRecognitionConfig config)
    {
        var serializer = new JavaScriptSerializer(); Result = serializer.Deserialize<ProcessRecognitionRule>(serializer.Serialize(rule));
        _config = ProcessRulesWindow.Copy(config); Text = "工序判断规则"; Size = new Size(650, 690); MinimumSize = new Size(480, 430);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var form = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(10) };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160)); form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string label, Control control) { int index = form.RowCount++; form.RowStyles.Add(new RowStyle(SizeType.AutoSize)); form.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 7, 3, 7) }, 0, index); form.Controls.Add(control, 1, index); }
        void Field(string key, string label, string value) { var box = new TextBox { Dock = DockStyle.Fill, Text = value ?? "" }; _fields[key] = box; Row(label, box); }
        Field("Name", "规则名称", Result.Name); _enabled.Checked = Result.Enabled; Row("状态", _enabled);
        _action.Items.AddRange(new object[] { "追加工序", "套用工序组" }); _action.SelectedIndex = Result.Action == "group" ? 1 : 0; Row("满足后执行", _action);
        Field("ProcessNames", "追加顺序（用 > 分隔）", Result.ProcessNames); Field("BeforeProcess", "插入到工序之前（可选）", Result.BeforeProcess); Field("GroupName", "使用工序组名", Result.GroupName);
        Row("填写说明", new Label { Text = "条件全部同时满足；空白表示不限。数字为空表示不限，0 表示零。插入位置留空追加到链末，填写后须在当前链中存在（例如压铆插在折弯前）。工序组须已建立。折弯/直管数量及无焊接证据条件需完整深度事实。", AutoSize = true, MaximumSize = new Size(380, 0) });
        Field("PartType", "零件类型（精确匹配）", Result.PartType); Field("NameContains", "零件名称包含", Result.NameContains);
        Field("MaterialContains", "材料包含", Result.MaterialContains); Field("FeatureContains", "特征名 / 类型包含", Result.FeatureContains);
        Field("MinBodyCount", "实体数至少", Number(Result.MinBodyCount)); Field("MaxBodyCount", "实体数至多", Number(Result.MaxBodyCount));
        Field("MinBendCount", "折弯数至少", Number(Result.MinBendCount)); Field("MaxBendCount", "折弯数至多", Number(Result.MaxBendCount));
        Field("MinTubeCount", "直管实体数至少", Number(Result.MinTubeCount)); Field("MaxTubeCount", "直管实体数至多", Number(Result.MaxTubeCount));
        _weld.Items.AddRange(new object[] { "不限", "有明确焊接证据", "没有明确焊接证据" });
        _weld.SelectedIndex = !Result.HasWeldEvidence.HasValue ? 0 : Result.HasWeldEvidence.Value ? 1 : 2; Row("焊接证据", _weld);
        _action.SelectedIndexChanged += (_, __) => UpdateAction(); UpdateAction();
        scroll.Controls.Add(form); Controls.Add(scroll); Controls.Add(_status);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = "确定规则", AutoSize = true }; save.Click += (_, __) => Save(); actions.Controls.Add(save);
        var cancel = new Button { Text = "取消", AutoSize = true }; cancel.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); }; actions.Controls.Add(cancel);
        Controls.Add(actions);
    }
    private static string Number(int? value) => value.HasValue ? value.Value.ToString() : "";
    private void UpdateAction() { _fields["ProcessNames"].Enabled = _fields["BeforeProcess"].Enabled = _action.SelectedIndex == 0; _fields["GroupName"].Enabled = _action.SelectedIndex == 1; }
    private string TextValue(string key) => _fields[key].Text.Trim();
    private int? Count(string key)
    {
        string text = TextValue(key); if (text.Length == 0) return null;
        if (!int.TryParse(text, out int count) || count < 0) throw new InvalidOperationException("数量条件须为非负整数，或留空表示不限。");
        return count;
    }
    private void Save()
    {
        try
        {
            var rule = new ProcessRecognitionRule { Name = TextValue("Name"), Enabled = _enabled.Checked, Action = _action.SelectedIndex == 1 ? "group" : "append",
                ProcessNames = _action.SelectedIndex == 0 ? TextValue("ProcessNames") : "", BeforeProcess = _action.SelectedIndex == 0 ? TextValue("BeforeProcess") : "", GroupName = _action.SelectedIndex == 1 ? TextValue("GroupName") : "",
                PartType = TextValue("PartType"), NameContains = TextValue("NameContains"), MaterialContains = TextValue("MaterialContains"), FeatureContains = TextValue("FeatureContains"),
                MinBodyCount = Count("MinBodyCount"), MaxBodyCount = Count("MaxBodyCount"), MinBendCount = Count("MinBendCount"), MaxBendCount = Count("MaxBendCount"),
                MinTubeCount = Count("MinTubeCount"), MaxTubeCount = Count("MaxTubeCount"), HasWeldEvidence = _weld.SelectedIndex == 0 ? (bool?)null : _weld.SelectedIndex == 1 };
            _config.CustomRules = new List<ProcessRecognitionRule> { rule };
            var errors = ProcessRuleEngine.Validate(_config); if (errors.Count > 0) throw new InvalidOperationException(string.Join("；", errors));
            Result = rule; DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
}
