using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

internal static class ComboAdvanceKeys
{
	public const string Default = "Ctrl";

	public static IReadOnlyList<string> Choices { get; } = BuildChoices();

	public static string Normalize(string value)
	{
		string text = (value ?? "").Trim();
		if (text.Equals("Control", StringComparison.OrdinalIgnoreCase)
			|| text.Equals("ControlKey", StringComparison.OrdinalIgnoreCase))
		{
			return "Ctrl";
		}
		if (text.Equals("Menu", StringComparison.OrdinalIgnoreCase))
		{
			return "Alt";
		}
		if (text.Equals("Escape", StringComparison.OrdinalIgnoreCase))
		{
			return "Esc";
		}
		string choice = Choices.FirstOrDefault(item => item.Equals(text, StringComparison.OrdinalIgnoreCase));
		if (choice != null)
		{
			return choice;
		}
		if (text.Length == 1 && char.IsLetterOrDigit(text[0]))
		{
			return text.ToUpperInvariant();
		}
		return Default;
	}

	public static bool TryGetVirtualKey(string value, out int virtualKey)
	{
		string key = Normalize(value);
		switch (key)
		{
		case "Ctrl": virtualKey = (int)Keys.ControlKey; return true;
		case "Shift": virtualKey = (int)Keys.ShiftKey; return true;
		case "Alt": virtualKey = (int)Keys.Menu; return true;
		case "Space": virtualKey = (int)Keys.Space; return true;
		case "Enter": virtualKey = (int)Keys.Enter; return true;
		case "Tab": virtualKey = (int)Keys.Tab; return true;
		case "Esc": virtualKey = (int)Keys.Escape; return true;
		}
		if (key.Length == 1 && char.IsLetterOrDigit(key[0]))
		{
			virtualKey = char.ToUpperInvariant(key[0]);
			return true;
		}
		if (key.Length > 1 && key[0] == 'F' && int.TryParse(key.Substring(1), out int function) && function >= 1 && function <= 12)
		{
			virtualKey = (int)Keys.F1 + function - 1;
			return true;
		}
		virtualKey = (int)Keys.ControlKey;
		return false;
	}

	public static string FromKeyCode(Keys keyCode)
	{
		switch (keyCode)
		{
		case Keys.ControlKey: return "Ctrl";
		case Keys.ShiftKey: return "Shift";
		case Keys.Menu: return "Alt";
		case Keys.Space: return "Space";
		case Keys.Enter: return "Enter";
		case Keys.Tab: return "Tab";
		case Keys.Escape: return "Esc";
		}
		if (keyCode >= Keys.F1 && keyCode <= Keys.F12)
		{
			return "F" + ((int)keyCode - (int)Keys.F1 + 1);
		}
		int code = (int)keyCode;
		if (code >= 'A' && code <= 'Z' || code >= '0' && code <= '9')
		{
			return ((char)code).ToString();
		}
		return Default;
	}

	private static IReadOnlyList<string> BuildChoices()
	{
		List<string> choices = new List<string>
		{
			"Ctrl", "Shift", "Alt", "Space", "Enter", "Tab", "Esc",
			"F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"
		};
		choices.AddRange(Enumerable.Range('A', 26).Select(code => ((char)code).ToString()));
		choices.AddRange(Enumerable.Range(0, 10).Select(number => number.ToString()));
		return choices;
	}
}
