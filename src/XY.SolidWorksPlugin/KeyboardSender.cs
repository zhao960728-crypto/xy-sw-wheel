using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace XY.SolidWorksPlugin;

internal static class KeyboardSender
{
	private const int VkShift = 0x10;
	private const int VkControl = 0x11;
	private const int VkMenu = 0x12;
	private const int VkLWin = 0x5B;
	private const int VkRWin = 0x5C;
	private const uint KeyEventFExtendedKey = 0x0001;
	private const uint KeyEventFKeyUp = 0x0002;
	internal static readonly int InputSize = Marshal.SizeOf(typeof(Input));

	public static void Send(string hotkey)
		=> Send(hotkey, (events, size) => SendInput((uint)events.Length, events, size),
			key => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0, VkKeyScan);

	// Test callers inject both native boundaries; no real key event is needed.
	internal static void Send(string hotkey, Func<Input[], int, uint> send, Func<int, bool> pressed, Func<char, short> scan = null)
	{
		if (send == null || pressed == null) throw new ArgumentNullException(send == null ? nameof(send) : nameof(pressed));
		if (InputSize != (IntPtr.Size == 8 ? 40 : 28)) throw new InvalidOperationException("Win32 INPUT layout is invalid; no keys were sent.");
		List<string> parts = (hotkey ?? "")
			.Split('+')
			.Select((string part) => part.Trim())
			.Where((string part) => part.Length > 0)
			.ToList();
		if (parts.Count == 0)
		{
			throw new InvalidOperationException("Hotkey is empty.");
		}
		List<byte> modifiers = new List<byte>();
		string keyToken = "";
		foreach (string part in parts)
		{
			if (TryResolveModifier(part, out byte modifier))
			{
				if (!modifiers.Contains(modifier))
				{
					modifiers.Add(modifier);
				}
			}
			else
			{
				keyToken = part;
			}
		}
		if (string.IsNullOrWhiteSpace(keyToken))
		{
			throw new InvalidOperationException("Hotkey has no key: " + hotkey);
		}
		byte key = ResolveKey(keyToken, modifiers, scan);
		if (key == 0 || key == byte.MaxValue) throw new InvalidOperationException("Unsupported hotkey key: " + keyToken);
		// Ownership is decided before injection. GetAsyncKeyState afterwards also
		// sees our injected downs and cannot distinguish them from physical keys.
		var owned = modifiers.Where(modifier => !pressed(modifier)
			&& (modifier != VkLWin || !pressed(VkRWin))).ToArray();
		var events = new List<Input>();
		foreach (byte modifier in owned) events.Add(Key(modifier, false));
		events.Add(Key(key, false)); events.Add(Key(key, true));
		for (int index = owned.Length - 1; index >= 0; index--) events.Add(Key(owned[index], true));
		Input[] batch = events.ToArray();
		uint inserted;
		try { inserted = send(batch, InputSize); }
		catch (Exception error)
		{
			// Without a returned count there is no confirmed injected prefix.
			// Never guess by replaying the shortcut or releasing somebody's key.
			throw new InvalidOperationException("快捷键发送结果无法确认，未重新发送。", error);
		}
		if (inserted == batch.Length) return;
		if (inserted > batch.Length) throw new InvalidOperationException("快捷键发送返回了无效数量，未重新发送。");
		var held = new List<Input>();
		for (int index = 0; index < inserted; index++)
		{
			Input input = batch[index];
			if ((input.Data.Keyboard.Flags & KeyEventFKeyUp) == 0) held.Add(input);
			else held.RemoveAll(prior => prior.Data.Keyboard.VirtualKey == input.Data.Keyboard.VirtualKey);
		}
		var releases = held.AsEnumerable().Reverse().Select(input => Key((byte)input.Data.Keyboard.VirtualKey, true)).ToArray();
		string cleanup = "";
		if (releases.Length > 0)
		{
			try
			{
				uint released = send(releases, InputSize);
				if (released != releases.Length) cleanup = "；释放清理未完成（" + released + "/" + releases.Length + "）";
			}
			catch (Exception error) { cleanup = "；释放清理失败：" + error.Message; }
		}
		throw new InvalidOperationException("快捷键未完整发送（" + inserted + "/" + batch.Length + "），未重放组合" + cleanup + "。");
	}

	internal static byte ResolveVirtualKeyForTest(string token)
	{
		return ResolveKey(token, new List<byte>());
	}

	private static bool TryResolveModifier(string token, out byte virtualKey)
	{
		switch (Normalize(token))
		{
		case "CTRL":
		case "CONTROL":
			virtualKey = VkControl;
			return true;
		case "SHIFT":
			virtualKey = VkShift;
			return true;
		case "ALT":
		case "MENU":
			virtualKey = VkMenu;
			return true;
		case "WIN":
		case "WINDOWS":
			virtualKey = VkLWin;
			return true;
		default:
			virtualKey = 0;
			return false;
		}
	}

	private static byte ResolveKey(string token, List<byte> modifiers, Func<char, short> scanKey = null)
	{
		string trimmed = (token ?? "").Trim();
		string key = Normalize(token);
		string wordKey = key.Replace("-", "").Replace("_", "");
		if (key.Length == 1)
		{
			char ch = key[0];
			if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'))
			{
				return (byte)ch;
			}
			short scan = (scanKey ?? VkKeyScan)(trimmed.Length > 0 ? trimmed[0] : key[0]);
			if (scan != -1)
			{
				AddImpliedModifiers((scan >> 8) & 0xFF, modifiers);
				return (byte)(scan & 0xFF);
			}
			return (byte)key[0];
		}
		if (wordKey.StartsWith("F", StringComparison.OrdinalIgnoreCase) && int.TryParse(wordKey.Substring(1), out int functionNumber) && functionNumber >= 1 && functionNumber <= 24)
		{
			return (byte)(0x70 + functionNumber - 1);
		}
		switch (key)
		{
		case "NUM*":
		case "NUMPAD*":
			return 0x6A;
		case "NUM+":
		case "NUMPAD+":
			return 0x6B;
		case "NUM-":
		case "NUMPAD-":
			return 0x6D;
		case "NUM/":
		case "NUMPAD/":
			return 0x6F;
		case "NUM.":
		case "NUMPAD.":
			return 0x6E;
		}
		switch (wordKey)
		{
		case "ESC":
		case "ESCAPE":
			return 0x1B;
		case "ENTER":
		case "RETURN":
			return 0x0D;
		case "TAB":
			return 0x09;
		case "SPACE":
			return 0x20;
		case "DEL":
		case "DELETE":
			return 0x2E;
		case "BACKSPACE":
		case "BKSP":
			return 0x08;
		case "INSERT":
		case "INS":
			return 0x2D;
		case "HOME":
			return 0x24;
		case "END":
			return 0x23;
		case "PAGEUP":
		case "PGUP":
			return 0x21;
		case "PAGEDOWN":
		case "PGDN":
			return 0x22;
		case "UP":
		case "UPARROW":
			return 0x26;
		case "DOWN":
		case "DOWNARROW":
			return 0x28;
		case "LEFT":
		case "LEFTARROW":
			return 0x25;
		case "RIGHT":
		case "RIGHTARROW":
			return 0x27;
		case "SEMICOLON":
			return 0xBA;
		case "COLON":
			AddModifier(modifiers, VkShift);
			return 0xBA;
		case "EQUAL":
		case "EQUALS":
			return 0xBB;
		case "PLUS":
			AddModifier(modifiers, VkShift);
			return 0xBB;
		case "COMMA":
			return 0xBC;
		case "LESS":
		case "LESSTHAN":
			AddModifier(modifiers, VkShift);
			return 0xBC;
		case "MINUS":
		case "DASH":
		case "HYPHEN":
			return 0xBD;
		case "UNDERSCORE":
			AddModifier(modifiers, VkShift);
			return 0xBD;
		case "PERIOD":
		case "DOT":
			return 0xBE;
		case "GREATER":
		case "GREATERTHAN":
			AddModifier(modifiers, VkShift);
			return 0xBE;
		case "SLASH":
		case "FORWARDSLASH":
			return 0xBF;
		case "QUESTION":
		case "QUESTIONMARK":
			AddModifier(modifiers, VkShift);
			return 0xBF;
		case "GRAVE":
		case "BACKQUOTE":
			return 0xC0;
		case "TILDE":
			AddModifier(modifiers, VkShift);
			return 0xC0;
		case "LEFTBRACKET":
		case "OPENBRACKET":
		case "LBRACKET":
			return 0xDB;
		case "LEFTBRACE":
		case "OPENBRACE":
		case "LBRACE":
			AddModifier(modifiers, VkShift);
			return 0xDB;
		case "BACKSLASH":
			return 0xDC;
		case "PIPE":
			AddModifier(modifiers, VkShift);
			return 0xDC;
		case "RIGHTBRACKET":
		case "CLOSEBRACKET":
		case "RBRACKET":
			return 0xDD;
		case "RIGHTBRACE":
		case "CLOSEBRACE":
		case "RBRACE":
			AddModifier(modifiers, VkShift);
			return 0xDD;
		case "APOSTROPHE":
		case "SINGLEQUOTE":
		case "QUOTE":
			return 0xDE;
		case "DOUBLEQUOTE":
		case "QUOTATIONMARK":
			AddModifier(modifiers, VkShift);
			return 0xDE;
		case "NUMMULTIPLY":
		case "NUMPADMULTIPLY":
		case "NUMASTERISK":
		case "NUMPADASTERISK":
			return 0x6A;
		case "NUMPLUS":
		case "NUMADD":
		case "NUMPADPLUS":
		case "NUMPADADD":
			return 0x6B;
		case "NUMMINUS":
		case "NUMSUBTRACT":
		case "NUMPADMINUS":
		case "NUMPADSUBTRACT":
			return 0x6D;
		case "NUMSLASH":
		case "NUMDIVIDE":
		case "NUMPADSLASH":
		case "NUMPADDIVIDE":
			return 0x6F;
		case "NUMDECIMAL":
		case "NUMPADDECIMAL":
			return 0x6E;
		}
		if (wordKey.StartsWith("NUMPAD", StringComparison.OrdinalIgnoreCase) && int.TryParse(wordKey.Substring(6), out int numpadDigit) && numpadDigit >= 0 && numpadDigit <= 9)
		{
			return (byte)(0x60 + numpadDigit);
		}
		if (wordKey.StartsWith("NUM", StringComparison.OrdinalIgnoreCase) && int.TryParse(wordKey.Substring(3), out int numDigit) && numDigit >= 0 && numDigit <= 9)
		{
			return (byte)(0x60 + numDigit);
		}
		throw new InvalidOperationException("Unsupported hotkey key: " + token);
	}

	private static void AddImpliedModifiers(int state, List<byte> modifiers)
	{
		if ((state & 1) != 0 && !modifiers.Contains(VkShift))
		{
			modifiers.Add(VkShift);
		}
		if ((state & 2) != 0 && !modifiers.Contains(VkControl))
		{
			modifiers.Add(VkControl);
		}
		if ((state & 4) != 0 && !modifiers.Contains(VkMenu))
		{
			modifiers.Add(VkMenu);
		}
	}

	private static void AddModifier(List<byte> modifiers, int virtualKey)
	{
		byte modifier = (byte)virtualKey;
		if (!modifiers.Contains(modifier))
		{
			modifiers.Add(modifier);
		}
	}

	private static string Normalize(string value)
	{
		return (value ?? "").Trim().Replace(" ", "").ToUpperInvariant();
	}

	private static Input Key(byte virtualKey, bool up) => new Input
	{
		Type = 1,
		Data = new InputUnion { Keyboard = new KeyboardInput
		{
			VirtualKey = virtualKey,
			Flags = (up ? KeyEventFKeyUp : 0) | (Extended(virtualKey) ? KeyEventFExtendedKey : 0)
		} }
	};
	private static bool Extended(byte key) => key >= 0x21 && key <= 0x28 || key == 0x2D || key == 0x2E
		|| key == 0x6F || key == VkLWin || key == VkRWin;

	[StructLayout(LayoutKind.Sequential)]
	internal struct Input { internal uint Type; internal InputUnion Data; }
	[StructLayout(LayoutKind.Explicit)]
	internal struct InputUnion
	{
		[FieldOffset(0)] internal MouseInput Mouse;
		[FieldOffset(0)] internal KeyboardInput Keyboard;
		[FieldOffset(0)] internal HardwareInput Hardware;
	}
	[StructLayout(LayoutKind.Sequential)]
	internal struct MouseInput
	{
		internal int X, Y;
		internal uint MouseData, Flags, Time;
		internal UIntPtr ExtraInfo;
	}
	[StructLayout(LayoutKind.Sequential)]
	internal struct KeyboardInput
	{
		internal ushort VirtualKey, ScanCode;
		internal uint Flags, Time;
		internal UIntPtr ExtraInfo;
	}
	[StructLayout(LayoutKind.Sequential)]
	internal struct HardwareInput { internal uint Message; internal ushort Low, High; }

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint SendInput(uint count, [In] Input[] events, int size);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern short VkKeyScan(char ch);
}
