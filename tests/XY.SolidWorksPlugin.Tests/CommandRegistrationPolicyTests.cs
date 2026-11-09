using System.Reflection;
using Xunit;
using System.Linq;

namespace XY.SolidWorksPlugin.Tests;

public sealed class CommandRegistrationPolicyTests
{
    [Theory]
    [InlineData(1, true, false, true)]
    [InlineData(1, false, false, true)]
    [InlineData(0, false, false, true)]
    [InlineData(0, true, false, false)]
    [InlineData(0, true, true, false)]
    [InlineData(0, false, true, false)]
    [InlineData(1, true, true, false)]
    [InlineData(2, false, false, false)]
    public void OldGroupCleanupRequiresSuccessOrVerifiedRuntimeAbsence(int result, bool before, bool after, bool expected)
    {
        Assert.Equal(expected, WheelCommandTabs.IsLegacyRemovalComplete(result, before, after));
        Assert.Equal(0, (int)SolidWorks.Interop.swconst.swRemoveCommandGroupErrors.swRemoveCommandGroup_Failed);
        Assert.Equal(1, (int)SolidWorks.Interop.swconst.swRemoveCommandGroupErrors.swRemoveCommandGroup_Success);
    }
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void CurrentIdsAloneDoNotProveOldTabsWereMigrated(bool registryExists, bool completed, bool expected)
    {
        int[] current = { 1, 3, 5, 2, 53, 14, 56 };
        Assert.Equal(expected, WheelCommandTabs.ShouldMigrateTabs(registryExists, completed, current));
        Assert.True(WheelCommandTabs.ShouldMigrateTabs(true, completed, Enumerable.Range(0, 53).ToArray()));
    }

    [Theory]
    [InlineData("33.5.0", @"Software\XY\XYRadialMenu.SW\CommandManager\33")]
    [InlineData("34.0.1", @"Software\XY\XYRadialMenu.SW\CommandManager\34")]
    public void MigrationMarkerIsOwnedByThisProductAndHostMajorVersion(string revision, string expected)
    {
        Assert.Equal(expected, WheelCommandTabs.MigrationRegistryPath(revision));
        Assert.Equal(1637552378, SwAddin.LegacyCommandGroupId);
        Assert.NotEqual(SwAddin.LegacyCommandGroupId, SwAddin.CommandGroupId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("0.0")]
    [InlineData("-1.0")]
    public void UnidentifiedHostDoesNotWriteAMigrationMarker(string revision) =>
        Assert.Throws<System.InvalidOperationException>(() => WheelCommandTabs.MigrationRegistryPath(revision));

    [Theory]
    [InlineData("quote")]
    [InlineData("property-operations")]
    public void RetiredActionsRemainReadableButCannotBeSelectedOrExecuted(string value)
    {
        Assert.False(BasicActions.Contains(value));
        Assert.DoesNotContain(value, BasicActions.All.Keys);
        var config = ConfigStore.Deserialize("{}");
        ConfigStore.Normalize(config);
        
        Assert.False(SwAddin.IsBasicActionEnabled(config, value));
    }

    [Fact]
    public void LegacyMigrationUsesOnlyRegisteredUserIdsAndDoesNotResetCurrentSchema()
    {
        Assert.True(WheelCommandTabs.RequiresLegacyMigration(Enumerable.Range(0, 53).ToArray()));
        Assert.True(WheelCommandTabs.RequiresLegacyMigration(new[] {0,1,3,4,5,6,2,8,11,10,9,-1}));
        Assert.True(WheelCommandTabs.RequiresLegacyMigration(new[] {1,3,4,5,6,2,8,11,10,9,-1,14}));
		Assert.False(WheelCommandTabs.RequiresLegacyMigration(new[] {1,3,5,2,53,14,56}));
        Assert.True(WheelCommandTabs.RequiresLegacyMigration(new[] {1,3,5,2,53,14}));
        Assert.Equal(53, SwAddin.FeatureSettingsUserId);
        Assert.True(WheelCommandTabs.RequiresLegacyMigration(new[] { 1, 3, 5, 2, 53 }));
        Assert.False(WheelCommandTabs.RequiresLegacyMigration(null));
    }

	[Fact]
	public void ToolbarContainsOnlyRequestedFeatures()
	{
		Assert.Equal(1637552379, SwAddin.CommandGroupId);
		Assert.Equal(7, SwAddin.ExpectedCommandGroupItemCount);
		int[] positions = WheelCommandTabs.Groups.SelectMany(group => group).ToArray();
		Assert.Equal(SwAddin.ExpectedCommandGroupItemCount, positions.Length);
		Assert.Equal(Enumerable.Range(0, SwAddin.ExpectedCommandGroupItemCount), positions.OrderBy(index => index));
		Assert.Equal(SwAddin.ExpectedCommandGroupItemCount, typeof(WheelCommandTabs).GetField("Icons", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null) is string[] icons ? icons.Length : -1);
		Assert.Equal(new[] { 1, 2, 3 }, WheelCommandTabs.DocumentTypes);
	}

	[Fact]
	public void EveryOwnedToolbarCommandUsesAResolvableIcon()
	{
		var icons = (string[])typeof(WheelCommandTabs).GetField("Icons", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
		Assert.Equal(SwAddin.ExpectedCommandGroupItemCount, icons.Length);
		Assert.Equal("watermark-settings", icons[1]);
		foreach (string icon in icons)
			using (var bitmap = UnifiedWheelArtwork.Render(icon, 32))
				Assert.Equal(32, bitmap.Width);
	}

	[Fact]
	public void RetainedToolbarButtonsHaveCallbacksAndContextEnablement()
	{
		foreach (string method in new[] { "ShowProcessRecognition", "ShowExplodeAllCommand" })
		{
			Assert.NotNull(typeof(SwAddin).GetMethod(method));
			Assert.NotNull(typeof(SwAddin).GetMethod(method + "Enable"));
		}
	}


	[Fact]
	public void NewCommandManagerButtonIconsCanBeRendered()
	{
		using var rename = WheelIconCatalog.Render("properties.edit", 32);
		using var export = WheelIconCatalog.Render("export", 32);
		using var propertySearch = WheelIconCatalog.Render("properties.search", 32);
		using var drawingScale = WheelIconCatalog.Render("properties.edit", 32);
		using var openPath = WheelIconCatalog.Render("folder.search", 32);
		using var configuration = WheelIconCatalog.Render("configuration", 32);
		using var cutList = WheelIconCatalog.Render("cutlist", 32);
		using var plot = WheelIconCatalog.Render("plot", 32);
		using var blueprint = WheelIconCatalog.Render("blueprint", 32);
		using var save = WheelIconCatalog.Render("save", 32);
		using var rotate = WheelIconCatalog.Render("rotate", 32);
		using var sheetMetal = WheelIconCatalog.Render("properties.edit", 32);
		Assert.Equal(32, rename.Width);
		Assert.Equal(32, export.Width);
		Assert.Equal(32, propertySearch.Width);
		Assert.Equal(32, drawingScale.Width);
		Assert.Equal(32, openPath.Width);
		Assert.Equal(32, configuration.Width);
		Assert.Equal(32, cutList.Width);
		Assert.Equal(32, plot.Width);
		Assert.Equal(32, blueprint.Width);
		Assert.Equal(32, save.Width);
		Assert.Equal(32, rotate.Width);
		Assert.Equal(32, sheetMetal.Width);
	}

	[Fact]
	public void CommandGroupUsesStableProductSpecificId()
	{
		Assert.Equal(1637552379, SwAddin.CommandGroupId);
		Assert.Equal(7, SwAddin.ExpectedCommandGroupItemCount);
		Assert.NotEqual(1637552377, SwAddin.CommandGroupId);
		Assert.NotEqual(902405, SwAddin.CommandGroupId);
		Assert.True(SwAddin.CommandGroupId > 1000000);
	}

	[Theory]
	[InlineData(1, true)]
	[InlineData(0, false)]
	[InlineData(2, false)]
	public void CommandGroupCreationAcceptsOnlySolidWorksSuccess(int result, bool expected)
	{
		Assert.Equal(expected, SwAddin.IsCommandGroupCreateSuccess(result));
	}

	[Fact]
	public void ToolsMenuRequiresEveryFeatureEntry()
	{
		Assert.Equal(6, SwAddin.ExpectedToolsMenuItemCount);
		Assert.Equal(4, SwAddin.ExpectedToolsMenuDocumentCount);
		Assert.Equal(1, SwAddin.DeferredCommandRegistrationAttemptCount);
		Assert.Equal(15, SwAddin.ToolsMenuDocumentTypes);
		Assert.True(SwAddin.AreAllToolsMenuEntriesRegistered(true, true, true, true, true, true));
		Assert.False(SwAddin.AreAllToolsMenuEntriesRegistered(true, true, true, true, true, false));
        Assert.False(SwAddin.AreAllToolsMenuEntriesRegistered(true, true, true, true, true));
		Assert.False(SwAddin.AreAllToolsMenuEntriesRegistered(true, true, true, true, true, true, true, true, false));
		Assert.False(SwAddin.AreAllToolsMenuEntriesRegistered(true, true, true, true, true, true, true, false, true));
		Assert.False(SwAddin.AreAllToolsMenuEntriesRegistered(true, true));
	}

	[Fact]
	public void CommandGroupRegistrationDoesNotUseLegacyNamedMenuRemoval()
	{
		MethodInfo legacyRemoval = typeof(SwAddin).GetMethod("RemoveLegacyToolsParentMenus", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.Null(legacyRemoval);
		Assert.NotNull(typeof(SwAddin).GetMethod("CreateToolsMenuEntries", BindingFlags.Instance | BindingFlags.NonPublic));
		Assert.NotNull(typeof(SwAddin).GetMethod("RemoveOwnedToolsParentMenus", BindingFlags.Instance | BindingFlags.NonPublic));
	}
}

