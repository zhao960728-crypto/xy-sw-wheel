using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace XY.SolidWorksPlugin;

internal sealed class ExistingProcessEditTarget
{
    internal ExistingProcessEditTarget(string configuration, string propertyName, PropertyWriteBatch.Snapshot snapshot)
    { Configuration = configuration ?? ""; PropertyName = propertyName; Snapshot = snapshot; }
    internal string Configuration { get; private set; }
    internal string PropertyName { get; private set; }
    internal PropertyWriteBatch.Snapshot Snapshot { get; private set; }
    internal string ScopeLabel => string.IsNullOrEmpty(Configuration) ? "文档自定义属性" : "配置“" + Configuration + "”";
    internal static ExistingProcessEditTarget Read(IModelDoc2 document, string configuration, string propertyName)
    {
        var manager = document.Extension.get_CustomPropertyManager(configuration ?? "");
        bool exists = (manager.GetNames() as object[] ?? Array.Empty<object>()).Select(Convert.ToString).Contains(propertyName, StringComparer.OrdinalIgnoreCase);
        if (!exists) return null;
        manager.Get4(propertyName, false, out string raw, out string resolved);
        return new ExistingProcessEditTarget(configuration, propertyName,
            new PropertyWriteBatch.Snapshot { Exists = true, Type = manager.GetType2(propertyName), Raw = raw ?? "", Resolved = resolved ?? raw ?? "" });
    }
    internal static ExistingProcessEditTarget Effective(IModelDoc2 document, string configuration, string propertyName)
    {
        var configured = Read(document, configuration, propertyName);
        if (configured != null) return configured;
        var general = Read(document, "", propertyName);
        return general;
    }
    internal static void Verify(PropertyWriteBatch.Store store, string propertyName, PropertyWriteBatch.Snapshot expected)
    {
        var actual = store.Read(propertyName);
        if (expected == null || actual.Exists != expected.Exists || actual.Type != expected.Type || actual.Raw != expected.Raw || actual.Resolved != expected.Resolved)
            throw new InvalidOperationException("模型中的工序在显示后已变化，未覆盖；当前编辑草稿保留。");
    }
    internal int Apply(PropertyWriteBatch.Store store, string sequence, IEnumerable<KeyValuePair<string,string>> explicitProperties = null)
    {
        Verify(store, PropertyName, Snapshot);
        var values = new List<KeyValuePair<string,string>>();
        if (!string.Equals(sequence ?? "", Snapshot.Resolved, StringComparison.Ordinal)) values.Add(new KeyValuePair<string,string>(PropertyName, sequence ?? ""));
        values.AddRange((explicitProperties ?? Enumerable.Empty<KeyValuePair<string,string>>()).Where(pair => !string.Equals(pair.Key, PropertyName, StringComparison.OrdinalIgnoreCase)));
        return PropertyWriteBatch.Apply(store, values);
    }
    internal IReadOnlyList<RecognizedProcess> Rows(string separator) => (Snapshot.Resolved ?? Snapshot.Raw ?? "").Split(new[] { string.IsNullOrEmpty(separator) ? "-" : separator, ">", "→" }, StringSplitOptions.RemoveEmptyEntries)
        .Select(name => new RecognizedProcess { Name = name.Trim(), Confidence = 100, Evidence = "模型已写工序：" + ScopeLabel }).ToList();
}
