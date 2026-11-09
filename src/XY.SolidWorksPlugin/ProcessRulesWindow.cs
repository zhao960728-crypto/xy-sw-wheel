using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Web.Script.Serialization;
namespace XY.SolidWorksPlugin;

internal sealed class ProcessRulesWindow : PluginToolWindow
{
    private readonly DataGridView _categories = Grid("类别名称", "工序类型属性值 / 已有工序组名");
    private readonly DataGridView _combinations = Grid("工序组名称", "顺序工序（用 > 分隔）");
    private readonly DataGridView _typeGroups = Grid("零件类型（如方通、角钢）", "默认工序组名称");
    private readonly DataGridView _mappings = Grid("自动识别的工序名称", "实际显示名称");
    private readonly DataGridView _catalog = Grid("工序名称", "来源 / 判断依据");
    private readonly DataGridView _rules = Grid("规则名称", "条件与动作（双击编辑）");
    private readonly TextBox _available = new TextBox { Dock = DockStyle.Bottom, Height = 70, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly Label _status = new Label { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(6) };
    private readonly TabControl _tabs = new TabControl { Dock = DockStyle.Fill, Multiline = true };
    public ProcessRecognitionConfig Result { get; private set; }
    public static ProcessRecognitionConfig Copy(ProcessRecognitionConfig config)
    {
        var serializer = new JavaScriptSerializer();
        var copy = serializer.Deserialize<ProcessRecognitionConfig>(serializer.Serialize(config ?? new ProcessRecognitionConfig()));
        copy.CustomRules ??= new List<ProcessRecognitionRule>(); return copy;
    }
    public static void CopyRules(ProcessRecognitionConfig from, ProcessRecognitionConfig to)
    {
        to.ProcessCategories = from.ProcessCategories; to.ProcessCombinations = from.ProcessCombinations;
        to.ProcessNameMappings = from.ProcessNameMappings; to.ProcessTypeGroups = from.ProcessTypeGroups;
        to.AvailableProcessNames = from.AvailableProcessNames; to.CustomRules = Copy(from).CustomRules; to.PartTypeChains = from.PartTypeChains;
    }
    public ProcessRulesWindow(ProcessRecognitionConfig config)
    {
        Result = Copy(config); Text = "工序目录、工序组与判断规则"; Size = new Size(900, 660); MinimumSize = new Size(680, 460);
        AddTab("工序目录", _catalog, "查看现有工序及来源。下面可加入仅供选择的自定义工序名（每行一个）；加入目录不会自动追加到所有零件。工序组决定顺序，判断规则决定何时使用。", _available);
        AddTab("工序组", _combinations, "一行是一套完整工序，使用 > 分隔。例如：折弯后焊接 = 激光下料>去毛刺>折弯>焊接>打磨>检验。应用工序组替换草稿，确认后才写入模型。");
        AddTab("类型默认组", _typeGroups, "将零件类型关联到“工序组”页中的组名。类型属性优先，再参考型材描述、名称；冲突不套用。组名修改或删除后，请同步修正本页关联。");
        AddTab("类别", _categories, "类别是工序类型属性的快捷选项；值可填工序类型（如焊接），或已有工序组名。类别、零件类型和单道工序是不同用途。");
        AddTab("工序名称", _mappings, "本页只重命名自动识别工序，不改变判断条件。人工工序和工序组保持原填写名称。完整可选名称与依据见“工序目录”。");
        AddTab("判断规则", _rules, "按表中顺序执行，单条规则的所有条件同时满足才生效。双击编辑；“追加工序”补入建议，“套用工序组”按完整组顺序生成建议。保存后下一次识别立即采用，已有工序和手工草稿保留。");
        var logic = new TabPage("分析逻辑");
        logic.Controls.Add(new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = ProcessAnalysisWindow.WorkflowText });
        _tabs.TabPages.Add(logic);
        _catalog.ReadOnly = _rules.ReadOnly = true; _catalog.AllowUserToAddRows = _rules.AllowUserToAddRows = false;
        _catalog.DefaultCellStyle.WrapMode = _rules.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _catalog.AutoSizeRowsMode = _rules.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _catalog.AllowUserToDeleteRows = _rules.AllowUserToDeleteRows = false;
        _available.Text = string.Join(Environment.NewLine, (config.AvailableProcessNames ?? "").Split(new[] { '|', '\r', '\n', ';', '；', ',', '，' }, StringSplitOptions.RemoveEmptyEntries));
        Fill(_typeGroups, ProcessRecognitionConfig.ParseProcessNameMappings(config.ProcessTypeGroups));
        Fill(_categories, ProcessRecognitionConfig.ParseProcessNameMappings(config.ProcessCategories));
        Fill(_combinations, ProcessRecognitionConfig.ParseCombinations(config.ProcessCombinations));
        Fill(_mappings, ProcessRecognitionConfig.ParseProcessNameMappings(config.ProcessNameMappings));
        RefreshCatalog(); RefreshRules();
        _rules.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditRule(false); };
        _tabs.SelectedIndexChanged += (_, __) => { if (_tabs.SelectedIndex == 0) RefreshCatalog(); };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(3), FlowDirection = FlowDirection.RightToLeft, WrapContents = true };
        ActionButton(actions, "保存规则", Save); ActionButton(actions, "取消", () => { DialogResult = DialogResult.Cancel; Close(); });
        ActionButton(actions, "删除选中行", Remove); ActionButton(actions, "添加一行", Add);
        ActionButton(actions, "编辑规则", () => EditRule(false));
        ActionButton(actions, "上移规则", () => MoveRule(-1)); ActionButton(actions, "下移规则", () => MoveRule(1));
        ActionButton(actions, "补充常用预设", AddPresets);
        Controls.Add(_tabs); Controls.Add(_status); Controls.Add(actions);
    }
    private static void ActionButton(Control parent, string text, Action action)
    { var button = new Button { Text = text, AutoSize = true }; button.Click += (_, __) => action(); parent.Controls.Add(button); }
    private static DataGridView Grid(string first, string second)
    {
        var grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, EditMode = DataGridViewEditMode.EditOnEnter, AutoGenerateColumns = false };
        grid.Columns.Add("Name", first); grid.Columns.Add("Value", second); grid.Columns[0].FillWeight = 36; grid.Columns[1].FillWeight = 64;
        return grid;
    }
    private void AddTab(string name, DataGridView grid, string hint, Control bottom = null)
    {
        var tab = new TabPage(name); tab.Controls.Add(grid); if (bottom != null) tab.Controls.Add(bottom);
        var label = new Label { Text = hint, Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        tab.SizeChanged += (_, __) => label.MaximumSize = new Size(Math.Max(100, tab.ClientSize.Width), 0);
        tab.Controls.Add(label); _tabs.TabPages.Add(tab);
    }
    private static void Fill(DataGridView grid, IEnumerable<KeyValuePair<string, string>> pairs) { foreach (var pair in pairs) grid.Rows.Add(pair.Key, pair.Value); }
    private DataGridView CurrentGrid() => _tabs.SelectedIndex >= 1 && _tabs.SelectedIndex <= 4 ? new[] { _combinations, _typeGroups, _categories, _mappings }[_tabs.SelectedIndex - 1] : null;
    private void Add()
    {
        if (_tabs.SelectedIndex == 5) { EditRule(true); return; }
        var grid = CurrentGrid(); if (grid == null) { _status.Text = "在目录下方输入自定义工序；判断规则页可添加条件规则。"; return; }
        int index = grid.Rows.Add(); grid.CurrentCell = grid.Rows[index].Cells[0]; grid.BeginEdit(true);
    }
    private void Remove()
    {
        if (FocusOpenEditor()) return;
        var grid = CurrentGrid();
        if (_tabs.SelectedIndex == 5)
        {
            foreach (DataGridViewRow row in _rules.SelectedRows) Result.CustomRules.Remove((ProcessRecognitionRule)row.Tag);
            RefreshRules(); return;
        }
        if (grid != null) foreach (DataGridViewRow row in grid.SelectedRows) if (!row.IsNewRow) grid.Rows.Remove(row);
    }
    private void RefreshCatalog()
    {
        ProcessRecognitionConfig draft;
        try { draft = ReadDraft(); }
        catch (Exception ex) { draft = Copy(Result); draft.AvailableProcessNames = _available.Text; _status.Text = "目录暂显示上次完整内容：" + ex.Message; }
        _catalog.Rows.Clear();
        foreach (var entry in ProcessPresets.Catalog(draft)) _catalog.Rows.Add(entry.Name, entry.Source + "；" + entry.Logic);
    }
    private void RefreshRules()
    {
        _rules.Rows.Clear();
        foreach (var rule in Result.CustomRules) { int index = _rules.Rows.Add(rule == null ? "无效空规则（请删除）" : (rule.Enabled ? "启用：" : "停用：") + rule.Name, ProcessRuleEngine.Describe(rule)); _rules.Rows[index].Tag = rule; }
    }
    private void MoveRule(int delta)
    {
        if (FocusOpenEditor()) return;
        var rule = _rules.CurrentRow?.Tag as ProcessRecognitionRule;
        if (_tabs.SelectedIndex != 5 || rule == null) { _status.Text = "请先在判断规则页选择要调整执行顺序的规则。"; return; }
        int source = Result.CustomRules.IndexOf(rule), target = source + delta;
        if (source < 0 || target < 0 || target >= Result.CustomRules.Count) return;
        Result.CustomRules.RemoveAt(source); Result.CustomRules.Insert(target, rule); RefreshRules();
        _rules.CurrentCell = _rules.Rows[target].Cells[0];
    }
    private void EditRule(bool add)
    {
        if (FocusOpenEditor()) return;
        ProcessRecognitionRule selected = add ? new ProcessRecognitionRule { Name = "新规则" } : _rules.CurrentRow?.Tag as ProcessRecognitionRule;
        if (selected == null) { _status.Text = "请先在判断规则页选择一条规则，或点击添加一行。"; return; }
        try
        {
            var draft = ReadDraft();
            var editor = new ProcessRuleEditorWindow(selected, draft);
            editor.ShowModeless(this, result => { if (result != DialogResult.OK) return; if (add) Result.CustomRules.Add(editor.Result); else Result.CustomRules[Result.CustomRules.IndexOf(selected)] = editor.Result; RefreshRules(); });
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private ProcessRecognitionConfig ReadDraft()
    {
        var draft = Copy(Result); draft.ProcessCategories = Read(_categories, false); draft.ProcessCombinations = Read(_combinations, true);
        draft.ProcessNameMappings = Read(_mappings, false); draft.ProcessTypeGroups = Read(_typeGroups, false);
        var names = _available.Text.Split(new[] { '\r', '\n', '|', ';', '；', ',', '，' }, StringSplitOptions.RemoveEmptyEntries).Select(name => name.Trim()).Where(name => name.Length > 0).ToArray();
        if (names.Length > 200 || names.Any(name => name.Length > 64 || name.IndexOfAny(new[] { '>', '→', '=' }) >= 0)) throw new InvalidOperationException("自定义工序最多 200 个，每个最多 64 字，不含 =、> 或 →。");
        draft.AvailableProcessNames = string.Join("|", names.Distinct(StringComparer.CurrentCultureIgnoreCase));
        return draft;
    }
    private void AddPresets()
    {
        if (FocusOpenEditor()) return;
        try
        {
            var draft = ReadDraft(); ProcessPresets.AddMissing(draft); Result = draft;
            _combinations.Rows.Clear(); Fill(_combinations, ProcessRecognitionConfig.ParseCombinations(draft.ProcessCombinations));
            _typeGroups.Rows.Clear(); Fill(_typeGroups, ProcessRecognitionConfig.ParseProcessNameMappings(draft.ProcessTypeGroups));
            _categories.Rows.Clear(); Fill(_categories, ProcessRecognitionConfig.ParseProcessNameMappings(draft.ProcessCategories));
            _mappings.Rows.Clear(); Fill(_mappings, ProcessRecognitionConfig.ParseProcessNameMappings(draft.ProcessNameMappings));
            RefreshCatalog(); _status.Text = "已补充缺少的常用预设，原内容保留；点击保存规则后生效。";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private static string Read(DataGridView grid, bool combination)
    {
        if (!grid.EndEdit()) throw new InvalidOperationException("请先完成单元格编辑。");
        var rows = grid.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow).Select(row => new[] { Convert.ToString(row.Cells[0].Value).Trim(), Convert.ToString(row.Cells[1].Value).Trim() }).Where(row => row[0].Length + row[1].Length > 0).ToArray();
        if (rows.Length > 50) throw new InvalidOperationException("每页最多 50 行。");
        if (rows.Any(row => row[0].Length == 0 || row[1].Length == 0 || row[0].Length > 64 || row[0].IndexOfAny(new[] { '=', '|', ';', '；', '\r', '\n' }) >= 0 || row[1].IndexOfAny(new[] { '|', ';', '；', '\r', '\n' }) >= 0)) throw new InvalidOperationException("每行两列都需填写；名称最多 64 字，不含 =、|、分号或换行。");
        if (rows.GroupBy(row => row[0], StringComparer.CurrentCultureIgnoreCase).Any(group => group.Count() > 1)) throw new InvalidOperationException("同一页的名称不能重复，请修改或删除重复行。");
        if (rows.Any(row => combination ? row[1].Split(new[] { '>', '→' }).Any(step => string.IsNullOrWhiteSpace(step) || step.Trim().Length > 64) || row[1].Split(new[] { '>', '→' }).Length > 30 : row[1].Length > 64)) throw new InvalidOperationException("每道工序/属性值最多 64 字，工序组最多 30 道工序，不留空步骤。");
        return string.Join("|", rows.Select(row => row[0] + "=" + row[1]));
    }
    private void Save()
    {
        if (FocusOpenEditor()) return;
        try
        {
            var draft = ReadDraft(); var groups = ProcessRecognitionConfig.ParseCombinations(draft.ProcessCombinations); var typeRows = ProcessRecognitionConfig.ParseProcessNameMappings(draft.ProcessTypeGroups);
            if (typeRows.Any(pair => !groups.Any(group => string.Equals(group.Key, pair.Value, StringComparison.CurrentCultureIgnoreCase)))) throw new InvalidOperationException("类型默认组必须引用当前工序组名称；组名修改或删除后请同步修正关联。");
            if (typeRows.GroupBy(pair => ProcessTypeGroupPolicy.CanonicalType(pair.Key), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1)) throw new InvalidOperationException("同一种类型只能关联一个默认组；方通与方管、角钢与角铁分别视为同类。");
            var oldGroups = ProcessRecognitionConfig.ParseCombinations(Result.ProcessCombinations);
            if (ProcessRecognitionConfig.ParseProcessNameMappings(draft.ProcessCategories).Any(pair => oldGroups.Any(group => string.Equals(group.Key, pair.Value, StringComparison.CurrentCultureIgnoreCase)) && !groups.Any(group => string.Equals(group.Key, pair.Value, StringComparison.CurrentCultureIgnoreCase))))
                throw new InvalidOperationException("类别仍引用已改名或删除的工序组；请在类别页同步修正关联。");
            var errors = ProcessRuleEngine.Validate(draft); if (errors.Count > 0) throw new InvalidOperationException(string.Join("；", errors));
            Result = draft; DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private bool FocusOpenEditor()
    {
        var editor = OwnedForms.OfType<ProcessRuleEditorWindow>().FirstOrDefault();
        if (editor == null) return false;
        editor.Activate(); _status.Text = "请先确定或取消正在编辑的判断规则，再保存或修改其他规则。"; return true;
    }
}
