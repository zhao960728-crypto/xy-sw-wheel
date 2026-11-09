using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace XY.SolidWorksPlugin;

// These facts carry evidence only. They never supply editable hole walls or
// authorize model edits. Legacy metadata is validated before use.
internal sealed class ProcessHoleFact
{
    public string ProcessName { get; set; } = "";
    public string Source { get; set; } = "";
    public string Evidence { get; set; } = "";
    public bool IsConfirmed { get; set; }
    public string Reference { get; set; } = "";
}

internal sealed class ProcessHoleFactMessage
{
    public IModelDoc2 Document { get; set; }
    public string Configuration { get; set; } = "";
    public int UpdateStamp { get; set; }
    public int? PreviousStamp { get; set; }
    public ProcessHoleFact[] Facts { get; set; } = Array.Empty<ProcessHoleFact>();
    public string Source { get; set; } = "";
}

internal static class ProcessHoleFactBridge
{
    private sealed class State { internal readonly Dictionary<string, ProcessHoleFactMessage> Configurations = new Dictionary<string, ProcessHoleFactMessage>(StringComparer.Ordinal); }
    private static readonly ConditionalWeakTable<object, State> States = new ConditionalWeakTable<object, State>();
    internal static event Action<ProcessHoleFactMessage> Changed;

    internal static bool SameFacts(IEnumerable<ProcessHoleFact> a, IEnumerable<ProcessHoleFact> b)
        => string.Join("\n", (a ?? Array.Empty<ProcessHoleFact>()).Select(Key).OrderBy(x => x, StringComparer.Ordinal)) == string.Join("\n", (b ?? Array.Empty<ProcessHoleFact>()).Select(Key).OrderBy(x => x, StringComparer.Ordinal));
    private static string Key(ProcessHoleFact f) => f == null ? "" : string.Join("\u001f", f.ProcessName, f.Source, f.Evidence, f.IsConfirmed.ToString(), f.Reference);
    internal static bool TryGet(IModelDoc2 document, string configuration, int stamp, out ProcessHoleFactMessage message)
    {
        message = null;
        return document != null && States.TryGetValue(document, out var state) && state.Configurations.TryGetValue(configuration ?? "", out message) && message.UpdateStamp == stamp;
    }
    internal static void Publish(IModelDoc2 document, string configuration, int stamp, IEnumerable<ProcessHoleFact> facts, string source, int? previousStamp = null)
    {
        if (document == null) return;
        var state = States.GetOrCreateValue(document);
        var rows = (facts ?? Array.Empty<ProcessHoleFact>()).Where(f => f != null).GroupBy(Key).Select(g => g.First()).ToArray();
        if (state.Configurations.TryGetValue(configuration ?? "", out var old) && old.UpdateStamp == stamp && SameFacts(old.Facts, rows)) return;
        var message = new ProcessHoleFactMessage { Document = document, Configuration = configuration ?? "", UpdateStamp = stamp, PreviousStamp = previousStamp, Facts = rows, Source = source ?? "" };
        state.Configurations[message.Configuration] = message;
        // A receiving window must never turn a successful CAD transaction into
        // a failed transaction because its own refresh failed.
        foreach (Action<ProcessHoleFactMessage> handler in Changed?.GetInvocationList() ?? Array.Empty<Delegate>())
            try { handler(message); } catch (Exception ex) { Logger.Trace("Hole/process fact recipient failed: " + ex.Message); }
    }

    internal static ProcessHoleFact[] ReadMetadata(IModelDoc2 document, IEnumerable<ProcessFeatureFact> featureFacts = null)
    {
        var result = FromFeatures(featureFacts ?? Array.Empty<ProcessFeatureFact>()).ToList();
        var clock = Stopwatch.StartNew(); var seen = new HashSet<string>(StringComparer.Ordinal);
        var journals = new List<ProcessLegacyHoleJournal>(); bool complete = true;
        void Visit(IFeature feature, int depth)
        {
            if (depth > 12 || seen.Count >= 2000 || clock.ElapsedMilliseconds > 350) { complete = false; return; }
            string reference = Reference(document, feature);
            if (reference.Length == 0 || !seen.Add(reference) || !Active(feature)) return;
            string type = feature.GetTypeName2();
            if (featureFacts == null)
            {
                var fact = new ProcessFeatureFact { Name = feature.Name, TypeName = type };
                if ((type == "HoleWzd" || type == "WizardHole") && feature.GetDefinition() is IWizardHoleFeatureData2 hole) fact.HoleWizardType = hole.Type;
                result.AddRange(FromFeatures(new[] { fact }));
            }
            if (type == "Attribute" && feature.GetSpecificFeature2() is IAttribute attribute && attribute.GetName().StartsWith("_XY_HoleProcess_v1", StringComparison.Ordinal))
                journals.Add(ProcessLegacyHoleJournal.Parse(((IParameter)attribute.GetParameter("Data")).GetStringValue()));
            for (var child = feature.IGetFirstSubFeature(); child != null && complete; child = child.IGetNextSubFeature()) Visit(child, depth + 1);
        }
        try
        {
            for (var feature = document.IFirstFeature(); feature != null && complete; feature = feature.IGetNextFeature()) Visit(feature, 0);
            if (complete && journals.Count == 1)
                foreach (var record in journals[0].Records)
                {
                    string[] required = new[] { record.ProcessFeature, record.ResizeFeature, record.RepairFeature, record.FillFeature, record.RemovalFeature, record.MergeFeature }.Where(x => !string.IsNullOrEmpty(x)).ToArray();
                    // Bottom-hole records may have no process feature. A record
                    // with no native feature evidence is not a confirmed process.
                    if (required.Length == 0 || required.Any(r => !ValidFeature(document, r))) continue;
                    if (record.Spec.Kind == ProcessLegacyHoleKind.底孔) continue;
                    result.Add(new ProcessHoleFact { ProcessName = JournalProcessName(record.Spec.Kind), Source = "NativeJournal", IsConfirmed = true, Reference = record.Id,
                        Evidence = "当前配置原生工艺记录，引用特征存在且未抑制：" + record.Spec.Kind + " / " + record.Spec.Name });
                }
        }
        catch (Exception ex) { Logger.Trace("Hole process metadata unavailable: " + ex.Message); }
        return result.GroupBy(Key).Select(g => g.First()).ToArray();
    }

    internal static string JournalProcessName(ProcessLegacyHoleKind kind)
        => kind == ProcessLegacyHoleKind.压铆螺母 || kind == ProcessLegacyHoleKind.压铆螺柱 || kind == ProcessLegacyHoleKind.压铆螺钉 ? "压铆"
        : kind == ProcessLegacyHoleKind.焊铆螺母 || kind == ProcessLegacyHoleKind.焊铆螺柱 ? "焊铆"
        : kind.ToString();

    internal static ProcessHoleFact[] FromFeatures(IEnumerable<ProcessFeatureFact> features)
    {
        var rows = new List<ProcessHoleFact>();
        foreach (var f in features.Where(f => f != null && !f.IsSuppressed))
        {
            string type = (f.TypeName ?? "").ToLowerInvariant();
            string wizard = f.HoleWizardType.HasValue && Enum.IsDefined(typeof(swWzdHoleTypes_e), f.HoleWizardType.Value) ? ((swWzdHoleTypes_e)f.HoleWizardType.Value).ToString().ToLowerInvariant() : "";
            string process = ProcessRecognitionService.WizardHoleIsTapped(f.HoleWizardType) ? "攻牙" : wizard.Contains("counterbore") || wizard.Contains("countersink") || type == "counterbore" || type == "countersink" ? "沉孔" : "";
            if (process.Length > 0) rows.Add(new ProcessHoleFact { ProcessName = process, IsConfirmed = true, Source = "NativeFeature", Evidence = "当前配置原生特征类型/孔向导定义：" + f.TypeName + " / " + f.Name, Reference = f.Name ?? "" });
            else if (type.Contains("cosmeticthread") || type == "thread")
                rows.Add(new ProcessHoleFact { ProcessName = "螺纹加工", Source = "FeatureCandidate", IsConfirmed = false, Evidence = "原生螺纹特征，内外螺纹与加工方法待核实：" + f.Name, Reference = f.Name ?? "" });
            else if ((f.Name ?? "").Contains("压铆") || (f.Name ?? "").Contains("攻牙") || (f.Name ?? "").Contains("沉孔"))
                rows.Add(new ProcessHoleFact { ProcessName = (f.Name ?? "").Contains("压铆") ? "压铆" : (f.Name ?? "").Contains("攻牙") ? "攻牙" : "沉孔", Source = "FeatureCandidate", IsConfirmed = false, Evidence = "仅特征名称线索，待孔工艺核实：" + f.Name, Reference = f.Name ?? "" });
        }
        return rows.ToArray();
    }
    private static string Reference(IModelDoc2 document, object value)
    { var bytes = document.Extension.GetPersistReference3(value) as byte[]; return bytes == null ? "" : Convert.ToBase64String(bytes); }
    private static bool ValidFeature(IModelDoc2 document, string reference)
    {
        var value = document.Extension.GetObjectByPersistReference3(Convert.FromBase64String(reference), out int status) as IFeature;
        return status == 0 && value != null && Active(value) && value.GetErrorCode() == 0;
    }
    private static bool Active(IFeature feature)
    {
        var value = feature.IsSuppressed2((int)swInConfigurationOpts_e.swThisConfiguration, null) as Array;
        return value != null && value.Length > 0 && !value.Cast<object>().Any(Convert.ToBoolean);
    }
}
