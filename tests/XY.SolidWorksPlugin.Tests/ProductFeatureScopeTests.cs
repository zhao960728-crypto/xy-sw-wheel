using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ProductFeatureScopeTests
{
    [Theory]
    [InlineData("hole-color")]
    [InlineData("hole-process")]
    [InlineData("explode-view")]
    [InlineData("panel-gap-adjustment")]
    [InlineData("panel-gap-display")]
    [InlineData("batch-sheet-metal-correction")]
    [InlineData("convert-to-sheet-metal-auto-corners")]
    [InlineData("property-operations")]
    [InlineData("quote")]
    [InlineData("cut-list-bom")]
    [InlineData("save-close-current-document")]
    [InlineData("rotate-selected-drawing-view-90")]
    [InlineData("open-model-path")]
    public void RetiredOwnCommandsAreAbsentAndCannotExecute(string key)
    {
        var config = new PluginConfig { ExplodeEnabled = true };
        Assert.DoesNotContain(key, BasicActions.All.Keys);
        Assert.False(SwAddin.IsBasicActionEnabled(config, key));
        Assert.Throws<InvalidOperationException>(() => ProductFeatureScope.EnsureExecutable(new MenuItemConfig { Kind = ActionKinds.Basic, Value = key }));
    }

    [Fact]
    public void EntireLegacyComboIsQuarantinedWithoutDroppingItsSteps()
    {
        var config = ConfigStore.Deserialize("{}");
        ConfigStore.Normalize(config);
        var combo = new MenuItemConfig { Slot = 0, Kind = ActionKinds.Combo, StopOnStepError = false,
            ComboCommands = new List<ComboCommandConfig>
            {
                new ComboCommandConfig { Kind = ActionKinds.NativeCommand, Value = "1387" },
                new ComboCommandConfig { Kind = ActionKinds.Basic, Value = "hole-color", OnError = ComboErrorPolicies.Skip },
                new ComboCommandConfig { Kind = ActionKinds.Hotkey, Value = "Ctrl+S" }
            } };
        var scene = config.Menus[SceneKeys.Part];
        scene.Rings[0].Items.Clear();
        scene.Rings[0].Items.Add(combo);
        Assert.Equal(1, ProductFeatureScope.NormalizeConfig(config));
        Assert.Empty(scene.Rings[0].Items);
        Assert.Contains(combo, scene.UnassignedItems);
        Assert.Equal(3, combo.ComboCommands.Count);
        Assert.Throws<InvalidOperationException>(() => ProductFeatureScope.EnsureExecutable(combo));
        Assert.Equal(0, ProductFeatureScope.NormalizeConfig(config));
    }

    [Theory]
    [InlineData(ActionKinds.NativeCommand, "1387")]
    [InlineData(ActionKinds.Hotkey, "Ctrl+S")]
    [InlineData(ActionKinds.ExternalPlugin, "third-party-tool")]
    [InlineData(ActionKinds.QinJian, "ThirdPartyCommand")]
    [InlineData(ActionKinds.JiaLiChuang, "ThirdPartyCommand")]
    [InlineData(ActionKinds.UiClick, "爆炸视图")]
    public void NativeAndUserThirdPartyCommandsRemainAvailable(string kind, string value)
    {
        Assert.False(ProductFeatureScope.IsRetiredAction(kind, value));
        ProductFeatureScope.EnsureExecutable(new MenuItemConfig { Kind = kind, Value = value });
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("watermark-settings")]
    [InlineData("process-recognition")]
    [InlineData("explode-all")]
    [InlineData("new-part")]
    [InlineData("view-front")]
    public void RetainedCategoriesAndNativeShortcutsStaySelectable(string key) => Assert.Contains(key, BasicActions.All.Keys);

    [Fact]
    public void RestoredExplosionDoesNotAutomaticallyReinsertUnassignedBindings()
    {
        var config = ConfigStore.Deserialize("{}");
        ConfigStore.Normalize(config);
        var scene = config.Menus[SceneKeys.Assembly];
        var active = new MenuItemConfig { Slot = 3, Kind = ActionKinds.Hotkey, Value = "Ctrl+S" };
        var unassigned = new MenuItemConfig { Slot = 1, Kind = ActionKinds.Basic, Value = "explode-all", Label = "旧爆炸按钮" };
        scene.Rings[0].Items.Add(active);
        scene.UnassignedItems.Add(unassigned);

        ConfigStore.Normalize(config);

        Assert.Same(active, Assert.Single(scene.Rings[0].Items));
        Assert.Equal(3, active.Slot);
        Assert.Same(unassigned, Assert.Single(scene.UnassignedItems));
        Assert.Equal("旧爆炸按钮", unassigned.Label);
        Assert.False(ProductFeatureScope.IsRetiredBinding(unassigned));
        ProductFeatureScope.EnsureExecutable(unassigned);
    }

    [Fact]
    public void ActiveOneClickExplosionSurvivesWhileLegacyExplodedViewIsQuarantined()
    {
        var config = ConfigStore.Deserialize("{}");
        ConfigStore.Normalize(config);
        var scene = config.Menus[SceneKeys.Assembly];
        var oneClick = new MenuItemConfig { Slot = 0, Kind = ActionKinds.Combo, ComboCommands = new List<ComboCommandConfig>
        {
            new ComboCommandConfig { Kind = ActionKinds.Basic, Value = "explode-all" }
        } };
        var retired = new MenuItemConfig { Slot = 1, Kind = ActionKinds.Basic, Value = "explode-view" };
        scene.Rings[0].Items.Add(oneClick);
        scene.Rings[0].Items.Add(retired);

        Assert.Equal(1, ProductFeatureScope.NormalizeConfig(config));
        Assert.Same(oneClick, Assert.Single(scene.Rings[0].Items));
        Assert.Same(retired, Assert.Single(scene.UnassignedItems));
        ProductFeatureScope.EnsureExecutable(oneClick);
        Assert.Throws<InvalidOperationException>(() => ProductFeatureScope.EnsureExecutable(retired));
    }

    [Fact]
    public void RemovedWindowImplementationsAreNotHostedByTheAddin()
    {
        string[] removed = { "_holeColorWindow", "_holeProcessWindow", "_explodedViewWindow", "_panelGapWindow", "_batchSheetMetalWindow", "_convertToSheetMetalWindow", "_propertyCommandWindow", "_quickWriterWindow" };
        foreach (string name in removed) Assert.Null(typeof(SwAddin).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic));
    }
}
