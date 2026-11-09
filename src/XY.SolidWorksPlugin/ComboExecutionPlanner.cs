using System;
using System.Collections.Generic;
using System.Linq;

namespace XY.SolidWorksPlugin;

internal sealed class ComboExecutionStep
{
	public ComboExecutionStep(
		ComboCommandConfig command,
		bool waitForManualCompletion,
		bool saveSelectionBefore = false,
		bool restoreSelectionAfter = false,
		string advanceKey = "",
		string waitMode = ComboWaitModes.None,
		int delayMs = 0,
		int timeoutMs = 0,
		string onError = ComboErrorPolicies.Stop,
		int retryCount = 0)
	{
		Command = command;
		WaitForManualCompletion = waitForManualCompletion;
		SaveSelectionBefore = saveSelectionBefore;
		RestoreSelectionAfter = restoreSelectionAfter;
		AdvanceKey = advanceKey ?? "";
		WaitMode = waitMode ?? ComboWaitModes.None;
		DelayMs = delayMs;
		TimeoutMs = timeoutMs;
		OnError = onError ?? ComboErrorPolicies.Stop;
		RetryCount = retryCount;
	}

	public ComboCommandConfig Command { get; }

	public bool WaitForManualCompletion { get; }

	public bool SaveSelectionBefore { get; }

	public bool RestoreSelectionAfter { get; }

	// Empty falls back to the cell key, then to the global key.
	public string AdvanceKey { get; }

	public string WaitMode { get; }

	public int DelayMs { get; }

	public int TimeoutMs { get; }

	public string OnError { get; }

	public int RetryCount { get; }

	public bool SkipsOnError => string.Equals(OnError, ComboErrorPolicies.Skip, StringComparison.OrdinalIgnoreCase);

	public bool RetriesOnError =>
		string.Equals(OnError, ComboErrorPolicies.Retry, StringComparison.OrdinalIgnoreCase) && RetryCount > 0;
}

internal static class ComboExecutionPlanner
{
	public static IReadOnlyList<ComboExecutionStep> Build(IEnumerable<ComboCommandConfig> commands)
		=> Build(commands, true);

	// stopOnStepError is the whole-cell switch: when it is off, a step that would
	// abort the sequence skips instead. A step that already chose Retry or Skip
	// keeps its own policy - the cell switch only relaxes the strictest one.
	public static IReadOnlyList<ComboExecutionStep> Build(IEnumerable<ComboCommandConfig> commands, bool stopOnStepError)
	{
		List<ComboExecutionStep> steps = new List<ComboExecutionStep>();
		foreach (ComboCommandConfig command in commands ?? Enumerable.Empty<ComboCommandConfig>())
		{
			if (!IsExecutable(command))
			{
				continue;
			}
			string policy = ComboErrorPolicies.Normalize(command.OnError);
			if (!stopOnStepError
				&& string.Equals(policy, ComboErrorPolicies.Stop, StringComparison.OrdinalIgnoreCase))
			{
				policy = ComboErrorPolicies.Skip;
			}
			steps.Add(new ComboExecutionStep(
				Clone(command),
				ShouldWaitForManualCompletion(command),
				command.SaveSelectionBefore,
				// Restoring only makes sense when something was captured for this step.
				command.RestoreSelectionAfter && command.SaveSelectionBefore,
				command.AdvanceKey,
				ComboWaitModes.Normalize(command.WaitMode),
				// A pure wait command already sleeps for Value during execution.
				// DelayMs is an additional wait only for non-wait commands.
				string.Equals(command.Kind, ActionKinds.Delay, StringComparison.OrdinalIgnoreCase) ? 0 : Math.Max(0, command.DelayMs),
				command.TimeoutMs,
				policy,
				command.RetryCount));
		}
		return steps;
	}

	private static bool ShouldWaitForManualCompletion(ComboCommandConfig command)
	{
		if (command.AutoConfirm)
		{
			return false;
		}
		string kind = (command.Kind ?? ActionKinds.Hotkey).Trim();
		if (kind.Equals(ActionKinds.Delay, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		// An explicit wait mode replaces the generic manual wait.
		return string.Equals(ComboWaitModes.Normalize(command.WaitMode), ComboWaitModes.None, StringComparison.Ordinal);
	}

	private static bool IsExecutable(ComboCommandConfig command)
	{
		return command != null && !string.IsNullOrWhiteSpace(command.Value);
	}

	private static ComboCommandConfig Clone(ComboCommandConfig command)
	{
		return command.Clone();
	}
}
