using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class SceneResolverTests
{
	[Theory]
	[InlineData(SceneKeys.Blank, 0, false)]
	[InlineData(SceneKeys.Part, 1, false)]
	[InlineData(SceneKeys.Assembly, 2, false)]
	[InlineData(SceneKeys.Drawing, 3, false)]
	[InlineData(SceneKeys.Sketch, 1, true)]
	public void EachPageCanBeDisabledWithoutFallingThroughOrLosingBindings(string key, int documentType, bool sketch)
	{
		var config = ConfigStore.Deserialize("{}");
		ConfigStore.Normalize(config);
		config.Menus[key].Rings[0].Items.Add(new MenuItemConfig { Slot = 0, Kind = ActionKinds.Hotkey, Value = "Ctrl+C", Label = "复制" });
		config.Menus[key].Enabled = false;
		config = ConfigStore.Clone(config);
		ConfigStore.Normalize(config);
		Assert.Equal(key, SceneResolver.Resolve(config, documentType, sketch));
		Assert.False(SceneResolver.IsEnabled(config, key));
		Assert.Contains(config.Menus[key].Rings[0].Items, item => item.Value == "Ctrl+C");
		config.Menus[key].Enabled = true;
		Assert.True(SceneResolver.IsEnabled(config, key));
		config.RadialMenuEnabled = false;
		Assert.False(SceneResolver.IsEnabled(config, key));
	}

	[Fact]
	public void OldSketchSettingKeepsFallbackUntilTheSketchSwitchIsExplicitlySet()
	{
		var config = ConfigStore.Deserialize("{}"); ConfigStore.Normalize(config);
		Assert.Equal(SceneKeys.Part, SceneResolver.Resolve(config, 1, true));
		config.SketchWheelEnabled = true;
		Assert.Equal(SceneKeys.Sketch, SceneResolver.Resolve(config, 1, true));
		config.Menus[SceneKeys.Sketch].Enabled = false;
		Assert.Equal(SceneKeys.Sketch, SceneResolver.Resolve(config, 1, true));
		Assert.False(SceneResolver.IsEnabled(config, SceneKeys.Sketch));
		Assert.True(SceneResolver.IsEnabled(config, SceneKeys.Part));
	}

	[Theory]
	[InlineData(1, "sw.part")]
	[InlineData(2, "sw.assembly")]
	[InlineData(3, "sw.drawing")]
	[InlineData(0, "sw.blank")]
	public void DisabledSketchWheelKeepsDocumentScene(int documentType, string expected)
	{
		Assert.Equal(expected, SceneResolver.Resolve(documentType, false, true));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	public void EnabledSketchWheelOverridesDocumentSceneWhileEditing(int documentType)
	{
		Assert.Equal(SceneKeys.Sketch, SceneResolver.Resolve(documentType, true, true));
	}

	[Fact]
	public void EngineeringDrawingKeepsDrawingSceneEvenWhenItExposesSketchObjects()
	{
		Assert.Equal(SceneKeys.Drawing, SceneResolver.Resolve(3, true, true));
	}

	[Fact]
	public void ExitingSketchReturnsToDocumentScene()
	{
		Assert.Equal(SceneKeys.Part, SceneResolver.Resolve(1, true, false));
	}
}
