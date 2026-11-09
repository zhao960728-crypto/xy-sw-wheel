using System;

namespace XY.SolidWorksPlugin;

// Used only from the owning SOLIDWORKS UI thread. User input is deliberately
// absent: the message pump gives input priority without a starvation timeout.
internal sealed class RecognitionSliceGate
{
	private readonly int _intervalMilliseconds;
	private DateTime _lastStarted = DateTime.MinValue;
	private bool _entered;

	internal RecognitionSliceGate(int intervalMilliseconds)
	{
		_intervalMilliseconds = Math.Max(1, intervalMilliseconds);
	}

	internal bool TryEnter(DateTime now)
	{
		if (_entered) return false;
		if (now >= _lastStarted && (now - _lastStarted).TotalMilliseconds < _intervalMilliseconds) return false;
		_entered = true;
		_lastStarted = now;
		return true;
	}

	internal void Exit() => _entered = false;
}
