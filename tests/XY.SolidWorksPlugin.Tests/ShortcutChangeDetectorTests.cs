using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ShortcutChangeDetectorTests
{
	[Fact]
	public void CompareReportsChangedConfiguredShortcut()
	{
		PluginConfig config = BuildConfig(new MenuItemConfig
		{
			Slot = 0,
			Label = "快速钣金..",
			Kind = ActionKinds.Hotkey,
			Value = "Ctrl+Alt+Semicolon"
		});
		KeyboardShortcutRow[] currentRows =
		{
			new KeyboardShortcutRow { Command = "快速钣金", Hotkey = "Alt+Shift+K", Category = "工具" }
		};

		ShortcutChangeResult result = ShortcutChangeDetector.Compare(config, currentRows).Single();

		Assert.Equal("快速钣金..", result.Command);
		Assert.Equal("Ctrl+Alt+Semicolon", result.ConfiguredHotkey);
		Assert.Equal("Alt+Shift+K", result.SolidWorksHotkey);
		Assert.Equal("SOLIDWORKS 快捷键已修改", result.Status);
	}

	[Fact]
	public void CompareChecksComboShortcutSteps()
	{
		PluginConfig config = BuildConfig(new MenuItemConfig
		{
			Slot = 1,
			Label = "组合",
			Kind = ActionKinds.Combo,
			Value = "组合",
			ComboCommands = new List<ComboCommandConfig>
			{
				new ComboCommandConfig { Label = "门板切口..", Kind = ActionKinds.Hotkey, Value = "Alt+U" }
			}
		});
		KeyboardShortcutRow[] currentRows =
		{
			new KeyboardShortcutRow { Command = "门板切口", Hotkey = "Ctrl+Alt+Shift+M", Category = "工具" }
		};

		ShortcutChangeResult result = ShortcutChangeDetector.Compare(config, currentRows).Single();

		Assert.Contains("组合第 1 步", result.Location);
		Assert.Equal("Ctrl+Alt+Shift+M", result.SolidWorksHotkey);
	}

	[Theory]
	[InlineData("快速钣金..", "快速钣金")]
	[InlineData("STEP 格式互换…", "STEP格式互换")]
	public void NormalizeCommandRemovesSolidWorksEllipsis(string input, string expected)
	{
		Assert.Equal(expected, ShortcutChangeDetector.NormalizeCommandForTest(input));
	}

	[Fact]
	public void SynchronizeUpdatesWheelAndComboHotkeysByCommand()
	{
		MenuItemConfig wheelItem = new MenuItemConfig
		{
			Slot = 0,
			Label = "快速钣金..",
			Kind = ActionKinds.Hotkey,
			Value = "Alt+Old"
		};
		MenuItemConfig comboItem = new MenuItemConfig
		{
			Slot = 1,
			Label = "组合",
			Kind = ActionKinds.Combo,
			Value = "组合",
			ComboCommands = new List<ComboCommandConfig>
			{
				new ComboCommandConfig { Label = "门板切口..", Kind = ActionKinds.Hotkey, Value = "Ctrl+Old" }
			}
		};
		PluginConfig config = BuildConfig(wheelItem, comboItem);
		KeyboardShortcutRow[] currentRows =
		{
			new KeyboardShortcutRow { Command = "快速钣金", Hotkey = "Alt+Shift+K" },
			new KeyboardShortcutRow { Command = "门板切口", Hotkey = "Alt+U" }
		};

		int updated = ShortcutChangeDetector.Synchronize(config, currentRows);

		Assert.Equal(2, updated);
		Assert.Equal("Alt+Shift+K", wheelItem.Value);
		Assert.Equal("Alt+U", comboItem.ComboCommands[0].Value);
	}

	[Fact]
	public void FindConflictsReportsSameHotkeyAssignedToDifferentCommands()
	{
		KeyboardShortcutRow[] currentRows =
		{
			new KeyboardShortcutRow { Command = "快速钣金", Hotkey = "Alt+K" },
			new KeyboardShortcutRow { Command = "门板切口", Hotkey = "Alt+K" },
			new KeyboardShortcutRow { Command = "重复行", Hotkey = "Alt+K" }
		};

		ShortcutConflictResult conflict = ShortcutChangeDetector.FindConflicts(currentRows).Single();

		Assert.Equal("Alt+K", conflict.Hotkey);
		Assert.Equal(3, conflict.Commands.Count);
	}

	private static PluginConfig BuildConfig(params MenuItemConfig[] items)
	{
		return new PluginConfig
		{
			Menus = new Dictionary<string, SceneMenuConfig>(StringComparer.OrdinalIgnoreCase)
			{
				[SceneKeys.Blank] = new SceneMenuConfig
				{
					Key = SceneKeys.Blank,
					Rings = new List<RingConfig>
					{
						new RingConfig
						{
							Index = 0,
							Slots = 8,
							Items = new List<MenuItemConfig>(items)
						}
					}
				}
			}
		};
	}
}
