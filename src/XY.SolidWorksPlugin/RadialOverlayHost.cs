using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

internal sealed class RadialOverlayHost : IDisposable
{
	private readonly Action<MenuItemConfig> _selected;
	private readonly IntPtr _hostWindow;
	private volatile bool _displayVisible;

	private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(initialState: false);

	private readonly object _moveSync = new object();
	private readonly object _postSync = new object();
	private readonly Queue<Action<RadialMenuWindow>> _pending = new Queue<Action<RadialMenuWindow>>();
	private readonly OverlayRequestLifecycle _requests = new OverlayRequestLifecycle();

	private Thread? _thread;

	private ApplicationContext? _context;

	private RadialMenuWindow? _window;

	private Point _latestCursor;

	private int _moveVersion;

	private bool _movePosted;

	private volatile bool _disposed;

	private int _warmupVersion;

	public RadialOverlayHost(Action<MenuItemConfig> selected) : this(selected, IntPtr.Zero) { }

	internal RadialOverlayHost(Action<MenuItemConfig> selected, IntPtr hostWindow)
	{
		_selected = selected;
		_hostWindow = hostWindow;
	}

	public void Start()
	{
		lock (_postSync)
		{
			if (_disposed || _thread != null) return;
			_thread = new Thread(Run)
			{
				IsBackground = true,
				Name = "XYCleanRadialOverlay"
			};
			_thread.SetApartmentState(ApartmentState.STA);
			_thread.Start();
		}
	}

	public void Show(PluginConfig config, string sceneKey, Point center, bool dragMode, MouseButtons triggerButton)
	{
		if (!SceneResolver.IsEnabled(config, sceneKey))
		{
			Cancel();
			if (_thread != null) Post(window => window.Hide());
			return;
		}
		Start();
		long timestamp = Stopwatch.GetTimestamp();
		// Publish once, just like the reference input pump. The host mouse
		// callback must return promptly so a release is delivered without waiting
		// for layout, text fitting, or the overlay's first native window creation.
		lock (_postSync)
		{
			var request = _requests.Begin(dragMode);
			if (request == null) return;
			Post(window =>
			{
				if (!_requests.CanShow(request))
				{
					if (_requests.IsCurrent(request)) { _requests.ClearDisplayed(); window.Hide(); }
					return;
				}
				_requests.ClearDisplayed();
				window.ShowMenu(config, sceneKey, center, dragMode, triggerButton);
				// Release can arrive on the host thread during a cold scene render.
				// Such a display was never available for an intentional selection.
				if (!window.Visible || !_requests.MarkShown(request)) window.Hide();
				Logger.Trace("Overlay show pipeline ms=" + ElapsedMs(timestamp));
			});
		}
	}

	public void Warmup(PluginConfig config, string preferredSceneKey)
	{
		if (!SceneResolver.IsEnabled(config, preferredSceneKey)) return;
		Start();
		int version = Interlocked.Increment(ref _warmupVersion);
		ThreadPool.QueueUserWorkItem(delegate
		{
			if (!_ready.Wait(3000) || _disposed || version != Volatile.Read(ref _warmupVersion))
			{
				return;
			}
			Post(delegate(RadialMenuWindow window)
			{
				if (version == Volatile.Read(ref _warmupVersion) && SceneResolver.IsEnabled(config, preferredSceneKey))
				{
					window.Warmup(config, preferredSceneKey);
				}
			});
		});
	}

	public void Update(Point cursor)
	{
		bool flag = false;
		lock (_moveSync)
		{
			_latestCursor = cursor;
			_moveVersion++;
			if (!_movePosted)
			{
				_movePosted = true;
				flag = true;
			}
		}
		if (flag)
		{
			Post(ProcessLatestCursor);
		}
	}

	public void Commit(Point cursor)
	{
		// Queue directly from the host mouse hook so release precedes the next show.
		// Do not block that hook while the overlay renders.
		lock (_postSync)
		{
			var request = _requests.ReleaseCurrentDrag();
			if (request == null) return;
			Post(window =>
			{
				if (!_requests.CanCommit(request)) return;
				try { window.CommitCursor(cursor); }
				finally { _requests.ClearDisplayed(); }
			});
		}
	}

	public void ReloadConfig()
	{
		Post(delegate(RadialMenuWindow window)
		{
			window.ReloadConfig();
		});
	}

	internal bool Cancel()
	{
		lock (_postSync)
		{
			if (!_displayVisible && !_requests.HasPending) return false;
			if (!_requests.CancelCurrent()) return false;
			Post(window => window.Hide());
			return true;
		}
	}

	public void Dispose()
	{
		lock (_postSync)
		{
			if (_disposed) return;
			_disposed = true;
			_pending.Clear();
			_requests.Dispose();
		}
		Interlocked.Increment(ref _warmupVersion);
		try
		{
			_ready.Wait(1000);
			RequestShutdown();
		}
		catch
		{
			_context?.ExitThread();
		}
		Thread thread = _thread;
		if (thread != null && thread != Thread.CurrentThread && thread.IsAlive)
		{
			thread.Join(1000);
		}
	}

	private void Run()
	{
		try
		{
			_context = new ApplicationContext();
			_window = new RadialMenuWindow(item =>
			{
				if (_requests.CanSelect)
					try { _selected(item); } finally { _requests.CancelCurrent(); }
			});
			_window.CreateControl();
			_ = _window.Handle;
			_window.SetHostWindow(_hostWindow);
			_window.VisibleChanged += (_, __) => _displayVisible = _window.Visible;
			lock (_postSync)
			{
				while (_pending.Count > 0) BeginPost(_window, _pending.Dequeue());
				_ready.Set();
			}
			if (_disposed)
			{
				_window.Close();
				return;
			}
			Application.Run(_context);
		}
		catch (Exception ex)
		{
			Logger.Error("Overlay thread failed", ex);
			_ready.Set();
		}
		finally
		{
			lock (_postSync) { _disposed = true; _pending.Clear(); _requests.Dispose(); }
			_window?.Dispose();
			_context?.Dispose();
			_ready.Set();
		}
	}

	private void RequestShutdown()
	{
		RadialMenuWindow window = _window;
		if (window == null || window.IsDisposed || !window.IsHandleCreated) return;
		Action close = () => { window.Close(); _context?.ExitThread(); };
		if (!window.InvokeRequired) { close(); return; }
		var done = new TaskCompletionSource<bool>();
		window.BeginInvoke((Action)(() =>
		{
			try { close(); }
			finally { done.TrySetResult(true); }
		}));
		// A timeout bounds the caller's wait, but must never cancel shutdown.
		// The callback still closes the window as soon as a busy render returns.
		if (!done.Task.Wait(1000)) Logger.Trace("Overlay shutdown queued behind active render.");
	}

	private void Post(Action<RadialMenuWindow> action)
	{
		lock (_postSync)
		{
			if (_disposed) return;
			RadialMenuWindow window = _window;
			if (!_ready.IsSet || window == null || !window.IsHandleCreated)
			{
				_pending.Enqueue(action);
				return;
			}
			if (!window.IsDisposed) BeginPost(window, action);
		}
	}

	private void BeginPost(RadialMenuWindow window, Action<RadialMenuWindow> action)
	{
		try
		{
			window.BeginInvoke((Action)delegate
			{
				if (_disposed || window.IsDisposed) return;
				try { action(window); }
				catch (Exception ex) { Logger.Error("Overlay posted action failed", ex); }
			});
		}
		catch (InvalidOperationException)
		{
		}
	}

	private void ProcessLatestCursor(RadialMenuWindow window)
	{
		Point latestCursor;
		int moveVersion;
		lock (_moveSync)
		{
			latestCursor = _latestCursor;
			moveVersion = _moveVersion;
		}
		window.UpdateCursor(latestCursor);
		bool flag = false;
		lock (_moveSync)
		{
			if (_moveVersion == moveVersion)
			{
				_movePosted = false;
			}
			else
			{
				flag = true;
			}
		}
		if (flag)
		{
			Post(ProcessLatestCursor);
		}
	}

	private static double ElapsedMs(long started)
	{
		return (double)(Stopwatch.GetTimestamp() - started) * 1000.0 / (double)Stopwatch.Frequency;
	}

	private void Send(Action<RadialMenuWindow> action, int timeoutMs)
	{
		RadialMenuWindow window = _window;
		if (window == null || window.IsDisposed || !window.IsHandleCreated)
		{
			return;
		}
		if (!window.InvokeRequired)
		{
			action(window);
			return;
		}
		var done = new TaskCompletionSource<bool>();
		int state = 0; // pending / running / cancelled before execution
		try
		{
			window.BeginInvoke((Action)delegate
			{
				if (Interlocked.CompareExchange(ref state, 1, 0) != 0) return;
				try
				{
					action(window);
				}
				catch (Exception ex2)
				{
					Logger.Error("Overlay invoke failed", ex2);
				}
				finally
				{
					done.TrySetResult(true);
				}
			});
			if (!done.Task.Wait(timeoutMs))
			{
				Interlocked.CompareExchange(ref state, 2, 0);
				Logger.Error("Overlay invoke timed out.");
			}
		}
		catch (InvalidOperationException)
		{
		}
	}
}
