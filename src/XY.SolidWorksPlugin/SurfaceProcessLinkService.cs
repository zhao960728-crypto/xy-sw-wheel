using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;

namespace XY.SolidWorksPlugin;

// Session-only provenance. No hidden model properties, global write hooks,
// geometry reads, configuration activation or document saves.
internal sealed class SurfaceProcessLinkState
{
    internal string Sequence, Surface;
    internal int Index = -1;
}

internal static class SurfaceProcessLinkPolicy
{
    internal static string MergeDraft(string draft, string previous, string updated, string separator)
    {
        string pattern = "(" + string.Join("|", new[] { string.IsNullOrEmpty(separator) ? "-" : separator, ">", "→" }.Distinct().Select(Regex.Escape)) + ")";
        var before = Regex.Split(previous ?? "", pattern); var after = Regex.Split(updated ?? "", pattern);
        if (before.Length != after.Length) return draft;
        var changed = Enumerable.Range(0, before.Length).Where(i => before[i] != after[i]).ToArray();
        if (changed.Length != 1 || changed[0] % 2 != 0) return draft;
        var tokens = Regex.Split(draft ?? "", pattern);
        var matching = Enumerable.Range(0, tokens.Length).Where(i => i % 2 == 0 && tokens[i].Trim() == before[changed[0]].Trim()).ToArray();
        if (matching.Length != 1) return draft; // User replaced/duplicated the slot: retain their choice.
        string original = tokens[matching[0]];
        tokens[matching[0]] = original.Substring(0, original.Length - original.TrimStart().Length) + after[changed[0]].Trim() + original.Substring(original.TrimEnd().Length);
        return string.Concat(tokens);
    }
    internal static bool Plan(string sequence, string surface, string separator, SurfaceProcessLinkState state, out string next, out int index, out string reason)
    {
        next = sequence ?? ""; index = -1; reason = "";
        separator = string.IsNullOrEmpty(separator) ? "-" : separator;
        surface = (surface ?? "").Trim();
        if (surface.Length == 0 || surface == "表面处理" || new[] { separator, ">", "→", "\r", "\n" }.Any(surface.Contains)) return false;
        var tokens = Regex.Split(next, "(" + string.Join("|", new[] { separator, ">", "→" }.Distinct().Select(Regex.Escape)) + ")");
        if (state.Sequence != null)
        {
            if (state.Sequence != next) { reason = "工序已有人工修改，表面处理自动联动已暂停，原工序保留。"; return false; }
            index = state.Index;
            if (index < 0 || index >= tokens.Length || tokens[index].Trim() != state.Surface) return false;
        }
        else
        {
            int[] matches = Enumerable.Range(0, tokens.Length).Where(i => i % 2 == 0 && tokens[i].Trim() == "表面处理").ToArray();
            if (matches.Length != 1) { if (matches.Length > 1) reason = "存在多道表面处理，无法唯一定位，未自动替换。"; return false; }
            index = matches[0];
        }
        string original = tokens[index];
        tokens[index] = original.Substring(0, original.Length - original.TrimStart().Length) + surface + original.Substring(original.TrimEnd().Length);
        next = string.Concat(tokens);
        return next != sequence;
    }
    internal static void Commit(SurfaceProcessLinkState state, string sequence, string surface, int index)
    { state.Sequence = sequence; state.Surface = surface.Trim(); state.Index = index; }
}

internal sealed class SurfaceProcessLinkService
{
    internal sealed class Result
    {
        internal IModelDoc2 Document;
        internal string Configuration, Surface = "", PreviousSequence, Sequence, Status = "";
        internal ExistingProcessEditTarget Source;
        internal bool Changed;
    }
    private readonly ConditionalWeakTable<IModelDoc2, Dictionary<string, SurfaceProcessLinkState>> _states = new ConditionalWeakTable<IModelDoc2, Dictionary<string, SurfaceProcessLinkState>>();

    internal Result Synchronize(IModelDoc2 document, string configuration, ProcessRecognitionConfig config)
    {
        var result = new Result { Document = document, Configuration = configuration ?? "" };
        if (document == null || config == null || !config.Enabled || document.GetType() != 1 || document.IsOpenedReadOnly() || document.GetActiveSketch2() != null) return result;
        var surface = ExistingProcessEditTarget.Effective(document, configuration, config.SurfacePropertyName);
        result.Surface = surface?.Snapshot.Resolved ?? "";
        var process = ExistingProcessEditTarget.Effective(document, configuration, config.SummaryPropertyName);
        if (process == null || surface == null) return result;
        if (process.Configuration.Length == 0 && surface.Configuration.Length > 0)
        { result.Status = "表面处理属于单独配置，工序属于共享文档，未跨配置修改共享工序。"; return result; }
        if (process.Snapshot.Type != 30 || process.Snapshot.Raw != process.Snapshot.Resolved)
        { result.Status = "工序由表达式或非文本属性提供，保留原属性，未自动替换。"; return result; }
        var states = _states.GetOrCreateValue(document);
        string key = process.Configuration + "\0" + process.PropertyName + "\0" + surface.Configuration + "\0" + surface.PropertyName;
        if (!states.TryGetValue(key, out var state)) states[key] = state = new SurfaceProcessLinkState();
        var processStore = new PropertyWriteBatch.ModelStore(document, process.Configuration);
        var surfaceStore = new PropertyWriteBatch.ModelStore(document, surface.Configuration);
        result.PreviousSequence = process.Snapshot.Raw;
        result.Changed = Apply(processStore, process.PropertyName, process.Snapshot, surfaceStore, surface.PropertyName, surface.Snapshot, config.Separator, state, out string sequence, out string status);
        result.Sequence = sequence; result.Status = status;
        if (result.Changed) result.Source = ExistingProcessEditTarget.Read(document, process.Configuration, process.PropertyName);
        return result;
    }

    internal static bool Apply(PropertyWriteBatch.Store processStore, string processName, PropertyWriteBatch.Snapshot process, PropertyWriteBatch.Store surfaceStore, string surfaceName, PropertyWriteBatch.Snapshot surface, string separator, SurfaceProcessLinkState state, out string sequence, out string status)
    {
        sequence = process.Raw; status = "";
        if (!process.Exists || process.Type != 30 || process.Raw != process.Resolved || !surface.Exists) return false;
        if (!SurfaceProcessLinkPolicy.Plan(process.Raw, surface.Resolved, separator, state, out sequence, out int index, out status)) return false;
        ExistingProcessEditTarget.Verify(processStore, processName, process);
        ExistingProcessEditTarget.Verify(surfaceStore, surfaceName, surface);
        PropertyWriteBatch.Apply(processStore, new[] { new KeyValuePair<string, string>(processName, sequence) });
        SurfaceProcessLinkPolicy.Commit(state, sequence, surface.Resolved, index);
        status = "已按表面处理属性同步该道工序；其他工序保持原顺序，请保存模型。";
        return true;
    }
}
