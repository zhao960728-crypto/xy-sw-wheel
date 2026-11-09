using System.Linq;

namespace XY.SolidWorksPlugin;

internal static class ShortcutPaletteDeployment
{
	public const string Value = "shortcut-palette";

	public static int EnsureInAvailableSlots(PluginConfig config)
	{
		if (config?.Menus == null) return 0;
		int added = 0;
		foreach (SceneMenuConfig scene in config.Menus.Values.Where(scene => scene != null))
		{
			if (scene.Rings.SelectMany(ring => ring.Items).Any(item => item.Kind == ActionKinds.ShortcutPalette && item.Value == Value)) continue;
			RingConfig ring = scene.Rings.OrderBy(ring => ring.Index).FirstOrDefault(candidate => candidate.Items.Count < candidate.Slots);
			if (ring == null) continue;
			int slot = Enumerable.Range(0, ring.Slots).First(index => ring.Items.All(item => item.Slot != index));
			ring.Items.Add(new MenuItemConfig { Slot = slot, Label = "\u5feb\u6377\u952e\u5019\u9009", Kind = ActionKinds.ShortcutPalette, Value = Value });
			added++;
		}
		return added;
	}
}
