using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace XY.SolidWorksPlugin;

internal sealed class ShortcutPaletteSnapshot
{
	public DateTime ReadAtLocal { get; set; }
	public List<KeyboardShortcutRow> Rows { get; set; } = new List<KeyboardShortcutRow>();
}

internal static class ShortcutPaletteStore
{
	public static string Path => System.IO.Path.Combine(ConfigStore.BaseDirectory, "shortcut-palette-cache.json");

	public static void Save(IEnumerable<KeyboardShortcutRow> rows)
	{
		ShortcutPaletteSnapshot snapshot = new ShortcutPaletteSnapshot
		{
			ReadAtLocal = DateTime.Now,
			Rows = (rows ?? Array.Empty<KeyboardShortcutRow>()).Where(ShortcutPaletteMatcher.IsNameCandidate)
				.Select(row => new KeyboardShortcutRow { Category = row.Category ?? "", Command = row.Command ?? "", Hotkey = row.Hotkey ?? "", ActionKind = row.ActionKind ?? ActionKinds.Hotkey, Value = row.Value ?? "" }).ToList()
		};
		Directory.CreateDirectory(ConfigStore.BaseDirectory);
		File.WriteAllText(Path, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(snapshot), new UTF8Encoding(true));
	}

	public static ShortcutPaletteSnapshot Load()
	{
		if (!File.Exists(Path)) return new ShortcutPaletteSnapshot();
		try
		{
			ShortcutPaletteSnapshot snapshot = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<ShortcutPaletteSnapshot>(File.ReadAllText(Path, Encoding.UTF8)) ?? new ShortcutPaletteSnapshot();
			snapshot.Rows = (snapshot.Rows ?? new List<KeyboardShortcutRow>()).Where(ShortcutPaletteMatcher.IsNameCandidate).ToList();
			return snapshot;
		}
		catch (Exception ex)
		{
			Logger.Error("Shortcut palette cache load failed", ex);
			return new ShortcutPaletteSnapshot();
		}
	}
}
