using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace XY.SolidWorksPlugin;

[ComVisible(true)]
[Guid("8D97B869-0F3D-4F13-901A-0A63D9D13A12")]
[ProgId("XY.SolidWorksPlugin")]
[ClassInterface(ClassInterfaceType.AutoDual)]
public sealed class SwAddin : ISwAddin
{
	public const string AddinGuid = "8D97B869-0F3D-4F13-901A-0A63D9D13A12";

	// SOLIDWORKS command-group IDs are process-wide. Use a stable ID derived from this
	// add-in's COM identity so loading the wheel never removes or replaces another add-in.
	// 1637552377 was used by releases whose command definitions changed in place.
	// SOLIDWORKS caches a command group's layout by ID, so migrate once to a new
	// product-specific ID instead of reusing the damaged layout.
	internal const int CommandGroupId = 1637552379;
	internal const int LegacyCommandGroupId = 1637552378;

	internal const int ExpectedCommandGroupItemCount = 7;
	// -1 is allocated implicitly by SOLIDWORKS and collided with the preceding
	// command after the toolbar was reduced. Use an unused, stable product ID.
	internal const int FeatureSettingsUserId = 53;
	internal const int ExplodeAllUserId = 14;
	internal const int BatchSheetMetalCorrectionUserId = 54;
	internal const int ConvertToSheetMetalUserId = 55;
	internal const int UserMacroImportUserId = 56;
	internal const int ExpectedToolsMenuItemCount = 6;
	internal const int ExpectedToolsMenuDocumentCount = 4;
	internal const int ToolsMenuDocumentTypes = (int)swDocTemplateTypes_e.swDocTemplateTypeNONE
		| (int)swDocTemplateTypes_e.swDocTemplateTypePART
		| (int)swDocTemplateTypes_e.swDocTemplateTypeASSEMBLY
		| (int)swDocTemplateTypes_e.swDocTemplateTypeDRAWING;

	private const int MenuRegistrationDelayMs = 1500;
	private const int MaximumOwnedParentMenusToRemove = 8;
	internal const int DeferredCommandRegistrationAttemptCount = 1;
	private readonly object _menuRegistrationSync = new object();
	private bool _commandGroupRegistrationComplete;
	private int _registeredCommandItemCount;
	private int _registeredToolsMenuDocumentCount;
	private string _commandIconDirectory;
	private bool _commandTabGroupCreated;
	private int[] _wheelTabRuntimeIds = Array.Empty<int>();
	private UserMacroCommandRegistry _userMacroRegistry;
	private string _wheelTabRegistryPath;
	private string _registeredWheelTabTitle = WheelCommandTabs.Title;
	private string _previousWheelTabTitle = WheelCommandTabs.Title;
	private int[] _previousMacroRuntimeIds = Array.Empty<int>();
	private int _userMacroBusy;
	private readonly HashSet<int> _pendingWheelTabMaintenanceTypes = new HashSet<int>();
	private bool _wheelTabRepairCurrentSchema;
	private bool _wheelTabSchemaMigrationPending;
	private string _wheelTabMigrationKey;
	private bool _wheelTabMaintenanceReady, _wheelTabMaintenanceBusy, _wheelTabContextObservationQueued;
	private IModelDoc2 _wheelTabContextDocument;
	private int _wheelTabContextType = -1, _wheelTabContextGeneration;
	private int _wheelTabMaintenanceAttemptsRemaining;
	private DateTime _nextWheelTabMaintenanceUtc;
	private System.Windows.Forms.Timer _wheelTabMaintenanceTimer;

	private const string DisplayName = "XY轮盘";

	private SldWorks? _swApp;
	private IntPtr _hostWindowCache;
	private static readonly uint HostProcessId = (uint)Process.GetCurrentProcess().Id;

	private DSldWorksEvents_Event? _swEvents;

	private long _commandEventSequence;

	private long _lastCommandCloseSequence;

	private long _idleEventSequence;

	private CommandManager _commandManager;
	private int _cookie;

	private MouseWheelTrigger? _trigger;

	private RadialOverlayHost? _overlay;

	private PropertyWatermarkHost? _propertyWatermark;

	private SwCommandDispatcher? _dispatcher;

	private SettingsWindow? _settingsWindow;

	private SettingsWindow? _featureSettingsWindow;

	private AssemblyProcessRecognitionWindow? _assemblyProcessWindow;
	private readonly DocumentRecognitionSessions<AssemblyProcessRecognitionWindow> _assemblyProcessSessions = new DocumentRecognitionSessions<AssemblyProcessRecognitionWindow>();
	private DateTime _lastProcessSessionCleanupUtc = DateTime.MinValue;
    private ProcessRecognitionTaskPaneHost? _processTaskPane;
    private UserControl? _processPanePlaceholder;
    private string _processPaneContext = "";
	private IModelDoc2? _processPaneDocument;
	private IModelDoc2? _automaticPartCandidate;
	private string _automaticPartCandidateConfiguration = "";
	private DateTime _automaticPartCandidateSinceUtc = DateTime.MinValue;
	private ProcessRecognitionWindow? _partProcessWindow;
	private PropertyWatermarkSettingsWindow? _watermarkSettingsWindow;

	private string _automaticAssemblyCandidateKey = "";
	private IModelDoc2? _automaticAssemblyCandidateDocument;
	private DateTime _automaticAssemblyCandidateSinceUtc = DateTime.MinValue;
	private readonly SurfaceProcessLinkService _surfaceProcessLinks = new SurfaceProcessLinkService();
	private bool _surfaceProcessLinkBusy;
	private readonly RecognitionSliceGate _backgroundRecognitionGate = new RecognitionSliceGate(250);
	private System.Windows.Forms.Timer? _backgroundRecognitionTimer;

	private static ShortcutPaletteWindow? _shortcutPalette;

	private PluginConfig _runtimeConfig = new PluginConfig();

	private string _runtimeSceneKey = "sw.blank";

	private volatile bool _connected;

	private int _menuRegistrationQueued;

	private readonly object _comboSync = new object();

	private CancellationTokenSource? _comboCancel;

	private Thread? _comboThread;

	private long _comboRunSequence;
	private int _explodeAllBusy;

	// Keep the startup path light.  External add-in icon discovery is deferred
	// until the first wheel display so a memory-constrained SOLIDWORKS session
	// can finish loading the host and its other add-ins.
	private bool _iconsInitialized;
	private bool _wheelIconRefreshBusy;
	private string _warmedIconScene = "";
	private string _pendingIconScene = "";
	private DateTime _pendingIconSceneSinceUtc;
	private DateTime _nextWheelIconProbeUtc;
	private DateTime _warmedIconConfigWriteUtc;
	private int _warmedIconAssemblyRevision = -1;
	private int _wheelIconAssemblyRevision;
	private bool _wheelIconAssemblyEventsSubscribed;

	public bool ConnectToSW(object ThisSW, int Cookie)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Expected O, but got Unknown
		try
		{
			Logger.Initialize();
			_swApp = (SldWorks)ThisSW;
			Logger.UpdateHostContext("SOLIDWORKS Revision=" + ((ISldWorks)_swApp).RevisionNumber());
			_swEvents = (DSldWorksEvents_Event)_swApp;
			_swEvents.CommandOpenPreNotify += OnCommandOpenPreNotify;
			_swEvents.CommandCloseNotify += OnCommandCloseNotify;
			_swEvents.OnIdleNotify += OnIdleNotify;
			_swEvents.ActiveDocChangeNotify += OnActiveDocumentChangedForWheelTabs;
			_cookie = Cookie;
			_connected = true;
			SubscribeWheelIconAssemblyChanges();
			ProcessHoleFactBridge.Changed += OnProcessHoleFactsChanged;
			_runtimeConfig = ConfigStore.Load();
			ProductFeatureScope.NormalizeConfig(_runtimeConfig);
			int shortcutPaletteAdded = ShortcutPaletteDeployment.EnsureInAvailableSlots(_runtimeConfig);
			// Do not inject retired property batch commands into existing wheels.
			if (shortcutPaletteAdded > 0)
			{
				ConfigStore.SaveWithBackup(_runtimeConfig, "wheel-feature-migration");
			}
			// Keep ConnectToSW lightweight.  Reading every connected add-in's icon
			// resources and shortcut/menu metadata calls into foreign COM add-ins
			// while SOLIDWORKS is still creating its main window.  Some hosts/add-ins
			// expose incomplete UI objects at this point and can exhaust GDI/WinForms
			// resources before the first idle cycle.  The first wheel invocation calls
			// RefreshRuntimeSceneKey again and performs the normal icon scan.
			RefreshRuntimeSceneKey(refreshIcons: false);
			_iconsInitialized = false;
			Logger.Info("ConnectToSW version " + typeof(SwAddin).Assembly.GetName().Version);
			((ISldWorks)_swApp).SetAddinCallbackInfo2(0L, (object)this, _cookie);
			_dispatcher = new SwCommandDispatcher();
			ApplyOptionalFeatureRuntimeState();
			QueueDeferredMenuRegistration();
			return true;
		}
		catch (Exception ex)
		{
			Logger.Error("ConnectToSW failed", ex);
			ResetWheelTabMaintenance();
			try { if (_swEvents != null) _swEvents.ActiveDocChangeNotify -= OnActiveDocumentChangedForWheelTabs; } catch { }
			UnsubscribeWheelIconAssemblyChanges();
			ProcessHoleFactBridge.Changed -= OnProcessHoleFactsChanged;
			WheelCommandTabs.DeleteIcons(_commandIconDirectory);
			_commandIconDirectory = null;
			Logger.Shutdown();
			ToolNotice.Show(ex.Message, DisplayName + " 加载失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			return false;
		}
	}

	private void OnProcessHoleFactsChanged(ProcessHoleFactMessage message)
	{
		if (!_connected || message == null || message.Source == "ProcessRead") return;
		_partProcessWindow?.ApplyHoleProcessFacts(message);
		foreach (var window in _assemblyProcessSessions.Values.Distinct())
			if (!window.IsDisposed) window.ApplyHoleProcessFacts(message);
	}

	public bool DisconnectFromSW()
	{
		Logger.Info("DisconnectFromSW");
		UnsubscribeWheelIconAssemblyChanges();
		ProcessHoleFactBridge.Changed -= OnProcessHoleFactsChanged;
		RadialCommandIcons.Clear();
		_iconsInitialized = false;
		bool success = true;
		try
		{
			_connected = false;
			Volatile.Write(ref _hostWindowCache, IntPtr.Zero);
			ResetWheelTabMaintenance();
			_backgroundRecognitionTimer?.Dispose();
			_backgroundRecognitionTimer = null;
			CancellationTokenSource comboCancel;
			Thread comboThread;
			lock (_comboSync)
			{
				comboCancel = _comboCancel;
				comboThread = _comboThread;
			}
			if (comboCancel != null)
			{
				success &= RunDisconnectCleanup("Cancel active combo during disconnect", comboCancel.Cancel);
			}
			if (comboThread != null && comboThread.IsAlive && !ReferenceEquals(comboThread, Thread.CurrentThread))
			{
				success &= RunDisconnectCleanup("Wait for active combo to stop during disconnect", delegate
				{
					if (!comboThread.Join(5000))
					{
						throw new TimeoutException("组合线程在 SOLIDWORKS 断开前未能及时停止。");
					}
				});
			}
			MouseWheelTrigger trigger = _trigger;
			_trigger = null;
			if (trigger != null)
			{
				success &= RunDisconnectCleanup("Dispose mouse trigger during disconnect", trigger.Dispose);
			}
			RadialOverlayHost overlay = _overlay;
			_overlay = null;
			if (overlay != null)
			{
				success &= RunDisconnectCleanup("Dispose radial overlay during disconnect", overlay.Dispose);
			}
			PropertyWatermarkHost propertyWatermark = _propertyWatermark;
			_propertyWatermark = null;
			if (propertyWatermark != null)
			{
				success &= RunDisconnectCleanup("Dispose property watermark during disconnect", propertyWatermark.Dispose);
			}
			CloseSettingsWindow();
			CloseFeatureSettingsWindow();
			ClosePartProcessWindow();
            CloseAssemblyProcessWindow();
            DisposeProcessTaskPane();




			_watermarkSettingsWindow?.Dispose(); _watermarkSettingsWindow = null;


			foreach (PluginToolWindow tool in Application.OpenForms.OfType<PluginToolWindow>().ToArray())
				success &= RunDisconnectCleanup("Dispose owned tool window during disconnect", tool.Dispose);
			CloseShortcutPalette();
			if (_swApp != null)
			{
				success &= RunDisconnectCleanup("Remove owned SOLIDWORKS Tools menus during disconnect", RemoveOwnedToolsParentMenus);
				success &= RunDisconnectCleanup("Remove owned wheel toolbar during disconnect", RemoveWheelCommandGroup);
			}
			SwCommandDispatcher dispatcher = _dispatcher;
			_dispatcher = null;
			if (dispatcher != null)
			{
				success &= RunDisconnectCleanup("Dispose SOLIDWORKS dispatcher during disconnect", dispatcher.Dispose);
			}
			DSldWorksEvents_Event swEvents = _swEvents;
			_swEvents = null;
			if (swEvents != null)
			{
				success &= RunDisconnectCleanup("Unsubscribe SOLIDWORKS events during disconnect", delegate
				{
					swEvents.CommandOpenPreNotify -= OnCommandOpenPreNotify;
					swEvents.CommandCloseNotify -= OnCommandCloseNotify;
					swEvents.OnIdleNotify -= OnIdleNotify;
					swEvents.ActiveDocChangeNotify -= OnActiveDocumentChangedForWheelTabs;
				});
			}
		}
		catch (Exception ex)
		{
			Logger.Error("DisconnectFromSW failed", ex);
			success = false;
		}
		finally
		{
			_commandManager = null;
			_swApp = null;
			WheelCommandTabs.DeleteIcons(_commandIconDirectory);
			_commandIconDirectory = null;
			Logger.Shutdown();
		}
		return success;
	}

	private static bool RunDisconnectCleanup(string operation, Action cleanup)
	{
		try
		{
			cleanup();
			return true;
		}
		catch (Exception ex)
		{
			Logger.Error(operation, ex);
			return false;
		}
	}

	private int OnCommandOpenPreNotify(int command, int userCommand)
	{
		Interlocked.Increment(ref _commandEventSequence);
		CommandRecordingSession.Observe((ISldWorks)_swApp, command, userCommand);
		return 0;
	}

	private int OnCommandCloseNotify(int command, int reason)
	{
		long sequence = Interlocked.Increment(ref _commandEventSequence);
		Volatile.Write(ref _lastCommandCloseSequence, sequence);
		CommandRecordingSession.ObserveClosed(command);
		return 0;
	}

	private int OnIdleNotify()
	{
		Interlocked.Increment(ref _idleEventSequence);
		if (_dispatcher?.HasPriorityWork == true) return 0;
		if (_wheelTabMaintenanceAttemptsRemaining > 0) RunWheelTabMaintenanceAttempt();
		WarmWheelIconsOnIdle();
		RunBackgroundRecognitionSlice();
		return 0;
	}

	private int OnActiveDocumentChangedForWheelTabs()
	{
		if (!_connected || !_wheelTabMaintenanceReady) return 0;
		if (_pendingWheelTabMaintenanceTypes.Count == 0)
		{ _wheelTabMaintenanceAttemptsRemaining = 0; _wheelTabMaintenanceTimer?.Stop(); return 0; }
		ObserveWheelTabMaintenanceContext();
		// Some native notifications arrive before ActiveDoc commits. Coalesce
		// one posted observation after the current message, without scanning CM
		// again when the document/type context is unchanged.
		if (_pendingWheelTabMaintenanceTypes.Count > 0 && _dispatcher != null && !_wheelTabContextObservationQueued)
		{
			_wheelTabContextObservationQueued = true;
			_dispatcher.Post(() => { _wheelTabContextObservationQueued = false; ObserveWheelTabMaintenanceContext(); });
		}
		return 0;
	}

	private void ObserveWheelTabMaintenanceContext(bool force = false)
	{
		// Registration can be deferred while SW starts. An early document event
		// must neither mark that context complete nor consume its later retries.
		if (!_connected || !_wheelTabMaintenanceReady || _swApp == null) return;
		if (_pendingWheelTabMaintenanceTypes.Count == 0)
		{ _wheelTabMaintenanceAttemptsRemaining = 0; _wheelTabMaintenanceTimer?.Stop(); return; }
		try
		{
			var document = ((ISldWorks)_swApp).ActiveDoc as IModelDoc2;
			int type = document?.GetType() ?? 0;
			if (!force && type == _wheelTabContextType && Equals(document, _wheelTabContextDocument)) return;
			_wheelTabContextDocument = document; _wheelTabContextType = type;
			_wheelTabContextGeneration++;
			_wheelTabMaintenanceAttemptsRemaining = type >= 1 && type <= 3 && _pendingWheelTabMaintenanceTypes.Contains(type) ? 3 : 0;
			_nextWheelTabMaintenanceUtc = DateTime.MinValue;
			RunWheelTabMaintenanceAttempt();
		}
		catch (Exception ex) { Logger.Trace("Wheel context maintenance deferred: " + ex.Message); }
	}

	private void RunWheelTabMaintenanceAttempt()
	{
		if (_pendingWheelTabMaintenanceTypes.Count == 0)
		{ _wheelTabMaintenanceAttemptsRemaining = 0; _wheelTabMaintenanceTimer?.Stop(); return; }
		if (!_connected || !_wheelTabMaintenanceReady || _swApp == null || _commandManager == null || _wheelTabMaintenanceBusy) return;
		if (_wheelTabMaintenanceAttemptsRemaining <= 0)
		{ _wheelTabMaintenanceTimer?.Stop(); return; }
		DateTime now = DateTime.UtcNow;
		if (now < _nextWheelTabMaintenanceUtc) return;
		_nextWheelTabMaintenanceUtc = now.AddMilliseconds(150);
		_wheelTabMaintenanceAttemptsRemaining--;
		int generation = _wheelTabContextGeneration;
		_wheelTabMaintenanceBusy = true;
		try
		{
			var current = ((ISldWorks)_swApp).ActiveDoc as IModelDoc2;
			int type = current?.GetType() ?? 0;
			if (type != _wheelTabContextType || !Equals(current, _wheelTabContextDocument))
			{ ObserveWheelTabMaintenanceContext(); return; }
			if (!_pendingWheelTabMaintenanceTypes.Contains(type)) { _wheelTabMaintenanceAttemptsRemaining = 0; return; }
			// A completed schema can contain a user-customized XY tab. When
			// only an old alias remains, remove that alias without rebuilding XY.
			bool completed = _wheelTabRepairCurrentSchema
				? WheelCommandTabs.TryMaintainConfiguredCurrentDocumentTab((ICommandManager)_commandManager, type,
				_wheelTabRuntimeIds, _registeredWheelTabTitle, _runtimeConfig.WheelCommandTabEnabled,
				_userMacroRegistry?.RuntimeIds ?? Array.Empty<int>(), _previousWheelTabTitle, _previousMacroRuntimeIds, _wheelTabSchemaMigrationPending)
				: WheelCommandTabs.TryRemoveLegacyOwnedTab((ICommandManager)_commandManager, type);
			if (completed && generation == _wheelTabContextGeneration)
			{
				_pendingWheelTabMaintenanceTypes.Remove(type);
				_wheelTabMaintenanceAttemptsRemaining = 0;
				CompleteWheelTabMigrationIfReady();
			}
		}
		catch (Exception ex) { Logger.Trace("Wheel current-context tab repair pending: " + ex.Message); }
		finally
		{
			_wheelTabMaintenanceBusy = false;
			if (_wheelTabMaintenanceAttemptsRemaining > 0 && _pendingWheelTabMaintenanceTypes.Count > 0)
			{
				// A pending transition gets bounded WM_TIMER retries even with
				// process recognition disabled and without an MFC idle callback.
				if (_wheelTabMaintenanceTimer == null)
				{
					_wheelTabMaintenanceTimer = new System.Windows.Forms.Timer { Interval = 150 };
					_wheelTabMaintenanceTimer.Tick += (_, __) => RunWheelTabMaintenanceAttempt();
				}
				_wheelTabMaintenanceTimer.Start();
			}
			else _wheelTabMaintenanceTimer?.Stop();
		}
	}

	private void CompleteWheelTabMigrationIfReady()
	{
		if (!_wheelTabRepairCurrentSchema || _pendingWheelTabMaintenanceTypes.Count > 0 || string.IsNullOrEmpty(_wheelTabMigrationKey)) return;
		WheelCommandTabs.MarkMigrationComplete(_wheelTabMigrationKey);
		Logger.Info("Wheel schema migration committed after all deferred contexts completed. Key=" + _wheelTabMigrationKey);
		_wheelTabMigrationKey = null;
		_wheelTabSchemaMigrationPending = false;
	}

	private void ResetWheelTabMaintenance()
	{
		_wheelTabMaintenanceTimer?.Dispose(); _wheelTabMaintenanceTimer = null;
		_wheelTabMaintenanceReady = false;
		_wheelTabContextObservationQueued = false;
		_wheelTabMaintenanceAttemptsRemaining = 0;
		_wheelTabRuntimeIds = Array.Empty<int>();
		_pendingWheelTabMaintenanceTypes.Clear();
		_wheelTabRepairCurrentSchema = false; _wheelTabSchemaMigrationPending = false; _wheelTabMigrationKey = null;
		_wheelTabRegistryPath = null;
		_previousMacroRuntimeIds = Array.Empty<int>();
		_wheelTabContextDocument = null; _wheelTabContextType = -1;
		_wheelTabContextGeneration++;
		_nextWheelTabMaintenanceUtc = DateTime.MinValue;
	}

	private void SubscribeWheelIconAssemblyChanges()
	{
		if (_wheelIconAssemblyEventsSubscribed) return;
		AppDomain.CurrentDomain.AssemblyLoad += OnWheelIconAssemblyLoaded;
		_wheelIconAssemblyEventsSubscribed = true;
	}

	private void UnsubscribeWheelIconAssemblyChanges()
	{
		if (!_wheelIconAssemblyEventsSubscribed) return;
		AppDomain.CurrentDomain.AssemblyLoad -= OnWheelIconAssemblyLoaded;
		_wheelIconAssemblyEventsSubscribed = false;
	}

	private void OnWheelIconAssemblyLoaded(object sender, AssemblyLoadEventArgs args)
	{
		// AssemblyLoad may run on another thread. Only mark the native STA idle
		// work dirty; never scan assemblies, touch COM or render here.
		Interlocked.Increment(ref _wheelIconAssemblyRevision);
	}

	private void WarmWheelIconsOnIdle()
	{
		if (!_connected || _swApp == null || _runtimeConfig?.RadialMenuEnabled != true || _wheelIconRefreshBusy || _dispatcher?.HasPriorityWork == true) return;
		DateTime now = DateTime.UtcNow;
		if (now < _nextWheelIconProbeUtc || Control.MouseButtons != MouseButtons.None) return;
		_nextWheelIconProbeUtc = now.AddMilliseconds(250);
		try
		{
			IntPtr host = GetSolidWorksWindowHandle();
			if (host == IntPtr.Zero || !NativeMethods.IsWindowEnabled(host)) return;
			string scene = RefreshRuntimeSceneKey(refreshIcons: false);
			if (!string.Equals(scene, _pendingIconScene, StringComparison.Ordinal))
			{
				_pendingIconScene = scene; _pendingIconSceneSinceUtc = now; return;
			}
			if ((now - _pendingIconSceneSinceUtc).TotalMilliseconds < 150) return;
			DateTime written = System.IO.File.GetLastWriteTimeUtc(ConfigStore.ConfigPath);
			int assemblyRevision = Volatile.Read(ref _wheelIconAssemblyRevision);
			if (_iconsInitialized && scene == _warmedIconScene && written == _warmedIconConfigWriteUtc && assemblyRevision == _warmedIconAssemblyRevision) return;
			_wheelIconRefreshBusy = true;
			RefreshRuntimeSceneKey(refreshIcons: true);
			_overlay?.Warmup(_runtimeConfig, scene);
		}
		catch (Exception ex) { Logger.Trace("Idle wheel icon warmup deferred: " + ex.Message); }
		finally { _wheelIconRefreshBusy = false; }
	}

	private void RunBackgroundRecognitionSlice()
	{
		if (_dispatcher?.HasPriorityWork == true) return;
		if (!_connected || _runtimeConfig?.ProcessRecognition?.Enabled != true) return;
		// Both WM_TIMER and OnIdleNotify run on the SW UI/STA thread. The shared
		// gate prevents nested COM work and duplicate slices; input activity itself
		// is not a blocker. WM_TIMER is low priority so queued input runs first.
		if (!_backgroundRecognitionGate.TryEnter(DateTime.UtcNow)) return;
		try
		{
			if (IsSolidWorksBusyForBackgroundRecognition()) return;
            SynchronizeCurrentSurfaceProcess();
            RefreshProcessSidebarContext();
            CleanupClosedProcessSessions();
            EnsureAutomaticPartRecognition();
            EnsureAutomaticAssemblyRecognition();
            _assemblyProcessWindow?.OnSolidWorksIdle();
		}
		catch (Exception ex) { Logger.Trace("Assembly process background slice skipped: " + ex.Message); }
		finally { _backgroundRecognitionGate.Exit(); }
	}

    private void SynchronizeCurrentSurfaceProcess()
    {
        var document = WatermarkPropertyAccess.ResolveDocument((ISldWorks)_swApp, true, out string configuration);
        SynchronizeSurfaceProcess(document, configuration);
    }

    private void SynchronizeSurfaceProcess(IModelDoc2 document, string configuration)
    {
        if (_surfaceProcessLinkBusy || _runtimeConfig?.ProcessRecognition?.Enabled != true) return;
        _surfaceProcessLinkBusy = true;
        try
        {
            var result = _surfaceProcessLinks.Synchronize(document, configuration, _runtimeConfig.ProcessRecognition);
            _partProcessWindow?.ApplySurfaceProcessLink(result);
            _assemblyProcessWindow?.ApplySurfaceProcessLink(result);
            if (result.Changed) _propertyWatermark?.Reload();
        }
        catch (Exception ex) { Logger.Trace("Surface process synchronization preserved current properties: " + ex.Message); }
        finally { _surfaceProcessLinkBusy = false; }
    }

	private void UpdateBackgroundRecognitionTimer()
	{
		if (_runtimeConfig?.ProcessRecognition?.Enabled != true)
		{
			_backgroundRecognitionTimer?.Dispose();
			_backgroundRecognitionTimer = null;
			return;
		}
		if (_backgroundRecognitionTimer != null) return;
		_backgroundRecognitionTimer = new System.Windows.Forms.Timer { Interval = 250 };
		_backgroundRecognitionTimer.Tick += (_, __) => RunBackgroundRecognitionSlice();
		_backgroundRecognitionTimer.Start();
	}

	private bool IsSolidWorksBusyForBackgroundRecognition()
	{
		SldWorks? swApp = _swApp;
		if (swApp == null) return true;
		IntPtr host = GetSolidWorksWindowHandle();
		if (host == IntPtr.Zero || !NativeMethods.IsWindowEnabled(host)) return true;
		// A held mouse button can mean view rotation or a component drag. Resume
		// at the next released-message slice, without an additional idle timeout.
		if (Control.MouseButtons != MouseButtons.None) return true;
		bool inProgress = false;
		int commandId = 0;
		string title = "";
		bool isUiActive = false;
		try { inProgress = ((ISldWorks)swApp).CommandInProgress; }
		catch { return true; }
		try { ((ISldWorks)swApp).GetRunningCommandInfo(out commandId, out title, out isUiActive); }
		catch { return true; }
		// SOLIDWORKS returns negative sentinel ids (for example -3) while idle.
		// They must not permanently suppress the automatic background queue.
		return inProgress || isUiActive || commandId > 0 || !string.IsNullOrWhiteSpace(title);
	}

	public void ShowRadialMenuCommand()
	{
		if (_runtimeConfig?.RadialMenuEnabled != true) return;
		Logger.Info("ShowRadialMenuCommand callback");
		OpenWheelNow(Cursor.Position, dragMode: false);
	}

	public int ShowRadialMenuEnable()
	{
		return SceneResolver.IsEnabled(_runtimeConfig, ResolveSceneKey()) ? 1 : 0;
	}

	public void OpenWheelMenuCallback()
	{
		if (_runtimeConfig?.RadialMenuEnabled != true) return;
		Logger.Info("OpenWheelMenuCallback callback");
		OpenWheelNow(Cursor.Position, dragMode: false);
	}

	public int OpenWheelMenuEnable()
	{
		return ShowRadialMenuEnable();
	}

	public void SettingsMenuCallback()
	{
		Logger.Info("SettingsMenuCallback callback");
		ShowSettings();
	}

	public int SettingsMenuEnable()
	{
		return 1;
	}

	public void FeatureSettingsMenuCallback()
	{
		Logger.Info("FeatureSettingsMenuCallback callback");
		ShowFeatureSettings();
	}

	public int FeatureSettingsMenuEnable()
	{
		return 1;
	}

	public void ShowUserMacroImportCommand()
	{
		ShowFeatureSettings();
		_featureSettingsWindow?.BeginImportUserMacro();
	}

	public int ShowUserMacroImportCommandEnable() => _connected ? 1 : 0;

	// SOLIDWORKS passes the argument text as a string, not an integer.
	public void RunUserMacroCommand(string data)
	{
		if (!_connected || _swApp == null) return;
		try
		{
			var command = UserMacroCommandRegistry.Resolve(_userMacroRegistry?.RegisteredCommands, _runtimeConfig.UserMacros, data);
			var document = ((ISldWorks)_swApp).ActiveDoc as IModelDoc2;
			if (_dispatcher == null) throw new InvalidOperationException("SOLIDWORKS 调度器不可用，宏尚未执行。");
			_dispatcher.PostPriority(() =>
			{
				try
				{
					if (!_connected || _swApp == null || !Equals(document, ((ISldWorks)_swApp).ActiveDoc as IModelDoc2))
						throw new InvalidOperationException("宏排队期间活动文档已变化，未改投新文档。");
					var currentCommand = UserMacroCommandRegistry.Resolve(_userMacroRegistry?.RegisteredCommands, _runtimeConfig.UserMacros, data);
					if (UserMacroCommandRegistry.Fingerprint(new[] { command }) != UserMacroCommandRegistry.Fingerprint(new[] { currentCommand }))
						throw new InvalidOperationException("宏排队期间配置已变化，未执行旧入口。");
					if (Interlocked.CompareExchange(ref _userMacroBusy, 1, 0) != 0)
						throw new InvalidOperationException("已有用户宏正在运行，请完成当前宏后再执行。");
					try
					{
						ActivateSolidWorks();
						if (!Equals(document, ((ISldWorks)_swApp).ActiveDoc as IModelDoc2))
							throw new InvalidOperationException("宏激发前活动文档已变化，未改投新文档。");
						currentCommand = UserMacroCommandRegistry.Resolve(_userMacroRegistry?.RegisteredCommands, _runtimeConfig.UserMacros, data);
						if (UserMacroCommandRegistry.Fingerprint(new[] { command }) != UserMacroCommandRegistry.Fingerprint(new[] { currentCommand }))
							throw new InvalidOperationException("宏激发前配置已变化，未执行旧入口。");
						UserMacroStore.Execute((ISldWorks)_swApp, command);
					}
					finally { Interlocked.Exchange(ref _userMacroBusy, 0); }
				}
				catch (Exception error) { ReportUserMacroFailure(error); }
			});
		}
		catch (Exception error) { ReportUserMacroFailure(error); }
	}

	public int RunUserMacroCommandEnable(string data)
	{
		if (!_connected || _swApp == null || Volatile.Read(ref _userMacroBusy) != 0) return 0;
		try
		{
			var command = UserMacroCommandRegistry.Resolve(_userMacroRegistry?.RegisteredCommands, _runtimeConfig.UserMacros, data);
			UserMacroStore.Validate(command);
			return 1;
		}
		catch { return 0; }
	}

	private void ReportUserMacroFailure(Exception error)
	{
		Logger.Error("User macro command failed", error);
		ToolNotice.Show(error.Message, "用户宏执行失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
	}

	public void ShowPropertyWatermarkSettings()
	{
		try
		{
			if (_watermarkSettingsWindow != null && !_watermarkSettingsWindow.IsDisposed) { _watermarkSettingsWindow.Activate(); return; }
			PropertyWatermarkSettingsWindow dialog = new PropertyWatermarkSettingsWindow(
				ConfigStore.Load,
				config =>
				{
					ConfigStore.Save(config);
					_runtimeConfig = config;
					ApplyOptionalFeatureRuntimeState();
				},
				() => _swApp == null ? null : (ISldWorks)_swApp,
				() => ApplyOptionalFeatureRuntimeState());
			_watermarkSettingsWindow = dialog;
			dialog.ShowModeless();
		}
		catch (Exception ex)
		{
			Logger.Error("ShowPropertyWatermarkSettings failed", ex);
			ToolNotice.Show(ex.Message, DisplayName + " 属性水印设置打开失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	public int ShowPropertyWatermarkSettingsEnable()
	{
		return 1;
	}




























































































	public void ShowProcessRecognition()
	{
		try
		{
			if (_runtimeConfig?.ProcessRecognition?.Enabled != true)
			{
				ToolNotice.Show("工序识别已关闭，请在轮盘设置的“工序识别”页面开启后再使用。", DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}
			IModelDoc2 current = _swApp == null ? null : ((ISldWorks)_swApp).ActiveDoc as IModelDoc2;
			if (current == null) { ShowProcessPanePlaceholder(true); return; }
            if (current.GetType() != (int)swDocumentTypes_e.swDocPART && current.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY) { ShowProcessPanePlaceholder(true); return; }
			if (current.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
			{
				EnsureAssemblyRecognition(current, showWindow: true);
				return;
			}
            ShowPartProcessSidebar(true);
			_propertyWatermark?.Reload();
		}
		catch (Exception ex)
		{
			Logger.Error("ShowProcessRecognition failed", ex);
			ToolNotice.Show(ex.Message, DisplayName + " 工序识别打开失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

    private void EnsureProcessTaskPane()
    {
        if (_processTaskPane != null) return;
        _processTaskPane = new ProcessRecognitionTaskPaneHost((ISldWorks)_swApp);
        _processPanePlaceholder = new UserControl { Dock = DockStyle.Fill, BackColor = System.Drawing.Color.FromArgb(245, 248, 246) };
        _processPanePlaceholder.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleCenter, Text = "打开零件或装配体后，\n在这里识别与编辑工序。" });
        _processTaskPane.Attach(_processPanePlaceholder, false);
    }

    private void ShowProcessPanePlaceholder(bool show)
    {
        EnsureProcessTaskPane();
        if (!ReferenceEquals(_processTaskPane.Content, _processPanePlaceholder)) _processTaskPane.Attach(_processPanePlaceholder, false);
        if (show) _processTaskPane.Show();
    }

    private void ShowPartProcessSidebar(bool show)
    {
			if (_partProcessWindow == null || _partProcessWindow.IsDisposed) _partProcessWindow = new ProcessRecognitionWindow(
				() => _swApp == null ? null : (ISldWorks)_swApp,
				() => _runtimeConfig.ProcessRecognition,
				config =>
				{
					_runtimeConfig.ProcessRecognition = config;
					ConfigStore.Save(_runtimeConfig);
				},
				() => _propertyWatermark?.Reload());
        EnsureProcessTaskPane();
        _partProcessWindow.SidebarCloseRequested = () => _processTaskPane?.Hide();
        _partProcessWindow.PrepareForSidebar();
        
        if (!ReferenceEquals(_processTaskPane.Content, _partProcessWindow)) _processTaskPane.Attach(_partProcessWindow, false);
        _partProcessWindow.ResumeRecognitionCore(background: !show, allowRead: show);
        if (show) _processTaskPane.Show();
    }

    private void AttachAssemblyProcessSidebar(AssemblyProcessRecognitionWindow window, bool show)
    {
        EnsureProcessTaskPane();
        window.SidebarCloseRequested = () => _processTaskPane?.Hide();
        window.PrepareForSidebar();
        if (!ReferenceEquals(_processTaskPane.Content, window)) _processTaskPane.Attach(window, false);
        if (show) _processTaskPane.Show();
    }

    private void RefreshProcessSidebarContext()
    {
        IModelDoc2 current = ((ISldWorks)_swApp).ActiveDoc as IModelDoc2;
        int type = current?.GetType() ?? 0;
        string key = current == null ? "blank" : type + ":" + (type == (int)swDocumentTypes_e.swDocPART || type == (int)swDocumentTypes_e.swDocASSEMBLY ? current.ConfigurationManager.ActiveConfiguration.Name : "");
        if (_processPaneContext == key && Equals(_processPaneDocument, current)) return;
        if (_assemblyProcessWindow != null && !_assemblyProcessWindow.MatchesDocument(current)) _assemblyProcessWindow.PauseForContext();
        if (current == null || (current.GetType() != (int)swDocumentTypes_e.swDocPART && current.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)) ShowProcessPanePlaceholder(false);
        else if (current.GetType() == (int)swDocumentTypes_e.swDocPART) ShowPartProcessSidebar(false);
        else if (_assemblyProcessWindow != null && _assemblyProcessWindow.MatchesDocument(current))
        { _assemblyProcessWindow.RefreshSettings(_runtimeConfig.ProcessRecognition); _assemblyProcessWindow.ResumeForDisplay(); _assemblyProcessWindow.StartBackground(); AttachAssemblyProcessSidebar(_assemblyProcessWindow, false); }
        else ShowProcessPanePlaceholder(false);
        _processPaneContext = key;
        _processPaneDocument = current;
    }
	private void EnsureAutomaticPartRecognition()
	{
		var current = ((ISldWorks)_swApp).ActiveDoc as IModelDoc2;
		if (current == null || current.GetType() != (int)swDocumentTypes_e.swDocPART)
		{ _automaticPartCandidate = null; _automaticPartCandidateConfiguration = ""; return; }
		string configuration = current.ConfigurationManager.ActiveConfiguration.Name;
		if (!Equals(_automaticPartCandidate, current) || _automaticPartCandidateConfiguration != configuration)
		{ _automaticPartCandidate = current; _automaticPartCandidateConfiguration = configuration; _automaticPartCandidateSinceUtc = DateTime.UtcNow; return; }
		if ((DateTime.UtcNow - _automaticPartCandidateSinceUtc).TotalMilliseconds < 500) return;
		if (_partProcessWindow == null || _partProcessWindow.IsDisposed) ShowPartProcessSidebar(false);
		_partProcessWindow.ResumeRecognitionCore(background: true);
	}
	private void CleanupClosedProcessSessions()
	{
		if ((DateTime.UtcNow - _lastProcessSessionCleanupUtc).TotalMilliseconds < 1000) return;
		_lastProcessSessionCleanupUtc = DateTime.UtcNow;
		object value = ((ISldWorks)_swApp).GetDocuments();
		if (value == null && ((ISldWorks)_swApp).ActiveDoc != null) return;
		if (value != null && !(value is Array)) return;
		var documents = value is Array array ? array.Cast<object>().ToArray() : Array.Empty<object>();
		_partProcessWindow?.RetainOpenDocuments(documents);
		foreach (var closed in _assemblyProcessSessions.RetainOpenDocuments(documents)) DisposeAssemblyProcessSession(closed);
	}

    private void DisposeProcessTaskPane()
    {
        _processTaskPane?.Dispose(); _processTaskPane = null;
        _processPanePlaceholder?.Dispose(); _processPanePlaceholder = null;
        _processPaneContext = "";
        _processPaneDocument = null;
    }

	private void EnsureAutomaticAssemblyRecognition()
	{
		if (_runtimeConfig.ProcessRecognition?.Enabled != true) return;
		IModelDoc2 document = _swApp == null ? null : ((ISldWorks)_swApp).ActiveDoc as IModelDoc2;
		if (document == null || document.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
		{
			ResetAutomaticAssemblyCandidate();
			return;
		}
		string key = document.ConfigurationManager.ActiveConfiguration.Name;
		// Do not join the document-open/create path. An assembly must stay active
		// for a short settle interval before the read-only loaded-component queue
		// is built. This also supports unsaved assemblies without saving them.
		if (!Equals(_automaticAssemblyCandidateDocument, document) || !string.Equals(_automaticAssemblyCandidateKey, key, StringComparison.OrdinalIgnoreCase))
		{
			_automaticAssemblyCandidateDocument = document;
			_automaticAssemblyCandidateKey = key;
			_automaticAssemblyCandidateSinceUtc = DateTime.UtcNow;
			return;
		}
		if ((DateTime.UtcNow - _automaticAssemblyCandidateSinceUtc).TotalMilliseconds < 500) return;
		if (_assemblyProcessWindow != null && !_assemblyProcessWindow.IsDisposed && _assemblyProcessWindow.MatchesDocument(document) && _assemblyProcessWindow.MatchesSettings(_runtimeConfig.ProcessRecognition)) return;
		EnsureAssemblyRecognition(document, showWindow: false);
	}

	private void ResetAutomaticAssemblyCandidate()
	{
		_automaticAssemblyCandidateKey = "";
		_automaticAssemblyCandidateDocument = null;
		_automaticAssemblyCandidateSinceUtc = DateTime.MinValue;
	}

	private void EnsureAssemblyRecognition(IModelDoc2 document, bool showWindow)
	{
		string configuration = document.ConfigurationManager.ActiveConfiguration.Name;
		if (_assemblyProcessSessions.TryGet(document, configuration, out AssemblyProcessRecognitionWindow saved) && !saved.IsDisposed)
		{
			if (_assemblyProcessWindow != null && !ReferenceEquals(_assemblyProcessWindow, saved)) _assemblyProcessWindow.PauseForContext();
			_assemblyProcessWindow = saved; saved.RefreshSettings(_runtimeConfig.ProcessRecognition);
			if (showWindow) saved.PromoteToInteractive();
			saved.ResumeForDisplay(); saved.StartBackground(); AttachAssemblyProcessSidebar(saved, showWindow); return;
		}
		_assemblyProcessWindow?.PauseForContext();
		AssemblyProcessRecognitionWindow window = new AssemblyProcessRecognitionWindow(
			() => _swApp == null ? null : (ISldWorks)_swApp,
			document,
			_runtimeConfig.ProcessRecognition,
			() => _propertyWatermark?.Reload(),
			automaticBackground: !showWindow);
		_assemblyProcessWindow = window;
		_assemblyProcessSessions.Put(document, configuration, window);
		window.SaveRules = rules => { var latest = ProcessRulesWindow.Copy(_runtimeConfig.ProcessRecognition); ProcessRulesWindow.CopyRules(rules, latest); _runtimeConfig.ProcessRecognition = latest; ConfigStore.Save(_runtimeConfig); };
		window.FormClosed += (_, __) => { _assemblyProcessSessions.Remove(window); if (ReferenceEquals(_assemblyProcessWindow, window)) _assemblyProcessWindow = null; };
		window.StartBackground();
        AttachAssemblyProcessSidebar(window, showWindow);
    }

    private void ApplyOptionalFeatureRuntimeState()
	{
		ProductFeatureScope.NormalizeConfig(_runtimeConfig);
        UpdateBackgroundRecognitionTimer();
        if (_runtimeConfig?.ProcessRecognition?.Enabled == true) EnsureProcessTaskPane();
        else DisposeProcessTaskPane();
		if (_runtimeConfig?.RadialMenuEnabled == true)
		{
			if (_overlay == null)
			{
				_overlay = new RadialOverlayHost(QueueExecuteItem, GetSolidWorksWindowHandle());
				_overlay.Start();
			}
			_overlay.Warmup(_runtimeConfig, _runtimeSceneKey);
			if (_trigger == null)
			{
				_trigger = new MouseWheelTrigger(() => SceneResolver.IsEnabled(_runtimeConfig, ResolveSceneKey())
					? _runtimeConfig.TriggerMode : TriggerModes.Disabled, () => _runtimeConfig.HoldDelayMs, GetSolidWorksWindowHandle, delegate
				{
					// The hold callback only resolves scene identity; foreign COM/icon reads run on idle.
					RefreshRuntimeSceneKey(refreshIcons: false);
					_overlay?.Start();
				}, delegate(Point point, MouseButtons button)
				{
					OpenWheelNow(point, dragMode: true, button);
				}, delegate(Point point)
				{
					_overlay?.Commit(point);
				}, cancelMenu: () => _overlay?.Cancel() == true);
				_trigger.Start();
			}
		}
		else
		{
			MouseWheelTrigger trigger = _trigger;
			_trigger = null;
			trigger?.Dispose();
			RadialOverlayHost overlay = _overlay;
			_overlay = null;
			overlay?.Dispose();
		}
		if (_runtimeConfig?.ShortcutPaletteEnabled != true)
		{
			CloseShortcutPalette();
		}
		bool watermarkEnabled = _runtimeConfig?.PropertyWatermark?.Enabled == true;
		if (watermarkEnabled)
		{
			if (_propertyWatermark == null)
			{
				_propertyWatermark = new PropertyWatermarkHost(
					GetSolidWorksWindowHandle,
					() => _swApp == null ? null : (ISldWorks)_swApp,
					() => _runtimeConfig.PropertyWatermark,
					() => ConfigStore.Save(_runtimeConfig),
					action => { if (_dispatcher != null) _dispatcher.Invoke(action); });
				_propertyWatermark.Start();
			}
			else _propertyWatermark.Reload();
		}
		else if (_propertyWatermark != null)
		{
			PropertyWatermarkHost watermark = _propertyWatermark;
			_propertyWatermark = null;
			watermark.Dispose();
		}

		if (_runtimeConfig?.ProcessRecognition?.Enabled != true)
		{
			ClosePartProcessWindow();
			CloseAssemblyProcessWindow();
			ResetAutomaticAssemblyCandidate();
		}


	}

	private static string AssemblyRecognitionKey(IModelDoc2 document)
	{
		string path = document?.GetPathName() ?? "";
		return path.Length > 0 ? path : document?.GetTitle() ?? "";
	}

	public int ShowProcessRecognitionEnable()
	{
		if (_runtimeConfig?.ProcessRecognition?.Enabled != true) return 0;
		IModelDoc2 document = _swApp == null ? null : ((ISldWorks)_swApp).ActiveDoc as IModelDoc2;
		if (document == null) return 0;
		int type = document.GetType();
		return type == (int)swDocumentTypes_e.swDocPART || type == (int)swDocumentTypes_e.swDocASSEMBLY ? 1 : 0;
	}






	public void ShowExplodeAllCommand()
	{
		try { ExecuteExplodeAll(); }
		catch (OperationCanceledException) { PostSolidWorksStatusBarText("XY轮盘：一键爆炸已取消。"); }
		catch (Exception ex)
		{
			Logger.Error("One-click explode failed", ex);
			ToolNotice.Show(ex.Message, "一键爆炸", MessageBoxButtons.OK, MessageBoxIcon.Information);
		}
	}

	public int ShowExplodeAllCommandEnable()
	{
		if (_runtimeConfig?.ExplodeEnabled != true || Volatile.Read(ref _explodeAllBusy) != 0) return 0;
		try
		{
			var document = _swApp == null ? null : ((ISldWorks)_swApp).ActiveDoc as IModelDoc2;
			return (document is IPartDoc || document is IAssemblyDoc) && !document.IsOpenedReadOnly() && document.GetActiveSketch2() == null ? 1 : 0;
		}
		catch { return 0; }
	}

	private void ExecuteExplodeAll()
	{
		if (_runtimeConfig?.ExplodeEnabled != true) throw new InvalidOperationException("一键爆炸已关闭，请在功能开关中启用。");
		if (_swApp == null) throw new InvalidOperationException("SOLIDWORKS 未连接。");
		if (Interlocked.CompareExchange(ref _explodeAllBusy, 1, 0) != 0) throw new InvalidOperationException("一键爆炸正在处理，请等待完成或按 Esc 取消。");
		try
		{
			var dispatcher = _dispatcher ?? throw new InvalidOperationException("SOLIDWORKS 主线程调度器不可用。");
			dispatcher.Invoke(() =>
			{
				using var cancellation = new CancellationTokenSource();
				var frame = ((ISldWorks)_swApp).IFrameObject() as IFrame;
				var service = new ExplodedViewService((ISldWorks)_swApp);
				service.CreateAllAutomatic(cancellation.Token, message =>
				{
					if (NativeMethods.GetAsyncKeyState(27) < 0) cancellation.Cancel();
					cancellation.Token.ThrowIfCancellationRequested();
					frame?.SetStatusBarText("XY轮盘：" + message + "（Esc 取消）");
				});
				frame?.SetStatusBarText("XY轮盘：" + service.LastAutomaticSummary);
			});
		}
		finally { Interlocked.Exchange(ref _explodeAllBusy, 0); }
	}



	private void OpenWheelNow(Point center, bool dragMode, MouseButtons triggerButton = MouseButtons.Right)
	{
		if (_runtimeConfig?.RadialMenuEnabled != true) return;
		try
		{
			PluginConfig runtimeConfig = _runtimeConfig;
			string sceneKey;
			if (!dragMode)
			{
				sceneKey = RefreshRuntimeSceneKey(refreshIcons: false);
			}
			else
			{
				// Even the first held-button activation must show immediately.
				// Prepare already captured the scene; draw cached/fallback artwork
				// until the native STA idle path finishes icon discovery.
				sceneKey = _runtimeSceneKey;
			}
			if (SceneResolver.IsEnabled(runtimeConfig, sceneKey))
				_overlay?.Show(runtimeConfig, sceneKey, center, dragMode, triggerButton);
		}
		catch (Exception ex)
		{
			Logger.Error("OpenWheelNow failed", ex);
			SldWorks? swApp = _swApp;
			if (swApp != null)
			{
				((ISldWorks)swApp).SendMsgToUser2("XY轮盘打开失败: " + ex.Message, 0, 0);
			}
		}
	}

	private void QueueExecuteItem(MenuItemConfig item)
	{
		NativeCommandLaunchTrace.Mark("queued", item.Value);
		if (!item.HasSteps() && item.Kind.Equals(ActionKinds.ShortcutPalette, StringComparison.OrdinalIgnoreCase)
			&& item.Value.Equals(ShortcutPaletteDeployment.Value, StringComparison.OrdinalIgnoreCase))
		{
			_dispatcher?.PostPriority(ShowShortcutPalette);
			return;
		}
		if (item.Kind.Equals(ActionKinds.Combo, StringComparison.OrdinalIgnoreCase) || item.HasSteps())
		{
			CancellationTokenSource cts;
			Thread? predecessor;
			long runSequence;
			lock (_comboSync)
			{
				_comboCancel?.Cancel();
				predecessor = _comboThread;
				cts = new CancellationTokenSource();
				_comboCancel = cts;
				runSequence = ++_comboRunSequence;
			}
			Thread thread = new Thread(delegate()
			{
				try
				{
					ComboRunOutcome outcome = ComboWorkerRunner.Run(
						predecessor,
						cts.Token,
						delegate(CancellationToken token)
						{
							Logger.Info($"Combo run {runSequence} started. ConfiguredSteps={item.ComboCommands?.Count ?? 0}");
							return ExecuteComboItem(item, token);
						},
						delegate(Exception ex)
						{
							Logger.Error($"Combo worker {runSequence} failed outside command execution", ex);
							PostSolidWorksStatusBarText("XY轮盘：组合执行已安全停止，详细信息已写入 error.log");
						});
					Logger.Info($"Combo run {runSequence} {outcome.ToString().ToLowerInvariant()}.");
				}
				finally
				{
					lock (_comboSync)
					{
						if (ReferenceEquals(_comboCancel, cts))
						{
							_comboCancel = null;
							_comboThread = null;
						}
					}
					cts.Dispose();
				}
			})
			{
				IsBackground = true,
				Name = "XYComboExecutor"
			};
			thread.SetApartmentState(ApartmentState.STA);
			lock (_comboSync)
			{
				_comboThread = thread;
			}
			thread.Start();
			return;
		}
		_dispatcher?.PostPriority(delegate
		{
			NativeCommandLaunchTrace.Mark("dispatch-start", item.Value);
			ExecuteItem(item);
		});
	}

	public void ShowSettings()
	{
		try
		{
			RefreshRuntimeSceneKey();
			if (_settingsWindow != null && !_settingsWindow.IsDisposed)
			{
				_settingsWindow.WindowState = FormWindowState.Normal;
				_settingsWindow.Activate();
				_settingsWindow.BringToFront();
				return;
			}
			_settingsWindow = new SettingsWindow(ConfigStore.Load, delegate(PluginConfig config)
			{
				SaveSettingsAndRefresh(config);
			}, GetSolidWorksWindowHandle,
				() => _swApp == null ? null : (ISldWorks)_swApp,
				() => _swApp == null ? null : (ICommandManager)GetCommandManager());
			_settingsWindow.FormClosed += delegate
			{
				_settingsWindow = null;
			};
			IntPtr solidWorksWindowHandle = GetSolidWorksWindowHandle();
			if (solidWorksWindowHandle != IntPtr.Zero)
			{
				_settingsWindow.StartPosition = FormStartPosition.Manual;
				if (NativeMethods.GetWindowRect(solidWorksWindowHandle, out var rect))
				{
					_settingsWindow.Location = new Point(rect.Left + Math.Max(20, (rect.Right - rect.Left - _settingsWindow.Width) / 2), rect.Top + 80);
				}
			}
			_settingsWindow.Show();
			_settingsWindow.Activate();
		}
		catch (Exception ex)
		{
			Logger.Error("ShowSettings failed", ex);
			ToolNotice.Show(ex.Message, DisplayName + " 设置打开失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	public void ShowFeatureSettings()
	{
		try
		{
			if (_featureSettingsWindow != null && !_featureSettingsWindow.IsDisposed)
			{
				_featureSettingsWindow.WindowState = FormWindowState.Normal;
				_featureSettingsWindow.Activate();
				_featureSettingsWindow.BringToFront();
				return;
			}
			SettingsWindow settingsWindow = new SettingsWindow(ConfigStore.Load, delegate(PluginConfig config)
			{
				SaveSettingsAndRefresh(config);
			}, GetSolidWorksWindowHandle,
				() => _swApp == null ? null : (ISldWorks)_swApp,
				() => _swApp == null ? null : (ICommandManager)GetCommandManager(),
				SettingsWindowMode.FeatureTools);
			_featureSettingsWindow = settingsWindow;
			settingsWindow.FormClosed += delegate { if (ReferenceEquals(_featureSettingsWindow, settingsWindow)) _featureSettingsWindow = null; };
			IntPtr solidWorksWindowHandle = GetSolidWorksWindowHandle();
			if (solidWorksWindowHandle != IntPtr.Zero && NativeMethods.GetWindowRect(solidWorksWindowHandle, out var rect))
			{
				settingsWindow.StartPosition = FormStartPosition.Manual;
				settingsWindow.Location = new Point(rect.Left + Math.Max(20, (rect.Right - rect.Left - settingsWindow.Width) / 2), rect.Top + 80);
			}
			settingsWindow.Show();
			settingsWindow.Activate();
		}
		catch (Exception ex)
		{
			Logger.Error("ShowFeatureSettings failed", ex);
			ToolNotice.Show(ex.Message, DisplayName + " 功能设置打开失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	public int ShowSettingsEnable()
	{
		return 1;
	}

	public string ReadExternalPluginCommandsForDiagnostics()
	{
		if (_swApp == null) return "connected=false";
		ICommandManager commandManager = (ICommandManager)GetCommandManager();
		bool success = ExternalPluginCommandCatalog.TryRead((ISldWorks)_swApp, commandManager, out List<KeyboardShortcutRow> rows, out string message);
		string sample = string.Join(" | ", rows.Take(10).Select(row => row.Command + "=" + row.Value));
		string result = $"success={success};count={rows.Count};message={message};sample={sample}";
		Logger.Info("External plugin command diagnostic: " + result);
		return result;
	}

	public void ShowShortcutPaletteCommand()
	{
		if (_runtimeConfig?.ShortcutPaletteEnabled != true) return;
		ShowShortcutPalette();
	}

	private void ShowShortcutPalette()
	{
		if (_runtimeConfig?.ShortcutPaletteEnabled != true) return;
		try
		{
			_shortcutPalette ??= new ShortcutPaletteWindow(QueueExecuteShortcut);
			ShortcutPaletteSnapshot snapshot = ShortcutPaletteStore.Load();
			List<KeyboardShortcutRow> rows = new List<KeyboardShortcutRow>(snapshot.Rows ?? new List<KeyboardShortcutRow>());
			if (_swApp != null)
			{
				ISldWorks solidWorks = (ISldWorks)_swApp;
				List<KeyboardShortcutRow> keyboardRows = SolidWorksShortcutBackupService.ReadCurrentShortcutRows(solidWorks);
				if (snapshot.Rows.Count == 0 && keyboardRows.Count > 0) ShortcutPaletteStore.Save(keyboardRows);
				MergeShortcutPaletteRows(rows, keyboardRows);
				try
				{
					ICommandManager commandManager = (ICommandManager)GetCommandManager();
					SolidWorksNativeCommandCatalog.TryRead(solidWorks, commandManager, out List<KeyboardShortcutRow> nativeRows, out _);
					MergeShortcutPaletteRows(rows, nativeRows);
					ExternalPluginCommandCatalog.TryRead(solidWorks, commandManager, out List<KeyboardShortcutRow> externalRows, out _);
					MergeShortcutPaletteRows(rows, externalRows);
				}
				catch (Exception ex)
				{
					Logger.Trace("Shortcut palette live command catalog read failed: " + ex.Message);
				}
				try
				{
					if (QinJianFunctionReader.TryRead(GetSolidWorksWindowHandle(), out List<KeyboardShortcutRow> qinJianRows, out _))
						MergeShortcutPaletteRows(rows, qinJianRows);
				}
				catch (Exception ex)
				{
					Logger.Trace("Shortcut palette QinJian catalog read failed: " + ex.Message);
				}
				try
				{
					if (JiaLiChuangFunctionCatalog.TryRead(solidWorks, out List<KeyboardShortcutRow> jiaLiChuangRows, out _))
						MergeShortcutPaletteRows(rows, jiaLiChuangRows);
				}
				catch (Exception ex)
				{
					Logger.Trace("Shortcut palette JiaLiChuang catalog read failed: " + ex.Message);
				}
			}
			foreach (KeyValuePair<string, string> action in BasicActions.All)
			{
				rows.Add(new KeyboardShortcutRow
				{
					Category = "SOLIDWORKS > 基础功能",
					Command = action.Value,
					Hotkey = "直接执行",
					ActionKind = ActionKinds.Basic,
					Value = action.Key
				});
			}
			rows = DistinctShortcutPaletteRows(rows)
				.Where(row => !ProductFeatureScope.IsRetiredAction(row.ActionKind, row.Value)).ToList();
			if (rows.Count == 0) throw new InvalidOperationException("当前没有可用的快捷键或功能命令。");
			_shortcutPalette.ShowPalette(rows, Cursor.Position);
		}
		catch (Exception ex)
		{
			Logger.Error("ShowShortcutPaletteCommand failed", ex);
			if (_swApp != null) ((ISldWorks)_swApp).SendMsgToUser2("读取 SOLIDWORKS 快捷键失败: " + ex.Message, 0, 0);
		}
	}

	private static void MergeShortcutPaletteRows(List<KeyboardShortcutRow> target, IEnumerable<KeyboardShortcutRow> incoming)
	{
		if (incoming == null) return;
		foreach (KeyboardShortcutRow row in incoming)
		{
			if (!ShortcutPaletteMatcher.IsNameCandidate(row)) continue;
			if (target.Any(existing => string.Equals(existing.ActionKind, row.ActionKind, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(existing.Category, row.Category, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(existing.Command, row.Command, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(existing.Value, row.Value, StringComparison.OrdinalIgnoreCase))) continue;
			target.Add(row);
		}
	}

	private static List<KeyboardShortcutRow> DistinctShortcutPaletteRows(IEnumerable<KeyboardShortcutRow> rows)
	{
		return (rows ?? Array.Empty<KeyboardShortcutRow>())
			.Where(row => ShortcutPaletteMatcher.IsNameCandidate(row)
				&& !ToolbarExternalCommandCatalog.IsRemovedCommand(-1, row.Command))
			.GroupBy(row => string.Join("\u001f", row.ActionKind ?? "", row.Category ?? "", row.Command ?? "", row.Value ?? ""), StringComparer.OrdinalIgnoreCase)
			.Select(group => group.First())
			.OrderBy(row => row.Command, StringComparer.CurrentCultureIgnoreCase)
			.ThenBy(row => row.Category, StringComparer.CurrentCultureIgnoreCase)
			.ToList();
	}

	public int ShowShortcutPaletteEnable()
	{
		return _runtimeConfig?.ShortcutPaletteEnabled == true ? 1 : 0;
	}

	private void QueueExecuteShortcut(KeyboardShortcutRow row)
	{
		_dispatcher?.Post(delegate
		{
			ExecuteShortcutPaletteRow(row);
		});
	}

	private void ExecuteShortcutPaletteRow(KeyboardShortcutRow row)
	{
		if (row == null || _swApp == null) return;
		try
		{
			if (!ShortcutPaletteMatcher.TryGetAction(row, out string kind, out string actionValue))
				throw new InvalidOperationException("候选命令身份无效，请重新读取。");
			if (kind.Equals(ActionKinds.Basic, StringComparison.OrdinalIgnoreCase)) ExecuteBasicAction(actionValue);
			else if (kind.Equals(ActionKinds.NativeCommand, StringComparison.OrdinalIgnoreCase)) ExecuteNativeCommand(actionValue);
			else if (kind.Equals(ActionKinds.QinJian, StringComparison.OrdinalIgnoreCase)) ExecuteQinJianFunction(actionValue, CancellationToken.None);
			else if (kind.Equals(ActionKinds.ExternalPlugin, StringComparison.OrdinalIgnoreCase)) ExecuteExternalPluginFunction(actionValue, CancellationToken.None);
			else if (kind.Equals(ActionKinds.JiaLiChuang, StringComparison.OrdinalIgnoreCase)) ExecuteJiaLiChuangFunction(actionValue);
			else if (kind.Equals(ActionKinds.UiClick, StringComparison.OrdinalIgnoreCase)) ClickSolidWorksControl(actionValue, CancellationToken.None);
			else if (kind.Equals(ActionKinds.Hotkey, StringComparison.OrdinalIgnoreCase)) SendHotkey(actionValue);
			else throw new InvalidOperationException("不支持此候选命令类型，请重新读取。");
		}
		catch (Exception ex)
		{
			Logger.Error("Shortcut palette execution failed", ex);
			((ISldWorks)_swApp).SendMsgToUser2("快捷键候选执行失败: " + ex.Message, 0, 0);
		}
	}

	public string RunCompatibilitySelfCheck()
	{
		try
		{
			bool qinJianOk = QinJianCommandCatalog.TryRead(out List<KeyboardShortcutRow> qinJianRows, out string qinJianMessage);
			bool jiaLiChuangOk = _swApp != null
				? JiaLiChuangFunctionCatalog.TryRead((ISldWorks)_swApp, out List<KeyboardShortcutRow> jiaLiChuangRows, out string jiaLiChuangMessage)
				: JiaLiChuangFunctionCatalog.TryRead(out jiaLiChuangRows, out jiaLiChuangMessage);
			string result = $"QinJian={(qinJianOk ? $"OK({qinJianRows.Count})" : "FAIL(" + qinJianMessage + ")")}; "
				+ $"JiaLiChuang={(jiaLiChuangOk ? $"OK({jiaLiChuangRows.Count})" : "FAIL(" + jiaLiChuangMessage + ")")}";
			Logger.Info("Compatibility self-check: " + result);
			return result;
		}
		catch (Exception ex)
		{
			Logger.Error("Compatibility self-check failed", ex);
			return "SELF_CHECK_FAILED: " + ex.Message;
		}
	}

	public string RunCommandRegistrationSelfCheck()
	{
		lock (_menuRegistrationSync)
		{
			return $"CommandGroupId={CommandGroupId}; RegistrationAttempts={DeferredCommandRegistrationAttemptCount}; CommandItems={_registeredCommandItemCount}/{ExpectedCommandGroupItemCount}; ToolsFrames={_registeredToolsMenuDocumentCount}/{ExpectedToolsMenuDocumentCount}; Complete={_commandGroupRegistrationComplete}";
		}
	}

	private void QueueDeferredMenuRegistration()
	{
		if (Interlocked.Exchange(ref _menuRegistrationQueued, 1) != 0)
		{
			return;
		}
		ThreadPool.QueueUserWorkItem(delegate
		{
			try
			{
				for (int attempt = 0; attempt < DeferredCommandRegistrationAttemptCount; attempt++)
				{
					Thread.Sleep(MenuRegistrationDelayMs);
					if (_connected)
					{
						_dispatcher?.Post(CreateCommandGroup);
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Error("Deferred command registration failed", ex);
			}
			finally
			{
				Interlocked.Exchange(ref _menuRegistrationQueued, 0);
			}
		});
	}

	private void CloseSettingsWindow()
	{
		SettingsWindow settingsWindow = _settingsWindow;
		_settingsWindow = null;
		if (settingsWindow == null || settingsWindow.IsDisposed)
		{
			return;
		}
		try
		{
			settingsWindow.Close();
			settingsWindow.Dispose();
		}
		catch (Exception ex)
		{
			Logger.Error("CloseSettingsWindow failed", ex);
		}
	}

	private void CloseFeatureSettingsWindow()
	{
		SettingsWindow settingsWindow = _featureSettingsWindow;
		_featureSettingsWindow = null;
		if (settingsWindow == null || settingsWindow.IsDisposed) return;
		try { settingsWindow.Close(); settingsWindow.Dispose(); }
		catch (Exception ex) { Logger.Error("CloseFeatureSettingsWindow failed", ex); }
	}

    private void ClosePartProcessWindow()
    {
        if (_processTaskPane != null && ReferenceEquals(_processTaskPane.Content, _partProcessWindow)) _processTaskPane.Detach();
        _partProcessWindow?.Dispose();
		_partProcessWindow = null;
		_automaticPartCandidate = null; _automaticPartCandidateConfiguration = "";
	}

	private void CloseAssemblyProcessWindow()
	{
        ResetAutomaticAssemblyCandidate();
        var windows = _assemblyProcessSessions.Values.ToList();
        if (_assemblyProcessWindow != null && !windows.Contains(_assemblyProcessWindow)) windows.Add(_assemblyProcessWindow);
        _assemblyProcessSessions.Clear(); _assemblyProcessWindow = null;
        foreach (var window in windows) DisposeAssemblyProcessSession(window);
    }
	private void DisposeAssemblyProcessSession(AssemblyProcessRecognitionWindow window)
	{
        if (ReferenceEquals(_assemblyProcessWindow, window)) _assemblyProcessWindow = null;
        if (window == null || window.IsDisposed) return;
        if (_processTaskPane != null && ReferenceEquals(_processTaskPane.Content, window)) _processTaskPane.Detach();
        try
        {
            window.CloseForShutdown();
			if (!window.IsDisposed) window.Dispose();
		}
		catch (Exception ex)
		{
			Logger.Error("Close assembly process recognition window failed", ex);
		}
	}



	private CommandManager GetCommandManager()
	{
		return _commandManager ?? (_commandManager = ((ISldWorks)_swApp).GetCommandManager(_cookie));
	}

	private void SaveSettingsAndRefresh(PluginConfig config)
	{
		UserMacroStore.NormalizeConfig(config);
		PluginConfig previous = _runtimeConfig;
		bool macrosChanged = UserMacroCommandRegistry.Fingerprint(previous.UserMacros) != UserMacroCommandRegistry.Fingerprint(config.UserMacros);
		bool tabChanged = macrosChanged || previous.WheelCommandTabEnabled != config.WheelCommandTabEnabled
			|| previous.WheelCommandTabName != config.WheelCommandTabName;
		if (tabChanged && _swApp != null)
		{
			UserMacroCommandRegistry.ValidateDefinitions(config.UserMacros);
			if (!_commandTabGroupCreated || _wheelTabRuntimeIds.Length != ExpectedCommandGroupItemCount) CreateCommandGroup();
			if (!_commandTabGroupCreated || _wheelTabRuntimeIds.Length != ExpectedCommandGroupItemCount)
				throw new InvalidOperationException("轮盘选项卡尚未完成注册，请稍后应用；配置尚未保存。");
			var manager = (ICommandManager)GetCommandManager();
			string oldTitle = _registeredWheelTabTitle;
			WheelCommandTabs.ValidateConfiguredTitle(manager, config.WheelCommandTabName, oldTitle,
				_wheelTabRuntimeIds.Concat(_userMacroRegistry?.RuntimeIds ?? Array.Empty<int>()));
			int[] oldMacroIds = _userMacroRegistry?.RuntimeIds.ToArray() ?? Array.Empty<int>();
			var pending = new HashSet<int>();
			try
			{
				_userMacroRegistry ??= new UserMacroCommandRegistry(manager);
				if (macrosChanged) _userMacroRegistry.Rebuild(config.UserMacros);
				WheelCommandTabs.RegisterConfigured(manager, _wheelTabRuntimeIds, config.WheelCommandTabName,
					config.WheelCommandTabEnabled, _userMacroRegistry.RuntimeIds, oldTitle, pending, false, oldMacroIds);
				ConfigStore.SaveWithBackup(config, "user-macro-tab-settings");
				_previousWheelTabTitle = oldTitle;
				_registeredWheelTabTitle = config.WheelCommandTabName;
				_previousMacroRuntimeIds = oldMacroIds;
				_pendingWheelTabMaintenanceTypes.Clear(); _pendingWheelTabMaintenanceTypes.UnionWith(pending);
				_wheelTabRepairCurrentSchema = true;
			}
			catch (Exception failure)
			{
				try
				{
					int[] failedMacroIds = _userMacroRegistry?.RuntimeIds.ToArray() ?? Array.Empty<int>();
					if (macrosChanged) _userMacroRegistry.Rebuild(previous.UserMacros);
					WheelCommandTabs.RegisterConfigured(manager, _wheelTabRuntimeIds, oldTitle, previous.WheelCommandTabEnabled,
						_userMacroRegistry?.RuntimeIds ?? Array.Empty<int>(), config.WheelCommandTabName, null, false, failedMacroIds);
				}
				catch (Exception rollback) { throw new AggregateException("设置未保存，选项卡恢复失败，请重新加载插件。", failure, rollback); }
				throw;
			}
		}
		else ConfigStore.Save(config);
		_runtimeConfig = ConfigStore.Clone(config);
		if (tabChanged)
		{
			RecordConfiguredTabOwnership();
			ObserveWheelTabMaintenanceContext(force: true);
		}
		RefreshRuntimeSceneKey();
		ApplyOptionalFeatureRuntimeState();
		_overlay?.ReloadConfig();
	}

	private void RecordConfiguredTabOwnership()
	{
		if (string.IsNullOrEmpty(_wheelTabRegistryPath)) return;
		try
		{
			WheelCommandTabs.RecordOwnedTitle(_wheelTabRegistryPath, _registeredWheelTabTitle);
			WheelCommandTabs.RecordOwnedMacroIds(_wheelTabRegistryPath, _userMacroRegistry?.RuntimeIds ?? Array.Empty<int>());
		}
		catch (Exception error) { Logger.Error("Persist owned user-configured tab identity failed", error); }
	}

	private void CreateCommandGroup()
	{
		if (_swApp == null)
		{
			return;
		}
		try
		{
			CommandManager commandManager = GetCommandManager();
			int createResult = 0;
			// Existing user command IDs remain unchanged. New commands are appended so
			// SOLIDWORKS keeps the saved mapping and toolbar layout.
			object registeredUserIds;
			bool hasPreviousGroup = ((ICommandManager)commandManager).GetGroupDataFromRegistry(CommandGroupId, out registeredUserIds);
			string migrationKey = WheelCommandTabs.MigrationRegistryPath(((ISldWorks)_swApp).RevisionNumber());
			_wheelTabRegistryPath = migrationKey;
			string previousOwnedTitle = WheelCommandTabs.ReadOwnedTitle(migrationKey);
			int[] previousOwnedMacroIds = WheelCommandTabs.ReadOwnedMacroIds(migrationKey);
			bool migrationComplete = WheelCommandTabs.IsMigrationComplete(migrationKey);
			bool migrateLegacyGroup = WheelCommandTabs.ShouldMigrateTabs(hasPreviousGroup, migrationComplete, registeredUserIds as int[]);
			Logger.Info($"Wheel schema migration. Group={CommandGroupId}; PreviousGroup={hasPreviousGroup}; Marker={migrationComplete}; Migrate={migrateLegacyGroup}");
			if (migrateLegacyGroup) WheelCommandTabs.MarkMigrationPending(migrationKey);
			CommandGroup obj2 = ((ICommandManager)commandManager).CreateCommandGroup2(CommandGroupId, DisplayName, "XY " + DisplayName, "轮盘、水印、工序与一键爆炸", -1, migrateLegacyGroup, ref createResult);
			if (obj2 == null || !IsCommandGroupCreateSuccess(createResult))
			{
				throw new InvalidOperationException($"XY轮盘命令组创建失败。Id={CommandGroupId}; Result={createResult}");
			}
			// The standard CommandGroup menu is not visible reliably in the target
			// SOLIDWORKS installation. Keep the command IDs, but expose exactly one
			// owned Tools submenu through ISldWorks.AddMenu/AddMenuItem3 below.
			((ICommandGroup)obj2).HasMenu = false;
			((ICommandGroup)obj2).HasToolbar = true;
			_commandTabGroupCreated = true;
			_commandIconDirectory = WheelCommandTabs.CreateIcons((ICommandGroup)obj2, System.IO.Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName));
			((ICommandGroup)obj2).ShowInDocumentType = ToolsMenuDocumentTypes;
			int[] itemIndexes = AddWheelCommandItems((ICommandGroup)obj2, (int)(swCommandItemType_e.swMenuItem | swCommandItemType_e.swToolbarItem));
			int registeredCount = itemIndexes.Count(index => index >= 0);
			bool activated = ((ICommandGroup)obj2).Activate();
			int registeredToolsFrames = CreateToolsMenuEntries();
			int[] runtimeCommandIds = activated && registeredCount == ExpectedCommandGroupItemCount
				? itemIndexes.Select(index => ((ICommandGroup)obj2).CommandID[index]).ToArray() : Array.Empty<int>();
			_userMacroRegistry ??= new UserMacroCommandRegistry((ICommandManager)commandManager);
			_userMacroRegistry.Rebuild(_runtimeConfig.UserMacros);
			var deferredTabTypes = new HashSet<int>();
			int registeredTabs = runtimeCommandIds.Length == ExpectedCommandGroupItemCount
				? WheelCommandTabs.RegisterConfigured((ICommandManager)commandManager, runtimeCommandIds, _runtimeConfig.WheelCommandTabName,
					_runtimeConfig.WheelCommandTabEnabled, _userMacroRegistry.RuntimeIds, previousOwnedTitle, deferredTabTypes, migrateLegacyGroup, previousOwnedMacroIds) : 0;
			bool complete = registeredCount == ExpectedCommandGroupItemCount
				&& activated
				&& registeredTabs == WheelCommandTabs.DocumentTypes.Length
				&& registeredToolsFrames == ExpectedToolsMenuDocumentCount;
			lock (_menuRegistrationSync)
			{
				_registeredCommandItemCount = registeredCount;
				_registeredToolsMenuDocumentCount = registeredToolsFrames;
				_commandGroupRegistrationComplete = false;
			}
			if (!complete)
			{
				throw new InvalidOperationException($"XY轮盘菜单注册不完整。CommandItems={registeredCount}/{ExpectedCommandGroupItemCount}; ToolsFrames={registeredToolsFrames}/{ExpectedToolsMenuDocumentCount}; Tabs={registeredTabs}/3; Activated={activated}");
			}
			if (migrateLegacyGroup) WheelCommandTabs.RemoveLegacyOwnedGroup((ICommandManager)commandManager);
			lock (_menuRegistrationSync) _commandGroupRegistrationComplete = true;
			_wheelTabRuntimeIds = runtimeCommandIds;
			_registeredWheelTabTitle = _runtimeConfig.WheelCommandTabName;
			_previousWheelTabTitle = previousOwnedTitle;
			_previousMacroRuntimeIds = previousOwnedMacroIds;
			_pendingWheelTabMaintenanceTypes.Clear();
			_pendingWheelTabMaintenanceTypes.UnionWith(deferredTabTypes);
			_wheelTabRepairCurrentSchema = migrateLegacyGroup || previousOwnedTitle != _runtimeConfig.WheelCommandTabName
				|| previousOwnedMacroIds.Length > 0 || _userMacroRegistry.RuntimeIds.Length > 0;
			_wheelTabSchemaMigrationPending = migrateLegacyGroup;
			_wheelTabMigrationKey = migrateLegacyGroup ? migrationKey : null;
			_wheelTabMaintenanceReady = true;
			RecordConfiguredTabOwnership();
			Logger.Info("Wheel context maintenance registered. RepairSchema=" + migrateLegacyGroup + "; PendingTypes=" + string.Join(",", deferredTabTypes.OrderBy(type => type)));
			// A blank-session deferred repair must survive closing SW without
			// ever activating that document type. Keep its registry marker pending.
			CompleteWheelTabMigrationIfReady();
			ObserveWheelTabMaintenanceContext(force: true);
			Logger.Info($"Command group activated. Id={CommandGroupId}; Result=Success({createResult}); CommandItems={registeredCount}/{ExpectedCommandGroupItemCount}; ToolsFrames={registeredToolsFrames}/{ExpectedToolsMenuDocumentCount}; Tabs={registeredTabs}/3; Activated={activated}");
		}
		catch (Exception ex)
		{
			Logger.Error("CreateCommandGroup failed", ex);
		}
	}

	private void RemoveWheelCommandGroup()
	{
		_userMacroRegistry?.Dispose(); _userMacroRegistry = null;
		if (!_commandTabGroupCreated || _swApp == null) return;
		var manager = (ICommandManager)GetCommandManager();
		// Runtime-only removal preserves the user's saved toolbar layout.
		if (manager.RemoveCommandGroup2(CommandGroupId, true) != (int)swRemoveCommandGroupErrors.swRemoveCommandGroup_Success)
			throw new InvalidOperationException("轮盘工具栏卸载失败。");
		_commandTabGroupCreated = false;
	}

	private static int[] AddWheelCommandItems(ICommandGroup group, int itemTypes)
	{
		// Preserve retained UserIDs; removed command identities are never reused.
		return new[]
		{
			group.AddCommandItem2("轮盘设置", -1, "设置轮盘、命令选择与组合录制", "轮盘设置", 0, "ShowSettings", "ShowSettingsEnable", 1, itemTypes),
			group.AddCommandItem2("属性水印设置", -1, "设置当前文档属性水印", "属性水印设置", 1, "ShowPropertyWatermarkSettings", "ShowPropertyWatermarkSettingsEnable", 3, itemTypes),
			group.AddCommandItem2("工序识别", -1, "根据实体几何识别工序；装配体可批量识别零件", "工序识别", 2, "ShowProcessRecognition", "ShowProcessRecognitionEnable", 5, itemTypes),
			group.AddCommandItem2("快捷键候选(&K)", -1, "输入功能名或按组合键筛选并执行", "快捷键候选(&K)", 3, "ShowShortcutPaletteCommand", "ShowShortcutPaletteEnable", 2, itemTypes),
			group.AddCommandItem2("功能设置", -1, "设置轮盘、水印、工序与一键爆炸", "功能设置", 4, "FeatureSettingsMenuCallback", "FeatureSettingsMenuEnable", FeatureSettingsUserId, itemTypes),
			group.AddCommandItem2("一键爆炸", -1, "自动分开多实体零件或装配体，保留原始模型", "一键爆炸", 5, "ShowExplodeAllCommand", "ShowExplodeAllCommandEnable", ExplodeAllUserId, itemTypes),
			group.AddCommandItem2("加入宏", -1, "导入用户宏并加入此选项卡；导入不执行", "加入宏", 6, "ShowUserMacroImportCommand", "ShowUserMacroImportCommandEnable", UserMacroImportUserId, itemTypes)
		};
	}

	internal static bool AreAllToolsMenuEntriesRegistered(params bool[] results)
	{
		if (results == null || results.Length != ExpectedToolsMenuItemCount)
		{
			return false;
		}
		for (int i = 0; i < results.Length; i++)
		{
			if (!results[i]) return false;
		}
		return true;
	}

	private int CreateToolsMenuEntries()
	{
		if (_swApp == null) return 0;
		RemoveOwnedToolsParentMenus();
		int registeredFrames = 0;
		foreach (int documentType in SolidWorksMenuDocumentTypes())
		{
			int parentMenuId = ((ISldWorks)_swApp).AddMenu(documentType, DisplayName, 13);
			bool settings = ((ISldWorks)_swApp).AddMenuItem3(documentType, _cookie, "轮盘设置@" + DisplayName, 1, "SettingsMenuCallback", "SettingsMenuEnable", "设置轮盘、命令选择与组合录制", "");
			bool featureSettings = ((ISldWorks)_swApp).AddMenuItem3(documentType, _cookie, "功能设置@" + DisplayName, 12, "FeatureSettingsMenuCallback", "FeatureSettingsMenuEnable", "设置轮盘、水印、工序与一键爆炸", "");
			bool shortcutPalette = ((ISldWorks)_swApp).AddMenuItem3(documentType, _cookie, "快捷键候选(&K)@" + DisplayName, 2, "ShowShortcutPaletteCommand", "ShowShortcutPaletteEnable", "输入功能名或按组合键筛选并执行", "");
			bool watermark = ((ISldWorks)_swApp).AddMenuItem3(documentType, _cookie, "属性水印设置@" + DisplayName, 3, "ShowPropertyWatermarkSettings", "ShowPropertyWatermarkSettingsEnable", "设置当前文档属性水印", "");
			bool processRecognition = ((ISldWorks)_swApp).AddMenuItem3(documentType, _cookie, "工序识别@" + DisplayName, 5, "ShowProcessRecognition", "ShowProcessRecognitionEnable", "根据实体几何识别工序；装配体可批量识别零件", "");
			bool explodeAll = ((ISldWorks)_swApp).AddMenuItem3(documentType, _cookie, "一键爆炸@" + DisplayName, ExplodeAllUserId, "ShowExplodeAllCommand", "ShowExplodeAllCommandEnable", "自动分开多实体零件或装配体，Esc 取消", "");
			bool complete = parentMenuId >= 0 && AreAllToolsMenuEntriesRegistered(settings, featureSettings, shortcutPalette, watermark, processRecognition, explodeAll);
			if (complete) registeredFrames++;
			Logger.Info($"Owned Tools submenu registered. DocType={documentType}; ParentMenuId={parentMenuId}; Complete={complete}; Settings={settings}; FeatureSettings={featureSettings}; ShortcutPalette={shortcutPalette}; Watermark={watermark}; ProcessRecognition={processRecognition}; ExplodeAll={explodeAll}");
		}
		return registeredFrames;
	}

	private void RemoveOwnedToolsParentMenus()
	{
		if (_swApp == null) return;
		// Both captions belong to this product. Retire the old parent caption when
		// migrating to the user's exact product name so stale tools do not linger.
		foreach (int documentType in SolidWorksMenuDocumentTypes())
		foreach (string title in new[] { DisplayName, "XY SOLIDWORKS 轮盘" })
		{
			int removed = 0;
			while (removed < MaximumOwnedParentMenusToRemove && ((ISldWorks)_swApp).RemoveMenu(documentType, title, "")) removed++;
			if (removed > 0) Logger.Info("Removed owned Tools parent menus. DocType=" + documentType + "; Title=" + title + "; Count=" + removed);
		}
	}

	private static int[] SolidWorksMenuDocumentTypes()
	{
		return new[]
		{
			(int)swDocumentTypes_e.swDocNONE,
			(int)swDocumentTypes_e.swDocPART,
			(int)swDocumentTypes_e.swDocASSEMBLY,
			(int)swDocumentTypes_e.swDocDRAWING
		};
	}

	private string ResolveSceneKey()
	{
		try
		{
			SldWorks? swApp = _swApp;
			object obj = ((swApp != null) ? ((ISldWorks)swApp).ActiveDoc : null);
			ModelDoc2 val = (ModelDoc2)((obj is ModelDoc2) ? obj : null);
			if (val == null)
			{
				return SceneKeys.Blank;
			}
			IModelDoc2 model = (IModelDoc2)val;
			bool isEditingSketch = false;
			if (_runtimeConfig != null)
			{
				try
				{
					isEditingSketch = SceneResolver.IsEditingSketch(model);
				}
				catch (COMException ex)
				{
					Logger.Info("Sketch scene detection unavailable: " + ex.Message);
				}
			}
			return SceneResolver.Resolve(_runtimeConfig, model.GetType(), isEditingSketch);
		}
		catch
		{
			return SceneKeys.Blank;
		}
	}

	internal static bool IsCommandGroupCreateSuccess(int result)
	{
		return result == (int)swCreateCommandGroupErrors.swCreateCommandGroup_Success;
	}

	private void CloseShortcutPalette()
	{
		ShortcutPaletteWindow palette = _shortcutPalette;
		_shortcutPalette = null;
		if (palette == null || palette.IsDisposed)
		{
			return;
		}
		palette.Close();
		palette.Dispose();
	}

	private string RefreshRuntimeSceneKey(bool refreshIcons = true)
	{
		_runtimeSceneKey = ResolveSceneKey();
		if (refreshIcons)
		{
			// Record the inputs present when discovery begins. A configuration or
			// assembly change during foreign COM discovery remains dirty and is
			// retried on the next safe idle pass instead of being marked current.
			string iconScene = _runtimeSceneKey;
			DateTime configWriteUtc = System.IO.File.GetLastWriteTimeUtc(ConfigStore.ConfigPath);
			int assemblyRevision = Volatile.Read(ref _wheelIconAssemblyRevision);
			RadialCommandIcons.Refresh(_swApp == null ? null : (ISldWorks)_swApp, iconScene);
			_iconsInitialized = true;
			_warmedIconScene = iconScene;
			_warmedIconConfigWriteUtc = configWriteUtc;
			_warmedIconAssemblyRevision = assemblyRevision;
		}
		return _runtimeSceneKey;
	}

	private void ExecuteItem(MenuItemConfig item)
	{
		if (_swApp == null)
		{
			return;
		}
		try
		{
			ProductFeatureScope.EnsureExecutable(item);
			// A recorded sequence runs for any kind, so a single function can also
			// hand off through several steps.
			if (item.Kind.Equals(ActionKinds.Combo, StringComparison.OrdinalIgnoreCase)
				|| (item.ComboCommands != null && item.ComboCommands.Count > 0))
			{
				ExecuteComboAction(item);
			}
			else if (item.Kind.Equals(ActionKinds.Hotkey, StringComparison.OrdinalIgnoreCase))
			{
				SendHotkey(item.Value);
			}
			else if (item.Kind.Equals(ActionKinds.UiClick, StringComparison.OrdinalIgnoreCase))
			{
				ClickSolidWorksControl(item.Value, CancellationToken.None);
			}
			else if (item.Kind.Equals(ActionKinds.NativeCommand, StringComparison.OrdinalIgnoreCase))
			{
				ExecuteNativeCommand(item.Value);
			}
			else if (item.Kind.Equals(ActionKinds.QinJian, StringComparison.OrdinalIgnoreCase))
			{
				ExecuteQinJianFunction(item.Value, CancellationToken.None);
			}
			else if (item.Kind.Equals(ActionKinds.ExternalPlugin, StringComparison.OrdinalIgnoreCase))
			{
				ExecuteExternalPluginFunction(item.Value, CancellationToken.None);
			}
			else if (item.Kind.Equals(ActionKinds.JiaLiChuang, StringComparison.OrdinalIgnoreCase))
			{
				ExecuteJiaLiChuangFunction(item.Value);
			}
			else if (item.Kind.Equals(ActionKinds.Basic, StringComparison.OrdinalIgnoreCase))
			{
				ExecuteBasicAction(item.Value);
			}
			else throw new InvalidOperationException("未知轮盘功能类型，未改投基础功能：" + item.Kind);
		}
		catch (Exception ex)
		{
			Logger.Error("Execute item failed: " + item.Label, ex);
			((ISldWorks)_swApp).SendMsgToUser2("XY轮盘执行失败: " + ex.Message, 0, 0);
		}
		finally { NativeCommandLaunchTrace.Mark("execute-return", item.Value); }
	}

	private ComboRunOutcome ExecuteComboItem(MenuItemConfig item, CancellationToken token)
	{
		if (_swApp == null)
		{
			return ComboRunOutcome.Cancelled;
		}
		try
		{
			ExecuteComboAction(item, token);
			return ComboRunOutcome.Completed;
		}
		catch (OperationCanceledException)
		{
			return ComboRunOutcome.Cancelled;
		}
		catch (Exception ex)
		{
			Logger.Error("Execute combo failed: " + item.Label, ex);
			PostSolidWorksStatusBarText("XY轮盘：组合已安全停止，详细信息已写入 error.log");
			return ComboRunOutcome.Failed;
		}
	}

	private void PostSolidWorksStatusBarText(string text)
	{
		SwCommandDispatcher dispatcher = _dispatcher;
		SldWorks swApp = _swApp;
		if (dispatcher == null || swApp == null)
		{
			return;
		}
		try
		{
			dispatcher.Post(delegate
			{
				if (!_connected)
				{
					return;
				}
				try
				{
					Frame frame = ((ISldWorks)swApp).IFrameObject();
					if (frame != null)
					{
						((IFrame)frame).SetStatusBarText(text ?? "");
					}
				}
				catch (Exception ex)
				{
					Logger.Trace("Post combo status-bar text failed: " + ex.Message);
				}
			});
		}
		catch (Exception ex)
		{
			Logger.Trace("Queue combo status-bar text failed: " + ex.Message);
		}
	}


	internal static bool IsBasicActionEnabled(PluginConfig config, string value)
	{
		if (!ProductFeatureScope.IsAllowedBasicAction(value)) return false;
		if (string.Equals(value?.Trim(), "explode-all", StringComparison.OrdinalIgnoreCase)) return config?.ExplodeEnabled == true;
		return !string.Equals(value?.Trim(), "process-recognition", StringComparison.OrdinalIgnoreCase)
			|| config?.ProcessRecognition?.Enabled == true;
	}

	private void ExecuteBasicAction(string value)
	{
		if (BasicActions.IsRetired(value)) throw new InvalidOperationException("该扩展功能已从当前精简版移除，原配置保留；组合执行已停止。");
		if (!IsBasicActionEnabled(_runtimeConfig, value)) throw new InvalidOperationException("该功能已关闭，请在功能开关中启用后再执行。");
		if (BasicActions.TryGetNativeCommandId(value, out int nativeId))
		{
			ExecuteNativeCommand(NativeCommandReference.Encode(nativeId));
			return;
		}
		switch ((value ?? "").Trim().ToLowerInvariant())
		{
		case "settings":
			ShowSettings();
			break;
		case "watermark-settings":
			ShowPropertyWatermarkSettings();
			break;
		case "process-recognition":
			ShowProcessRecognition();
			break;
		case "explode-all":
			ExecuteExplodeAll();
			break;
		case "clear-selection":
		{
			SldWorks? swApp = _swApp;
			object obj = ((swApp != null) ? ((ISldWorks)swApp).ActiveDoc : null);
			ModelDoc2 val = (ModelDoc2)((obj is ModelDoc2) ? obj : null);
			if (val != null)
			{
				((IModelDoc2)val).ClearSelection2(true);
			}
			break;
		}
		default:
			throw new InvalidOperationException("未知 SW 基础功能: " + value);
		}
	}

	private void ExecuteNativeCommand(string value)
	{
		bool immediateView = IsImmediateComboCommand(new ComboCommandConfig { Kind = ActionKinds.NativeCommand, Value = value });
		NativeCommandLaunchTrace.Mark("activate-start", value);
		if (!immediateView) ActivateSolidWorks();
		NativeCommandLaunchTrace.Mark("activate-ready", value);
		SwCommandDispatcher dispatcher = _dispatcher ?? throw new InvalidOperationException("SOLIDWORKS 主线程调度器不可用。");
		dispatcher.Invoke(() =>
		{
			NativeCommandLaunchTrace.Mark("run-start", value);
			try
			{
				if (!SolidWorksNativeCommandCatalog.TryExecute(_swApp == null ? null : (ISldWorks)_swApp, value, out string message))
					throw new InvalidOperationException(message);
			}
			finally { NativeCommandLaunchTrace.Mark("run-return", value); }
		});
	}

	private void ExecuteComboAction(MenuItemConfig item)
	{
		ExecuteComboAction(item, CancellationToken.None);
	}

	private void ExecuteComboAction(MenuItemConfig item, CancellationToken token)
	{
		ProductFeatureScope.EnsureExecutable(item);
		if (item.ComboCommands == null || item.ComboCommands.Count == 0)
		{
			throw new InvalidOperationException("组合触发为空。");
		}
		IReadOnlyList<ComboExecutionStep> steps = ComboExecutionPlanner.Build(item.ComboCommands, item.StopOnStepError);
		if (steps.Count == 0)
		{
			throw new InvalidOperationException("组合触发为空。");
		}
		SelectionSnapshot selectionSnapshot = null;
		FeatureTreeExpansionSnapshot featureTreeSnapshot = CaptureFeatureTreeExpansionSnapshot();
		bool comboAdvanceEnabled = _runtimeConfig.GlobalComboAdvanceEnabled || item.ComboAdvanceEnabled;
		string comboAdvanceKey = _runtimeConfig.GlobalComboAdvanceEnabled
			? _runtimeConfig.ComboAdvanceKey
			: item.ComboAdvanceKey;
		IntPtr solidWorksWindow = GetSolidWorksWindowHandle();
		try
		{
			for (int index = 0; index < steps.Count; index++)
			{
				ComboExecutionStep step = steps[index];
				bool requireAdvanceKey = RequiresStepAdvanceKey(step.Command, index, steps.Count);
				bool doctorAdvanceFallback = UsesDoctorAdvanceKeyFallback(step.Command, index, steps.Count);
				token.ThrowIfCancellationRequested();
				Stopwatch stepWatch = Stopwatch.StartNew();
				Logger.Info($"Combo step {index + 1}/{steps.Count} started: {ComboStepName(step.Command)}, auto={step.Command.AutoConfirm}");
				// A step may override the shared hand-off key; otherwise the cell
				// or global key keeps driving the whole sequence.
				using ComboAdvanceController advance = new ComboAdvanceController(
					token,
					ShouldEnableStepAdvanceKey(step.Command, comboAdvanceEnabled, index, steps.Count),
					ResolveStepAdvanceKey(step, comboAdvanceKey),
					solidWorksWindow);
				bool statusTextShown = false;
				int attempts = step.RetriesOnError ? step.RetryCount + 1 : 1;
				try
				{
					for (int attempt = 1; attempt <= attempts; attempt++)
					{
						try
						{
							if (doctorAdvanceFallback)
							{
								SetComboStatusBarText(BuildDoctorAdvanceFallbackStatusText(step.Command, advance.Key));
								statusTextShown = true;
							}
							ExecuteComboStep(step, ref selectionSnapshot, advance);
							if (step.WaitMode == ComboWaitModes.Delay && step.DelayMs > 0)
							{
								WaitComboStepDelay(step.DelayMs, token);
							}
							if (requireAdvanceKey)
							{
								SetComboStatusBarText(BuildComboAdvanceStatusText(step.Command, advance.Key));
								statusTextShown = true;
								Logger.Info($"Combo step {index + 1}/{steps.Count} is complete and requires user key {advance.Key} before continuing: {ComboStepName(step.Command)}");
								WaitForStepAdvance(advance, step, token, index + 1, steps.Count);
							}
							Logger.Info($"Combo step {index + 1}/{steps.Count} completed in {stepWatch.ElapsedMilliseconds} ms: {ComboStepName(step.Command)}");
							break;
						}
						catch (OperationCanceledException) when (advance.AdvanceRequested && !token.IsCancellationRequested)
						{
							Logger.Info($"Combo step {index + 1}/{steps.Count} advanced by user key {advance.Key}: {ComboStepName(step.Command)}");
							break;
						}
						catch (OperationCanceledException)
						{
							throw;
						}
						catch (Exception ex)
						{
							if (attempt < attempts)
							{
								Logger.Info($"Combo step {index + 1}/{steps.Count} failed, retry {attempt}/{step.RetryCount}: {ComboStepName(step.Command)} - {ex.Message}");
								continue;
							}
							if (step.SkipsOnError)
							{
								Logger.Info($"Combo step {index + 1}/{steps.Count} failed and was skipped by its error policy: {ComboStepName(step.Command)} - {ex.Message}");
								break;
							}
							throw new InvalidOperationException($"第 {index + 1} 步“{ComboStepName(step.Command)}”失败，组合已停止。{ex.Message}", ex);
						}
					}
				}
				finally
				{
					if (statusTextShown || step.Command.RequireAdvanceKey || IsDoctorPersistentTaskPaneCommand(step.Command))
					{
						SetComboStatusBarText("");
					}
				}
			}
		}
		finally
		{
			RestoreFeatureTreeExpansionSnapshot(featureTreeSnapshot);
		}
	}

	private void SetComboStatusBarText(string text)
	{
		try
		{
			SldWorks swApp = _swApp;
			SwCommandDispatcher dispatcher = _dispatcher;
			if (swApp == null || dispatcher == null)
			{
				return;
			}
			dispatcher.Invoke(delegate
			{
				Frame frame = ((ISldWorks)swApp).IFrameObject();
				if (frame != null)
				{
					((IFrame)frame).SetStatusBarText(text ?? "");
				}
			});
		}
		catch (Exception ex)
		{
			Logger.Trace("Update combo status-bar text failed: " + ex.Message);
		}
	}

	private void ExecuteComboStep(ComboExecutionStep step, ref SelectionSnapshot selectionSnapshot, ComboAdvanceController advance)
	{
		CancellationToken token = advance.Token;
		token.ThrowIfCancellationRequested();
		if (step.SaveSelectionBefore)
		{
			selectionSnapshot = CaptureSelectionSnapshot();
		}
		if (CanExecuteImmediateComboStep(step))
		{
			// A confirmed native view/basic action with no explicit wait does
			// not open a command dialog. Avoid all running-command, window and
			// UI-text discovery before dispatching this identity.
			ExecuteComboCommand(step.Command, token);
			advance.Arm();
			if (step.RestoreSelectionAfter && selectionSnapshot != null)
			{
				RestoreSelectionSnapshot(selectionSnapshot);
				selectionSnapshot = null;
			}
			return;
		}
		RunningCommandSnapshot before = CaptureRunningCommand();
		WindowSnapshot beforeWindows = CaptureSolidWorksWindowSnapshot();
		long commandEventBaseline = Volatile.Read(ref _commandEventSequence);
		long idleEventBaseline = Volatile.Read(ref _idleEventSequence);
		string stepTargets = ManualCompletionTargets(step.Command);
		bool targetWasVisibleBefore = stepTargets.Length > 0 && IsSolidWorksTextVisibleFast(stepTargets);
		bool isQinJian = IsQinJianComboCommand(step.Command);
		bool isJiaLiChuang = IsJiaLiChuangComboCommand(step.Command);
		bool isDirectPlugin = isQinJian || isJiaLiChuang;
		bool doctorBatchFormat = IsDoctorBatchFormatConversion(step.Command);
		bool doctorPersistentTaskPane = IsDoctorPersistentTaskPaneCommand(step.Command);
		bool doctorPropertyTaskPane = IsDoctorPropertyTaskPaneCommand(step.Command);
		bool doctorSingleTrigger = UsesDoctorUiThreadSingleTrigger(step.Command);
		if (doctorSingleTrigger)
		{
			SwCommandDispatcher dispatcher = _dispatcher ?? throw new InvalidOperationException("SOLIDWORKS 主线程调度器不可用。");
			dispatcher.Invoke(delegate
			{
				ExecuteComboCommand(step.Command, token);
			});
			Logger.Info($"Doctor combo step dispatched exactly once on the SOLIDWORKS UI thread: {ComboStepName(step.Command)}");
			return;
		}
		ExecuteComboCommand(step.Command, token);
		advance.Arm();
		if (step.WaitMode == ComboWaitModes.WindowClosed)
		{
			if (doctorPropertyTaskPane)
			{
				WaitForDoctorPropertyTaskPaneCompletion(step.Command, token);
			}
			else if (isDirectPlugin)
			{
				WaitForManualCommandCompletion(before, beforeWindows, step.Command, stepTargets, targetWasVisibleBefore, idleEventBaseline, token);
			}
			else
			{
				WaitForLegacyManualCommandCompletion(before, beforeWindows, step.Command, stepTargets, targetWasVisibleBefore, commandEventBaseline, token);
			}
			if (advance.Enabled)
			{
				SetComboStatusBarText(BuildComboAdvanceStatusText(step.Command, advance.Key));
				WaitForStepAdvance(advance, step, token, 0, 0);
			}
			return;
		}
		if (step.WaitMode == ComboWaitModes.PropertyPane)
		{
			WaitForDoctorPropertyTaskPaneCompletion(step.Command, token);
			return;
		}
		if (step.WaitMode == ComboWaitModes.ManualConfirm)
		{
			if (advance.Enabled)
			{
				SetComboStatusBarText(BuildComboAdvanceStatusText(step.Command, advance.Key));
				WaitForStepAdvance(advance, step, token, 0, 0);
			}
			return;
		}
		if (step.Command.RequireAdvanceKey && advance.Enabled)
		{
			SetComboStatusBarText(BuildComboAdvanceStatusText(step.Command, advance.Key));
			WaitForStepAdvance(advance, step, token, 0, 0);
			return;
		}
		if (IsImmediateComboCommand(step.Command)) return;
		if (doctorPersistentTaskPane)
		{
			WaitForDoctorPersistentTaskPane(step.Command, token);
			if (advance.Enabled) SetComboStatusBarText(BuildComboAdvanceStatusText(step.Command, advance.Key));
			WaitForStepAdvance(advance, step, token, 0, 0);
		}
		else if (doctorPropertyTaskPane)
		{
			WaitForDoctorPropertyTaskPaneCompletion(step.Command, token);
		}
		else if (doctorBatchFormat)
		{
			WaitForDoctorBatchFormatCompletion(step.Command, idleEventBaseline, token);
		}
		else if (isDirectPlugin && step.Command.AutoConfirm)
		{
			CompleteAutomaticCommand(before, beforeWindows, step.Command, stepTargets, targetWasVisibleBefore, commandEventBaseline, idleEventBaseline, token);
		}
		else if (isDirectPlugin && step.WaitForManualCompletion)
		{
			WaitForManualCommandCompletion(before, beforeWindows, step.Command, stepTargets, targetWasVisibleBefore, idleEventBaseline, token);
		}
		else if (!isDirectPlugin)
		{
			if (step.Command.AutoConfirm)
			{
				CompleteLegacyAutomaticCommand(before, beforeWindows, step.Command, stepTargets, targetWasVisibleBefore, commandEventBaseline, token);
			}
			else if (step.WaitForManualCompletion)
			{
				WaitForLegacyManualCommandCompletion(before, beforeWindows, step.Command, stepTargets, targetWasVisibleBefore, commandEventBaseline, token);
			}
		}
		if (isDirectPlugin)
		{
			WaitForComboStepSettle(step.Command, idleEventBaseline, token);
		}
		if (step.RestoreSelectionAfter && selectionSnapshot != null)
		{
			RestoreSelectionSnapshot(selectionSnapshot);
			selectionSnapshot = null;
		}
	}

	private void ExecuteComboCommand(ComboCommandConfig command, CancellationToken token)
	{
		string kind = (command.Kind ?? ActionKinds.Hotkey).Trim();
		if (kind.Equals(ActionKinds.Basic, StringComparison.OrdinalIgnoreCase))
		{
			ExecuteBasicAction(command.Value);
		}
		else if (kind.Equals(ActionKinds.UiClick, StringComparison.OrdinalIgnoreCase))
		{
			ClickSolidWorksControl(command.Value, token);
		}
		else if (kind.Equals(ActionKinds.NativeCommand, StringComparison.OrdinalIgnoreCase))
		{
			ExecuteNativeCommand(command.Value);
		}
		else if (kind.Equals(ActionKinds.QinJian, StringComparison.OrdinalIgnoreCase))
		{
			ExecuteQinJianFunction(command.Value, token);
		}
		else if (kind.Equals(ActionKinds.ExternalPlugin, StringComparison.OrdinalIgnoreCase))
		{
			ExecuteExternalPluginFunction(command.Value, token);
		}
		else if (kind.Equals(ActionKinds.JiaLiChuang, StringComparison.OrdinalIgnoreCase))
		{
			ExecuteJiaLiChuangFunction(command.Value);
		}
		else if (kind.Equals(ActionKinds.Delay, StringComparison.OrdinalIgnoreCase))
		{
			Sleep(token, ParseDelay(command.Value));
		}
		else if (kind.Equals(ActionKinds.Hotkey, StringComparison.OrdinalIgnoreCase))
		{
			SendHotkey(command.Value);
		}
		else throw new InvalidOperationException("未知组合功能类型，未改发快捷键：" + kind);
	}

	private void WaitForLegacyManualCommandCompletion(RunningCommandSnapshot before, WindowSnapshot beforeWindows, ComboCommandConfig command, string manualUiTargets, bool targetWasVisibleBefore, long commandEventBaseline, CancellationToken token)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		bool sawTarget = false;
		IntPtr manualWindow = IntPtr.Zero;
		IntPtr actionButton = IntPtr.Zero;
		TimeSpan? actionReleasedAt = null;
		bool actionBecameDisabled = false;
		bool leftWasDown = IsVirtualKeyDown(0x01);
		bool enterWasDown = IsVirtualKeyDown(0x0D);
		TimeSpan? runningSince = null;
		bool sawSustainedCommand = false;
		TimeSpan nextCommandProbe = TimeSpan.Zero;
		ManualInteractionGate completionGate = new ManualInteractionGate();
		TimeSpan maxTimeout = TimeSpan.FromMinutes(10);
		TimeSpan uiProbeInterval = TimeSpan.FromMilliseconds(40);
		TimeSpan nextUiProbe = TimeSpan.Zero;
		while (stopwatch.Elapsed < maxTimeout)
		{
			token.ThrowIfCancellationRequested();
			if (!completionGate.HasObservedInteraction && stopwatch.Elapsed >= nextCommandProbe)
			{
				nextCommandProbe = stopwatch.Elapsed + TimeSpan.FromMilliseconds(30);
				bool running = IsNewRunningCommand(before, CaptureRunningCommand());
				if (running)
				{
					runningSince ??= stopwatch.Elapsed;
					sawSustainedCommand |= stopwatch.Elapsed - runningSince.Value >= TimeSpan.FromMilliseconds(300);
				}
				else if (sawSustainedCommand)
				{
					Logger.Trace($"Doctor manual combo step completed: {ComboStepName(command)}, source=solidworks-command, elapsedMs={stopwatch.ElapsedMilliseconds}");
					return;
				}
			}
			bool targetVisible = sawTarget;
			if (stopwatch.Elapsed >= nextUiProbe)
			{
				nextUiProbe = stopwatch.Elapsed + uiProbeInterval;
				targetVisible = !targetWasVisibleBefore && !string.IsNullOrWhiteSpace(manualUiTargets) && IsSolidWorksTextVisibleFast(manualUiTargets);
				if (targetVisible)
				{
					sawTarget = true;
				}
			}
			if (manualWindow == IntPtr.Zero)
			{
				manualWindow = FindNewSolidWorksWindow(beforeWindows);
			}
			bool windowAlive = manualWindow != IntPtr.Zero && NativeMethods.IsWindowVisible(manualWindow);
			if (windowAlive && actionButton == IntPtr.Zero)
			{
				actionButton = UiControlClicker.FindByTextNative(manualWindow, "开始|执行|确定|完成|应用|生成|保存");
			}
			short leftState = NativeMethods.GetAsyncKeyState(0x01);
			bool leftDown = (leftState & 0x8000) != 0;
			bool leftPressedSinceLastProbe = (leftState & 0x0001) != 0;
			bool enterDown = IsVirtualKeyDown(0x0D);
			if (!actionReleasedAt.HasValue && actionButton != IntPtr.Zero && ((((leftWasDown && !leftDown) || leftPressedSinceLastProbe) && IsCursorInside(actionButton)) || (enterWasDown && !enterDown && NativeMethods.GetForegroundWindow() == manualWindow)))
			{
				actionReleasedAt = stopwatch.Elapsed;
			}
			leftWasDown = leftDown;
			enterWasDown = enterDown;
			bool actionCommitted = false;
			if (actionReleasedAt.HasValue)
			{
				bool enabled = NativeMethods.IsWindowEnabled(actionButton);
				actionBecameDisabled |= !enabled;
				actionCommitted = !windowAlive || (actionBecameDisabled && enabled && stopwatch.Elapsed - actionReleasedAt.Value >= TimeSpan.FromMilliseconds(500));
			}
			if (completionGate.Observe(targetVisible, windowAlive, actionCommitted))
			{
				string source = actionCommitted ? "primary-action" : (!windowAlive && manualWindow != IntPtr.Zero ? "window-closed" : "panel-closed");
				Logger.Trace($"Doctor manual combo step completed: {ComboStepName(command)}, source={source}, elapsedMs={stopwatch.ElapsedMilliseconds}");
				return;
			}
			Sleep(token, 10);
		}
		throw new TimeoutException($"等待外部命令“{ComboStepName(command)}”手动完成超时，组合已停止。");
	}

	private void WaitForDoctorPersistentTaskPane(ComboCommandConfig command, CancellationToken token)
	{
		const string paneShellTargets = "自动工程图";
		const string paneContentTargets = "选择文件|支持多选|快速按Excel查找图纸|打开上一张|打开下一张|开始(S)";
		Stopwatch stopwatch = Stopwatch.StartNew();
		IntPtr floatingWindow = IntPtr.Zero;
		DoctorTaskPaneCompletionGate completionGate = new DoctorTaskPaneCompletionGate();
		bool waitingLogged = false;
		while (true)
		{
			token.ThrowIfCancellationRequested();
			if (floatingWindow == IntPtr.Zero)
			{
				floatingWindow = FindDoctorAutomaticDrawingWindow();
				if (floatingWindow != IntPtr.Zero)
				{
					Logger.Info($"Doctor automatic-drawing selection window detected; waiting for the user to hand off to the task pane: HWnd=0x{floatingWindow.ToInt64():X}");
				}
			}
			bool floatingVisible = floatingWindow != IntPtr.Zero && NativeMethods.IsWindowVisible(floatingWindow);
			IntPtr solidWorksWindow = GetSolidWorksWindowHandle();
			bool paneShellVisible = solidWorksWindow != IntPtr.Zero && UiControlClicker.ContainsByTextInRoot(solidWorksWindow, paneShellTargets);
			bool paneContentVisible = solidWorksWindow != IntPtr.Zero && UiControlClicker.ContainsByTextInRoot(solidWorksWindow, paneContentTargets);
			if (completionGate.Observe(floatingVisible, paneShellVisible, paneContentVisible))
			{
				Logger.Info($"Doctor task pane is ready for manual Start and will remain open: {ComboStepName(command)}, elapsedMs={stopwatch.ElapsedMilliseconds}");
				return;
			}
			if (!waitingLogged && (floatingVisible || paneShellVisible))
			{
				waitingLogged = true;
				Logger.Info($"Doctor automatic-drawing UI detected; waiting for the right task pane without clicking Start, closing, or retriggering: {ComboStepName(command)}");
			}
			Sleep(token, 25);
		}
	}

	private void WaitForDoctorPropertyTaskPaneCompletion(ComboCommandConfig command, CancellationToken token)
	{
		const string paneTargets = "属性处理|->更新属性|组合属性强制写入展开配置属性|当前配置特定属性";
		Stopwatch stopwatch = Stopwatch.StartNew();
		DoctorPropertyTaskPaneCompletionGate gate = new DoctorPropertyTaskPaneCompletionGate();
		IntPtr actionButton = IntPtr.Zero;
		bool leftWasDown = IsVirtualKeyDown(0x01);
		bool actionPointerArmed = false;
		Logger.Info($"Doctor property task pane is waiting for the user's actual Update Properties action: {ComboStepName(command)}");
		while (true)
		{
			token.ThrowIfCancellationRequested();
			IntPtr solidWorksWindow = GetSolidWorksWindowHandle();
			bool paneVisible = solidWorksWindow != IntPtr.Zero
				&& UiControlClicker.ContainsByTextInRoot(solidWorksWindow, paneTargets);
			if ((actionButton == IntPtr.Zero || !NativeMethods.IsWindow(actionButton)) && solidWorksWindow != IntPtr.Zero)
			{
				actionButton = UiControlClicker.FindByTextNative(solidWorksWindow, "->更新属性|更新属性");
			}
			bool actionButtonFound = actionButton != IntPtr.Zero && NativeMethods.IsWindow(actionButton);
			bool actionButtonEnabled = actionButtonFound && NativeMethods.IsWindowEnabled(actionButton);
			short leftState = NativeMethods.GetAsyncKeyState(0x01);
			bool leftDown = (leftState & 0x8000) != 0;
			bool leftPressedSinceLastProbe = (leftState & 0x0001) != 0;
			bool cursorInsideAction = actionButtonFound && IsCursorInside(actionButton);
			actionPointerArmed |= leftDown && cursorInsideAction;
			bool actionReleased = actionPointerArmed && leftWasDown && !leftDown
				|| leftPressedSinceLastProbe && cursorInsideAction;
			if (actionReleased)
			{
				actionPointerArmed = false;
			}
			leftWasDown = leftDown;
			bool commandRunning = CaptureRunningCommand().IsRunning;
			if (gate.Observe(paneVisible, actionButtonFound, actionButtonEnabled, actionReleased, commandRunning, Volatile.Read(ref _idleEventSequence)))
			{
				Logger.Info($"Doctor property task-pane step completed without closing the pane: {ComboStepName(command)}, elapsedMs={stopwatch.ElapsedMilliseconds}");
				return;
			}
			Sleep(token, 10);
		}
	}

	private void WaitForDoctorBatchFormatCompletion(ComboCommandConfig command, long idleEventBaseline, CancellationToken token)
	{
		DoctorBatchCompletionGate gate = new DoctorBatchCompletionGate();
		IntPtr batchWindow = IntPtr.Zero;
		IntPtr startButton = IntPtr.Zero;
		bool leftWasDown = IsVirtualKeyDown(0x01);
		bool enterWasDown = IsVirtualKeyDown(0x0D);
		bool spaceWasDown = IsVirtualKeyDown(0x20);
		bool sWasDown = IsVirtualKeyDown(0x53);
		Logger.Info($"Doctor batch format step is waiting for actual completion: {ComboStepName(command)}");
		while (true)
		{
			token.ThrowIfCancellationRequested();
			if (batchWindow == IntPtr.Zero)
			{
				batchWindow = FindDoctorBatchFormatWindow();
				if (batchWindow != IntPtr.Zero)
				{
					startButton = UiControlClicker.FindByTextNative(batchWindow, "开始(S)|开始");
					Logger.Info($"Doctor batch format window detected: HWnd=0x{batchWindow.ToInt64():X}");
				}
			}

			bool windowVisible = batchWindow != IntPtr.Zero && NativeMethods.IsWindowVisible(batchWindow);
			if (windowVisible && startButton == IntPtr.Zero)
			{
				startButton = UiControlClicker.FindByTextNative(batchWindow, "开始(S)|开始");
			}
			bool startButtonFound = startButton != IntPtr.Zero && NativeMethods.IsWindow(startButton);
			bool startButtonEnabled = startButtonFound && NativeMethods.IsWindowEnabled(startButton);
			short leftState = NativeMethods.GetAsyncKeyState(0x01);
			bool leftDown = (leftState & 0x8000) != 0;
			bool leftPressedSinceLastProbe = (leftState & 0x0001) != 0;
			bool enterDown = IsVirtualKeyDown(0x0D);
			bool spaceDown = IsVirtualKeyDown(0x20);
			bool sDown = IsVirtualKeyDown(0x53);
			bool mouseAction = startButtonFound
				&& (((leftWasDown && !leftDown) || leftPressedSinceLastProbe) && IsCursorInside(startButton));
			bool keyboardAction = windowVisible
				&& NativeMethods.GetForegroundWindow() == batchWindow
				&& ((enterWasDown && !enterDown) || (spaceWasDown && !spaceDown) || (sWasDown && !sDown));
			bool completed = gate.Observe(windowVisible, startButtonFound, startButtonEnabled, mouseAction || keyboardAction);
			leftWasDown = leftDown;
			enterWasDown = enterDown;
			spaceWasDown = spaceDown;
			sWasDown = sDown;

			if (gate.ClosedWithoutStarting)
			{
				if (ShouldFailDoctorBatchCloseWithoutStart(command))
				{
					throw new InvalidOperationException($"外部批量处理窗口在未开始处理时关闭，组合已停止：{ComboStepName(command)}");
				}
				Logger.Info($"Doctor batch format window closed without a reliable start signal; continuing normal detection and keeping the advance key available: {ComboStepName(command)}");
				gate = new DoctorBatchCompletionGate();
				batchWindow = IntPtr.Zero;
				startButton = IntPtr.Zero;
				continue;
			}
			if (completed)
			{
				string source = windowVisible ? "start-button-disabled-enabled" : "window-hidden";
				if (windowVisible)
				{
					NativeMethods.PostMessage(batchWindow, 0x0010, IntPtr.Zero, IntPtr.Zero);
					while (NativeMethods.IsWindowVisible(batchWindow))
					{
						token.ThrowIfCancellationRequested();
						Sleep(token, 20);
					}
				}
				WaitForDoctorBatchPostCompletionIdle(idleEventBaseline, token);
				Logger.Info($"Doctor batch format step completed from actual signal: {ComboStepName(command)}, source={source}");
				return;
			}
			Sleep(token, 10);
		}
	}

	private void WaitForDoctorBatchPostCompletionIdle(long idleEventBaseline, CancellationToken token)
	{
		while (true)
		{
			token.ThrowIfCancellationRequested();
			if (Volatile.Read(ref _idleEventSequence) > idleEventBaseline && !CaptureRunningCommand().IsRunning)
			{
				return;
			}
			Sleep(token, 10);
		}
	}

	private void CloseLegacyCommandWindowPreservingSelection(IntPtr window, ComboCommandConfig command, CancellationToken token)
	{
		if (window == IntPtr.Zero || !NativeMethods.IsWindow(window))
		{
			return;
		}
		SelectionSnapshot selection = CaptureSelectionSnapshot();
		NativeMethods.PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
		{
			token.ThrowIfCancellationRequested();
			if (!NativeMethods.IsWindow(window) || !NativeMethods.IsWindowVisible(window))
			{
				if (selection != null)
				{
					RestoreSelectionSnapshot(selection);
				}
				Logger.Info($"Doctor command window closed before the next combo step: {ComboStepName(command)}");
				return;
			}
			Sleep(token, 10);
		}
		throw new TimeoutException($"外部命令“{ComboStepName(command)}”已执行，但工具窗口未正常关闭，组合已停止。");
	}

	private void CompleteLegacyAutomaticCommand(RunningCommandSnapshot before, WindowSnapshot beforeWindows, ComboCommandConfig command, string completionTargets, bool targetWasVisibleBefore, long commandEventBaseline, CancellationToken token)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		TimeSpan? closeObservedAt = null;
		while (stopwatch.Elapsed < TimeSpan.FromSeconds(4))
		{
			token.ThrowIfCancellationRequested();
			if (Volatile.Read(ref _lastCommandCloseSequence) > commandEventBaseline)
			{
				closeObservedAt ??= stopwatch.Elapsed;
			}
			RunningCommandSnapshot current = CaptureRunningCommand();
			bool running = IsNewRunningCommand(before, current);
			IntPtr commandWindow = FindNewSolidWorksWindow(beforeWindows);
			if (commandWindow != IntPtr.Zero)
			{
				IntPtr actionButton = UiControlClicker.FindByTextNative(commandWindow, "开始|执行|确定|完成|应用|生成|保存|立即");
				if (actionButton != IntPtr.Zero)
				{
					UiControlClicker.ClickNativeControl(actionButton);
					WaitForLegacyAutomaticWindowAction(commandWindow, actionButton, token);
				}
				else
				{
					NativeMethods.SetForegroundWindow(commandWindow);
					Sleep(token, 40);
					KeyboardSender.Send("Enter");
					WaitForWindowGone(commandWindow, TimeSpan.FromSeconds(5), token);
				}
				return;
			}
			bool targetVisible = !targetWasVisibleBefore && completionTargets.Length > 0 && IsSolidWorksTextVisibleFast(completionTargets);
			if (targetVisible || running)
			{
				SendHotkey("Enter");
				WaitForLegacyAutomaticPanelCompletion(before, completionTargets, targetVisible, token);
				return;
			}
			if (closeObservedAt.HasValue && stopwatch.Elapsed - closeObservedAt.Value >= TimeSpan.FromMilliseconds(750))
			{
				return;
			}

			Sleep(token, 20);
		}
		throw new InvalidOperationException($"外部自动步骤“{ComboStepName(command)}”未检测到可执行界面或完成状态。");
	}

	private void WaitForLegacyAutomaticWindowAction(IntPtr window, IntPtr actionButton, CancellationToken token)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		bool sawDisabled = false;
		while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
		{
			token.ThrowIfCancellationRequested();
			if (!NativeMethods.IsWindowVisible(window))
			{
				return;
			}
			bool enabled = NativeMethods.IsWindowEnabled(actionButton);
			sawDisabled |= !enabled;
			if ((sawDisabled && enabled) || (!sawDisabled && stopwatch.Elapsed >= TimeSpan.FromMilliseconds(300)))
			{
				NativeMethods.ShowWindow(window, 0);
				return;
			}
			Sleep(token, 20);
		}
		throw new TimeoutException("外部自动操作窗口未在规定时间内完成。");
	}

	private void WaitForLegacyAutomaticPanelCompletion(RunningCommandSnapshot before, string completionTargets, bool sawTarget, CancellationToken token)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		bool sawRunning = false;
		while (stopwatch.Elapsed < TimeSpan.FromSeconds(5))
		{
			token.ThrowIfCancellationRequested();
			RunningCommandSnapshot current = CaptureRunningCommand();
			sawRunning |= IsNewRunningCommand(before, current);
			bool targetVisible = sawTarget && completionTargets.Length > 0 && IsSolidWorksTextVisibleFast(completionTargets);
			if ((sawTarget && !targetVisible) || (sawRunning && !current.IsRunning))
			{
				return;
			}
			Sleep(token, 20);
		}
		throw new TimeoutException("外部自动确定后命令界面未结束。");
	}

	private void WaitForManualCommandCompletion(RunningCommandSnapshot before, WindowSnapshot beforeWindows, ComboCommandConfig command, string manualUiTargets, bool targetWasVisibleBefore, long idleEventBaseline, CancellationToken token)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		bool sawTarget = false;
		IntPtr manualWindow = IntPtr.Zero;
		IntPtr actionButton = IntPtr.Zero;
		bool leftWasDown = IsVirtualKeyDown(0x01);
		bool enterWasDown = IsVirtualKeyDown(0x0D);
		bool spaceWasDown = IsVirtualKeyDown(0x20);
		bool sawRunningCommand = false;
		TimeSpan nextCommandProbe = TimeSpan.Zero;
		ManualInteractionGate completionGate = new ManualInteractionGate();
		long? completionIdleBaseline = null;
		string completionSource = "";
		TimeSpan maxTimeout = TimeSpan.FromMinutes(10);
		TimeSpan uiProbeInterval = TimeSpan.FromMilliseconds(40);
		TimeSpan nextUiProbe = TimeSpan.Zero;
		while (stopwatch.Elapsed < maxTimeout)
		{
			token.ThrowIfCancellationRequested();
			if (!completionGate.HasObservedInteraction && stopwatch.Elapsed >= nextCommandProbe)
			{
				nextCommandProbe = stopwatch.Elapsed + TimeSpan.FromMilliseconds(30);
				bool running = IsNewRunningCommand(before, CaptureRunningCommand());
				if (running)
				{
					sawRunningCommand = true;
				}
				else if (sawRunningCommand && !completionIdleBaseline.HasValue)
				{
					completionIdleBaseline = Volatile.Read(ref _idleEventSequence);
					completionSource = "solidworks-command";
				}
			}
			bool targetVisible = sawTarget;
			if (stopwatch.Elapsed >= nextUiProbe)
			{
				nextUiProbe = stopwatch.Elapsed + uiProbeInterval;
				targetVisible = !targetWasVisibleBefore && !string.IsNullOrWhiteSpace(manualUiTargets) && IsSolidWorksTextVisibleFast(manualUiTargets);
				if (targetVisible)
				{
					sawTarget = true;
				}
			}
			if (manualWindow == IntPtr.Zero)
			{
				manualWindow = FindNewSolidWorksWindow(beforeWindows);
			}
			bool windowAlive = manualWindow != IntPtr.Zero && NativeMethods.IsWindowVisible(manualWindow);
			if (windowAlive && actionButton == IntPtr.Zero)
			{
				actionButton = UiControlClicker.FindByTextNative(manualWindow, "确定|完成|应用|生成|执行|开始|保存|确认|一键删除|一键执行|一键处理");
			}
			short leftState = NativeMethods.GetAsyncKeyState(0x01);
			bool leftDown = (leftState & 0x8000) != 0;
			bool leftPressedSinceLastProbe = (leftState & 0x0001) != 0;
			bool enterDown = IsVirtualKeyDown(0x0D);
			bool spaceDown = IsVirtualKeyDown(0x20);
			bool primaryActionReleased = actionButton != IntPtr.Zero
				&& ((((leftWasDown && !leftDown) || leftPressedSinceLastProbe) && IsCursorInside(actionButton))
					|| ((enterWasDown && !enterDown) || (spaceWasDown && !spaceDown)) && NativeMethods.GetForegroundWindow() == manualWindow);
			if (primaryActionReleased && !completionIdleBaseline.HasValue)
			{
				completionIdleBaseline = Volatile.Read(ref _idleEventSequence);
				completionSource = "primary-action";
			}
			leftWasDown = leftDown;
			enterWasDown = enterDown;
			spaceWasDown = spaceDown;
			if (completionGate.Observe(targetVisible, windowAlive) && !completionIdleBaseline.HasValue)
			{
				completionIdleBaseline = Volatile.Read(ref _idleEventSequence);
				completionSource = !windowAlive && manualWindow != IntPtr.Zero ? "window-closed" : "panel-closed";
			}
			if (completionIdleBaseline.HasValue && IsCompletionIdle(command, completionIdleBaseline.Value))
			{
				if (completionSource.Equals("primary-action", StringComparison.Ordinal) && manualWindow != IntPtr.Zero)
				{
					CloseCompletedCommandWindowPreservingSelection(manualWindow, command, token);
					completionSource += "+window-closed";
				}
				Logger.Trace($"Manual combo step completed: {ComboStepName(command)}, source={completionSource}, elapsedMs={stopwatch.ElapsedMilliseconds}");
				return;
			}
			if (AllowsUnobservedComboCompletion(command)
				&& !completionGate.HasObservedInteraction
				&& manualWindow == IntPtr.Zero
				&& !sawTarget
				&& !sawRunningCommand
				&& IsCompletionIdle(command, idleEventBaseline))
			{
				Logger.Trace($"Direct plugin combo step completed synchronously and SOLIDWORKS returned idle: {ComboStepName(command)}");
				return;
			}
			Sleep(token, 10);
		}
		throw new TimeoutException($"等待“{ComboStepName(command)}”手动完成超时，组合已停止。");
	}

	private static bool IsVirtualKeyDown(int virtualKey)
	{
		return (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
	}

	private static bool IsCursorInside(IntPtr window)
	{
		if (window == IntPtr.Zero || !NativeMethods.GetWindowRect(window, out NativeMethods.RECT rect))
		{
			return false;
		}
		Point cursor = Cursor.Position;
		return cursor.X >= rect.Left && cursor.X < rect.Right && cursor.Y >= rect.Top && cursor.Y < rect.Bottom;
	}

	private void CompleteAutomaticCommand(RunningCommandSnapshot before, WindowSnapshot beforeWindows, ComboCommandConfig command, string completionTargets, bool targetWasVisibleBefore, long commandEventBaseline, long idleEventBaseline, CancellationToken token)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		long? closeIdleBaseline = null;
		bool allowUnobservedCompletion = AllowsUnobservedComboCompletion(command);
		while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
		{
			token.ThrowIfCancellationRequested();
			if (Volatile.Read(ref _lastCommandCloseSequence) > commandEventBaseline && !closeIdleBaseline.HasValue)
			{
				closeIdleBaseline = Volatile.Read(ref _idleEventSequence);
			}
			RunningCommandSnapshot current = CaptureRunningCommand();
			bool running = IsNewRunningCommand(before, current);
			IntPtr commandWindow = FindNewSolidWorksWindow(beforeWindows);
			if (commandWindow != IntPtr.Zero)
			{
				IntPtr actionButton = UiControlClicker.FindByTextNative(commandWindow, "确定|完成|应用|生成|执行|开始|保存|确认|立即|一键删除|一键执行|一键处理");
				if (actionButton != IntPtr.Zero)
				{
					long actionIdleBaseline = Volatile.Read(ref _idleEventSequence);
					UiControlClicker.ClickNativeControl(actionButton);
					WaitForAutomaticWindowAction(command, actionIdleBaseline, token);
					CloseCompletedCommandWindowPreservingSelection(commandWindow, command, token);
				}
				else
				{
					NativeMethods.SetForegroundWindow(commandWindow);
					long actionIdleBaseline = Volatile.Read(ref _idleEventSequence);
					KeyboardSender.Send("Enter");
					WaitForAutomaticWindowAction(command, actionIdleBaseline, token);
					CloseCompletedCommandWindowPreservingSelection(commandWindow, command, token);
				}
				return;
			}
			bool targetVisible = !targetWasVisibleBefore && completionTargets.Length > 0 && IsSolidWorksTextVisibleFast(completionTargets);
			if (targetVisible || (!allowUnobservedCompletion && running))
			{
				long actionIdleBaseline = Volatile.Read(ref _idleEventSequence);
				SendHotkey("Enter");
				WaitForAutomaticPanelCompletion(before, command, completionTargets, targetVisible, actionIdleBaseline, token);
				return;
			}
			if (closeIdleBaseline.HasValue && IsCompletionIdle(command, closeIdleBaseline.Value))
			{
				return;
			}
			if (allowUnobservedCompletion && !running && IsCompletionIdle(command, idleEventBaseline))
			{
				Logger.Info($"Direct plugin combo step callback returned and SOLIDWORKS reported idle: {ComboStepName(command)}");
				return;
			}
			Sleep(token, 20);
		}
		throw new InvalidOperationException($"自动步骤“{ComboStepName(command)}”未收到窗口、命令关闭或 SOLIDWORKS 空闲完成信号。");
	}

	private bool IsCompletionIdle(ComboCommandConfig command, long idleEventBaseline)
	{
		if (Volatile.Read(ref _idleEventSequence) <= idleEventBaseline)
		{
			return false;
		}
		string kind = (command?.Kind ?? ActionKinds.Hotkey).Trim();
		return kind.Equals(ActionKinds.QinJian, StringComparison.OrdinalIgnoreCase)
			|| kind.Equals(ActionKinds.JiaLiChuang, StringComparison.OrdinalIgnoreCase)
			|| !CaptureRunningCommand().IsRunning;
	}

	private void WaitForComboStepSettle(ComboCommandConfig command, long idleEventBaseline, CancellationToken token)
	{
		string kind = (command?.Kind ?? ActionKinds.Hotkey).Trim();
		if (kind.Equals(ActionKinds.Delay, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
		{
			token.ThrowIfCancellationRequested();
			if (IsCompletionIdle(command, idleEventBaseline))
			{
				return;
			}
			Sleep(token, 10);
		}
		throw new TimeoutException($"步骤“{ComboStepName(command)}”结束后 SOLIDWORKS 未确认空闲，组合已停止以避免下一步重叠。");
	}

	private void WaitForAutomaticWindowAction(ComboCommandConfig command, long idleEventBaseline, CancellationToken token)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
		{
			token.ThrowIfCancellationRequested();
			if (IsCompletionIdle(command, idleEventBaseline))
			{
				return;
			}
			Sleep(token, 10);
		}
		throw new TimeoutException("自动操作已执行，但 SOLIDWORKS 未确认该步骤结束。");
	}

	private void CloseCompletedCommandWindowPreservingSelection(IntPtr window, ComboCommandConfig command, CancellationToken token)
	{
		if (window == IntPtr.Zero || !NativeMethods.IsWindow(window))
		{
			return;
		}
		SelectionSnapshot selection = CaptureSelectionSnapshot();
		long closeIdleBaseline = Volatile.Read(ref _idleEventSequence);
		NativeMethods.PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
		{
			token.ThrowIfCancellationRequested();
			if (!NativeMethods.IsWindow(window) && IsCompletionIdle(command, closeIdleBaseline))
			{
				if (selection != null)
				{
					RestoreSelectionSnapshot(selection);
				}
				Logger.Info($"Completed command window closed before next combo step: {ComboStepName(command)}");
				return;
			}
			Sleep(token, 10);
		}
		throw new TimeoutException($"“{ComboStepName(command)}”已执行，但工具窗口未正常结束，组合已停止。");
	}

	private void WaitForAutomaticPanelCompletion(RunningCommandSnapshot before, ComboCommandConfig command, string completionTargets, bool sawTarget, long idleEventBaseline, CancellationToken token)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		bool sawRunning = false;
		bool completionObserved = false;
		while (stopwatch.Elapsed < TimeSpan.FromSeconds(5))
		{
			token.ThrowIfCancellationRequested();
			RunningCommandSnapshot current = CaptureRunningCommand();
			sawRunning |= IsNewRunningCommand(before, current);
			bool targetVisible = sawTarget && completionTargets.Length > 0 && IsSolidWorksTextVisibleFast(completionTargets);
			completionObserved |= (sawTarget && !targetVisible) || (sawRunning && !current.IsRunning);
			if (completionObserved && IsCompletionIdle(command, idleEventBaseline))
			{
				return;
			}
			Sleep(token, 20);
		}
		throw new TimeoutException("自动确定后命令界面未结束。");
	}

	private bool WaitForSolidWorksText(string targetText, TimeSpan timeout)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		do
		{
			if (IsSolidWorksTextVisible(targetText))
			{
				return true;
			}
			Thread.Sleep(120);
		}
		while (stopwatch.Elapsed < timeout);
		return false;
	}

	private bool IsSolidWorksTextVisible(string targetText)
	{
		IntPtr root = GetSolidWorksWindowHandle();
		return root != IntPtr.Zero && UiControlClicker.ContainsByText(root, targetText);
	}

	private bool IsSolidWorksTextVisibleFast(string targetText)
	{
		IntPtr root = GetSolidWorksWindowHandle();
		return root != IntPtr.Zero && UiControlClicker.ContainsByTextNative(root, targetText);
	}

	private void WaitForSolidWorksTextGone(string targetText, TimeSpan timeout, CancellationToken token)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < timeout)
		{
			token.ThrowIfCancellationRequested();
			IntPtr root = GetSolidWorksWindowHandle();
			if (root == IntPtr.Zero || !UiControlClicker.ContainsByText(root, targetText))
			{
				return;
			}
			Sleep(token, 80);
		}
	}

	private IntPtr WaitForNewSolidWorksWindow(WindowSnapshot before, TimeSpan timeout)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		do
		{
			IntPtr window = FindNewSolidWorksWindow(before);
			if (window != IntPtr.Zero)
			{
				return window;
			}
			Thread.Sleep(120);
		}
		while (stopwatch.Elapsed < timeout);
		return IntPtr.Zero;
	}

	private IntPtr FindNewSolidWorksWindow(WindowSnapshot before)
	{
		foreach (IntPtr window in EnumerateSolidWorksWindows())
		{
			if (!before.Contains(window) && IsInteractiveCommandWindow(window))
			{
				return window;
			}
		}
		return IntPtr.Zero;
	}

	private IntPtr FindDoctorBatchFormatWindow()
	{
		foreach (IntPtr window in EnumerateSolidWorksWindows())
		{
			if (!IsInteractiveCommandWindow(window))
			{
				continue;
			}
			string title = GetNativeWindowText(window);
			if (title.IndexOf("中间格式互换", StringComparison.OrdinalIgnoreCase) >= 0
				&& UiControlClicker.FindByTextNative(window, "开始(S)|开始") != IntPtr.Zero)
			{
				return window;
			}
		}
		return IntPtr.Zero;
	}

	private IntPtr FindDoctorAutomaticDrawingWindow()
	{
		foreach (IntPtr window in EnumerateSolidWorksWindows())
		{
			if (!IsInteractiveCommandWindow(window))
			{
				continue;
			}
			string title = GetNativeWindowText(window);
			if (title.Equals("自动工程图", StringComparison.OrdinalIgnoreCase)
				&& UiControlClicker.FindByTextNative(window, "开始(S)") != IntPtr.Zero)
			{
				return window;
			}
		}
		return IntPtr.Zero;
	}

	private static string GetNativeWindowText(IntPtr window)
	{
		int length = NativeMethods.GetWindowTextLength(window);
		if (length <= 0)
		{
			return "";
		}
		char[] text = new char[length + 1];
		int read = NativeMethods.GetWindowText(window, text, text.Length);
		return read <= 0 ? "" : new string(text, 0, read);
	}

	private static bool IsInteractiveCommandWindow(IntPtr window)
	{
		if (!NativeMethods.IsWindowVisible(window) || !NativeMethods.IsWindowEnabled(window))
		{
			return false;
		}
		if (!NativeMethods.GetWindowRect(window, out NativeMethods.RECT rect))
		{
			return false;
		}
		return rect.Right - rect.Left >= 160 && rect.Bottom - rect.Top >= 100;
	}

	private static void WaitForWindowGone(IntPtr window, TimeSpan timeout, CancellationToken token)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < timeout)
		{
			token.ThrowIfCancellationRequested();
			if (!NativeMethods.IsWindowVisible(window) || !NativeMethods.IsWindowEnabled(window))
			{
				return;
			}
			Sleep(token, 80);
		}
	}

	private bool WaitForCommandStart(RunningCommandSnapshot before, TimeSpan timeout, CancellationToken token, out RunningCommandSnapshot startedCommand)
	{
		startedCommand = default;
		Stopwatch stopwatch = Stopwatch.StartNew();
		do
		{
			token.ThrowIfCancellationRequested();
			RunningCommandSnapshot current = CaptureRunningCommand();
			if (IsNewRunningCommand(before, current))
			{
				startedCommand = current;
				return true;
			}
			Sleep(token, 25);
		}
		while (stopwatch.Elapsed < timeout);
		return false;
	}

	private static bool IsNewRunningCommand(RunningCommandSnapshot before, RunningCommandSnapshot current)
	{
		return current.IsRunning && (!current.SameCommand(before) || !before.IsRunning);
	}

	private void WaitForCommandCompletion(TimeSpan timeout, CancellationToken token)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < timeout)
		{
			token.ThrowIfCancellationRequested();
			if (!CaptureRunningCommand().IsRunning)
			{
				return;
			}
			Sleep(token, 35);
		}
	}

	private static void Sleep(CancellationToken token, int milliseconds)
	{
		if (milliseconds <= 0)
		{
			token.ThrowIfCancellationRequested();
			return;
		}
		if (token.WaitHandle.WaitOne(milliseconds))
		{
			throw new OperationCanceledException(token);
		}
	}

	private RunningCommandSnapshot CaptureRunningCommand()
	{
		SldWorks? swApp = _swApp;
		if (swApp == null)
		{
			return default;
		}
		bool inProgress = false;
		int commandId = 0;
		string title = "";
		bool isUiActive = false;
		try
		{
			inProgress = ((ISldWorks)swApp).CommandInProgress;
		}
		catch
		{
		}
		try
		{
			((ISldWorks)swApp).GetRunningCommandInfo(out commandId, out title, out isUiActive);
		}
		catch
		{
		}
		return new RunningCommandSnapshot(inProgress || isUiActive || commandId != 0 || !string.IsNullOrWhiteSpace(title), commandId, title ?? "");
	}

	private WindowSnapshot CaptureSolidWorksWindowSnapshot()
	{
		return new WindowSnapshot(EnumerateSolidWorksWindows());
	}

	private IReadOnlyList<IntPtr> EnumerateSolidWorksWindows()
	{
		IntPtr root = GetSolidWorksWindowHandle();
		if (root == IntPtr.Zero)
		{
			return Array.Empty<IntPtr>();
		}
		NativeMethods.GetWindowThreadProcessId(root, out uint rootProcessId);
		if (rootProcessId == 0)
		{
			return Array.Empty<IntPtr>();
		}
		List<IntPtr> windows = new List<IntPtr>();
		NativeMethods.EnumWindows(delegate(IntPtr hwnd, IntPtr _)
		{
			NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
			if (processId == rootProcessId && NativeMethods.IsWindowVisible(hwnd))
			{
				windows.Add(hwnd);
			}
			return true;
		}, IntPtr.Zero);
		return windows.Distinct().ToList();
	}

	private SelectionSnapshot CaptureSelectionSnapshot()
	{
		try
		{
			ModelDoc2 model = ActiveModel();
			if (model == null)
			{
				return null;
			}
			ModelDocExtension extension = ((IModelDoc2)model).Extension;
			SelectionMgr selectionMgr = ((IModelDoc2)model).ISelectionManager;
			if (extension == null || selectionMgr == null)
			{
				return null;
			}
			int count = selectionMgr.GetSelectedObjectCount2(-1);
			if (count <= 0)
			{
				return null;
			}
			List<SelectionReference> references = new List<SelectionReference>();
			for (int index = 1; index <= count; index++)
			{
				object selected = selectionMgr.GetSelectedObject6(index, -1);
				if (selected == null)
				{
					continue;
				}
				object persistReference = extension.GetPersistReference3(selected);
				if (persistReference == null)
				{
					continue;
				}
				int mark = 0;
				try
				{
					mark = selectionMgr.GetSelectedObjectMark(index);
				}
				catch
				{
				}
				references.Add(new SelectionReference(persistReference, mark));
			}
			return references.Count == 0 ? null : new SelectionSnapshot(references);
		}
		catch (Exception ex)
		{
			Logger.Error("Capture selection snapshot failed", ex);
			return null;
		}
	}

	private void RestoreSelectionSnapshot(SelectionSnapshot snapshot)
	{
		if (snapshot == null || snapshot.References.Count == 0)
		{
			return;
		}
		try
		{
			ModelDoc2 model = ActiveModel();
			if (model == null)
			{
				return;
			}
			ModelDocExtension extension = ((IModelDoc2)model).Extension;
			SelectionMgr selectionMgr = ((IModelDoc2)model).ISelectionManager;
			if (extension == null || selectionMgr == null)
			{
				return;
			}
			((IModelDoc2)model).ClearSelection2(true);
			foreach (SelectionReference reference in snapshot.References)
			{
				int errorCode = 0;
				object restored = extension.GetObjectByPersistReference3(reference.PersistReference, out errorCode);
				if (restored == null)
				{
					continue;
				}
				SelectData selectData = selectionMgr.CreateSelectData();
				if (selectData != null)
				{
					((ISelectData)selectData).Mark = reference.Mark;
				}
				selectionMgr.AddSelectionListObject(restored, selectData);
			}
		}
		catch (Exception ex)
		{
			Logger.Error("Restore selection snapshot failed", ex);
		}
	}

	private FeatureTreeExpansionSnapshot CaptureFeatureTreeExpansionSnapshot()
	{
		FeatureTreeExpansionSnapshot snapshot = null;
		try
		{
			SwCommandDispatcher dispatcher = _dispatcher;
			if (dispatcher == null)
			{
				return null;
			}
			dispatcher.Invoke(delegate
			{
				ModelDoc2 model = ActiveModel();
				FeatureManager manager = model == null ? null : ((IModelDoc2)model).FeatureManager;
				TreeControlItem root = manager == null ? null : ((IFeatureManager)manager).GetFeatureTreeRootItem();
				if (root == null)
				{
					return;
				}
				List<FeatureTreeExpansionState> states = new List<FeatureTreeExpansionState>();
				CaptureFeatureTreeExpansionStates(root, states);
				snapshot = states.Count == 0 ? null : new FeatureTreeExpansionSnapshot(states);
			});
		}
		catch (Exception ex)
		{
			Logger.Error("Capture feature-tree expansion snapshot failed", ex);
		}
		return snapshot;
	}

	private static void CaptureFeatureTreeExpansionStates(TreeControlItem item, List<FeatureTreeExpansionState> states)
	{
		if (item == null)
		{
			return;
		}
		states.Add(new FeatureTreeExpansionState(item, ((ITreeControlItem)item).Expanded));
		for (TreeControlItem child = ((ITreeControlItem)item).GetFirstChild(); child != null; child = ((ITreeControlItem)child).GetNext())
		{
			CaptureFeatureTreeExpansionStates(child, states);
		}
	}

	private void RestoreFeatureTreeExpansionSnapshot(FeatureTreeExpansionSnapshot snapshot)
	{
		if (snapshot == null || snapshot.States.Count == 0)
		{
			return;
		}
		try
		{
			SwCommandDispatcher dispatcher = _dispatcher;
			if (dispatcher == null)
			{
				return;
			}
			dispatcher.Invoke(delegate
			{
				foreach (FeatureTreeExpansionState state in snapshot.States)
				{
					((ITreeControlItem)state.Item).Expanded = state.Expanded;
				}
			});
		}
		catch (Exception ex)
		{
			Logger.Error("Restore feature-tree expansion snapshot failed", ex);
		}
	}

	private ModelDoc2 ActiveModel()
	{
		SldWorks? swApp = _swApp;
		object activeDoc = swApp == null ? null : ((ISldWorks)swApp).ActiveDoc;
		return activeDoc as ModelDoc2;
	}

	private readonly struct RunningCommandSnapshot
	{
		public RunningCommandSnapshot(bool isRunning, int commandId, string title)
		{
			IsRunning = isRunning;
			CommandId = commandId;
			Title = title ?? "";
		}

		public bool IsRunning { get; }

		private int CommandId { get; }

		private string Title { get; }

		public bool SameCommand(RunningCommandSnapshot other)
		{
			return IsRunning == other.IsRunning
				&& CommandId == other.CommandId
				&& string.Equals(Title, other.Title, StringComparison.OrdinalIgnoreCase);
		}
	}

	private sealed class SelectionSnapshot
	{
		public SelectionSnapshot(List<SelectionReference> references)
		{
			References = references;
		}

		public List<SelectionReference> References { get; }
	}

	private sealed class FeatureTreeExpansionSnapshot
	{
		public FeatureTreeExpansionSnapshot(List<FeatureTreeExpansionState> states)
		{
			States = states;
		}

		public List<FeatureTreeExpansionState> States { get; }
	}

	private sealed class FeatureTreeExpansionState
	{
		public FeatureTreeExpansionState(TreeControlItem item, bool expanded)
		{
			Item = item;
			Expanded = expanded;
		}

		public TreeControlItem Item { get; }

		public bool Expanded { get; }
	}

	private sealed class SelectionReference
	{
		public SelectionReference(object persistReference, int mark)
		{
			PersistReference = persistReference;
			Mark = mark;
		}

		public object PersistReference { get; }

		public int Mark { get; }
	}

	private sealed class WindowSnapshot
	{
		private readonly HashSet<IntPtr> _windows;

		public WindowSnapshot(IEnumerable<IntPtr> windows)
		{
			_windows = new HashSet<IntPtr>(windows ?? Enumerable.Empty<IntPtr>());
		}

		public bool Contains(IntPtr window)
		{
			return _windows.Contains(window);
		}
	}

	private void ClickSolidWorksControl(string targetText, CancellationToken token)
	{
		if (string.IsNullOrWhiteSpace(targetText))
		{
			throw new InvalidOperationException("点击目标为空。");
		}
		IntPtr root = GetSolidWorksWindowHandle();
		if (root == IntPtr.Zero) throw new InvalidOperationException("找不到 SOLIDWORKS 主窗口。");
		NativeMethods.GetWindowThreadProcessId(root, out uint hostProcess);
		IntPtr foreground = NativeMethods.GetForegroundWindow();
		NativeMethods.GetWindowThreadProcessId(foreground, out uint foregroundProcess);
		if (foreground == IntPtr.Zero || foregroundProcess != hostProcess)
		{
			ActivateSolidWorks(); foreground = NativeMethods.GetForegroundWindow();
			NativeMethods.GetWindowThreadProcessId(foreground, out foregroundProcess);
		}
		if (foregroundProcess != hostProcess) throw new InvalidOperationException("SOLIDWORKS 当前界面未获得焦点，未点击其他程序。");
		// A text-only legacy UI step is scoped to the current native window.
		// It never searches every document/dialog in the host for the first name.
		if (!UiControlClicker.ClickUniqueByText(foreground, targetText, TimeSpan.FromMilliseconds(3000), token, out string message))
		{
			throw new InvalidOperationException(message);
		}
	}

	private void ExecuteQinJianFunction(string actionValue, CancellationToken token)
	{
		ActivateSolidWorks();
		SldWorks swApp = _swApp ?? throw new InvalidOperationException("SOLIDWORKS 未连接。");
		if (QinJianActionValue.TryParse(actionValue, out string tabName, out string commandName)
			&& TryExecuteCurrentPluginUi(tabName, commandName, token)) return;
		bool catalogSucceeded = false;
		bool callbackInvoked = false;
		bool identityRejected = false;
		string catalogMessage = "";
		SwCommandDispatcher dispatcher = _dispatcher ?? throw new InvalidOperationException("SOLIDWORKS 主线程调度器不可用。");
		dispatcher.Invoke(delegate
		{
			catalogSucceeded = QinJianCommandCatalog.TryExecute((ISldWorks)swApp, actionValue, out catalogMessage, out callbackInvoked, out identityRejected);
		});
		if (catalogSucceeded)
		{
			return;
		}
		if (callbackInvoked || identityRejected)
		{
			throw new InvalidOperationException(catalogMessage);
		}
		IntPtr root = GetSolidWorksWindowHandle();
		if (!QinJianFunctionReader.TryExecute(root, actionValue, TimeSpan.FromMilliseconds(3500), token, out string message))
		{
			throw new InvalidOperationException(catalogMessage + System.Environment.NewLine + message);
		}
	}

	private void ExecuteExternalPluginFunction(string actionValue, CancellationToken token)
	{
		// Live readers and the click recorder retain toolbar references. They
		// use the external action kind and must share the validated dispatcher.
		if (ExternalPluginActionValue.TryParse(actionValue, out string tabName, out string commandName))
		{
			ActivateSolidWorks();
			if (TryExecuteCurrentPluginUi(tabName, commandName, token)) return;
			if (!QinJianFunctionReader.TryExecuteExternalPlugin(GetSolidWorksWindowHandle(), actionValue, TimeSpan.FromMilliseconds(3500), token, out string uiMessage))
				throw new InvalidOperationException(uiMessage);
			return;
		}
		if (ExternalToolbarCommandReference.TryDecode(actionValue, out _, out _))
		{
			token.ThrowIfCancellationRequested();
			_dispatcher.Invoke(() =>
			{
				if (!SolidWorksNativeCommandCatalog.TryExecute((ISldWorks)_swApp, actionValue, out string error))
					throw new InvalidOperationException(error);
			});
			return;
		}
		if (!ExternalNamedCommandReference.TryDecode(actionValue, out int expectedType, out string category, out string name))
			throw new InvalidOperationException("该外部功能缺少文档环境与名称映射，请重新读取外部插件。");
		token.ThrowIfCancellationRequested();
		_dispatcher.Invoke(() =>
		{
			if (!ExternalPluginCommandCatalog.TryExecuteCurrent((ISldWorks)_swApp, expectedType, category, name, out string error))
				throw new InvalidOperationException(error);
		});
	}

	private bool TryExecuteCurrentPluginUi(string tabName, string commandName, CancellationToken token)
	{
		token.ThrowIfCancellationRequested();
		bool handled = false, submitted = false; string message = "";
		_dispatcher.Invoke(() => submitted = ExternalPluginCommandCatalog.TryExecuteUiReference((ISldWorks)_swApp,
			tabName, commandName, out handled, out message));
		if (handled && !submitted) throw new InvalidOperationException(message);
		return submitted;
	}

	private void ExecuteJiaLiChuangFunction(string callbackName)
	{
		ActivateSolidWorks();
		SldWorks swApp = _swApp ?? throw new InvalidOperationException("SOLIDWORKS 未连接。");
		bool succeeded = false;
		string catalogMessage = "";
		SwCommandDispatcher dispatcher = _dispatcher ?? throw new InvalidOperationException("SOLIDWORKS 主线程调度器不可用。");
		dispatcher.Invoke(delegate
		{
			succeeded = JiaLiChuangFunctionCatalog.TryExecute((ISldWorks)swApp, callbackName, out catalogMessage, out _);
		});
		if (!succeeded)
		{
			throw new InvalidOperationException(catalogMessage);
		}
	}

	private static int ParseDelay(string value)
	{
		if (!int.TryParse((value ?? "").Trim(), out int delayMs))
		{
			return 200;
		}
		return Math.Max(0, Math.Min(10000, delayMs));
	}

	private static bool IsUiClick(ComboCommandConfig command)
	{
		return string.Equals((command.Kind ?? "").Trim(), ActionKinds.UiClick, StringComparison.OrdinalIgnoreCase);
	}

	internal static bool IsPassThroughComboCommand(ComboCommandConfig command)
	{
		string text = CleanManualCompletionTarget(command?.Label) + " " + (command?.Value ?? "");
		return text.IndexOf("快速钣金", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	internal static bool UsesDoctorUiThreadSingleTrigger(ComboCommandConfig command)
	{
		return !IsQinJianComboCommand(command)
			&& !IsJiaLiChuangComboCommand(command)
			&& IsPassThroughComboCommand(command);
	}

	private static string DoctorCommandText(ComboCommandConfig command)
	{
		if (ExternalNamedCommandReference.TryDecode(command?.Value, out int _, out string category, out string name))
			return category.IndexOf("博士", StringComparison.OrdinalIgnoreCase) >= 0 || category.IndexOf("Doctor", StringComparison.OrdinalIgnoreCase) >= 0 ? name : "";
		return (command?.Label ?? "") + " " + (command?.Value ?? "");
	}

	internal static bool IsDoctorBatchFormatConversion(ComboCommandConfig command)
	{
		if (command == null || IsQinJianComboCommand(command) || IsJiaLiChuangComboCommand(command))
		{
			return false;
		}
		string text = DoctorCommandText(command);
		return text.IndexOf("格式互换", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("格式转换", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	internal static bool IsDoctorPersistentTaskPaneCommand(ComboCommandConfig command)
	{
		if (command == null || IsQinJianComboCommand(command) || IsJiaLiChuangComboCommand(command))
		{
			return false;
		}
		string text = DoctorCommandText(command);
		return text.IndexOf("自动出图", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("自动工程图", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	// A recorded step can pin its own hand-off key; empty falls back to the key
	// resolved for the whole sequence.
	internal static string ResolveStepAdvanceKey(ComboExecutionStep step, string sequenceKey)
	{
		if (step?.Command == null) return ComboAdvanceKeys.Normalize(sequenceKey);
		string key = (step.Command.AdvanceKey ?? "").Trim();
		return key.Length > 0 ? ComboAdvanceKeys.Normalize(key) : ComboAdvanceKeys.Normalize(sequenceKey);
	}

	private static void WaitComboStepDelay(int delayMs, CancellationToken token)
	{
		int remaining = Math.Max(0, Math.Min(600000, delayMs));
		const int slice = 50;
		while (remaining > 0)
		{
			token.ThrowIfCancellationRequested();
			int wait = Math.Min(slice, remaining);
			Thread.Sleep(wait);
			remaining -= wait;
		}
	}

	// An expired manual hand-off is a failed step; the existing stop/retry/skip
	// policy decides what happens next instead of silently advancing.
	private static void WaitForStepAdvance(ComboAdvanceController advance, ComboExecutionStep step, CancellationToken token, int stepNumber, int stepCount)
	{
		if (step.TimeoutMs > 0)
		{
			if (advance.Enabled && !advance.WaitForAdvance(step.TimeoutMs))
				throw new TimeoutException("等待继续键超时：" + ComboStepName(step.Command));
			return;
		}
		advance.WaitForAdvance();
	}

	internal static bool RequiresStepAdvanceKey(ComboCommandConfig command, int stepIndex, int stepCount)
	{
		return command != null
			&& !command.AutoConfirm
			&& command.RequireAdvanceKey
			&& stepIndex >= 0
			&& stepIndex < stepCount - 1;
	}

	internal static bool UsesDoctorAdvanceKeyFallback(ComboCommandConfig command, int stepIndex, int stepCount)
	{
		return command != null
			&& !command.AutoConfirm
			&& stepIndex >= 0
			&& stepIndex < stepCount - 1
			&& (IsDoctorBatchFormatConversion(command) || IsDoctorPropertyTaskPaneCommand(command));
	}

	internal static bool ShouldEnableStepAdvanceKey(ComboCommandConfig command, bool comboAdvanceEnabled, int stepIndex, int stepCount)
	{
		return command != null
			&& (!command.AutoConfirm || IsDoctorPersistentTaskPaneCommand(command))
			&& stepIndex >= 0
			&& stepIndex < stepCount - 1
			&& (comboAdvanceEnabled || command.RequireAdvanceKey || IsDoctorPersistentTaskPaneCommand(command)
				|| UsesDoctorAdvanceKeyFallback(command, stepIndex, stepCount)
				|| string.Equals(ComboWaitModes.Normalize(command.WaitMode), ComboWaitModes.ManualConfirm, StringComparison.Ordinal));
	}

	internal static bool CanExecuteImmediateComboStep(ComboExecutionStep step)
	{
		return step?.Command != null
			&& string.Equals(step.WaitMode, ComboWaitModes.None, StringComparison.Ordinal)
			&& !step.Command.RequireAdvanceKey
			&& IsImmediateComboCommand(step.Command);
	}

	internal static bool IsImmediateComboCommand(ComboCommandConfig command)
	{
		if (command?.Kind == ActionKinds.Delay) return true;
		if (command?.Kind == ActionKinds.NativeCommand && NativeCommandReference.TryDecodeId(command.Value, out int id))
			return new[] { "swCommands_ZoomToFit", "swCommands_ZoomToFitKey", "swCommands_Front", "swCommands_Back", "swCommands_Left", "swCommands_Right", "swCommands_Top", "swCommands_Bottom", "swCommands_Isometric" }.Contains(((SolidWorks.Interop.swcommands.swCommands_e)id).ToString());
		if (command?.Kind != ActionKinds.Basic) return false;
		return new[] { "new-part", "new-assembly", "new-drawing", "rebuild", "force-rebuild", "normal-to", "zoom-fit", "clear-selection", "view-front", "view-back", "view-left", "view-right", "view-top", "view-bottom", "view-isometric" }.Contains(command.Value);
	}

	internal static bool ShouldFailDoctorBatchCloseWithoutStart(ComboCommandConfig command)
	{
		return command == null || command.AutoConfirm;
	}

	internal static string BuildComboAdvanceStatusText(ComboCommandConfig command, string key)
	{
		return $"XY轮盘：{ComboStepName(command)}功能完成后，按 {ComboAdvanceKeys.Normalize(key)} 进入下一步";
	}

	internal static string BuildDoctorAdvanceFallbackStatusText(ComboCommandConfig command, string key)
	{
		return $"XY轮盘：正在判断{ComboStepName(command)}是否完成；如已完成，可按 {ComboAdvanceKeys.Normalize(key)} 手动进入下一步";
	}

	internal static bool IsDoctorPropertyTaskPaneCommand(ComboCommandConfig command)
	{
		if (command == null || IsQinJianComboCommand(command) || IsJiaLiChuangComboCommand(command))
		{
			return false;
		}
		string text = DoctorCommandText(command);
		return text.IndexOf("属性处理", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	internal static bool AllowsUnobservedComboCompletion(ComboCommandConfig command)
	{
		return IsQinJianComboCommand(command) || IsJiaLiChuangComboCommand(command);
	}

	internal static bool IsQinJianComboCommand(ComboCommandConfig command)
	{
		return string.Equals((command?.Kind ?? "").Trim(), ActionKinds.QinJian, StringComparison.OrdinalIgnoreCase);
	}

	internal static bool IsJiaLiChuangComboCommand(ComboCommandConfig command)
	{
		return string.Equals((command?.Kind ?? "").Trim(), ActionKinds.JiaLiChuang, StringComparison.OrdinalIgnoreCase);
	}


	private static string ComboStepName(ComboCommandConfig command)
	{
		if (command == null)
		{
			return "未知命令";
		}
		string label = (command.Label ?? "").Trim();
		if (label.Length > 0)
		{
			return label;
		}
		string value = (command.Value ?? "").Trim();
		return value.Length == 0 ? "未知命令" : value;
	}

	private static string ManualCompletionTargets(ComboCommandConfig command)
	{
		if (command == null)
		{
			return "";
		}
		List<string> targets = new List<string>();
		AddManualCompletionTarget(targets, command.Label);
		if (!LooksLikeHotkey(command.Value))
		{
			AddManualCompletionTarget(targets, command.Value);
		}
		return string.Join("|", targets);
	}

	private static void AddManualCompletionTarget(List<string> targets, string value)
	{
		string target = CleanManualCompletionTarget(value);
		if (target.Length > 0 && !targets.Contains(target, StringComparer.OrdinalIgnoreCase))
		{
			targets.Add(target);
		}
	}

	private static string CleanManualCompletionTarget(string value)
	{
		string text = (value ?? "").Trim();
		while (text.EndsWith(".", StringComparison.Ordinal) || text.EndsWith("。", StringComparison.Ordinal) || text.EndsWith("…", StringComparison.Ordinal))
		{
			text = text.Substring(0, text.Length - 1).Trim();
		}
		return text;
	}

	private static bool LooksLikeHotkey(string value)
	{
		string text = (value ?? "").Trim();
		return text.IndexOf('+') >= 0
			|| text.StartsWith("Ctrl", StringComparison.OrdinalIgnoreCase)
			|| text.StartsWith("Alt", StringComparison.OrdinalIgnoreCase)
			|| text.StartsWith("Shift", StringComparison.OrdinalIgnoreCase)
			|| text.StartsWith("F", StringComparison.OrdinalIgnoreCase) && text.Length <= 3;
	}

	private void SendHotkey(string hotkey)
	{
		if (!ShortcutPaletteMatcher.IsHotkey(hotkey))
		{
			throw new InvalidOperationException("快捷键格式无效，未把功能编码或路径作为按键发送。");
		}
		ActivateSolidWorks();
		KeyboardSender.Send(hotkey);
	}

	private void ActivateSolidWorks()
	{
		NativeCommandLaunchTrace.Mark("focus-window-start", "host-focus");
		IntPtr root = GetSolidWorksWindowHandle();
		NativeCommandLaunchTrace.Mark("focus-window-ready", "host-focus");
		if (root == IntPtr.Zero)
		{
			throw new InvalidOperationException("找不到 SOLIDWORKS 主窗口。");
		}
		// A wheel gesture normally ends inside the already foreground host.
		// Preserve its sketch/view focus rather than focusing the frame again.
		IntPtr foregroundBefore = NativeMethods.GetForegroundWindow();
		if (!NativeMethods.IsIconic(root) && (foregroundBefore == root || NativeMethods.IsChild(root, foregroundBefore))) return;
		NativeCommandLaunchTrace.Mark("focus-restore-start", "host-focus");
		uint targetThread = NativeMethods.GetWindowThreadProcessId(root, out _);
		uint currentThread = NativeMethods.GetCurrentThreadId();
		bool attached = targetThread != 0 && targetThread != currentThread && NativeMethods.AttachThreadInput(currentThread, targetThread, true);
		try
		{
			if (NativeMethods.IsIconic(root))
			{
				NativeMethods.ShowWindow(root, 9);
			}
			NativeMethods.BringWindowToTop(root);
			NativeMethods.SetForegroundWindow(root);
			NativeMethods.SetFocus(root);
		}
		finally
		{
			if (attached)
			{
				NativeMethods.AttachThreadInput(currentThread, targetThread, false);
			}
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		NativeCommandLaunchTrace.Mark("focus-restore-return", "host-focus");
		do
		{
			IntPtr foreground = NativeMethods.GetForegroundWindow();
			if (foreground == root || NativeMethods.IsChild(root, foreground))
			{
				return;
			}
			Thread.Sleep(10);
		}
		while (stopwatch.Elapsed < TimeSpan.FromMilliseconds(500));
		throw new InvalidOperationException("SOLIDWORKS 主窗口未能获得键盘焦点，组合已停止以避免步骤乱序。");
	}

	private IntPtr GetSolidWorksWindowHandle()
	{
		if (!_connected || _swApp == null) return IntPtr.Zero;
		IntPtr cached = Volatile.Read(ref _hostWindowCache);
		if (IsCurrentHostWindow(cached)) return cached;
		IntPtr current = ReadSolidWorksWindowHandle();
		if (!IsCurrentHostWindow(current)) return IntPtr.Zero;
		Volatile.Write(ref _hostWindowCache, current);
		return current;
	}

	private static bool IsCurrentHostWindow(IntPtr window)
		=> window != IntPtr.Zero && NativeMethods.IsWindow(window)
			&& NativeMethods.GetWindowThreadProcessId(window, out uint process) != 0 && process == HostProcessId;

	private IntPtr ReadSolidWorksWindowHandle()
	{
		try
		{
			if (!_connected || _swApp == null)
			{
				return IntPtr.Zero;
			}
			Frame val = ((ISldWorks)_swApp).IFrameObject();
			if (val != null)
			{
				long hWndx = ((IFrame)val).GetHWndx64();
				if (hWndx != 0L)
				{
					return new IntPtr(hWndx);
				}
				int hWnd = ((IFrame)val).GetHWnd();
				if (hWnd != 0)
				{
					return new IntPtr(hWnd);
				}
			}
			object obj = ((ISldWorks)_swApp).Frame();
			object obj2 = obj?.GetType().InvokeMember("GetHWndx64", BindingFlags.InvokeMethod, null, obj, Array.Empty<object>());
			if (obj2 == null)
			{
				goto IL_00a8;
			}
			if (obj2 is long)
			{
				if ((long)obj2 == 0L)
				{
					goto IL_00a8;
				}
			}
			else if (obj2 is int && (int)obj2 == 0)
			{
				goto IL_00a8;
			}
			bool flag = false;
			goto IL_00b0;
			IL_00a8:
			flag = true;
			goto IL_00b0;
			IL_00b0:
			if (flag)
			{
				obj2 = obj?.GetType().InvokeMember("GetHWnd", BindingFlags.InvokeMethod, null, obj, Array.Empty<object>());
			}
			return (obj2 is int value) ? new IntPtr(value) : ((!(obj2 is long value2)) ? IntPtr.Zero : new IntPtr(value2));
		}
		catch (InvalidComObjectException)
		{
			// SOLIDWORKS can detach the RCW before the watermark thread receives its close message.
			// The host window is already gone, so this is a normal shutdown state rather than a plugin error.
			return IntPtr.Zero;
		}
		catch (COMException) when (!_connected)
		{
			return IntPtr.Zero;
		}
		catch (Exception ex)
		{
			Logger.Error("GetSolidWorksWindowHandle failed", ex);
			return IntPtr.Zero;
		}
	}

	[ComRegisterFunction]
	public static void RegisterFunction(Type type)
	{
		try
		{
			RegisterAddin(Registry.LocalMachine);
			RegisterAddin(Registry.CurrentUser);
		}
		catch
		{
		}
	}

	[ComUnregisterFunction]
	public static void UnregisterFunction(Type type)
	{
		try
		{
			UnregisterAddin(Registry.LocalMachine);
			UnregisterAddin(Registry.CurrentUser);
		}
		catch
		{
		}
	}

	private static void RegisterAddin(RegistryKey root)
	{
		using RegistryKey registryKey = root.CreateSubKey("SOFTWARE\\SolidWorks\\Addins\\{8D97B869-0F3D-4F13-901A-0A63D9D13A12}");
		registryKey?.SetValue(null, 1, RegistryValueKind.DWord);
		registryKey?.SetValue("Title", DisplayName);
		registryKey?.SetValue("Description", "轮盘、水印、工序与一键爆炸");
		using RegistryKey registryKey2 = root.CreateSubKey("SOFTWARE\\SolidWorks\\AddInsStartup");
		registryKey2?.SetValue("{8D97B869-0F3D-4F13-901A-0A63D9D13A12}", 1, RegistryValueKind.DWord);
	}

	private static void UnregisterAddin(RegistryKey root)
	{
		root.DeleteSubKeyTree("SOFTWARE\\SolidWorks\\Addins\\{8D97B869-0F3D-4F13-901A-0A63D9D13A12}", throwOnMissingSubKey: false);
		using RegistryKey registryKey = root.OpenSubKey("SOFTWARE\\SolidWorks\\AddInsStartup", writable: true);
		registryKey?.DeleteValue("{8D97B869-0F3D-4F13-901A-0A63D9D13A12}", throwOnMissingValue: false);
	}
}
