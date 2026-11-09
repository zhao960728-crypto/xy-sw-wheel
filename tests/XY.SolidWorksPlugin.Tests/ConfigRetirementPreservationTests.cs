using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ConfigRetirementPreservationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LongRecordingsRemainCompleteAcrossNormalizationAndSaving(bool retiredStepAfterSixty)
    {
        var config = new PluginConfig();
        ConfigStore.Normalize(config);
        var commands = Enumerable.Range(1, 75).Select(index => new ComboCommandConfig
        {
            Kind = ActionKinds.Hotkey, Value = "Ctrl+" + (index % 10), Label = "步骤" + index
        }).ToList();
        if (retiredStepAfterSixty)
            commands[60] = new ComboCommandConfig { Kind = ActionKinds.Basic, Value = "legacy-removed-own-tool", Label = "旧功能" };
        config.Menus[SceneKeys.Part].Rings[0].Items.Add(new MenuItemConfig
        {
            Kind = ActionKinds.Combo, Label = "长录制", Slot = 0, ComboCommands = commands
        });

        ConfigStore.Normalize(config);
        var restored = ConfigStore.Deserialize(ConfigStore.Serialize(config));
        ConfigStore.Normalize(restored);
        var scene = restored.Menus[SceneKeys.Part];
        var preserved = Assert.Single(retiredStepAfterSixty
            ? scene.UnassignedItems : scene.Rings.SelectMany(ring => ring.Items));
        Assert.Equal(75, preserved.ComboCommands.Count);
        Assert.Equal(commands.Select(command => command.Value), preserved.ComboCommands.Select(command => command.Value));
        Assert.Equal("步骤75", preserved.ComboCommands[74].Label);
        if (retiredStepAfterSixty)
        {
            Assert.Empty(scene.Rings.SelectMany(ring => ring.Items));
            Assert.Throws<System.InvalidOperationException>(() => ProductFeatureScope.EnsureExecutable(preserved));
        }
        else
            ProductFeatureScope.EnsureExecutable(preserved);
    }

    [Fact]
    public void UnknownRetiredStepCannotDisappearAndLeaveTheRestOfTheComboExecutable()
    {
        var config = new PluginConfig();
        ConfigStore.Normalize(config);
        var combo = new MenuItemConfig
        {
            Kind = ActionKinds.Combo, Label = "旧组合", Slot = 0,
            ComboCommands = new List<ComboCommandConfig>
            {
                new ComboCommandConfig { Kind = ActionKinds.Basic, Value = "legacy-removed-own-tool" },
                new ComboCommandConfig { Kind = ActionKinds.Basic, Value = "rebuild" }
            }
        };
        config.Menus[SceneKeys.Part].Rings[0].Items.Add(combo);
        ConfigStore.Normalize(config);
        var restored = ConfigStore.Deserialize(ConfigStore.Serialize(config));
        ConfigStore.Normalize(restored);
        var scene = restored.Menus[SceneKeys.Part];
        Assert.Empty(scene.Rings.SelectMany(ring => ring.Items));
        var preserved = Assert.Single(scene.UnassignedItems);
        Assert.Equal(2, preserved.ComboCommands.Count);
        Assert.Equal("legacy-removed-own-tool", preserved.ComboCommands[0].Value);
        Assert.Throws<System.InvalidOperationException>(() => ProductFeatureScope.EnsureExecutable(preserved));
    }
}
