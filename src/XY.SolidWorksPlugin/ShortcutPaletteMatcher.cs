using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

internal static class ShortcutPaletteMatcher
{
	public static IReadOnlyList<KeyboardShortcutRow> MatchByName(IEnumerable<KeyboardShortcutRow> rows, string name)
	{
		string keyword = NormalizeName(name);
		if (keyword.Length == 0) return Array.Empty<KeyboardShortcutRow>();
		List<KeyboardShortcutRow> usable = (rows ?? Array.Empty<KeyboardShortcutRow>())
			.Where(IsNameCandidate)
			.ToList();
		IReadOnlyList<KeyboardShortcutRow> exact = usable
			.Where(row => NormalizeName(row.Command) == keyword)
			.GroupBy(CommandIdentity, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
			.OrderBy(row => row.Command, StringComparer.CurrentCultureIgnoreCase)
			.ToList();
		if (exact.Count > 0) return exact;
		string[] words = (name ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Select(NormalizeName).Where(word => word.Length > 0).ToArray();
		return usable
			.Where(row => words.All(word => NormalizeName((row.Category ?? "") + row.Command).IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0))
			.GroupBy(CommandIdentity, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
			.OrderBy(row => row.Command, StringComparer.CurrentCultureIgnoreCase)
			.ThenBy(row => row.Category, StringComparer.CurrentCultureIgnoreCase)
			.ToList();
	}

	public static IReadOnlyList<KeyboardShortcutRow> Match(IEnumerable<KeyboardShortcutRow> rows, IEnumerable<string> pressedTokens)
	{
		HashSet<string> pressed = new HashSet<string>((pressedTokens ?? Array.Empty<string>()).Select(NormalizeToken).Where(token => token.Length > 0), StringComparer.OrdinalIgnoreCase);
		if (pressed.Count == 0) return Array.Empty<KeyboardShortcutRow>();
		return (rows ?? Array.Empty<KeyboardShortcutRow>()).Where(row => IsNameCandidate(row) && IsHotkey(row.Hotkey) && TokenSet(row.Hotkey).IsSupersetOf(pressed))
			.GroupBy(CommandIdentity, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
			.OrderBy(row => TokenSet(row.Hotkey).Count - pressed.Count).ThenBy(row => row.Hotkey, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.Command, StringComparer.CurrentCultureIgnoreCase).ToList();
	}

	public static IReadOnlyList<string> TokensFromKeyData(Keys keyData)
	{
		List<string> tokens = new List<string>();
		if ((keyData & Keys.Control) == Keys.Control) tokens.Add("Ctrl");
		if ((keyData & Keys.Alt) == Keys.Alt) tokens.Add("Alt");
		if ((keyData & Keys.Shift) == Keys.Shift) tokens.Add("Shift");
		Keys key = keyData & Keys.KeyCode;
		string token = KeyToken(key);
		if (token.Length > 0 && !tokens.Contains(token, StringComparer.OrdinalIgnoreCase)) tokens.Add(token);
		return tokens;
	}

	private static HashSet<string> TokenSet(string hotkey) => new HashSet<string>((hotkey ?? "").Split('+').Select(NormalizeToken).Where(token => token.Length > 0), StringComparer.OrdinalIgnoreCase);

	private static string KeyToken(Keys key)
	{
		if (key >= Keys.D0 && key <= Keys.D9) return ((int)key - (int)Keys.D0).ToString();
		switch (key)
		{
			case Keys.ControlKey: case Keys.LControlKey: case Keys.RControlKey: return "Ctrl";
			case Keys.Menu: case Keys.LMenu: case Keys.RMenu: return "Alt";
			case Keys.ShiftKey: case Keys.LShiftKey: case Keys.RShiftKey: return "Shift";
			case Keys.None: return "";
			default: return key.ToString();
		}
	}

	private static string NormalizeToken(string value)
	{
		string token = (value ?? "").Trim();
		if (token.Length == 2 && (token[0] == 'D' || token[0] == 'd') && char.IsDigit(token[1])) return token.Substring(1);
		if (token.Equals("Esc", StringComparison.OrdinalIgnoreCase)) return "Escape";
		if (token.Equals("Del", StringComparison.OrdinalIgnoreCase)) return "Delete";
		if (token.Equals("PgUp", StringComparison.OrdinalIgnoreCase)) return "PageUp";
		if (token.Equals("PgDn", StringComparison.OrdinalIgnoreCase)) return "PageDown";
		if (token.Equals("Control", StringComparison.OrdinalIgnoreCase)) return "Ctrl";
		if (token.Equals("Menu", StringComparison.OrdinalIgnoreCase)) return "Alt";
		return token;
	}

	private static string NormalizeName(string value)
		=> new string((value ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

	internal static bool IsNameCandidate(KeyboardShortcutRow row) => TryGetAction(row, out _, out _);

	internal static bool TryGetAction(KeyboardShortcutRow row, out string kind, out string value)
	{
		kind = (row?.ActionKind ?? ActionKinds.Hotkey).Trim(); value = (row?.Value ?? "").Trim();
		if (row == null) return false;
		if (kind.Equals(ActionKinds.Hotkey, StringComparison.OrdinalIgnoreCase))
		{ value = (row.Hotkey ?? "").Trim(); return IsHotkey(value); }
		if (kind.Equals(ActionKinds.Basic, StringComparison.OrdinalIgnoreCase)) return BasicActions.Contains(value);
		if (kind.Equals(ActionKinds.NativeCommand, StringComparison.OrdinalIgnoreCase))
			return NativeCommandReference.TryDecodeId(value, out _) || NativeCommandReference.TryDecode(value, out _, out _) || ExternalToolbarCommandReference.TryDecode(value, out _, out _);
		if (kind.Equals(ActionKinds.ExternalPlugin, StringComparison.OrdinalIgnoreCase))
			return ExternalToolbarCommandReference.TryDecode(value, out _, out _) || ExternalNamedCommandReference.TryDecode(value, out _, out _, out _) || ExternalPluginActionValue.TryParse(value, out _, out _);
		if (kind.Equals(ActionKinds.QinJian, StringComparison.OrdinalIgnoreCase)) return QinJianActionValue.TryParse(value, out _, out _);
		if (kind.Equals(ActionKinds.JiaLiChuang, StringComparison.OrdinalIgnoreCase)) return value.Length > 0;
		if (kind.Equals(ActionKinds.UiClick, StringComparison.OrdinalIgnoreCase)) return value.Length > 0;
		return false;
	}

	internal static string CommandIdentity(KeyboardShortcutRow row)
	{
		if (!TryGetAction(row, out string kind, out string value)) return "";
		// An assigned shortcut can address different commands in different
		// environments; retain its source and command when no native ID exists.
		return kind.Equals(ActionKinds.Hotkey, StringComparison.OrdinalIgnoreCase)
			? kind + "\u001f" + value + "\u001f" + row.Category + "\u001f" + row.Command
			: kind + "\u001f" + value;
	}

	internal static bool IsHotkey(string text)
	{
		var parts = (text ?? "").Split('+').Select(part => part.Trim()).Where(part => part.Length > 0).ToList();
		var keys = parts.Where(part => !new[] { "Ctrl", "Control", "Shift", "Alt", "Menu", "Win", "Windows" }.Contains(part, StringComparer.OrdinalIgnoreCase)).ToList();
		if (keys.Count != 1 || keys[0].Length == 1 && keys[0][0] > 127) return false;
		try { KeyboardSender.ResolveVirtualKeyForTest(keys[0]); return true; }
		catch (InvalidOperationException) { return false; }
	}

	internal static string SourceLabel(KeyboardShortcutRow row)
	{
		string category = (row?.Category ?? "").Trim();
		string kind = row?.ActionKind ?? ActionKinds.Hotkey;
		string route = kind == ActionKinds.NativeCommand ? "原生命令" : kind == ActionKinds.Basic ? "基础功能"
			: kind == ActionKinds.Hotkey ? "快捷键" : "外部功能";
		if (kind == ActionKinds.NativeCommand && NativeCommandReference.TryDecodeId(row.Value, out int nativeId)) route += " #" + nativeId;
		else if (ExternalToolbarCommandReference.TryDecode(row?.Value, out _, out int toolbarId)) route += " #" + toolbarId;
		return category.Length > 0 ? route + " · " + category : route;
	}
}
