using System;

namespace XY.SolidWorksPlugin;

// Coalesces the reference plugin's bursty model/document notifications. A
// completion can acknowledge only its own generation, never a later request.
internal sealed class WatermarkRefreshQueue
{
    private readonly object _gate = new object();
    private long _generation;
    private long _completed;
    private long _due;

    internal void Request(long now, int delay = 0)
    {
        lock (_gate) { _generation++; _due = now + Math.Max(0, delay); }
    }

    internal bool TryBegin(long now, out long token)
    {
        lock (_gate) { token = _generation; return _completed != _generation && now >= _due; }
    }

    internal void Complete(long token)
    {
        lock (_gate) { if (token == _generation) _completed = token; }
    }

    internal void Cancel()
    {
        lock (_gate) { _generation++; _completed = _generation; }
    }

    internal bool Pending { get { lock (_gate) return _completed != _generation; } }
}
