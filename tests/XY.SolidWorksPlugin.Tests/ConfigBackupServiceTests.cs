using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ConfigBackupServiceTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "xy-radial-backup-tests-" + Guid.NewGuid().ToString("N"));

	[Fact]
	public void ExportThenImportPackageRestoresConfigAndCopiesBackgroundAssets()
	{
		Directory.CreateDirectory(_root);
		string sourceBackground = Path.Combine(_root, "background.png");
		File.WriteAllBytes(sourceBackground, new byte[] { 137, 80, 78, 71, 1, 2, 3, 4 });
		string packagePath = Path.Combine(_root, "backup.xyradial");
		string importedAssets = Path.Combine(_root, "imported-assets");
		PluginConfig config = new PluginConfig
		{
			TriggerMode = TriggerModes.RightHold,
			HoldDelayMs = 220,
			MenuSizePx = 690,
			CenterRadiusPx = 61,
			FontSizePx = 17,
			FontName = "SimSun",
			OpacityPercent = 88,
			SketchWheelEnabled = true,
			GlobalComboAdvanceEnabled = false,
			ComboAdvanceKey = "F8",
			Menus = new Dictionary<string, SceneMenuConfig>(StringComparer.OrdinalIgnoreCase)
			{
				[SceneKeys.Blank] = new SceneMenuConfig
				{
					Key = SceneKeys.Blank,
					BackgroundImagePath = sourceBackground,
					BackgroundOpacityPercent = 45,
					MenuSizePx = 640,
					CenterRadiusPx = 52,
					Rings = new List<RingConfig>
					{
						new RingConfig
						{
							Index = 0,
							Slots = 8,
							SizeWeight = 3,
							Enabled = true,
							Items = new List<MenuItemConfig>
							{
								new MenuItemConfig
								{
									Slot = 0,
									Label = "new",
									Kind = ActionKinds.Hotkey,
									Value = "Ctrl+N",
									FontSizePx = 18,
									FontName = "Microsoft YaHei",
									TextColor = "#C00000"
								},
								new MenuItemConfig
								{
									Slot = 1,
									Label = "combo",
									Kind = ActionKinds.Combo,
									Value = "combo",
									ComboAdvanceEnabled = true,
									ComboAdvanceKey = "Shift",
									ComboCommands = new List<ComboCommandConfig>
									{
										new ComboCommandConfig { Label = "A", Kind = ActionKinds.Hotkey, Value = "Ctrl+A", AutoConfirm = true, RequireAdvanceKey = true },
										new ComboCommandConfig { Label = "rebuild", Kind = ActionKinds.Basic, Value = "rebuild", AutoConfirm = false },
										new ComboCommandConfig { Label = "condition", Kind = ActionKinds.UiClick, Value = "条件选面", AutoConfirm = false },
										new ComboCommandConfig { Label = "wait", Kind = ActionKinds.Delay, Value = "300", AutoConfirm = false },
										new ComboCommandConfig { Label = "qinjian", Kind = ActionKinds.QinJian, Value = "琴剑钣金 > 门板切口", AutoConfirm = true },
										new ComboCommandConfig { Label = "jlc", Kind = ActionKinds.JiaLiChuang, Value = "快速倒角Show", AutoConfirm = false }
									}
								}
							}
						},
						new RingConfig
						{
							Index = 1,
							Slots = 12,
							SizeWeight = 2,
							Enabled = false,
							Items = new List<MenuItemConfig>()
						}
					}
				},
				[SceneKeys.Part] = new SceneMenuConfig
				{
					Key = SceneKeys.Part,
					BackgroundOpacityPercent = 72,
					MenuSizePx = 580,
					CenterRadiusPx = 47,
					Rings = new List<RingConfig>
					{
						new RingConfig { Index = 0, Slots = 16, SizeWeight = 4, Enabled = true }
					}
				}
			}
		};

		ConfigBackupService.Export(config, packagePath);

		using (ZipArchive archive = ZipFile.OpenRead(packagePath))
		{
			Assert.Contains(archive.Entries, entry => entry.FullName == "manifest.json");
			Assert.Contains(archive.Entries, entry => entry.FullName == "config.json");
			Assert.Contains(archive.Entries, entry => entry.FullName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)
				&& entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
		}

		PluginConfig imported = ConfigBackupService.Import(packagePath, importedAssets);

		Assert.Equal(TriggerModes.RightHold, imported.TriggerMode);
		Assert.Equal(220, imported.HoldDelayMs);
		Assert.Equal(690, imported.MenuSizePx);
		Assert.Equal(61, imported.CenterRadiusPx);
		Assert.Equal(17, imported.FontSizePx);
		Assert.Equal("SimSun", imported.FontName);
		Assert.Equal(88, imported.OpacityPercent);
		Assert.True(imported.SketchWheelEnabled);
		Assert.False(imported.GlobalComboAdvanceEnabled);
		Assert.Equal("F8", imported.ComboAdvanceKey);
		Assert.Equal(SceneKeys.All.Length, imported.Menus.Count);
		SceneMenuConfig importedScene = imported.Menus[SceneKeys.Blank];
		Assert.Equal(45, importedScene.BackgroundOpacityPercent);
		Assert.Equal(640, importedScene.MenuSizePx);
		Assert.Equal(52, importedScene.CenterRadiusPx);
		Assert.NotEqual(sourceBackground, importedScene.BackgroundImagePath);
		Assert.StartsWith(importedAssets, importedScene.BackgroundImagePath, StringComparison.OrdinalIgnoreCase);
		Assert.True(File.Exists(importedScene.BackgroundImagePath));
		Assert.Equal(File.ReadAllBytes(sourceBackground), File.ReadAllBytes(importedScene.BackgroundImagePath));
		Assert.Equal(2, importedScene.Rings.Count);
		Assert.Equal(3, importedScene.Rings[0].SizeWeight);
		Assert.True(importedScene.Rings[0].Enabled);
		Assert.Equal(12, importedScene.Rings[1].Slots);
		Assert.Equal(2, importedScene.Rings[1].SizeWeight);
		Assert.False(importedScene.Rings[1].Enabled);
		Assert.Equal(18, importedScene.Rings[0].Items[0].FontSizePx);
		Assert.Equal("Microsoft YaHei", importedScene.Rings[0].Items[0].FontName);
		Assert.Equal("#C00000", importedScene.Rings[0].Items[0].TextColor);
		MenuItemConfig combo = importedScene.Rings[0].Items.Single(item => item.Kind == ActionKinds.Combo);
		Assert.True(combo.ComboAdvanceEnabled);
		Assert.Equal("Shift", combo.ComboAdvanceKey);
		Assert.Equal(6, combo.ComboCommands.Count);
		Assert.Equal("Ctrl+A", combo.ComboCommands[0].Value);
		Assert.True(combo.ComboCommands[0].AutoConfirm);
		Assert.True(combo.ComboCommands[0].RequireAdvanceKey);
		Assert.Equal("rebuild", combo.ComboCommands[1].Value);
		Assert.Equal(ActionKinds.UiClick, combo.ComboCommands[2].Kind);
		Assert.Equal("条件选面", combo.ComboCommands[2].Value);
		Assert.Equal(ActionKinds.Delay, combo.ComboCommands[3].Kind);
		Assert.Equal("300", combo.ComboCommands[3].Value);
		Assert.Equal(ActionKinds.QinJian, combo.ComboCommands[4].Kind);
		Assert.Equal("琴剑钣金 > 门板切口", combo.ComboCommands[4].Value);
		Assert.True(combo.ComboCommands[4].AutoConfirm);
		Assert.Equal(ActionKinds.JiaLiChuang, combo.ComboCommands[5].Kind);
		Assert.Equal("快速倒角Show", combo.ComboCommands[5].Value);
		Assert.False(combo.ComboCommands[5].AutoConfirm);
		SceneMenuConfig partScene = imported.Menus[SceneKeys.Part];
		Assert.Equal(72, partScene.BackgroundOpacityPercent);
		Assert.Equal(580, partScene.MenuSizePx);
		Assert.Equal(47, partScene.CenterRadiusPx);
		Assert.Equal(16, partScene.Rings[0].Slots);
		Assert.Equal(4, partScene.Rings[0].SizeWeight);
	}

	[Fact]
	public void CustomIconsRemainPortableAndSharedAfterOriginalFilesAreRemoved()
	{
		Directory.CreateDirectory(_root);
		string firstFolder = Path.Combine(_root, "first");
		string secondFolder = Path.Combine(_root, "second");
		Directory.CreateDirectory(firstFolder);
		Directory.CreateDirectory(secondFolder);
		string firstIcon = Path.Combine(firstFolder, "icon.png");
		string secondIcon = Path.Combine(secondFolder, "icon.png");
		byte[] firstBytes = { 137, 80, 78, 71, 1, 2, 3 };
		byte[] secondBytes = { 137, 80, 78, 71, 4, 5, 6 };
		File.WriteAllBytes(firstIcon, firstBytes);
		File.WriteAllBytes(secondIcon, secondBytes);
		PluginConfig config = ConfigWithIcons(firstIcon, firstIcon, secondIcon);
		config.Menus[SceneKeys.Blank].Rings[0].Items[0].IconId = "gear";
		string package = Path.Combine(_root, "icons.xyradial");
		ConfigBackupService.Export(config, package);
		Assert.Equal(firstIcon, config.Menus[SceneKeys.Blank].Rings[0].Items[0].IconPath);
		using (ZipArchive archive = ZipFile.OpenRead(package))
		{
			Assert.Equal(2, archive.Entries.Count(entry => entry.FullName.StartsWith("assets/")));
		}
		File.Delete(firstIcon);
		File.Delete(secondIcon);

		string importedAssets = Path.Combine(_root, "restored-icons");
		PluginConfig imported = ConfigBackupService.Import(package, importedAssets);
		List<MenuItemConfig> items = imported.Menus[SceneKeys.Blank].Rings[0].Items;
		Assert.Equal(items[0].IconPath, items[1].IconPath);
		Assert.NotEqual(items[0].IconPath, items[2].IconPath);
		Assert.StartsWith(importedAssets + Path.DirectorySeparatorChar, items[0].IconPath, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(firstBytes, File.ReadAllBytes(items[0].IconPath));
		Assert.Equal(secondBytes, File.ReadAllBytes(items[2].IconPath));
		Assert.Equal("gear", items[0].IconId);
		Assert.Equal(ActionKinds.Hotkey, items[0].Kind);
		Assert.Equal("Ctrl+N", items[0].Value);
	}

	[Fact]
	public void ImportRejectsBackupWithMissingPackagedIcon()
	{
		Directory.CreateDirectory(_root);
		string icon = Path.Combine(_root, "missing.png");
		File.WriteAllBytes(icon, new byte[] { 1, 2, 3 });
		string package = Path.Combine(_root, "missing-icon.xyradial");
		ConfigBackupService.Export(ConfigWithIcons(icon), package);
		using (ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Update))
		{
			archive.Entries.Single(entry => entry.FullName.StartsWith("assets/")).Delete();
		}

		InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
			ConfigBackupService.Import(package, Path.Combine(_root, "missing-assets")));
		Assert.Contains("缺少图标资源", error.Message);
	}

	[Fact]
	public void ImportJsonCopiesRelativeIconAndRejectsEscapingRelativeReference()
	{
		Directory.CreateDirectory(_root);
		string jsonFolder = Path.Combine(_root, "json");
		Directory.CreateDirectory(jsonFolder);
		string icon = Path.Combine(jsonFolder, "icon.png");
		File.WriteAllBytes(icon, new byte[] { 7, 8, 9 });
		string json = Path.Combine(jsonFolder, "config.json");
		File.WriteAllText(json, ConfigStore.Serialize(ConfigWithIcons("icon.png")));
		PluginConfig imported = ConfigBackupService.Import(json, Path.Combine(_root, "json-assets"));
		Assert.Equal(File.ReadAllBytes(icon), File.ReadAllBytes(imported.Menus[SceneKeys.Blank].Rings[0].Items[0].IconPath));

		File.WriteAllBytes(Path.Combine(_root, "outside.png"), new byte[] { 4, 5, 6 });
		File.WriteAllText(json, ConfigStore.Serialize(ConfigWithIcons("../outside.png")));
		Assert.Throws<InvalidDataException>(() => ConfigBackupService.Import(json, Path.Combine(_root, "escape-assets")));
	}

	private static PluginConfig ConfigWithIcons(params string[] paths)
	{
		return new PluginConfig
		{
			Menus = new Dictionary<string, SceneMenuConfig>
			{
				[SceneKeys.Blank] = new SceneMenuConfig
				{
					Key = SceneKeys.Blank,
					Rings = new List<RingConfig>
					{
						new RingConfig
						{
							Index = 0, Slots = 8, Enabled = true,
							Items = paths.Select((path, index) => new MenuItemConfig
							{
								Slot = index, Label = "Item " + index, Kind = ActionKinds.Hotkey,
								Value = "Ctrl+N", IconPath = path
							}).ToList()
						}
					}
				}
			}
		};
	}

	[Fact]
	public void ExportAtomicallyReplacesAnExistingBackup()
	{
		Directory.CreateDirectory(_root);
		string packagePath = Path.Combine(_root, "existing.xyradial");
		File.WriteAllText(packagePath, "old backup must survive until replacement succeeds");
		PluginConfig config = new PluginConfig
		{
			HoldDelayMs = 777,
			OpacityPercent = 81
		};

		ConfigBackupService.Export(config, packagePath);
		PluginConfig imported = ConfigBackupService.Import(packagePath, Path.Combine(_root, "atomic-assets"));

		Assert.Equal(777, imported.HoldDelayMs);
		Assert.Equal(81, imported.OpacityPercent);
		Assert.Empty(Directory.GetFiles(_root, "*.tmp-*", SearchOption.TopDirectoryOnly));
	}

	[Fact]
	public void CombinedBackupCarriesConfigAndSolidWorksShortcutsIndependently()
	{
		Directory.CreateDirectory(_root);
		string shortcutPath = Path.Combine(_root, "source.sldreg");
		string shortcutReport = Path.Combine(_root, "source.txt");
		string packagePath = Path.Combine(_root, "combined.xyradial");
		ShortcutRegistryData shortcutData = new ShortcutRegistryData
		{
			VersionName = "SOLIDWORKS 2025",
			CustomizationVersion = 300,
			Entries =
			{
				new ShortcutRegistryEntry { Plate = "tPlate0", Collection = "AddArray", LeafName = "23_49", Flags = 0x17, Key = 0x31, CommandId = 0xA2BF }
			}
		};
		File.WriteAllText(shortcutPath, SolidWorksShortcutBackupService.BuildRegistryText(shortcutData));
		File.WriteAllText(shortcutReport, "类别\t命令\t快捷键\r\n插件快捷键 > 琴剑钣金\t门板切口\tAlt+Shift+1");

		ConfigBackupService.Export(new PluginConfig { HoldDelayMs = 456 }, packagePath, new ConfigBackupExportOptions
		{
			IncludeConfig = true,
			ShortcutRegistryFile = shortcutPath,
			ShortcutReportFile = shortcutReport
		});

		ConfigBackupContents contents = ConfigBackupService.Inspect(packagePath);
		Assert.True(contents.HasConfig);
		Assert.True(contents.HasShortcuts);
		PluginConfig imported = ConfigBackupService.Import(packagePath, Path.Combine(_root, "combined-assets"));
		Assert.Equal(456, imported.HoldDelayMs);
		ExtractedShortcutBackup extracted = ConfigBackupService.ExtractShortcuts(packagePath, Path.Combine(_root, "shortcuts"));
		Assert.True(File.Exists(extracted.RegistryFilePath));
		Assert.True(File.Exists(extracted.ReportFilePath));
		ShortcutRegistryData parsed = SolidWorksShortcutBackupService.ParseRegistryFile(extracted.RegistryFilePath);
		Assert.Single(parsed.Entries);
		Assert.Contains("门板切口", File.ReadAllText(extracted.ReportFilePath));
	}

	[Fact]
	public void ShortcutOnlyBackupDisablesConfigComponent()
	{
		Directory.CreateDirectory(_root);
		string shortcutPath = Path.Combine(_root, "only.sldreg");
		string packagePath = Path.Combine(_root, "shortcut-only.xyradial");
		File.WriteAllText(shortcutPath, SolidWorksShortcutBackupService.BuildRegistryText(new ShortcutRegistryData
		{
			VersionName = "SOLIDWORKS 2025",
			Entries =
			{
				new ShortcutRegistryEntry { Plate = "tPlate0", Collection = "AddArray", LeafName = "11_65", Flags = 0x0B, Key = 0x41, CommandId = 42 }
			}
		}));

		ConfigBackupService.Export(new PluginConfig(), packagePath, new ConfigBackupExportOptions
		{
			IncludeConfig = false,
			ShortcutRegistryFile = shortcutPath
		});

		ConfigBackupContents contents = ConfigBackupService.Inspect(packagePath);
		Assert.False(contents.HasConfig);
		Assert.True(contents.HasShortcuts);
		Assert.Throws<InvalidDataException>(() => ConfigBackupService.Import(packagePath, Path.Combine(_root, "missing-config-assets")));
	}

	[Fact]
	public void InspectAndExtractAcceptWindowsBackslashZipEntries()
	{
		Directory.CreateDirectory(_root);
		string packagePath = Path.Combine(_root, "windows-paths.xyradial");
		using (ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
		{
			using (StreamWriter manifest = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
			{
				manifest.Write("{\"Schema\":\"xiao-yang-radial-backup/v3\",\"HasShortcuts\":true}");
			}
			using (StreamWriter shortcut = new StreamWriter(archive.CreateEntry(@"shortcuts\solidworks-shortcuts.sldreg").Open()))
			{
				shortcut.Write(SolidWorksShortcutBackupService.BuildRegistryText(new ShortcutRegistryData
				{
					VersionName = "SOLIDWORKS 2025",
					Entries =
					{
						new ShortcutRegistryEntry { Plate = "tPlate0", Collection = "AddArray", LeafName = "11_65", Flags = 0x0B, Key = 0x41, CommandId = 42 }
					}
				}));
			}
		}

		ConfigBackupContents contents = ConfigBackupService.Inspect(packagePath);
		Assert.False(contents.HasConfig);
		Assert.True(contents.HasShortcuts);
		ExtractedShortcutBackup extracted = ConfigBackupService.ExtractShortcuts(packagePath, Path.Combine(_root, "extracted-windows-paths"));
		Assert.Single(SolidWorksShortcutBackupService.ParseRegistryFile(extracted.RegistryFilePath).Entries);
	}

	public void Dispose()
	{
		if (Directory.Exists(_root))
		{
			Directory.Delete(_root, recursive: true);
		}
	}
}
