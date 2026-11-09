using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

internal sealed class SwCommandDispatcher : NativeWindow, IDisposable
{
    private const int WmInvoke = 2465;
    private readonly object _sync = new object();
    private readonly Queue<Work> _normal = new Queue<Work>();
    private readonly Queue<Work> _priority = new Queue<Work>();
    private readonly int _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
    private volatile bool _disposed;
    private int _priorityExecuting;
    private readonly Func<IntPtr, int, IntPtr, IntPtr, bool> _post;

    private sealed class Work
    {
        internal Action Run;
        internal Action<Exception> Cancel;
        internal bool Cancelled;
        internal void Abort(Exception error) { if (!Cancelled) { Cancelled = true; Cancel?.Invoke(error); } }
    }

    // Invoke is a synchronous boundary. Completing a Task with asynchronous
    // continuations can defer even its net48 blocking-wait notification until
    // the thread pool progresses. Owner shutdown must wake a waiting caller
    // directly, including when every existing pool thread is occupied.
    private sealed class InvokeCompletion
    {
        private readonly object _sync = new object();
        private bool _done;
        private ExceptionDispatchInfo _failure;

        internal void Complete(Exception error = null)
        {
            lock (_sync)
            {
                if (_done) return;
                if (error != null) _failure = ExceptionDispatchInfo.Capture(error);
                _done = true;
                Monitor.PulseAll(_sync);
            }
        }

        internal void Wait()
        {
            ExceptionDispatchInfo failure;
            lock (_sync)
            {
                while (!_done) Monitor.Wait(_sync);
                failure = _failure;
            }
            failure?.Throw();
        }
    }

    internal bool HasPriorityWork
    {
        get { lock (_sync) return _priority.Count > 0 || _priorityExecuting > 0; }
    }

    public SwCommandDispatcher() : this(NativeMethods.PostMessage) { }
    internal SwCommandDispatcher(Func<IntPtr, int, IntPtr, IntPtr, bool> post)
    {
        _post = post ?? throw new ArgumentNullException(nameof(post));
        CreateHandle(new CreateParams { Caption = "XYCleanSwDispatcher" });
    }

    public void Invoke(Action action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        if (_disposed) throw new ObjectDisposedException(nameof(SwCommandDispatcher));
        if (Thread.CurrentThread.ManagedThreadId == _ownerThreadId) { action(); return; }
        var completed = new InvokeCompletion();
        Enqueue(new Work
        {
            Run = () =>
            {
                try { action(); completed.Complete(); }
                catch (Exception error) { completed.Complete(error); }
            },
            Cancel = error => completed.Complete(error)
        }, priority: true);
        completed.Wait();
    }

    public void Post(Action action) => Post(action, false);
    internal void PostPriority(Action action) => Post(action, true);

    private void Post(Action action, bool priority)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        Enqueue(new Work { Run = action }, priority);
    }

    private void Enqueue(Work work, bool priority)
    {
        lock (_sync)
        {
            if (_disposed || Handle == IntPtr.Zero) throw new ObjectDisposedException(nameof(SwCommandDispatcher));
            var queue = priority ? _priority : _normal;
            queue.Enqueue(work);
            if (!_post(Handle, WmInvoke, IntPtr.Zero, IntPtr.Zero))
            {
                var error = new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "无法排队 SOLIDWORKS 主线程操作，未在其他线程执行。");
                work.Abort(error);
                // This work has no wakeup. Retaining it could consume an older
                // action's message and leave that action waiting indefinitely.
                for (int index = queue.Count; index > 0; index--)
                {
                    var queued = queue.Dequeue();
                    if (!ReferenceEquals(queued, work)) queue.Enqueue(queued);
                }
                throw error;
            }
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmInvoke) Drain();
        else base.WndProc(ref message);
    }

    private void Drain()
    {
        if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId) throw new InvalidOperationException("SOLIDWORKS UI dispatch ran on the wrong thread.");
        var budget = Stopwatch.StartNew();
        for (int count = 0; count < 16 && !_disposed; count++)
        {
            Work work; bool foreground;
            lock (_sync)
            {
                foreground = _priority.Count > 0;
                var queue = foreground ? _priority : _normal;
                if (queue.Count == 0) return;
                work = queue.Dequeue();
                if (!work.Cancelled && foreground) _priorityExecuting++;
            }
            if (!work.Cancelled)
            {
                try { work.Run(); }
                catch (Exception error) { Logger.Error("Dispatched command failed", error); }
                finally { if (foreground) lock (_sync) _priorityExecuting--; }
            }
            // Yield between foreground actions and bounded background batches.
            if (foreground || budget.ElapsedMilliseconds >= 8) return;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId) throw new InvalidOperationException("SOLIDWORKS 调度器须在所属界面线程释放。");
        lock (_sync)
        {
            _disposed = true;
            var error = new ObjectDisposedException(nameof(SwCommandDispatcher));
            foreach (var work in _priority) work.Abort(error);
            foreach (var work in _normal) work.Abort(error);
            _priority.Clear(); _normal.Clear();
        }
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}
