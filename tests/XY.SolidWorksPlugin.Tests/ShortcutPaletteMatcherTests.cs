using System.Linq;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ShortcutPaletteMatcherTests
{
	[Fact]
	public void CtrlMatchesEveryShortcutContainingCtrl()
	{
		KeyboardShortcutRow[] rows = { new KeyboardShortcutRow { Hotkey = "Ctrl+C", Command = "Copy" }, new KeyboardShortcutRow { Hotkey = "Alt+C", Command = "Alternate" }, new KeyboardShortcutRow { Hotkey = "Shift+Ctrl+V", Command = "Paste" } };
		Assert.Equal(new[] { "Copy", "Paste" }, ShortcutPaletteMatcher.Match(rows, new[] { "Ctrl" }).Select(row => row.Command).ToArray());
	}

	[Fact]
	public void CombinationMatchesRegardlessOfModifierOrder()
	{
		KeyboardShortcutRow[] rows = { new KeyboardShortcutRow { Hotkey = "Shift+Ctrl+C", Command = "Copy" }, new KeyboardShortcutRow { Hotkey = "Ctrl+V", Command = "Paste" } };
		Assert.Equal("Copy", ShortcutPaletteMatcher.Match(rows, new[] { "Ctrl", "C" }).Single().Command);
	}

	[Fact]
	public void FunctionNameMatchesTheShortcutRowForDirectPaletteInput()
	{
		KeyboardShortcutRow[] rows =
		{
			new KeyboardShortcutRow { Hotkey = "Ctrl+E", Command = "边线法兰", Category = "SOLIDWORKS > 钣金" },
			new KeyboardShortcutRow { Hotkey = "Ctrl+F", Command = "圆角", Category = "SOLIDWORKS > 特征" }
		};

		Assert.Equal("Ctrl+E", ShortcutPaletteMatcher.MatchByName(rows, "边线法兰").Single().Hotkey);
		Assert.Equal("Ctrl+F", ShortcutPaletteMatcher.MatchByName(rows, "圆角").Single().Hotkey);
	}

	[Fact]
	public void FunctionNameSearchReturnsNoRowsForUnknownText()
	{
		var rows = new[] { new KeyboardShortcutRow { Hotkey = "Ctrl+E", Command = "边线法兰" } };
		Assert.Empty(ShortcutPaletteMatcher.MatchByName(rows, "不存在的功能"));
	}

	[Fact]
	public void FunctionNameSearchIncludesDirectFunctionWithoutConfiguredShortcut()
	{
		var row = new KeyboardShortcutRow { Command = "一键爆炸", ActionKind = ActionKinds.Basic, Value = "explode-all" };
		Assert.Same(row, ShortcutPaletteMatcher.MatchByName(new[] { row }, "一键爆炸").Single());
	}

	[Fact]
	public void ControlKeyProducesCtrlToken() => Assert.Equal(new[] { "Ctrl" }, ShortcutPaletteMatcher.TokensFromKeyData(Keys.ControlKey));
}
