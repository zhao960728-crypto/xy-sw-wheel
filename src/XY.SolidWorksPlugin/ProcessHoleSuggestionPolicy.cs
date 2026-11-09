using System;
using System.Collections.Generic;
using System.Linq;

namespace XY.SolidWorksPlugin;

internal static class ProcessHoleSuggestionPolicy
{
    internal static void MarkGeometryPending(ProcessGeometryFact geometry)
    {
        if (geometry == null) return;
        geometry.GeometryComplete = false;
        foreach (var body in geometry.BodyFacts ?? Array.Empty<ProcessGeometryFact>()) MarkGeometryPending(body);
    }
    internal static IReadOnlyList<RecognizedProcess> MergeMissing(IEnumerable<RecognizedProcess> current, IEnumerable<ProcessHoleFact> facts, ProcessRecognitionConfig config)
    {
        var result = (current ?? Array.Empty<RecognizedProcess>()).Select(row => new RecognizedProcess { Name = row.Name, Confidence = row.Confidence, Evidence = row.Evidence }).ToList();
        var mappings = ProcessRecognitionConfig.ParseProcessNameMappings(config?.ProcessNameMappings);
        string Display(string name) => mappings.FirstOrDefault(pair => string.Equals(pair.Key, Canonical(name), StringComparison.OrdinalIgnoreCase)).Value ?? Canonical(name);
        var anchors = new[] { "折弯", "段差折弯", "压死边", "卷圆", "焊接", "组焊", "检验", "包装", "出货" }.Select(Display).ToArray();
        foreach (var fact in (facts ?? Array.Empty<ProcessHoleFact>()).Where(f => f != null && f.IsConfirmed && !string.IsNullOrWhiteSpace(f.ProcessName)))
        {
            string name = Display(fact.ProcessName);
            if (result.Any(row => string.Equals(Canonical(row.Name), Canonical(name), StringComparison.OrdinalIgnoreCase))) continue;
            int index = result.FindIndex(row => anchors.Any(anchor => string.Equals(row.Name, anchor, StringComparison.OrdinalIgnoreCase)));
            if (index < 0) index = result.Count;
            result.Insert(index, new RecognizedProcess { Name = name, Confidence = 99, Evidence = "孔工艺已核实事实：" + fact.Evidence });
        }
        return result;
    }
    private static string Canonical(string name)
    {
        string value = (name ?? "").Trim();
        if (value.Equals("攻牙", StringComparison.OrdinalIgnoreCase)) return "攻丝";
        if (value.StartsWith("压铆", StringComparison.Ordinal)) return "压铆";
        if (value.StartsWith("焊铆", StringComparison.Ordinal)) return "焊铆";
        if (value.StartsWith("压钉", StringComparison.Ordinal)) return "压钉";
        return value;
    }
}
