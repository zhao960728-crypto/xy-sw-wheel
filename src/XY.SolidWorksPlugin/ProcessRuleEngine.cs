using System;
using System.Collections.Generic;
using System.Linq;

namespace XY.SolidWorksPlugin;

// Persisted, declarative rules. All filled conditions must match; list order is execution order.
internal sealed class ProcessRecognitionRule
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string PartType { get; set; } = "";
    public string NameContains { get; set; } = "";
    public string MaterialContains { get; set; } = "";
    public string FeatureContains { get; set; } = "";
    public int? MinBodyCount { get; set; }
    public int? MaxBodyCount { get; set; }
    public int? MinBendCount { get; set; }
    public int? MaxBendCount { get; set; }
    public int? MinTubeCount { get; set; }
    public int? MaxTubeCount { get; set; }
    public bool? HasWeldEvidence { get; set; }
    public string Action { get; set; } = "append";
    public string ProcessNames { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string BeforeProcess { get; set; } = "";
}

internal sealed class ProcessRuleEvaluation
{
    public IReadOnlyList<RecognizedProcess> Processes { get; set; } = Array.Empty<RecognizedProcess>();
    public IReadOnlyList<string> Matches { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
}

internal static class ProcessRuleEngine
{
    private static readonly char[] Forbidden = { '=', '|', ';', '；', '\r', '\n' };

    public static IReadOnlyList<string> Validate(ProcessRecognitionConfig config)
    {
        var errors = new List<string>();
        var rules = config?.CustomRules ?? new List<ProcessRecognitionRule>();
        if (rules.Count > 100) errors.Add("判断规则最多 100 条；请减少规则后保存，系统不会截断。");
        if (rules.Where(rule => rule != null).GroupBy(rule => (rule.Name ?? "").Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            errors.Add("判断规则名称不能重复。");
        for (int i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            string prefix = "规则 " + (i + 1) + "：";
            if (rule == null) { errors.Add(prefix + "内容为空。"); continue; }
            if (!ValidText(rule.Name, 64, true)) errors.Add(prefix + "名称必填、最多 64 字且不能含分隔符。");
            if (new[] { rule.PartType, rule.NameContains, rule.MaterialContains, rule.FeatureContains }.Any(value => !ValidText(value, 128, false)))
                errors.Add(prefix + "条件文字最多 128 字，不能含分隔符。");
            if (!RangeValid(rule.MinBodyCount, rule.MaxBodyCount) || !RangeValid(rule.MinBendCount, rule.MaxBendCount) || !RangeValid(rule.MinTubeCount, rule.MaxTubeCount))
                errors.Add(prefix + "数量必须为非负整数，最小值不能大于最大值。");
            if (!HasConditions(rule)) errors.Add(prefix + "至少填写一个条件，避免误套所有零件。");
            if (rule.Action == "append")
            {
                if (!ValidSequence(rule.ProcessNames)) errors.Add(prefix + "追加工序须填写 1–30 道工序，以 > 分隔，每道最多 64 字，不留空步骤。");
                if (!string.IsNullOrWhiteSpace(rule.GroupName)) errors.Add(prefix + "追加动作不能同时填写工序组名。");
                if (!ValidText(rule.BeforeProcess, 64, false) || (rule.BeforeProcess ?? "").IndexOfAny(new[] { '>', '→' }) >= 0) errors.Add(prefix + "插入位置最多 64 字，须为单个完整工序名称。");
            }
            else if (rule.Action == "group")
            {
                if (!ValidText(rule.GroupName, 64, true) || ProcessRecognitionConfig.ResolveCombination(config, rule.GroupName).Count == 0)
                    errors.Add(prefix + "套组动作须引用存在的工序组。");
                if (!string.IsNullOrWhiteSpace(rule.ProcessNames)) errors.Add(prefix + "套组动作不能同时填写追加工序。");
                if (!string.IsNullOrWhiteSpace(rule.BeforeProcess)) errors.Add(prefix + "套组动作不能同时填写插入位置。");
            }
            else errors.Add(prefix + "动作只能为 append（追加工序）或 group（套工序组）。");
        }
        string[] names = (config?.AvailableProcessNames ?? "").Split(new[] { '|', '\r', '\n', ';', '；', '，', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (names.Length > 200 || names.Any(name => !ValidText(name, 64, true))) errors.Add("可选工序目录最多 200 项，每项 1–64 字且不能含 =。");
        return errors;
    }

    public static string Describe(ProcessRecognitionRule rule)
    {
        if (rule == null) return "空规则";
        var conditions = new List<string>();
        AddText(conditions, "类型等于", rule.PartType);
        AddText(conditions, "名称包含", rule.NameContains);
        AddText(conditions, "材料包含", rule.MaterialContains);
        AddText(conditions, "未抑制特征包含", rule.FeatureContains);
        AddRange(conditions, "实体数", rule.MinBodyCount, rule.MaxBodyCount);
        AddRange(conditions, "折弯数", rule.MinBendCount, rule.MaxBendCount);
        AddRange(conditions, "直管数", rule.MinTubeCount, rule.MaxTubeCount);
        if (rule.HasWeldEvidence.HasValue) conditions.Add(rule.HasWeldEvidence.Value ? "有焊接证据" : "无焊接证据");
        return (rule.Enabled ? "启用；" : "停用；") + string.Join(" 且 ", conditions) + " → " + (rule.Action == "group" ? "套组“" + rule.GroupName + "”" : "追加“" + rule.ProcessNames + "”" + (string.IsNullOrWhiteSpace(rule.BeforeProcess) ? "到链末" : "到“" + rule.BeforeProcess + "”之前"));
    }

    public static ProcessRuleEvaluation Preview(ProcessRecognitionConfig config, ProcessRecognitionInput input)
        => Apply(config, input, Array.Empty<RecognizedProcess>());

    public static ProcessRuleEvaluation Preview(ProcessRecognitionConfig config, ProcessRecognitionInput input, IEnumerable<RecognizedProcess> baseProcesses)
        => Apply(config, input, baseProcesses);

    public static ProcessRuleEvaluation Apply(ProcessRecognitionConfig config, ProcessRecognitionInput input, IEnumerable<RecognizedProcess> existing)
    {
        var result = (existing ?? Array.Empty<RecognizedProcess>()).Select(item => new RecognizedProcess { Name = item.Name, Confidence = item.Confidence, Evidence = item.Evidence }).ToList();
        var original = result.Select(item => new RecognizedProcess { Name = item.Name, Confidence = item.Confidence, Evidence = item.Evidence }).ToList();
        var errors = Validate(config);
        if (errors.Count > 0) return new ProcessRuleEvaluation { Processes = result, Errors = errors };
        var matches = new List<string>();
        if (input == null) return new ProcessRuleEvaluation { Processes = result, Errors = new[] { "没有可供预览的识别事实。" } };
        var enabled = (config?.CustomRules ?? new List<ProcessRecognitionRule>()).Where(rule => rule.Enabled).ToArray();
        if (PurchasedPartPolicy.IsPurchased(input) && enabled.Length > 0)
            return new ProcessRuleEvaluation { Processes = result, Matches = new[] { "外购/标准件身份优先，未自动执行制造判断规则。" } };
        foreach (var rule in enabled)
        {
            string reason;
            if (!Matches(rule, input, out reason)) continue;
            string evidence = "用户判断规则“" + rule.Name + "”：" + reason;
            var added = rule.Action == "group" ? ProcessRecognitionConfig.ResolveCombination(config, rule.GroupName).Select(item => item.Name).ToList()
                : ProcessRecognitionConfig.CombinationSteps(rule.ProcessNames).ToList();
            if (rule.Action == "group")
            {
                result = added.Select(name => new RecognizedProcess { Name = name, Confidence = 99, Evidence = evidence }).ToList();
                matches.Add(evidence + "；套组“" + rule.GroupName + "”");
                continue;
            }
            int insertion = result.Count;
            if (!string.IsNullOrWhiteSpace(rule.BeforeProcess))
            {
                insertion = result.FindIndex(item => string.Equals(item.Name, rule.BeforeProcess.Trim(), StringComparison.OrdinalIgnoreCase));
                if (insertion < 0) return new ProcessRuleEvaluation { Processes = original, Errors = new[] { "规则“" + rule.Name + "”的插入位置“" + rule.BeforeProcess + "”不在当前工序链；未应用任何自定义规则。请修正位置或用工序组明确顺序。" } };
            }
            foreach (string name in added)
            {
                // An inspection after the anchor is a separate stage. Keep it and
                // insert the requested earlier inspection instead of silently reusing it.
                var search = string.IsNullOrWhiteSpace(rule.BeforeProcess) ? result : result.Take(insertion);
                var found = search.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
                if (found == null) result.Insert(insertion++, new RecognizedProcess { Name = name, Confidence = 99, Evidence = evidence });
                else found.Evidence = found.Evidence + "；" + evidence;
            }
            matches.Add(evidence + "；" + (rule.Action == "group" ? "套组“" + rule.GroupName + "”" : "追加“" + rule.ProcessNames + "”"));
        }
        return new ProcessRuleEvaluation { Processes = result, Matches = matches };
    }

    public static bool HasWeldEvidence(ProcessRecognitionInput input)
        => ProcessManufacturingPlan.HasWeldingEvidence(input);

    private static bool Matches(ProcessRecognitionRule rule, ProcessRecognitionInput input, out string reason)
    {
        reason = "";
        string type = string.IsNullOrWhiteSpace(input.PartType) ? PartTypeCatalog.FromText("", input.DocumentTitle, input.ProfileDescription).Type : input.PartType;
        if (type.Length == 0 && input.Geometry != null && input.Geometry.GeometryComplete)
            type = PartTypeCatalog.FromGeometry(input.Geometry.SheetBodyCount > 0, false, HasWeldEvidence(input), input.Geometry.StraightTubeBodyCount > 0, false, false, input.IsAssembly).Type;
        string profileKind = input.Geometry?.GeometryComplete == true ? input.Geometry.ProfileKind : "";
        if (type == PartTypes.Profile && !string.IsNullOrWhiteSpace(profileKind)) type = profileKind;
        if (!string.IsNullOrWhiteSpace(rule.PartType) && !string.Equals(ProcessTypeGroupPolicy.CanonicalType(type), ProcessTypeGroupPolicy.CanonicalType(rule.PartType), StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(rule.NameContains) && !Contains(input.DocumentTitle, rule.NameContains)) return false;
        if (!string.IsNullOrWhiteSpace(rule.MaterialContains) && !Contains(input.Material, rule.MaterialContains)) return false;
        if (!string.IsNullOrWhiteSpace(rule.FeatureContains) && !(input.Features ?? Array.Empty<ProcessFeatureFact>()).Any(fact => fact != null && !fact.IsSuppressed && Contains(fact.Name + " " + fact.TypeName, rule.FeatureContains))) return false;
        bool geometryCondition = rule.MinBodyCount.HasValue || rule.MaxBodyCount.HasValue || rule.MinBendCount.HasValue || rule.MaxBendCount.HasValue || rule.MinTubeCount.HasValue || rule.MaxTubeCount.HasValue;
        // A zero-count default object from a quick read cannot establish a geometric absence.
        if (geometryCondition && (input.Geometry == null || input.Geometry.SolidBodyCount < 1)) return false;
        bool detailedGeometryCondition = rule.MaxBodyCount.HasValue || rule.MinBendCount.HasValue || rule.MaxBendCount.HasValue || rule.MinTubeCount.HasValue || rule.MaxTubeCount.HasValue;
        if (detailedGeometryCondition && !input.Geometry.GeometryComplete) return false;
        // An upper bound needs the complete feature scan as well: a missing
        // native bend feature cannot be interpreted as a zero-bend model.
        if (rule.MaxBendCount.HasValue && !input.FeatureReadComplete) return false;
        if (geometryCondition && (!InRange(input.Geometry.SolidBodyCount, rule.MinBodyCount, rule.MaxBodyCount) || !InRange(input.Geometry.BendCount, rule.MinBendCount, rule.MaxBendCount) || !InRange(input.Geometry.StraightTubeBodyCount, rule.MinTubeCount, rule.MaxTubeCount))) return false;
        if (rule.HasWeldEvidence == false && (input.Geometry == null || input.Geometry.SolidBodyCount < 1 || !input.Geometry.GeometryComplete || !input.FeatureReadComplete)) return false;
        if (rule.HasWeldEvidence.HasValue && HasWeldEvidence(input) != rule.HasWeldEvidence.Value) return false;
        reason = Describe(rule);
        return true;
    }

    private static bool HasConditions(ProcessRecognitionRule rule) => !string.IsNullOrWhiteSpace(rule.PartType + rule.NameContains + rule.MaterialContains + rule.FeatureContains) || rule.MinBodyCount.HasValue || rule.MaxBodyCount.HasValue || rule.MinBendCount.HasValue || rule.MaxBendCount.HasValue || rule.MinTubeCount.HasValue || rule.MaxTubeCount.HasValue || rule.HasWeldEvidence.HasValue;
    private static bool Contains(string text, string value) => (text ?? "").IndexOf((value ?? "").Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    private static bool ValidText(string value, int max, bool required) => (!required || !string.IsNullOrWhiteSpace(value)) && (value ?? "").Trim().Length <= max && (value ?? "").IndexOfAny(Forbidden) < 0;
    private static bool ValidSequence(string sequence) { var values = (sequence ?? "").Split(new[] { '>', '→' }); return values.Length <= 30 && values.All(value => ValidText(value, 64, true)); }
    private static bool RangeValid(int? min, int? max) => (!min.HasValue || min.Value >= 0) && (!max.HasValue || max.Value >= 0) && (!min.HasValue || !max.HasValue || min.Value <= max.Value);
    private static bool InRange(int value, int? min, int? max) => (!min.HasValue || value >= min.Value) && (!max.HasValue || value <= max.Value);
    private static void AddText(List<string> target, string title, string value) { if (!string.IsNullOrWhiteSpace(value)) target.Add(title + "“" + value.Trim() + "”"); }
    private static void AddRange(List<string> target, string title, int? min, int? max) { if (min.HasValue || max.HasValue) target.Add(title + " " + (min.HasValue ? min.Value.ToString() : "0") + "～" + (max.HasValue ? max.Value.ToString() : "不限")); }
}
