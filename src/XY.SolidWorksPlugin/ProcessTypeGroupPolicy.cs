using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace XY.SolidWorksPlugin;

internal sealed class ProcessTypeGroupMatch
{
    internal string Group { get; set; } = "";
    internal string Type { get; set; } = "";
    internal string Reason { get; set; } = "未匹配类型默认组";
    internal bool Matched => Group.Length > 0;
}

internal static class ProcessTypeGroupPolicy
{
    internal static string CanonicalType(string type)
    {
        string value = (type ?? "").Trim();
        if (new[] { "方通", "方管", "矩形管", "矩形钢管", "方钢管", "square tube", "rectangular tube", "SHS", "RHS" }.Contains(value, StringComparer.OrdinalIgnoreCase)) return "方通";
        if (new[] { "角钢", "角铁", "L型钢", "angle steel", "angle iron" }.Contains(value, StringComparer.OrdinalIgnoreCase)) return "角钢";
        if (new[] { "圆管", "钢管", "无缝管", "圆钢管", "round tube", "circular tube", "CHS" }.Contains(value, StringComparer.OrdinalIgnoreCase)) return "圆管";
        if (new[] { "槽钢", "槽形钢", "U型钢", "channel steel", "U channel", "U-channel" }.Contains(value, StringComparer.OrdinalIgnoreCase)) return "槽钢";
        if (new[] { "工字钢", "工字梁", "I型钢", "I型梁", "I-beam", "I beam" }.Contains(value, StringComparer.OrdinalIgnoreCase)) return "工字钢";
        if (new[] { "H钢", "H型钢", "H型梁", "H-beam", "H beam" }.Contains(value, StringComparer.OrdinalIgnoreCase)) return "H型钢";
        if (new[] { "C型钢", "C型檩条", "C channel", "C-channel" }.Contains(value, StringComparer.OrdinalIgnoreCase)) return "C型钢";
        if (new[] { "Z型钢", "Z型檩条", "Z channel", "Z-channel" }.Contains(value, StringComparer.OrdinalIgnoreCase)) return "Z型钢";
        if (new[] { "T型钢", "T型材", "T-section", "T section" }.Contains(value, StringComparer.OrdinalIgnoreCase)) return "T型钢";
        if (new[] { "铝型材", "工业铝型材", "欧标铝型材" }.Contains(value, StringComparer.OrdinalIgnoreCase)) return "铝型材";
        return value;
    }

    internal static ProcessTypeGroupMatch Resolve(ProcessRecognitionConfig config, ProcessRecognitionInput input)
    {
        var mappings = ProcessRecognitionConfig.ParseProcessNameMappings(config?.ProcessTypeGroups);
        if (mappings.Count == 0 || input == null) return new ProcessTypeGroupMatch();
        if (PurchasedPartPolicy.IsPurchased(input))
        {
            var supply = mappings.Where(pair => pair.Key == "外购件" || pair.Key == "外购").ToArray();
            return supply.Length == 0 ? new ProcessTypeGroupMatch { Reason = "外购身份优先，尚未配置外购默认组；未套用加工类型组" } : Select(config, supply, "外购身份");
        }
        string declared = CanonicalType(input.PartType);
        if (PurchasedPartPolicy.ExplicitFlag(input.Purchase) == false && (declared == "外购" || PurchasedPartPolicy.IsSupplyType(declared)))
            return new ProcessTypeGroupMatch { Reason = "人工已明确非外购，旧外购类型不能自动套用外购组" };
        var profileEvidence = ProcessStructuralProfilePolicy.Inspect(input);
        var exact = mappings.Where(pair => string.Equals(CanonicalType(pair.Key), declared, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length > 0)
        {
            if (ProcessStructuralProfilePolicy.IsKnownKind(declared) && profileEvidence.HasGeometryConflictWith(declared))
            {
                string reason = profileEvidence.ConflictReason;
                if (reason.Length == 0) reason = "类型属性“" + input.PartType + "”与实体截面“" + string.Join("、", profileEvidence.GeometryKinds) + "”不一致";
                return new ProcessTypeGroupMatch { Reason = reason };
            }
            return Select(config, exact, "类型属性");
        }
        // An explicit unrelated type is authoritative. A generic profile type may be refined by a name.
        if (declared.Length > 0 && declared != PartTypes.Profile && declared != "型材")
            return new ProcessTypeGroupMatch { Reason = "类型属性“" + input.PartType + "”未配置默认组" };
        string text = (input.DocumentTitle ?? "") + " " + (input.ProfileDescription ?? "") + " " + (input.ItemDescription ?? "") + " " + (input.StandardCode ?? "");
        if (profileEvidence.HasTextConflict || profileEvidence.HasGeometryConflict || profileEvidence.HasConflict)
            return new ProcessTypeGroupMatch { Reason = profileEvidence.ConflictReason };
        var types = profileEvidence.TextKinds.Count == 1
            ? profileEvidence.TextKinds.ToList()
            : profileEvidence.HasReliableGeometry ? new List<string> { profileEvidence.GeometryKinds[0] } : new List<string>();
        var candidates = mappings.Where(pair => types.Contains(CanonicalType(pair.Key))).ToArray();
        string source = profileEvidence.TextKinds.Count == 0 ? "完整且同类的管材实体几何" : "名称/型材描述或同类管材几何";
        return candidates.Length == 0 ? new ProcessTypeGroupMatch { Reason = "没有明确的细分类类型依据或尚未配置对应组" } : Select(config, candidates, source);
    }

    private static ProcessTypeGroupMatch Select(ProcessRecognitionConfig config, KeyValuePair<string,string>[] matches, string source)
    {
        if (matches.Length != 1) return new ProcessTypeGroupMatch { Reason = "同一类型关联了多条默认组规则，请先修正规则" };
        var match = matches[0];
        if (ProcessRecognitionConfig.ResolveCombination(config, match.Value).Count == 0)
            return new ProcessTypeGroupMatch { Reason = "关联的工序组“" + match.Value + "”不存在" };
        return new ProcessTypeGroupMatch { Group = match.Value, Type = match.Key, Reason = source + "匹配“" + match.Key + "”" };
    }
}

internal sealed class ProcessStructuralProfileEvidence
{
    internal IReadOnlyList<string> TextKinds { get; set; } = Array.Empty<string>();
    internal IReadOnlyList<string> GeometryKinds { get; set; } = Array.Empty<string>();
    internal bool GeometryComplete { get; set; }
    internal bool GeometryUniform { get; set; }
    internal bool HasTextConflict => TextKinds.Count > 1;
    internal bool HasGeometryConflict => GeometryKinds.Count > 1;
    internal bool HasConflict => HasTextConflict || HasGeometryConflict || (TextKinds.Count == 1 && GeometryKinds.Count == 1 && !string.Equals(TextKinds[0], GeometryKinds[0], StringComparison.OrdinalIgnoreCase));
    internal bool HasReliableGeometry => GeometryComplete && GeometryUniform && GeometryKinds.Count == 1;
    internal string Kind => TextKinds.Count == 1 ? TextKinds[0] : GeometryKinds.Count == 1 ? GeometryKinds[0] : "";
    internal bool HasGeometryConflictWith(string declaredKind)
        => GeometryKinds.Count > 0 && (GeometryKinds.Count > 1 || !GeometryKinds.Contains(declaredKind, StringComparer.OrdinalIgnoreCase));

    internal string ConflictReason
    {
        get
        {
            if (HasTextConflict) return "名称/型材规格同时包含多种型材：" + string.Join("、", TextKinds);
            if (HasGeometryConflict) return "不同实体包含多种型材截面：" + string.Join("、", GeometryKinds);
            if (TextKinds.Count == 1 && GeometryKinds.Count == 1) return "文字/规格型材“" + TextKinds[0] + "”与实体截面“" + GeometryKinds[0] + "”不一致";
            return "";
        }
    }
}

// Structural-profile evidence follows the Doctor reference model: a known
// section/metadata signal may refine the route, while mixed or incomplete
// sections stay pending instead of being merged into one stock route.
internal static class ProcessStructuralProfilePolicy
{
    internal static ProcessStructuralProfileEvidence Inspect(ProcessRecognitionInput input)
    {
        var geometry = input?.Geometry;
        var textKinds = DetectKinds((input?.DocumentTitle ?? "") + " " + (input?.ProfileDescription ?? "") + " " + (input?.ItemDescription ?? "") + " " + (input?.StandardCode ?? ""));
        var bodyFacts = FlattenBodies(geometry?.BodyFacts).ToArray();
        var geometryKinds = new List<string>();
        if (!string.IsNullOrWhiteSpace(geometry?.ProfileKind)) geometryKinds.Add(ProcessTypeGroupPolicy.CanonicalType(geometry.ProfileKind));
        geometryKinds.AddRange(bodyFacts.Where(body => body.GeometryComplete && !string.IsNullOrWhiteSpace(body.ProfileKind))
            .Select(body => ProcessTypeGroupPolicy.CanonicalType(body.ProfileKind)));
        geometryKinds = geometryKinds.Where(IsKnownKind).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        bool bodyUniform = bodyFacts.Length == 0 || bodyFacts.All(body => body.GeometryComplete
            && string.Equals(ProcessTypeGroupPolicy.CanonicalType(body.ProfileKind), geometryKinds.FirstOrDefault(), StringComparison.OrdinalIgnoreCase));
        string geometryKind = ProcessTypeGroupPolicy.CanonicalType(geometry?.ProfileKind);
        bool topologyUniform = geometry != null && geometry.StraightTubeBodyCount == geometry.SolidBodyCount
            || (geometry != null && !string.IsNullOrWhiteSpace(geometry.ProfileKind)
                && IsKnownKind(geometryKind) && geometryKind != "方通" && geometryKind != "圆管");
        bool uniform = geometry?.GeometryComplete == true
            && geometry.SolidBodyCount > 0
            && topologyUniform
            && geometryKinds.Count == 1
            && bodyUniform;
        return new ProcessStructuralProfileEvidence
        {
            TextKinds = textKinds,
            GeometryKinds = geometryKinds,
            GeometryComplete = geometry?.GeometryComplete == true,
            GeometryUniform = uniform
        };
    }

    internal static IReadOnlyList<string> DetectKinds(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        var kinds = new List<string>();
        bool Has(params string[] values) => values.Any(value => text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0);
        if (Has("方通", "方管", "矩形管", "矩形钢管", "方钢管", "square tube", "rectangular tube", "SHS", "RHS")) kinds.Add("方通");
        if (Has("角钢", "角铁", "L型钢", "angle steel", "angle iron") || Regex.IsMatch(text, @"(?<![A-Za-z0-9])L\s*\d{2,4}(?:\s*[x×*]\s*\d{1,4}){1,2}(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) kinds.Add("角钢");
        if (Has("圆管", "圆钢管", "无缝管", "round tube", "circular tube", "CHS")) kinds.Add("圆管");
        if (Has("槽钢", "槽形钢", "U型钢", "channel steel") || Regex.IsMatch(text, @"(?<![A-Za-z0-9])U\s*\d{2,4}(?:\s*[x×*]\s*\d{1,4}){1,3}(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) kinds.Add("槽钢");
        if (Has("工字钢", "工字梁", "I型钢", "I型梁", "I-beam", "I beam") || Regex.IsMatch(text, @"(?<![A-Za-z0-9])I\s*\d{2,4}(?:\s*[x×*]\s*\d{1,4}){1,3}(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) kinds.Add("工字钢");
        if (Has("H钢", "H型钢", "H型梁", "H-beam", "H beam") || Regex.IsMatch(text, @"(?<![A-Za-z0-9])H\s*\d{2,4}(?:\s*[x×*]\s*\d{1,4}){1,3}(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) kinds.Add("H型钢");
        if (Has("C型钢", "C型檩条", "C channel", "C-channel") || Regex.IsMatch(text, @"(?<![A-Za-z0-9])C\s*\d{2,4}(?:\s*[x×*]\s*\d{1,4}){1,3}(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) kinds.Add("C型钢");
        if (Has("Z型钢", "Z型檩条", "Z channel", "Z-channel") || Regex.IsMatch(text, @"(?<![A-Za-z0-9])Z\s*\d{2,4}(?:\s*[x×*]\s*\d{1,4}){1,3}(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) kinds.Add("Z型钢");
        if (Has("T型钢", "T型材", "T-section", "T section") || Regex.IsMatch(text, @"(?<![A-Za-z0-9])T\s*\d{2,4}(?:\s*[x×*]\s*\d{1,4}){1,3}(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) kinds.Add("T型钢");
        if (Has("铝型材", "工业铝型材", "欧标铝型材")) kinds.Add("铝型材");
        return kinds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static bool IsKnownKind(string value) => new[] { "方通", "角钢", "圆管", "槽钢", "工字钢", "H型钢", "C型钢", "Z型钢", "T型钢", "铝型材" }.Contains(value, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<ProcessGeometryFact> FlattenBodies(IEnumerable<ProcessGeometryFact> facts)
    {
        foreach (var fact in facts ?? Enumerable.Empty<ProcessGeometryFact>())
        {
            if (fact == null) continue;
            var nested = fact.BodyFacts ?? Array.Empty<ProcessGeometryFact>();
            if (nested.Count == 0) yield return fact;
            else foreach (var body in FlattenBodies(nested)) yield return body;
        }
    }
}
