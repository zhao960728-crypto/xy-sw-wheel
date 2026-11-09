using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
namespace XY.SolidWorksPlugin;

internal sealed class ProcessAnalysisWindow : PluginToolWindow
{
    internal const string WorkflowText = "识别与编辑流程\r\n\r\n1. 快速识别读取特征和已有属性；深度识别再读取实体几何。导入的无特征方通等需深度识别。几何读取未完成时，不能把缺少折弯或焊接证据当成确定没有该工序。\r\n\r\n2. 已写工序和手工草稿保留。明确工序组按组顺序使用；类型默认组只对没有既有工序或人工选择的对象提供默认建议。类别是工序类型属性快捷选项。\r\n\r\n3. 特征、实体几何、材料和明确属性产生自动建议；工序名称映射只改显示名称。直方通截面圆角不计折弯；多个实体本身不等于已确认焊接。\r\n\r\n4. 判断规则在自动识别时按表中顺序执行，所有条件同时满足才应用。已有工序、人工草稿和明确工序组保持。仅加入工序目录不会自动追加工序。\r\n\r\n5. 多实体先逐实体判断下料、折弯和机加工路线，再检查整件的焊接或装配阶段；有折弯且有焊接证据时安排先折弯后焊接。大件拆分方式、拆分位置、焊接接头及焊后变形需要工艺确认，识别只给建议。\r\n\r\n6. 修改规则后重新识别可立即预览；已有草稿继续保留，需要替换时先取消“保留手工调整”。核对顺序和依据后，点击识别窗的确认写入按钮才修改模型属性，不自动拆分或保存模型。";
    internal string AnalysisText { get; }
    public ProcessAnalysisWindow(ProcessRecognitionConfig config, ProcessRecognitionInput input = null, bool deep = false, IEnumerable<RecognizedProcess> currentRows = null)
    {
        Text = "工序查看与分析逻辑"; Size = new Size(940, 690); MinimumSize = new Size(620, 460);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        AnalysisText = Describe(config, input, deep, currentRows);
        TextTab(tabs, "当前对象与工序", AnalysisText); TextTab(tabs, "判断顺序与限制", WorkflowText);
        var catalog = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoGenerateColumns = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        catalog.Columns.Add("Name", "现有工序名称"); catalog.Columns.Add("Source", "来源"); catalog.Columns.Add("Logic", "判断依据 / 用途");
        catalog.DefaultCellStyle.WrapMode = DataGridViewTriState.True; catalog.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        foreach (var entry in ProcessPresets.Catalog(config)) catalog.Rows.Add(entry.Name, entry.Source, entry.Logic);
        var catalogTab = new TabPage("工序目录"); catalogTab.Controls.Add(catalog); tabs.TabPages.Add(catalogTab);
        var types = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoGenerateColumns = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        types.Columns.Add("Type", "零件分类"); types.Columns.Add("Evidence", "分类依据"); types.Columns.Add("Keywords", "内置识别关键词");
        types.DefaultCellStyle.WrapMode = DataGridViewTriState.True; types.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        foreach (var preset in PartTypeCatalog.Presets) types.Rows.Add(preset.Type, preset.Evidence, string.Join("、", preset.Keywords));
        var typeTab = new TabPage("零件分类预设"); typeTab.Controls.Add(types); typeTab.Controls.Add(new Label { Text = "本表展示分类器使用的关键词。显式类型/外购标记优先；同名加工附件会保留制造身份，名称线索仍需核对实际模型。", Dock = DockStyle.Top, Height = 48, Padding = new Padding(6) }); tabs.TabPages.Add(typeTab);
        Controls.Add(tabs); var close = new Button { Text = "关闭", Dock = DockStyle.Bottom, Height = 38 }; close.Click += (_, __) => Close(); Controls.Add(close);
    }
    private static void TextTab(TabControl tabs, string title, string text)
    { var tab = new TabPage(title); tab.Controls.Add(new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = text }); tabs.TabPages.Add(tab); }
    internal static string Describe(ProcessRecognitionConfig config, ProcessRecognitionInput input, bool deep, IEnumerable<RecognizedProcess> currentRows)
    {
        var text = new StringBuilder();
        text.AppendLine("当前列表（已有工序 / 手工草稿，尚不代表新识别建议）：");
        foreach (var row in currentRows ?? Array.Empty<RecognizedProcess>()) text.AppendLine("• " + row.Name + "：" + row.Evidence);
        if (input == null) { text.AppendLine("\r\n尚未读取本对象的完整分析输入。请先选择零件并识别；导入件和多实体请用深度识别。缓存列表只能查看工序，不能替代实体分析。"); return text.ToString(); }
        text.AppendLine("\r\n对象：" + input.DocumentTitle + "；读取模式：" + (deep ? "深度" : "快速"));
        text.AppendLine("类型：" + input.PartType + "；工序类型：" + input.ProcessType + "；材料：" + input.Material);
        if (!input.FeatureReadComplete) text.AppendLine("特征读取达到时间/数量限额，当前仅为部分事实；缺少某工序证据不代表模型没有该工序。");
        var geometry = input.Geometry ?? new ProcessGeometryFact { GeometryComplete = false };
        text.AppendLine("实体证据：实体 " + geometry.SolidBodyCount + "；钣金实体 " + geometry.SheetBodyCount
            + "；直管实体 " + geometry.StraightTubeBodyCount + "；折弯几何 " + geometry.BendCount
            + "；闭合平面内轮廓 " + geometry.CutoutCount + (geometry.CutoutCountReliable ? "（总数已核对）" : "（总数未确认，不写入）") + "；板厚 " + (geometry.ThicknessMm > 0 ? geometry.ThicknessMm.ToString("0.###") + " mm" : "未测得")
            + "；几何读取 " + (geometry.GeometryComplete ? "完整" : "不完整"));
        if (!string.IsNullOrWhiteSpace(geometry.CutoutCountEvidence)) text.AppendLine("内轮廓核对：" + geometry.CutoutCountEvidence);
        if (geometry.PressFitGeometryCount > 0 || geometry.CounterboreGeometryCount > 0)
            text.AppendLine("紧固件几何证据：压铆/焊铆候选 " + geometry.PressFitGeometryCount + "；沉孔/锪孔候选 " + geometry.CounterboreGeometryCount
                + "；" + (string.IsNullOrWhiteSpace(geometry.FastenerGeometryEvidence) ? "连接方式需人工确认" : geometry.FastenerGeometryEvidence));
        else if (deep && geometry.GeometryComplete)
            text.AppendLine("紧固件几何证据：未发现同轴内孔＋外圆柱、分级沉孔或相连内壁；普通单圆孔无法仅凭实体几何确认压铆。");
        var profileEvidence = ProcessStructuralProfilePolicy.Inspect(input);
        text.AppendLine("型材证据：名称/规格 " + (profileEvidence.TextKinds.Count == 0 ? "无" : string.Join("、", profileEvidence.TextKinds))
            + "；实体截面 " + (profileEvidence.GeometryKinds.Count == 0 ? "无" : string.Join("、", profileEvidence.GeometryKinds))
            + "；统一可靠截面 " + (profileEvidence.HasReliableGeometry ? profileEvidence.Kind : "否"));
        if (profileEvidence.HasConflict)
            text.AppendLine("型材复核：" + profileEvidence.ConflictReason + "；不会把不同截面自动合并为一个型材工序组。");
        string[] nativeTypes = (input.Features ?? Array.Empty<ProcessFeatureFact>())
            .Where(fact => fact != null && !fact.IsSuppressed && !string.IsNullOrWhiteSpace(fact.TypeName))
            .Select(fact => fact.TypeName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToArray();
        text.AppendLine("原生特征类型证据：" + (nativeTypes.Length == 0 ? "无" : string.Join("、", nativeTypes)));
        text.AppendLine("\r\n按当前规则重新计算的建议（不会覆盖当前列表或写入模型）：");
        try { foreach (var row in ProcessRecognitionService.Recognize(input, deep, ProcessRulesWindow.Copy(config))) text.AppendLine("• " + row.Name + "（" + row.Confidence + "%）：" + row.Evidence); }
        catch (Exception ex) { text.AppendLine("无法生成建议：" + ex.Message); }
        text.AppendLine("\r\n实体路线与整件汇合：\r\n" + ProcessManufacturingPlan.Describe(input, deep, config));
        text.AppendLine("\r\n孔工艺联动事实（快速读取，不遍历孔面）：");
        foreach (var fact in input.HoleFacts ?? Array.Empty<ProcessHoleFact>())
            text.AppendLine("• " + fact.ProcessName + "；" + (fact.IsConfirmed ? "已确认类型" : "候选，待孔工艺复核") + "；来源：" + fact.Source + "；" + fact.Evidence);
        if ((input.HoleFacts?.Count ?? 0) == 0) text.AppendLine("尚无孔工艺结果；普通切孔不能仅凭名称或孔径判为压铆/攻牙。");
        text.AppendLine("孔工艺完成识别或处理后会传回当前对象的工序建议。压铆/攻牙相对折弯、焊接的顺序仍需按工具可达性、螺纹保护与装配要求核对。");
        text.AppendLine("\r\n自定义判断规则：");
        var customRules = config.CustomRules ?? new List<ProcessRecognitionRule>();
        if (customRules.Count == 0) text.AppendLine("尚未配置。可在工序组/类型设置的判断规则页加入。");
        foreach (var rule in customRules) text.AppendLine(rule == null ? "无效空规则，请在判断规则页修正。" : (rule.Enabled ? "启用：" : "停用：") + rule.Name + "；" + ProcessRuleEngine.Describe(rule));
        text.AppendLine("\r\n当前事实的规则条件试算（已有工序、人工草稿或明确工序组优先时不自动应用）：");
        try
        {
            var baselineConfig = ProcessRulesWindow.Copy(config); baselineConfig.CustomRules.Clear();
            var baseline = ProcessRecognitionService.Recognize(input, deep, baselineConfig);
            var evaluation = ProcessRuleEngine.Apply(config, input, baseline);
            foreach (var match in evaluation.Matches) text.AppendLine("• " + match);
            foreach (var error in evaluation.Errors) text.AppendLine("• 规则不可执行：" + error);
            if (evaluation.Matches.Count == 0 && evaluation.Errors.Count == 0) text.AppendLine("没有规则满足当前事实条件。");
        }
        catch (Exception ex) { text.AppendLine("规则试算不可用：" + ex.Message); }
        text.AppendLine("\r\n以上为当前已读取快照。模型或设置变化后，请重新识别再确认写入。");
        return text.ToString();
    }
}
