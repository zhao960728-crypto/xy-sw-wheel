using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swcommands;

namespace XY.SolidWorksPlugin;

// One recording per host. Records command identities, never screen coordinates
// or keyboard text entered into an unrelated application.
internal sealed class CommandRecordingSession : IDisposable
{
	// View changes, file operations and window switches are the user reaching for
	// the next command, never part of the work being recorded.
	private static readonly HashSet<string> NoiseCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"swCommands_ZoomToFit", "swCommands_ZoomToFitKey", "swCommands_ZoomInOut", "swCommands_ZoomInOutKey",
		"swCommands_ZoomToArea", "swCommands_ZoomToAreaKey", "swCommands_ZoomToSelection", "swCommands_ZoomToSelectionKey",
		"swCommands_NormalTo", "swCommands_NormalToKey", "swCommands_RotateView", "swCommands_RotateViewKey",
		"swCommands_Pan", "swCommands_PanKey", "swCommands_Front", "swCommands_Back", "swCommands_Left",
		"swCommands_Right", "swCommands_Top", "swCommands_Bottom", "swCommands_Isometric",
		"swCommands_Save", "swCommands_SaveAs", "swCommands_SaveAll", "swCommands_Print", "swCommands_PrintPreview",
		"swCommands_Close", "swCommands_CloseAll", "swCommands_Open", "swCommands_NewPart", "swCommands_NewAssembly",
		"swCommands_NewDrawing", "swCommands_Undo", "swCommands_Redo", "swCommands_Rebuild", "swCommands_ForceRebuild",
		"swCommands_ViewZoomPage", "swCommands_Refresh", "swCommands_Help", "swCommands_About"
	};

	private static CommandRecordingSession _active;
	private readonly List<ComboCommandConfig> _commands = new List<ComboCommandConfig>();
	private readonly Stopwatch _clockWatch = Stopwatch.StartNew();
	private readonly Func<long> _clock;
	private bool _gapArmed;
	private bool _paused;
	private long _lastCommandAt;
	private long _pauseStartedAt;
	private long _pausedGapMs;
	private long _lastNotificationAt;
	private ComboCommandConfig _lastNotification;
	private int _replaceIndex = -1;
	private bool _observing;

	internal CommandRecordingSession(Func<long> clock = null)
	{
		_clock = clock ?? (() => _clockWatch.ElapsedMilliseconds);
	}

	public event Action Changed;

	public bool Paused
	{
		get => _paused;
		set
		{
			if (_paused == value) return;
			long now = _clock();
			if (value) _pauseStartedAt = now;
			else if (_gapArmed) _pausedGapMs += Math.Max(0, now - _pauseStartedAt);
			_paused = value;
			_lastNotification = null;
		}
	}

	public bool IsReplacing => _replaceIndex >= 0;

	public int ReplacingIndex => _replaceIndex;

	public string LastIssue { get; private set; } = "";

	// Single-command capture includes view/file commands which step recording filters.
	public bool CaptureSingle { get; set; }

	public int Skipped { get; private set; }

	public int Filtered { get; private set; }

	public int Merged { get; private set; }

	// Off by default: gaps are usually thinking time, not a required wait.
	public bool RecordDelays { get; set; }

	public int DelayThresholdMs { get; set; } = 1500;

	public IReadOnlyList<ComboCommandConfig> Commands => _commands;

	public void Start()
	{
		if (_active != null && _active != this) throw new InvalidOperationException("已有一个命令录制窗口，请先结束该录制。");
		_active = this;
		LastIssue = "";
		ResetGap();
	}

	// A completed host command is a real boundary. The next identical command
	// must be retained even if the user repeats it immediately.
	public static void ObserveClosed(int commandId)
	{
		if (_active != null) _active._lastNotification = null;
	}

	public static void Observe(ISldWorks host, int commandId, int userCommand)
	{
		CommandRecordingSession session = _active;
		if (session == null || session.Paused || session._observing) return;
		session._observing = true;
		try
		{
			IModelDoc2 doc = host.ActiveDoc as IModelDoc2;
			int documentType = doc?.GetType() ?? 0;
			// SOLIDWORKS reports the add-in's user command ID separately from the
			// swCommands_User_* event type.  Keep the user ID for third-party
			// commands, but use the actual Command argument for native commands.
			int id = userCommand > 0 ? userCommand : commandId;
			string name = ExternalMenuCommandCatalog.CleanName(host.GetMenuStrings(id, documentType, out string _));
			bool native = userCommand <= 0 && Enum.IsDefined(typeof(swCommands_e), commandId);
			if (!native && ToolbarExternalCommandCatalog.IsRemovedCommand(id, name))
			{
				session.Skipped++;
				session.Changed?.Invoke();
				return;
			}
			string liveCategory = "";
			if (!native && ToolbarExternalCommandCatalog.TryResolveLiveIdentity(host, documentType, id, out string liveName, out string resolvedCategory))
			{
				// The live button/menu identity wins over GetMenuStrings and the
				// static CSV mapping because command IDs can be reused by add-ins.
				if (liveName.Length > 0) name = liveName;
				liveCategory = resolvedCategory;
			}
			if (!native && ToolbarExternalCommandCatalog.IsRemovedCommand(id, name))
			{
				session.Skipped++;
				session.Changed?.Invoke();
				return;
			}
			if (!TryCreateRecordedCommand(id, native, documentType, name, liveCategory, out ComboCommandConfig captured, out string issue))
			{
				session.Skipped++;
				session.LastIssue = issue;
				session.Changed?.Invoke();
				return;
			}
			if (!session.CaptureSingle && native && NoiseCommandNames.Contains(((swCommands_e)id).ToString()))
			{
				session.Filtered++;
				session.Changed?.Invoke();
				return;
			}
			session.Append(captured, true);
		}
		catch (Exception ex)
		{
			session.Skipped++;
			session.LastIssue = "读取命令 " + commandId + " 失败：" + ex.Message;
			Logger.Trace("Recorder skipped unidentified command: " + ex.Message);
			session.Changed?.Invoke();
		}
		finally { session._observing = false; }
	}

	internal static bool TryCreateRecordedCommand(int commandId, bool native, int documentType, string name, string liveCategory,
		out ComboCommandConfig command, out string message)
	{
		command = null;
		message = "";
		name = (name ?? "").Trim();
		bool knownNativeIdentity = native && commandId >= 0 && Enum.IsDefined(typeof(swCommands_e), commandId);
		// Some valid native commands have no GetMenuStrings caption. Their
		// enum identity is authoritative; only the display label needs a fallback.
		if (name.Length == 0 && knownNativeIdentity) name = Enum.GetName(typeof(swCommands_e), commandId);
		string category = (liveCategory ?? "").Trim();
		const string prefix = "第三方插件 > ";
		if (category.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) category = category.Substring(prefix.Length).Trim();
		if (name.Length == 0 || (native ? !knownNativeIdentity : commandId <= 0 || documentType < 1 || documentType > 3))
		{
			message = "未识别命令 " + commandId + "（文档类型 " + documentType + "），未写入步骤。";
			return false;
		}
		if (!native && (category.Length == 0 || category.Equals("未分类插件", StringComparison.OrdinalIgnoreCase)))
		{
			message = $"未捕获“{name}”：无法确认插件菜单或工具栏完整路径。请从目标插件的可见菜单或工具栏重新激发。";
			return false;
		}
		command = new ComboCommandConfig
		{
			Label = name, Kind = native ? ActionKinds.NativeCommand : ActionKinds.ExternalPlugin,
			Value = native ? NativeCommandReference.Encode(commandId) : ExternalNamedCommandReference.Encode(documentType, prefix + category, name),
			RequireAdvanceKey = true
		};
		return true;
	}

	internal void Append(ComboCommandConfig command, bool fromHostNotification = false)
	{
		if (Paused || command == null || string.IsNullOrWhiteSpace(command.Value)) return;
		LastIssue = "";
		long now = _clock();
		// Only suppress a burst of duplicate host notifications. Two consecutive
		// user commands are valid steps; they are never unconditionally merged.
		if (fromHostNotification && _lastNotification != null
			&& now - _lastNotificationAt >= 0 && now - _lastNotificationAt <= 120
			&& string.Equals(_lastNotification.Kind, command.Kind, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(_lastNotification.Value, command.Value, StringComparison.OrdinalIgnoreCase))
		{
			Merged++;
			Changed?.Invoke();
			return;
		}
		_lastNotification = fromHostNotification ? command.Clone() : null;
		_lastNotificationAt = now;
		if (IsReplacing)
		{
			// Replace synchronously inside the host event, before a nested or second
			// command can be captured. Keep the existing wait/failure settings.
			ComboCommandConfig replacement = _commands[_replaceIndex].Clone();
			replacement.Label = command.Label;
			replacement.Kind = command.Kind;
			replacement.Value = command.Value;
			_commands[_replaceIndex] = replacement;
			_replaceIndex = -1;
			Paused = true;
			ResetGap();
			Changed?.Invoke();
			return;
		}
		AppendDelayIfNeeded();
		_commands.Add(command.Clone());
		_lastCommandAt = now;
		_pausedGapMs = 0;
		_gapArmed = true;
		// Pause synchronously in the host notification so nested/next commands
		// cannot replace the user's first click before the UI processes its event.
		if (CaptureSingle) Paused = true;
		Changed?.Invoke();
	}

	private void AppendDelayIfNeeded()
	{
		if (!RecordDelays || !_gapArmed) return;
		long elapsed = Math.Max(0, _clock() - _lastCommandAt - _pausedGapMs);
		int threshold = Math.Max(0, DelayThresholdMs);
		if (elapsed < threshold) return;
		// Only wait steps make sense in a replay; the wait is capped so a long
		// pause never blocks a sequence for minutes.
		int delay = (int)Math.Min(60000, Math.Max(threshold, elapsed));
		_commands.Add(new ComboCommandConfig
		{
			Label = "等待 " + (delay / 1000.0).ToString("0.0") + " 秒",
			Kind = ActionKinds.Delay,
			Value = delay.ToString(),
			WaitMode = ComboWaitModes.Delay,
			DelayMs = delay
		});
	}

	public void Insert(int index, ComboCommandConfig command)
	{
		if (IsReplacing || command == null || string.IsNullOrWhiteSpace(command.Value) && command.Kind != ActionKinds.Delay) return;
		int target = Math.Max(0, Math.Min(_commands.Count, index));
		_commands.Insert(target, command.Clone());
		ResetGap();
		Changed?.Invoke();
	}

	public void RemoveAt(int index)
	{
		if (!IsReplacing && index >= 0 && index < _commands.Count)
		{
			_commands.RemoveAt(index);
			ResetGap();
			Changed?.Invoke();
		}
	}

	public void Move(int index, int delta)
	{
		int target = index + delta;
		if (IsReplacing || index < 0 || index >= _commands.Count || target < 0 || target >= _commands.Count) return;
		ComboCommandConfig command = _commands[index];
		_commands[index] = _commands[target];
		_commands[target] = command;
		ResetGap();
		Changed?.Invoke();
	}

	// Re-recording one step keeps the rest of the sequence intact.
	public void Replace(int index, ComboCommandConfig command)
	{
		if (IsReplacing || index < 0 || index >= _commands.Count || command == null || string.IsNullOrWhiteSpace(command.Value)) return;
		_commands[index] = command.Clone();
		ResetGap();
		Changed?.Invoke();
	}

	public bool BeginReplace(int index)
	{
		if (index < 0 || index >= _commands.Count) return false;
		_replaceIndex = index;
		ResetGap();
		Paused = false;
		Changed?.Invoke();
		return true;
	}

	public void CancelReplace()
	{
		_replaceIndex = -1;
		Paused = true;
		ResetGap();
		Changed?.Invoke();
	}

	private void ResetGap()
	{
		_gapArmed = false;
		_pausedGapMs = 0;
		_lastNotification = null;
	}

	public void Dispose()
	{
		if (ReferenceEquals(_active, this)) _active = null;
		Paused = true;
		_replaceIndex = -1;
		ResetGap();
	}
}
