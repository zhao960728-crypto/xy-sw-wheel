using System.Linq;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ConfigStoreTests
{
	[Fact]
	public void EmptyConfigCreatesFiveBlankScenesWithoutBundledCommands()
	{
		PluginConfig config = ConfigStore.Deserialize("{}");
		ConfigStore.Normalize(config);

		Assert.Equal(5, config.Menus.Count);
		Assert.All(config.Menus.Values, scene =>
		{
			Assert.Single(scene.Rings);
			Assert.Empty(scene.Rings.Single().Items);
			Assert.True(string.IsNullOrEmpty(scene.BackgroundImagePath));
		});
	}

	[Fact]
	public void OldConfigGetsSafeComboAdvanceDefaults()
	{
		PluginConfig config = ConfigStore.Deserialize("{}");

		ConfigStore.Normalize(config);

		Assert.False(config.GlobalComboAdvanceEnabled);
		Assert.False(config.SketchWheelEnabled);
		Assert.True(config.RadialMenuEnabled);
		Assert.True(config.ShortcutPaletteEnabled);
		Assert.True(config.ExplodeEnabled);
		Assert.Equal("Ctrl", config.ComboAdvanceKey);
		Assert.Equal("SimSun", config.FontName);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void SavedExplosionSwitchSurvivesRoundTripWithoutChangingOtherFeatureChoices(bool enabled)
	{
		PluginConfig config = ConfigStore.Deserialize("{\"ExplodeEnabled\":" + (enabled ? "true" : "false")
			+ ",\"RadialMenuEnabled\":false,\"ShortcutPaletteEnabled\":false,\"PropertyWatermark\":{\"Enabled\":false},\"ProcessRecognition\":{\"Enabled\":false}}");
		ConfigStore.Normalize(config);
		PluginConfig copy = ConfigStore.Deserialize(ConfigStore.Serialize(config));
		ConfigStore.Normalize(copy);

		Assert.Equal(enabled, copy.ExplodeEnabled);
		Assert.False(copy.RadialMenuEnabled);
		Assert.False(copy.ShortcutPaletteEnabled);
		Assert.False(copy.PropertyWatermark.Enabled);
		Assert.False(copy.ProcessRecognition.Enabled);
	}

	[Fact]
	public void ExistingCellFontOverridesSurviveNormalization()
	{
		PluginConfig config = ConfigStore.Deserialize("{\"Menus\":{\"sw.part\":{\"Rings\":[{\"Index\":0,\"Slots\":8,\"Items\":[{\"Slot\":0,\"Kind\":\"hotkey\",\"Value\":\"Ctrl+1\",\"FontName\":\"Microsoft YaHei\"}]}]}}}");

		ConfigStore.Normalize(config);

		Assert.Equal("SimSun", config.FontName);
		Assert.Equal("Microsoft YaHei", config.Menus[SceneKeys.Part].Rings[0].Items.Single().FontName);
	}

	[Fact]
	public void GlobalComboAdvanceEnablesEveryComboAcrossScenes()
	{
		PluginConfig config = ConfigStore.Deserialize("{\"GlobalComboAdvanceEnabled\":true,\"ComboAdvanceKey\":\"F8\",\"Menus\":{\"sw.part\":{\"Rings\":[{\"Index\":0,\"Slots\":8,\"Items\":[{\"Slot\":0,\"Kind\":\"combo\",\"Value\":\"test\",\"ComboCommands\":[{\"Kind\":\"hotkey\",\"Value\":\"Ctrl+N\"}]}]}]}}}");

		ConfigStore.Normalize(config);

		MenuItemConfig combo = config.Menus[SceneKeys.Part].Rings[0].Items.Single();
		Assert.True(combo.ComboAdvanceEnabled);
		Assert.Equal("F8", combo.ComboAdvanceKey);
	}

	[Fact]
	public void OldConfigGetsProcessRecognitionDefaultsWithoutChangingExistingSettings()
	{
		PluginConfig config = ConfigStore.Deserialize("{\"TriggerMode\":\"middle-hold\",\"ProcessRecognition\":{\"SummaryPropertyName\":\"工艺流程\",\"SurfaceMaterialRules\":\"AISI=拉丝|Q235=烤漆\"}}");

		ConfigStore.Normalize(config);

		Assert.Equal("middle-hold", config.TriggerMode);
		Assert.True(config.ProcessRecognition.Enabled);
		Assert.Equal("工艺流程", config.ProcessRecognition.SummaryPropertyName);
		Assert.Equal(ProcessRecognitionModes.Deep, config.ProcessRecognition.DefaultMode);
		Assert.True(config.ProcessRecognition.SyncWatermark);
		Assert.True(config.ProcessRecognition.WriteGeometryProperties);
		Assert.Equal("板厚", config.ProcessRecognition.ThicknessPropertyName);
		Assert.Equal("折弯下刀数", config.ProcessRecognition.BendCountPropertyName);
		Assert.Equal("工序类型", config.ProcessRecognition.ProcessTypePropertyName);
		Assert.Equal("备注", config.ProcessRecognition.RemarkPropertyName);
		Assert.Equal(1, config.ProcessRecognition.AssemblyBatchSize);
		Assert.True(config.ProcessRecognition.AssemblyAutoRecognition);
		Assert.Contains("AISI=拉丝", config.ProcessRecognition.SurfaceMaterialRules);
	}

}
