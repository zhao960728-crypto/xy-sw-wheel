using System;

namespace XY.SolidWorksPlugin;

// The physical button can already belong to the next hold by the time a cold
// overlay processes an old Show. Pair each release with its own display.
internal sealed class OverlayRequestLifecycle
{
    internal sealed class Request
    {
        internal readonly bool DragMode;
        internal bool Released;
        internal Request(bool dragMode) => DragMode = dragMode;
    }

    private readonly object _sync = new object();
    private Request? _current;
    private Request? _displayed;
    private bool _disposed;

    internal Request? Begin(bool dragMode)
    {
        lock (_sync)
        {
            if (_disposed) return null;
            return _current = new Request(dragMode);
        }
    }

    internal Request? ReleaseCurrentDrag()
    {
        lock (_sync)
        {
            if (_disposed || _current == null || !_current.DragMode || _current.Released) return null;
            _current.Released = true;
            return _current;
        }
    }

    internal bool CanShow(Request request)
    {
        lock (_sync) return !_disposed && ReferenceEquals(_current, request) && !request.Released;
    }

    internal bool IsCurrent(Request request)
    {
        lock (_sync) return !_disposed && ReferenceEquals(_current, request);
    }

    internal bool MarkShown(Request request)
    {
        lock (_sync)
        {
            if (_disposed || !ReferenceEquals(_current, request) || request.Released) return false;
            _displayed = request;
            return true;
        }
    }

    internal bool CanCommit(Request request)
    {
        lock (_sync) return !_disposed && ReferenceEquals(_current, request) && ReferenceEquals(_displayed, request);
    }

    internal bool CanSelect
    {
        get { lock (_sync) return !_disposed && _current != null && ReferenceEquals(_current, _displayed); }
    }

    internal void ClearDisplayed()
    {
        lock (_sync) _displayed = null;
    }

    internal bool HasPending
    {
        get { lock (_sync) return !_disposed && _current != null && !_current.Released && !ReferenceEquals(_current, _displayed); }
    }

    internal bool CancelCurrent()
    {
        lock (_sync)
        {
            bool existed = !_disposed && _current != null;
            _current = _displayed = null;
            return existed;
        }
    }

    internal void Dispose()
    {
        lock (_sync) { _disposed = true; _current = null; _displayed = null; }
    }
}
