using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace XY.SolidWorksPlugin;

// A read-only manufacturing proposal. It never splits or changes model bodies.
internal sealed class ProcessBodyRoute
{
    public string BodyName { get; set; } = "";
    public string Type { get; set; } = "";
    public IReadOnlyList<string> Processes { get; set; } = Array.Empty<string>();
    public string Evidence { get; set; } = "";
}

internal sealed class ProcessManufacturingPlan
{
    public IReadOnlyList<ProcessBodyRoute> BodyRoutes { get; private set; } = Array.Empty<ProcessBodyRoute>();
    public string Summary { get; private set; } = "";
    public IReadOnlyList<string> Warnings { get; private set; } = Array.Empty<string>();

    internal static bool HasWeldingEvidence(ProcessRecognitionInput input)
    {
        if (input == null) return false;
        string declared = (input.PartType ?? "") + " " + (input.ProcessType ?? "") + " " + (input.ExistingProcess ?? "");
        if (new[] { "焊接", "组焊", "总焊", "焊合", "焊件", "封角", "点焊", "氩弧焊", "二保焊", "焊螺" }.Any(declared.Contains)) return true;
        return (input.Features ?? Array.Empty<ProcessFeatureFact>()).Any(f => f != null && !f.IsSuppressed &&
            (new[] { "weldbead", "cosmeticweld", "weldcorner", "weldsymbol" }.Any(word => (f.TypeName ?? "").ToLowerInvariant().Contains(word))
             || new[] { "焊缝", "焊接", "组焊", "焊合", "点焊", "氩弧焊", "二保焊", "焊螺" }.Any(word => (f.Name ?? "").Contains(word))));
    }

    public static ProcessManufacturingPlan Build(ProcessRecognitionInput input, bool deep, ProcessRecognitionConfig config)
    {
        if (input == null) return new ProcessManufacturingPlan { Summary = "尚未读取模型。请先识别，导入实体或方通建议使用深度识别。" };
        if (PurchasedPartPolicy.IsPurchased(input)) return new ProcessManufacturingPlan {
            Summary = (input.IsAssembly ? "外购装配总成" : "外购零件") + "按整件供应：外购 → 来料检验 → 入库。",
            Warnings = new[] { PurchasedPartPolicy.Display(input) + "；确认外购标记只写所选文件/引用配置，保留内部零件、工序和其他配置。" } };
        var geometry = input.Geometry ?? new ProcessGeometryFact { GeometryComplete = false };
        var warnings = new List<string>();
        if (!deep || !geometry.GeometryComplete)
            warnings.Add("几何尚未完整读取：未识别到的管材、折弯不能视为不存在；请深度识别或人工复核。" );
        var routes = new List<ProcessBodyRoute>();
        var bodies = geometry.BodyFacts ?? Array.Empty<ProcessGeometryFact>();
        var declaredProfile = PartTypeCatalog.FromText(input.PartType, input.DocumentTitle, input.ProfileDescription);
        var profileEvidence = ProcessStructuralProfilePolicy.Inspect(input);
        string declaredProfileKind = ProcessTypeGroupPolicy.CanonicalType(input.PartType);
        if (!ProcessStructuralProfilePolicy.IsKnownKind(declaredProfileKind) && profileEvidence.TextKinds.Count == 1) declaredProfileKind = profileEvidence.TextKinds[0];
        bool declaredProfileRoute = declaredProfile.Type == PartTypes.Profile
            && !profileEvidence.HasConflict
            && (profileEvidence.GeometryKinds.Count == 0 || (profileEvidence.GeometryKinds.Count == 1 && (string.IsNullOrWhiteSpace(declaredProfileKind) || string.Equals(profileEvidence.GeometryKinds[0], declaredProfileKind, StringComparison.OrdinalIgnoreCase))));
        bool profileOnly = !profileEvidence.HasConflict && bodies.Count > 0 && bodies.All(body => body != null && !body.IsNativeSheetMetal
            && (body.StraightTubeBodyCount > 0 || ProcessStructuralProfilePolicy.IsKnownKind(ProcessTypeGroupPolicy.CanonicalType(body.ProfileKind)) || declaredProfileRoute));
        foreach (var body in bodies.Where(b => b != null))
        {
            var steps = new List<string>();
            string type, evidence;
            if (body.GeometryComplete && body.StraightTubeBodyCount > 0)
            {
                type = string.IsNullOrWhiteSpace(body.ProfileKind) ? "方通/直管型材" : body.ProfileKind; evidence = "闭合等截面管壁与端口证据；截面圆角不计折弯";
                steps.Add("锯切下料");
            }
            else if (body.GeometryComplete && !body.IsNativeSheetMetal
                && (ProcessStructuralProfilePolicy.IsKnownKind(ProcessTypeGroupPolicy.CanonicalType(body.ProfileKind)) || declaredProfileRoute))
            {
                string bodyKind = ProcessTypeGroupPolicy.CanonicalType(body.ProfileKind);
                type = ProcessStructuralProfilePolicy.IsKnownKind(bodyKind) ? bodyKind : (string.IsNullOrWhiteSpace(declaredProfileKind) ? PartTypes.Profile : declaredProfileKind);
                evidence = (ProcessStructuralProfilePolicy.IsKnownKind(bodyKind) ? "实体截面/型材元数据“" + bodyKind + "”" : declaredProfile.Evidence + "；明确型材声明")
                    + "；优先按现货型材下料，不把现货角钢当折弯板下料";
                steps.Add("锯切下料");
            }
            else if (body.IsNativeSheetMetal || (body.GeometryComplete && body.IsSheetLike))
            {
                type = PartTypes.SheetMetal;
                evidence = body.IsNativeSheetMetal ? "原生钣金实体" : "等厚薄壁实体几何，板件类型待工艺确认";
                steps.Add("激光下料");
            }
            else { type = "待确认实体"; evidence = "缺少可确认的板材/型材依据，需确定备料方式"; }
            if (steps.Count > 0 && config?.IncludeDeburr != false) steps.Add(config?.DeburrProcessName ?? "去毛刺");
            if (body.GeometryComplete && body.BendCount > 0 && body.StraightTubeBodyCount == 0
                && !(profileOnly && !body.IsNativeSheetMetal))
            {
                steps.Add("折弯"); evidence += "；检测到 " + body.BendCount + " 组折弯几何";
            }
            if (!body.GeometryComplete) evidence += "；仅轻量信息或局部几何，待深度复核";
            routes.Add(new ProcessBodyRoute { BodyName = string.IsNullOrWhiteSpace(body.BodyName) ? "实体 " + (routes.Count + 1) : body.BodyName,
                Type = type, Processes = steps, Evidence = evidence });
        }
        bool bend = (geometry.BendCount > 0 && !profileOnly) || (input.Features ?? Array.Empty<ProcessFeatureFact>()).Any(f => f != null && !f.IsSuppressed &&
            new[] { "onebend", "sketchbend", "edgeflange", "miterflange", "loftedbend", "hem", "jog" }.Any(token => (f.TypeName ?? "").IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0));
        bool weld = HasWeldingEvidence(input);
        IReadOnlyList<string> confirmedHoleSteps = ConfirmedHoleProcesses(input.HoleFacts);
        string holeRoute = confirmedHoleSteps.Count == 0 ? "" : "；已确认孔工艺：" + string.Join(" → ", confirmedHoleSteps);
        string summary = weld
            ? (geometry.SolidBodyCount > 1 ? "各实体分别下料/加工" : "单实体按当前毛坯加工") + holeRoute + (bend ? " → 完成所需折弯" : "") + " → 定位组对 → 焊接 → 焊后检验。"
            : geometry.SolidBodyCount > 1 ? "各实体分别下料/加工" + holeRoute + "；当前没有明确焊接证据，汇合方式需确认。" : "按单实体加工路线处理" + holeRoute + "。";
        if (geometry.SolidBodyCount > 1)
            warnings.Add("多实体只表示模型含多个实体。分件路线是制造建议，写入的工序仍为零件汇总；不会自动拆模型或建立装配体。" );
        if (profileEvidence.HasConflict)
            warnings.Add("型材证据冲突：" + profileEvidence.ConflictReason + "；各实体必须分别确认截面/材质/长度，不能按一个型材组混合套料。" );
        if (bend && weld) warnings.Add("建议顺序为折弯后焊接；焊后是否还需折弯、工具可达性和焊接变形须按图纸复核。" );
        if (routes.Count > 0 && routes.Any(r => r.Type == "待确认实体")) warnings.Add("存在待确认实体，不能据其他实体的结果推定其下料或折弯工艺。" );
        warnings.Add("大件是否拆分不能仅由外形尺寸确定：需设备有效行程、运输限制、允许拼缝位置及图纸要求；可用自定义规则明确添加拆分/拼焊工序。" );
        return new ProcessManufacturingPlan { BodyRoutes = routes, Summary = summary, Warnings = warnings };
    }

    internal static IReadOnlyList<string> ConfirmedHoleProcesses(IEnumerable<ProcessHoleFact> facts)
    {
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["钻孔"] = 30, ["沉孔"] = 35, ["攻丝"] = 40, ["压铆"] = 45, ["压钉"] = 46,
            ["焊铆"] = 47, ["热熔孔"] = 48, ["翻孔"] = 49, ["拉铆"] = 65
        };
        string Canonical(string name)
        {
            string value = (name ?? "").Trim();
            if (value.Equals("攻牙", StringComparison.OrdinalIgnoreCase)) return "攻丝";
            if (value.StartsWith("压铆", StringComparison.Ordinal)) return "压铆";
            if (value.StartsWith("焊铆", StringComparison.Ordinal)) return "焊铆";
            return value;
        }
        return (facts ?? Array.Empty<ProcessHoleFact>())
            .Where(fact => fact != null && fact.IsConfirmed && !string.IsNullOrWhiteSpace(fact.ProcessName))
            .Select(fact => Canonical(fact.ProcessName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => rank.TryGetValue(name, out int order) ? order : 55)
            .ThenBy(name => name, StringComparer.CurrentCulture)
            .ToArray();
    }

    public static string Describe(ProcessRecognitionInput input, bool deep, ProcessRecognitionConfig config)
    {
        var plan = Build(input, deep, config);
        var text = new StringBuilder(ProcessPartClassification.Describe(input));
        text.AppendLine().AppendLine().Append(plan.Summary);
        foreach (var row in plan.BodyRoutes)
            text.AppendLine().AppendLine().Append(row.BodyName).Append(" [").Append(row.Type).Append("]：")
                .Append(row.Processes.Count > 0 ? string.Join(" → ", row.Processes) : "工艺待确认")
                .AppendLine().Append("依据：").Append(row.Evidence);
        foreach (var warning in plan.Warnings) text.AppendLine().AppendLine().Append("复核：").Append(warning);
        return text.ToString();
    }
}
