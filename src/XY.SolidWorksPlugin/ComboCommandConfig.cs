using System;

namespace XY.SolidWorksPlugin;

// How a step decides it may move on. "None" lets the shared advance key rule
// apply, which keeps old configurations behaving exactly as before.
internal static class ComboWaitModes
{
	public const string None = "None";
	public const string Delay = "Delay";
	public const string WindowClosed = "WindowClosed";
	public const string PropertyPane = "PropertyPane";
	public const string ManualConfirm = "ManualConfirm";

	public static string Normalize(string value)
	{
		string mode = (value ?? "").Trim();
		if (mode.Equals(Delay, StringComparison.OrdinalIgnoreCase)) return Delay;
		if (mode.Equals(WindowClosed, StringComparison.OrdinalIgnoreCase)) return WindowClosed;
		if (mode.Equals(PropertyPane, StringComparison.OrdinalIgnoreCase)) return PropertyPane;
		if (mode.Equals(ManualConfirm, StringComparison.OrdinalIgnoreCase)) return ManualConfirm;
		return None;
	}
}

internal static class ComboErrorPolicies
{
	public const string Stop = "Stop";
	public const string Skip = "Skip";
	public const string Retry = "Retry";

	public static string Normalize(string value)
	{
		string policy = (value ?? "").Trim();
		if (policy.Equals(Skip, StringComparison.OrdinalIgnoreCase)) return Skip;
		if (policy.Equals(Retry, StringComparison.OrdinalIgnoreCase)) return Retry;
		return Stop;
	}
}

internal sealed class ComboCommandConfig
{
	public string Label { get; set; } = "";

	public string Kind { get; set; } = ActionKinds.Hotkey;

	public string Value { get; set; } = "";

	public bool AutoConfirm { get; set; }

	public bool RequireAdvanceKey { get; set; }

	// Empty means "use the cell or global key", so one key still drives a whole
	// sequence unless a step deliberately overrides it.
	public string AdvanceKey { get; set; } = "";

	public string WaitMode { get; set; } = ComboWaitModes.None;

	public int DelayMs { get; set; }

	public int TimeoutMs { get; set; }

	public string OnError { get; set; } = ComboErrorPolicies.Stop;

	public int RetryCount { get; set; }

	public bool SaveSelectionBefore { get; set; }

	public bool RestoreSelectionAfter { get; set; }

	internal ComboCommandConfig Clone() => new ComboCommandConfig
	{
		Label = Label,
		Kind = Kind,
		Value = Value,
		AutoConfirm = AutoConfirm,
		RequireAdvanceKey = RequireAdvanceKey,
		AdvanceKey = AdvanceKey,
		WaitMode = WaitMode,
		DelayMs = DelayMs,
		TimeoutMs = TimeoutMs,
		OnError = OnError,
		RetryCount = RetryCount,
		SaveSelectionBefore = SaveSelectionBefore,
		RestoreSelectionAfter = RestoreSelectionAfter
	};
}
