using System;
using System.Collections.Generic;
using System.Linq;

namespace XY.SolidWorksPlugin;

// Session data belongs to an open document/configuration, not to its file name.
// Unsaved documents and Save As therefore cannot share or lose another draft.
internal sealed class DocumentRecognitionSessions<T> where T : class
{
    private readonly List<Entry> _entries = new List<Entry>();
    private sealed class Entry { public object Document; public string Configuration; public T Value; }
    internal IReadOnlyList<T> Values => _entries.Select(entry => entry.Value).ToArray();
    internal int Count => _entries.Count;
    internal void Put(object document, string configuration, T value)
    {
        if (document == null || value == null) throw new ArgumentNullException(document == null ? nameof(document) : nameof(value));
        _entries.RemoveAll(entry => Equals(entry.Document, document) && SameConfiguration(entry.Configuration, configuration));
        _entries.Add(new Entry { Document = document, Configuration = configuration ?? "", Value = value });
    }
    internal bool TryGet(object document, string configuration, out T value)
    {
        value = _entries.LastOrDefault(entry => Equals(entry.Document, document) && SameConfiguration(entry.Configuration, configuration))?.Value;
        return value != null;
    }
    internal IReadOnlyList<T> RetainOpenDocuments(IEnumerable<object> documents)
    {
        var open = (documents ?? Array.Empty<object>()).ToArray();
        var closed = _entries.Where(entry => !open.Any(document => Equals(entry.Document, document))).ToArray();
        foreach (var entry in closed) _entries.Remove(entry);
        return closed.Select(entry => entry.Value).ToArray();
    }
    internal void Remove(T value) => _entries.RemoveAll(entry => ReferenceEquals(entry.Value, value));
    internal void Clear() => _entries.Clear();
    private static bool SameConfiguration(string first, string second) => string.Equals(first ?? "", second ?? "", StringComparison.OrdinalIgnoreCase);
}
