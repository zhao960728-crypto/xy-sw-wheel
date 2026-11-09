using System.Linq;
using System.Collections.Generic;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class SolidWorksNativeCommandCatalogTests
{
	[Fact]
	public void NativeCommandIdReferenceRoundTrips()
	{
		string value = NativeCommandReference.Encode(43);

		Assert.True(NativeCommandReference.TryDecodeId(value, out int commandId));
		Assert.Equal(43, commandId);
	}

	[Fact]
	public void ExternalPluginReferenceKeepsPluginIdentityAndCommandId()
	{
		string value = ExternalPluginCommandReference.Encode("{26DFFF7F-E231-42BC-BEF0-6EEEB0D56C3B}", "博士钣金", "QuickSplit", 41775);

		Assert.True(ExternalPluginCommandReference.TryDecodeId(value, out int commandId));
		Assert.Equal(41775, commandId);
		Assert.Contains("26DFFF7F", value);
	}

	[Fact]
	public void ExternalToolbarReferenceKeepsDocumentTypeAndCommandId()
	{
		string value = ExternalToolbarCommandReference.Encode(1, 41775);

		Assert.True(ExternalToolbarCommandReference.TryDecode(value, out int documentType, out int commandId));
		Assert.Equal(1, documentType);
		Assert.Equal(41775, commandId);
	}

	[Fact]
	public void ExternalToolbarReferenceKeepsCaptionForCurrentSessionRematch()
	{
		string value = ExternalToolbarCommandReference.Encode(2, 41775, "STEP格式互换");

		Assert.True(ExternalToolbarCommandReference.TryDecode(value, out int documentType, out int commandId));
		Assert.Equal(2, documentType);
		Assert.Equal(41775, commandId);
		Assert.Equal("STEP格式互换", ExternalToolbarCommandReference.Name(value));
	}

	[Fact]
	public void EmbeddedExternalCommandMappingIsLimitedToCapturedDocumentEnvironment()
	{
		Assert.True(ToolbarExternalCommandCatalog.TryResolveMappedNameForTest(1, 41658, out string name));
		Assert.Equal("快速上色", name);
		Assert.False(ToolbarExternalCommandCatalog.TryResolveMappedNameForTest(2, 41658, out _));
	}

	[Fact]
	public void RemovedDoctorBottomHoleCommandIsAbsentButToothHoleCommandRemainsMapped()
	{
		Assert.False(ToolbarExternalCommandCatalog.TryResolveMappedNameForTest(1, 41770, out _));
		Assert.True(ToolbarExternalCommandCatalog.TryResolveMappedNameForTest(1, 41771, out string name));
		Assert.Equal("压铆拉铆[牙孔]", name);
		Assert.True(ToolbarExternalCommandCatalog.IsRemovedCommand(41770));
		Assert.True(ToolbarExternalCommandCatalog.IsRemovedCommand(-1, "压铆拉铆[底孔]"));
		Assert.False(ToolbarExternalCommandCatalog.IsRemovedCommand(41771, "压铆拉铆[牙孔]"));
	}

	[Fact]
	public void EmbeddedExternalCommandMappingResolvesPluginNameAndCategory()
	{
		Assert.True(ToolbarExternalCommandCatalog.TryResolveMappedCommand(1, "第三方插件 > 博士钣金", "STEP格式互换", out int commandId));
		Assert.Equal(41690, commandId);
		Assert.True(ToolbarExternalCommandCatalog.TryResolveMappedCategory(1, commandId, out string category));
		Assert.Equal("博士钣金", category);
	}

	[Theory]
	[InlineData("DoctorSheetMetal_Plus AddIn", "博士钣金", true)]
		[InlineData("DoctorSheetMetal_Plus AddIn", "Dr工具", true)]
	[InlineData("QinJianSwTool", "琴剑钣金", true)]
	[InlineData("嘉立创FA零件库插件", "嘉立创 Ican", true)]
	[InlineData("SolidWorks 盛兴钣金培训插件", "博士钣金", false)]
	public void ExternalPluginRegistryTitlesMatchChineseCommandFamilies(string registeredName, string requestedName, bool expected)
	{
		Assert.Equal(expected, ExternalPluginCommandCatalog.PluginNamesMatchForTest(registeredName, requestedName));
	}

	[Fact]
	public void KnownExternalCatalogRowsUseExternalActionIdentity()
	{
		var row = ToolbarExternalCommandCatalog.KnownCatalog(1).Single(item => item.Command == "STEP格式互换");
		Assert.Equal(ActionKinds.ExternalPlugin, row.ActionKind);
		Assert.True(ExternalNamedCommandReference.TryDecode(row.Value, out int documentType, out string category, out string name));
		Assert.Equal(1, documentType);
		Assert.Equal("第三方插件 > 博士钣金", category);
		Assert.Equal("STEP格式互换", name);
	}

	[Fact]
	public void DoctorCatalogExposesAllMappedFunctionsExceptRetiredBottomHole()
	{
		var rows = ToolbarExternalCommandCatalog.KnownCatalog(1).ToList();
		Assert.True(rows.Count >= 190);
		Assert.Contains(rows, row => row.Category.Contains("博士钣金") && row.Command == "快速上色");
		Assert.Contains(rows, row => row.Category.Contains("Dr工具") && row.Command == "全视图调整标注");
		Assert.DoesNotContain(rows, row => row.Command.Contains("底孔"));
		Assert.DoesNotContain(rows, row => ToolbarExternalCommandCatalog.IsRemovedCommand(-1, row.Command));
	}

	[Fact]
	public void ExternalRowsWithReusedCommandIdsKeepSeparatePluginIdentities()
	{
		var rows = ExternalPluginCommandCatalog.DistinctRows(new[]
		{
			new KeyboardShortcutRow { Category = "第三方插件 > A", Command = "同名功能", Value = ExternalToolbarCommandReference.Encode(1, 41000, "同名功能") },
			new KeyboardShortcutRow { Category = "第三方插件 > B", Command = "同名功能", Value = ExternalToolbarCommandReference.Encode(1, 41000, "同名功能") },
			new KeyboardShortcutRow { Category = "第三方插件 > A", Command = "同名功能", Value = ExternalToolbarCommandReference.Encode(1, 41001, "同名功能") }
		});
		Assert.Equal(3, rows.Count);
	}

	[Fact]
	public void ToolbarClickPointPacksClientCoordinatesForMouseMessage()
	{
		long packed = ToolbarExternalCommandCatalog.PackPointForTest(123, 456).ToInt64();
		Assert.Equal(123, packed & 0xFFFF);
		Assert.Equal(456, (packed >> 16) & 0xFFFF);
	}

	[Fact]
	public void LegacyNativeCommandReferenceStillRoundTripsChineseNames()
	{
		string value = NativeCommandReference.Encode("草图", "直线/中心线");

		Assert.True(NativeCommandReference.TryDecode(value, out string category, out string command));
		Assert.Equal("草图", category);
		Assert.Equal("直线/中心线", command);
	}

	[Fact]
	public void BuildRowsCreatesDirectNativeCommandsAndRemovesDuplicates()
	{
		var rows = SolidWorksNativeCommandCatalog.BuildRows("特征(F)", new[] { "抽壳", "抽壳", "拔模" });

		Assert.Equal(2, rows.Count);
		Assert.All(rows, row => Assert.Equal(ActionKinds.NativeCommand, row.ActionKind));
		Assert.All(rows, row => Assert.Equal("已识别，执行时校验", row.Hotkey));
		Assert.All(rows, row => Assert.Equal("SOLIDWORKS > 特征", row.Category));
		Assert.Contains(rows, row => row.Command == "抽壳");
	}

	[Fact]
	public void NativeRowsKeepDistinctIdsWithTheSameDisplayName()
	{
		var rows = new[] {
			new KeyboardShortcutRow { Category = "SOLIDWORKS > 特征", Command = "阵列", Value = NativeCommandReference.Encode(43) },
			new KeyboardShortcutRow { Category = "SOLIDWORKS > 特征", Command = "阵列", Value = NativeCommandReference.Encode(44) },
			new KeyboardShortcutRow { Category = "SOLIDWORKS > 特征", Command = "阵列", Value = NativeCommandReference.Encode(43) }
		};
		Assert.Equal(2, SolidWorksNativeCommandCatalog.OrderDistinctRows(rows).Count);
		Assert.False(SolidWorksNativeCommandCatalog.TryResolveLegacyRows(rows, "特征", "阵列", out _));
	}

	[Fact]
	public void LegacyResolutionUsesCategoryAndRefusesAmbiguousNames()
	{
		var rows = new[] {
			new KeyboardShortcutRow { Category = "SOLIDWORKS > 草图", Command = "阵列", Value = NativeCommandReference.Encode(43) },
			new KeyboardShortcutRow { Category = "SOLIDWORKS > 特征", Command = "阵列", Value = NativeCommandReference.Encode(44) }
		};
		Assert.True(SolidWorksNativeCommandCatalog.TryResolveLegacyRows(rows, "特征", "阵列", out int id));
		Assert.Equal(44, id);
		Assert.False(SolidWorksNativeCommandCatalog.TryResolveLegacyRows(rows, "其他", "阵列", out _));
	}

	[Fact]
	public void ReadRequiresActualMenuEvidenceAndPreservesLocalizedName()
	{
		const int id = 9; // swCommands_Fillet, verified against the installed interop assembly.
		Assert.Null(SolidWorksNativeCommandCatalog.BuildNativeRow(id, "", ""));
		Assert.Null(SolidWorksNativeCommandCatalog.BuildNativeRow(int.MaxValue, "插件功能", "特征"));
		var row = SolidWorksNativeCommandCatalog.BuildNativeRow(id, "Fillet(&F)...\tCtrl+F", "Features");
		Assert.NotNull(row);
		Assert.Equal("Fillet", row.Command);
		Assert.Equal("SOLIDWORKS > 特征 > Features", row.Category);
		Assert.Equal(NativeCommandReference.Encode(id), row.Value);
		Assert.Equal("已识别，执行时校验", row.Hotkey);
	}

	[Fact]
	public void CommandWithNoParentMenuStillHasNativeIdentity()
	{
		var row = SolidWorksNativeCommandCatalog.BuildNativeRow(43, "真实菜单命令", "");
		Assert.NotNull(row);
		Assert.Equal("真实菜单命令", row.Command);
		Assert.Equal(NativeCommandReference.Encode(43), row.Value);
	}

	[Fact]
	public void NativeMenuParsingPreservesLiteralAmpersands()
	{
		var row = SolidWorksNativeCommandCatalog.BuildNativeRow(43, "Move && Copy(&M)...\tCtrl+M", "Tools");
		Assert.NotNull(row);
		Assert.Equal("Move & Copy", row.Command);
	}

	[Theory]
	[InlineData("草图绘制实体(&K)", "SOLIDWORKS > 草图 > 草图绘制实体")]
	[InlineData("草图工具(&K)", "SOLIDWORKS > 草图 > 草图工具")]
	[InlineData("尺寸(&S)", "SOLIDWORKS > 草图 > 尺寸")]
	[InlineData("零部件(&O)", "SOLIDWORKS > 装配体 > 零部件")]
	[InlineData("参考几何体(&G)", "SOLIDWORKS > 特征 > 参考几何体")]
	public void BuildsDetailedCategoryTreeFromSolidWorksParentMenu(string parentMenu, string expected)
	{
		Assert.Equal(expected, SolidWorksNativeCommandCatalog.BuildCategory(parentMenu));
	}

	[Fact]
	public void ConfigNormalizationKeepsNativeCommandIds()
	{
		string value = NativeCommandReference.Encode(305);
		PluginConfig config = ConfigStore.Deserialize("{\"Menus\":{\"sw.part\":{\"Rings\":[{\"Index\":0,\"Slots\":8,\"Items\":[{\"Slot\":0,\"Kind\":\"sw-native-command\",\"Value\":\"" + value + "\"}]}]}}}");

		ConfigStore.Normalize(config);

		MenuItemConfig item = config.Menus[SceneKeys.Part].Rings.Single().Items.Single();
		Assert.Equal(ActionKinds.NativeCommand, item.Kind);
		Assert.Equal(value, item.Value);
	}

	[Fact]
	public void ShortcutClassificationPreservesCopiedPluginCategoryBeforeNameLookup()
	{
		var known = new Dictionary<string, string> { ["门板切口"] = "琴剑钣金" };

		string category = SolidWorksNativeCommandCatalog.ClassifyShortcutCategory("博士钣金", "门板切口",
			new Dictionary<string, string>(), known);

		Assert.Equal("插件快捷键 > 博士钣金", category);
	}

	[Fact]
	public void ShortcutClassificationUsesDoctorCategoryForGenericDoctorCommands()
	{
		var known = new Dictionary<string, string> { ["门板切口"] = "琴剑钣金" };

		string category = SolidWorksNativeCommandCatalog.ClassifyShortcutCategory("工具", "门板切口",
			new Dictionary<string, string>(), known);

		Assert.Equal("插件快捷键 > 博士钣金", category);
	}

	[Theory]
	[InlineData("门板切口")]
	[InlineData("快速工程图")]
	public void ShortcutClassificationKeepsDoctorCommandsOutOfQinJianBranch(string command)
	{
		var known = new Dictionary<string, string> { [command] = "琴剑钣金" };

		string category = SolidWorksNativeCommandCatalog.ClassifyShortcutCategory("工具", command,
			new Dictionary<string, string>(), known);

		Assert.Equal("插件快捷键 > 博士钣金", category);
	}

	[Fact]
	public void ShortcutClassificationKeepsAmbiguousGenericCommandOutOfPluginBranches()
	{
		string category = SolidWorksNativeCommandCatalog.ClassifyShortcutCategory("工具", "同名命令",
			new Dictionary<string, string>(), new Dictionary<string, string>());

		Assert.Equal("其他快捷键 > 工具", category);
	}

	[Fact]
	public void KeyboardReadExcludesQinJianAndJiaLiChuangPluginBranches()
	{
		var rows = new[]
		{
			new KeyboardShortcutRow { Category = "插件快捷键 > 琴剑钣金", Command = "门板切口" },
			new KeyboardShortcutRow { Category = "插件快捷键 > 嘉立创 Ican", Command = "快速倒角" },
			new KeyboardShortcutRow { Category = "插件快捷键 > 博士钣金", Command = "快速工程图" },
			new KeyboardShortcutRow { Category = "SOLIDWORKS 快捷键 > 文件", Command = "打开" }
		};

		List<KeyboardShortcutRow> result = SolidWorksNativeCommandCatalog.ExcludeSeparatelyReadPluginShortcuts(rows);

		Assert.Equal(2, result.Count);
		Assert.Contains(result, row => row.Category.Contains("博士钣金"));
		Assert.Contains(result, row => row.Category.Contains("SOLIDWORKS"));
	}

	[Fact]
	public void LegacyRingWeightsMigrateToOrderedPixelDiameters()
	{
		PluginConfig config = ConfigStore.Deserialize("{\"Menus\":{\"sw.part\":{\"MenuSizePx\":550,\"CenterRadiusPx\":24,\"Rings\":[{\"Index\":0,\"Slots\":8,\"SizeWeight\":1,\"Enabled\":true},{\"Index\":1,\"Slots\":12,\"SizeWeight\":2,\"Enabled\":true},{\"Index\":2,\"Slots\":16,\"SizeWeight\":1,\"Enabled\":true}]}}}");

		ConfigStore.Normalize(config);

		RingConfig[] rings = config.Menus[SceneKeys.Part].Rings.ToArray();
		Assert.Equal(550, rings[2].DiameterPx);
		Assert.True(rings[0].DiameterPx > 48);
		Assert.True(rings[0].DiameterPx < rings[1].DiameterPx);
		Assert.True(rings[1].DiameterPx < rings[2].DiameterPx);
	}
}
