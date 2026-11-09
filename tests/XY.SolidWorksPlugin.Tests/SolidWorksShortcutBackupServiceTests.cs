using System.Linq;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class SolidWorksShortcutBackupServiceTests
{
	[Fact]
	public void RegistryTextRoundTripKeepsOnlyShortcutEntries()
	{
		ShortcutRegistryData source = new ShortcutRegistryData
		{
			VersionName = "SOLIDWORKS 2025",
			CustomizationVersion = 300,
			Entries =
			{
				new ShortcutRegistryEntry { Plate = "tPlate0", Collection = "AddArray", LeafName = "23_49", Flags = 0x17, Key = 0x31, CommandId = 0xA2BF },
				new ShortcutRegistryEntry { Plate = "tPlate1", Collection = "AddArray", LeafName = "23_49", Flags = 0x17, Key = 0x31, CommandId = 0xA2BF },
				new ShortcutRegistryEntry { Plate = "tPlate0", Collection = "SubArray", LeafName = "3_46", Flags = 0x03, Key = 0x2E, CommandId = 0x8023 }
			}
		};

		string text = SolidWorksShortcutBackupService.BuildRegistryText(source);
		ShortcutRegistryData parsed = SolidWorksShortcutBackupService.ParseRegistryText(text);

		Assert.StartsWith("REGEDIT4", text);
		Assert.Contains(";SolidWorks Settings Wizard", text);
		Assert.DoesNotContain("System Options", text);
		Assert.Equal("SOLIDWORKS 2025", parsed.VersionName);
		Assert.Equal(300, parsed.CustomizationVersion);
		Assert.Equal(3, parsed.Entries.Count);
		Assert.Equal(2, parsed.Entries.Count(entry => entry.Collection == "AddArray"));
		Assert.Equal(0xA2BF, parsed.Entries.First(entry => entry.Plate == "tPlate1").CommandId);
	}

	[Theory]
	[InlineData(0x17, 0x31, "Alt+Shift+1")]
	[InlineData(0x0B, 0x41, "Ctrl+A")]
	[InlineData(0x1F, 0x70, "Ctrl+Alt+Shift+F1")]
	public void DecodeHotkeyMatchesSolidWorksAcceleratorFlags(int flags, int key, string expected)
	{
		Assert.Equal(expected, SolidWorksShortcutBackupService.DecodeHotkey(flags, key));
	}

	[Fact]
	public void SourceEntriesPresentIgnoresRepeatedDocumentPlates()
	{
		ShortcutRegistryData source = new ShortcutRegistryData
		{
			Entries =
			{
				new ShortcutRegistryEntry { Plate = "tPlate0", Collection = "AddArray", Flags = 0x17, Key = 0x31, CommandId = 100 },
				new ShortcutRegistryEntry { Plate = "tPlate1", Collection = "AddArray", Flags = 0x17, Key = 0x31, CommandId = 100 }
			}
		};
		ShortcutRegistryData target = new ShortcutRegistryData
		{
			Entries =
			{
				new ShortcutRegistryEntry { Plate = "tPlate2", Collection = "AddArray", Flags = 0x17, Key = 0x31, CommandId = 100 }
			}
		};

		Assert.True(SolidWorksShortcutBackupService.SourceEntriesPresent(source, target));
	}
}
