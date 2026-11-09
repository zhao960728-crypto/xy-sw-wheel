using System;
using System.Threading;

namespace XY.SolidWorksPlugin;

internal sealed class ComboAdvanceController : IDisposable
{
	private readonly CancellationToken _outerToken;
	private readonly CancellationTokenSource _linkedCancellation;
	private readonly CancellationTokenSource _monitorStop = new CancellationTokenSource();
	private readonly int _virtualKey;
	private readonly uint _solidWorksProcessId;
	private Thread? _monitorThread;
	private int _armed;
	private int _advanceRequested;

	public ComboAdvanceController(CancellationToken outerToken, bool enabled, string key, IntPtr solidWorksWindow)
	{
		_outerToken = outerToken;
		_linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
		ComboAdvanceKeys.TryGetVirtualKey(key, out _virtualKey);
		if (solidWorksWindow != IntPtr.Zero)
		{
			NativeMethods.GetWindowThreadProcessId(solidWorksWindow, out _solidWorksProcessId);
		}
		Enabled = enabled && _solidWorksProcessId != 0;
		Key = ComboAdvanceKeys.Normalize(key);
	}

	public bool Enabled { get; }

	public string Key { get; }

	public bool AdvanceRequested => Volatile.Read(ref _advanceRequested) != 0;

	public CancellationToken Token => _linkedCancellation.Token;

	public void Arm()
	{
		if (!Enabled || Interlocked.Exchange(ref _armed, 1) != 0)
		{
			return;
		}
		_monitorThread = new Thread(Monitor)
		{
			IsBackground = true,
			Name = "XYComboAdvanceKey"
		};
		_monitorThread.Start();
	}

	public void WaitForAdvance()
	{
		if (!Enabled)
		{
			return;
		}
		Arm();
		Token.WaitHandle.WaitOne();
		Token.ThrowIfCancellationRequested();
	}

	// Returns false when the timeout elapses first. A step with a timeout moves
	// on by itself instead of waiting indefinitely for a key press.
	public bool WaitForAdvance(int timeoutMs)
	{
		if (!Enabled)
		{
			return false;
		}
		Arm();
		if (!Token.WaitHandle.WaitOne(Math.Max(0, timeoutMs)))
		{
			return false;
		}
		Token.ThrowIfCancellationRequested();
		return true;
	}

	private void Monitor()
	{
		ComboAdvanceGate gate = new ComboAdvanceGate();
		gate.Arm(IsKeyDown());
		while (!_outerToken.IsCancellationRequested && !_monitorStop.IsCancellationRequested)
		{
			bool keyDown = IsKeyDown();
			if (gate.Observe(keyDown, IsSolidWorksForeground()))
			{
				Interlocked.Exchange(ref _advanceRequested, 1);
				_linkedCancellation.Cancel();
				return;
			}
			if (_monitorStop.Token.WaitHandle.WaitOne(10))
			{
				return;
			}
		}
	}

	private bool IsKeyDown()
	{
		return (NativeMethods.GetAsyncKeyState(_virtualKey) & 0x8000) != 0;
	}

	private bool IsSolidWorksForeground()
	{
		IntPtr foreground = NativeMethods.GetForegroundWindow();
		if (foreground == IntPtr.Zero)
		{
			return false;
		}
		NativeMethods.GetWindowThreadProcessId(foreground, out uint processId);
		return processId == _solidWorksProcessId;
	}

	public void Dispose()
	{
		_monitorStop.Cancel();
		if (_monitorThread != null && _monitorThread.IsAlive && !ReferenceEquals(Thread.CurrentThread, _monitorThread))
		{
			_monitorThread.Join(500);
		}
		_monitorStop.Dispose();
		_linkedCancellation.Dispose();
	}
}
