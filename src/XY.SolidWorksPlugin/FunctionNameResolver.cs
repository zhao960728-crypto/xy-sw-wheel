using System;
using System.Collections.Generic;
using System.Linq;

namespace XY.SolidWorksPlugin;

// Resolves a user-entered function name to the same typed command identity
// used by the command catalog.  Matching a name must never invent a hotkey or
// change a command ID; an ambiguous name is deliberately rejected.
internal static class FunctionNameResolver
{
    internal static bool TryResolve(IEnumerable<KeyboardShortcutRow> rows, string input, out KeyboardShortcutRow result, out string message)
    {
        result = null;
        message = "";
        string text = (input ?? "").Trim();
        string key = Normalize(text);
        if (key.Length == 0) { message = "请输入功能名。"; return false; }

        var candidates = FindCandidates(rows, text);
        if (candidates.Count == 1) { result = candidates[0]; return true; }
        message = candidates.Count > 1 ? AmbiguousMessage(candidates) : "未找到功能“" + text + "”。请先读取 SW 命令或外部功能列表。";
        return false;
    }

    internal static IReadOnlyList<KeyboardShortcutRow> FindCandidates(IEnumerable<KeyboardShortcutRow> rows, string input)
    {
        string key = Normalize(input);
        if (key.Length == 0) return Array.Empty<KeyboardShortcutRow>();
        var candidates = (rows ?? Enumerable.Empty<KeyboardShortcutRow>()).Concat(BasicRows())
            .Where(ShortcutPaletteMatcher.IsNameCandidate).ToList();
        var exact = candidates.Where(row => NameKeys(row).Contains(key, StringComparer.OrdinalIgnoreCase)).ToList();
        string[] words = (input ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize).Where(word => word.Length > 0).ToArray();
        var matches = exact.Count > 0 ? exact : candidates.Where(row => words.All(word =>
            NameKeys(row).Any(name => name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
            || Normalize(row.Category).IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
        // Filter before folding aliases: a later alias can be the name entered
        // by the user, while both aliases retain one persisted command identity.
        return matches.GroupBy(ShortcutPaletteMatcher.CommandIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).OrderBy(row => row.Command, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Category, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    internal static IEnumerable<KeyboardShortcutRow> BasicRows()
    {
        foreach (KeyValuePair<string, string> action in BasicActions.All)
            yield return new KeyboardShortcutRow
            {
                Category = "SOLIDWORKS > 基础功能",
                Command = action.Value,
                Hotkey = "基础功能",
                ActionKind = ActionKinds.Basic,
                Value = action.Key
            };
    }

    private static IEnumerable<string> NameKeys(KeyboardShortcutRow row)
    {
        string command = (row.Command ?? "").Trim();
        string value = (row.Value ?? "").Trim();
        if (command.Length > 0) yield return Normalize(command);
        if (value.Length > 0 && !NativeCommandReference.TryDecodeId(value, out _)
            && !ExternalNamedCommandReference.TryDecode(value, out _, out _, out _)
            && !ExternalToolbarCommandReference.TryDecode(value, out _, out _)) yield return Normalize(value);
        if (string.Equals(row.ActionKind, ActionKinds.Basic, StringComparison.OrdinalIgnoreCase))
        {
            foreach (string display in BasicActions.SearchNames(value))
                if (display.Length > 0) yield return Normalize(display);
        }
    }

    private static string Normalize(string value)
        => new string((value ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static string AmbiguousMessage(IEnumerable<KeyboardShortcutRow> rows)
        => "功能名对应多个命令，请从命令列表中选择来源：" + string.Join("；", rows.Take(5).Select(row =>
            (row.Command ?? "").Trim() + "（" + ShortcutPaletteMatcher.SourceLabel(row) + "）"));
}
