namespace XY.SolidWorksPlugin;

internal sealed class KeyboardShortcutRow
{
	public string Category { get; set; } = "";

	public string Command { get; set; } = "";

	public string Hotkey { get; set; } = "";

	public string ActionKind { get; set; } = ActionKinds.Hotkey;

	public string Value { get; set; } = "";
}
