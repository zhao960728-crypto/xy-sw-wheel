using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ShortcutPaletteDeploymentTests
{
	[Fact]
	public void AddsPaletteOnlyToEmptySlotsAndPreservesExistingCommands()
	{
		PluginConfig config = ConfigStore.Deserialize("{\"Menus\":{\"sw.part\":{\"Rings\":[{\"Index\":0,\"Slots\":2,\"Items\":[{\"Slot\":0,\"Kind\":\"hotkey\",\"Value\":\"Ctrl+1\"}]}]}}}");
		int added = ShortcutPaletteDeployment.EnsureInAvailableSlots(config);
		Assert.Equal(1, added);
		Assert.Equal("Ctrl+1", config.Menus["sw.part"].Rings[0].Items.Find(item => item.Slot == 0).Value);
		Assert.Contains(config.Menus["sw.part"].Rings[0].Items, item => item.Kind == ActionKinds.ShortcutPalette && item.Slot == 1);
	}
}
