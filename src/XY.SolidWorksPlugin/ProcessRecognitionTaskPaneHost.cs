using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace XY.SolidWorksPlugin;

// Owns only this product's native tab and its empty hosting surface. Content is
// borrowed from the add-in, so switching/disposing the tab never destroys drafts.
internal sealed class ProcessRecognitionTaskPaneHost : IDisposable
{
    internal interface IView : IDisposable
    {
        bool Display(IntPtr handle);
        bool Show();
        bool Hide();
        bool Delete();
    }

    private readonly int _threadId = Thread.CurrentThread.ManagedThreadId;
    private readonly Func<IView> _createView;
    private UserControl _surface;
    private IView _view;
    private BorrowedContent _content;
    private bool _disposed;
    internal Control Content => _content?.Control;
    internal bool IsCreated => _view != null;

    public ProcessRecognitionTaskPaneHost(ISldWorks host)
    {
        if (host == null) throw new ArgumentNullException(nameof(host));
        VerifyThread();
        var frame = host.Frame() as IFrame;
        IntPtr window = frame == null ? IntPtr.Zero : new IntPtr(frame.GetHWndx64());
        if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out _) != GetCurrentThreadId())
            throw new InvalidOperationException("工序侧边栏必须在 SOLIDWORKS 主界面线程创建。");
        _createView = () => new NativeView(host);
    }

    internal ProcessRecognitionTaskPaneHost(Func<IView> createView)
    {
        VerifyThread();
        _createView = createView ?? throw new ArgumentNullException(nameof(createView));
    }

    public void Attach(Control content) => Attach(content, true);

    public void Attach(Control content, bool show)
    {
        VerifyUsable();
        if (content == null) throw new ArgumentNullException(nameof(content));
        if (content.IsDisposed) throw new ObjectDisposedException(content.Name);
        if (content.IsHandleCreated && content.InvokeRequired)
            throw new InvalidOperationException("工序内容必须与侧边栏位于同一界面线程。");
        if (ReferenceEquals(Content, content)) { if (show) Show(); return; }
        bool hadView = _view != null;
        BorrowedContent incoming = null;
        try
        {
            EnsureView();
            incoming = new BorrowedContent(content);
            incoming.Embed(_surface);
            if (show && !_view.Show()) throw new InvalidOperationException("SOLIDWORKS 未能显示工序识别侧边栏。");
            var previous = _content;
            if (previous != null)
            {
                previous.Control.Disposed -= ContentDisposed;
                previous.Restore();
            }
            _content = incoming;
            _content.Control.Disposed += ContentDisposed;
        }
        catch (Exception failure)
        {
            try
            {
                incoming?.Restore();
                if (_content != null && !_content.Control.IsDisposed)
                {
                    if (_content.Control.Parent != _surface) _content.Embed(_surface);
                    _content.Control.Disposed -= ContentDisposed;
                    _content.Control.Disposed += ContentDisposed;
                    _content.Control.BringToFront();
                }
                if (!hadView) ReleaseView();
            }
            catch (Exception cleanup) { throw new AggregateException("工序侧边栏创建失败，清理也未完成；请保留宿主对象重试释放。", failure, cleanup); }
            throw;
        }
    }

    public void Show()
    {
        VerifyUsable();
        if (_view == null || Content == null) return;
        Content.Show();
        if (!_view.Show()) throw new InvalidOperationException("SOLIDWORKS 未能显示工序识别侧边栏。");
    }

    public void Hide()
    {
        VerifyUsable();
        if (_view != null && !_view.Hide()) throw new InvalidOperationException("SOLIDWORKS 未能隐藏工序识别侧边栏。");
    }

    public Control Detach()
    {
        VerifyUsable();
        if (_content == null) return null;
        Hide();
        var previous = _content;
        previous.Control.Disposed -= ContentDisposed;
        previous.Restore();
        _content = null;
        return previous.Control;
    }

    private void EnsureView()
    {
        if (_view != null) return;
        _surface = new UserControl { BackColor = PluginToolWindow.Canvas, Margin = Padding.Empty };
        _view = _createView() ?? throw new InvalidOperationException("SOLIDWORKS 未能创建工序识别选项卡。");
        if (!_view.Display(_surface.Handle)) throw new InvalidOperationException("SOLIDWORKS 未能嵌入工序识别界面。");
    }

    private void ContentDisposed(object sender, EventArgs args)
    {
        if (_disposed || !ReferenceEquals(sender, Content)) return;
        _content.Control.Disposed -= ContentDisposed;
        _content = null;
        // Disposal can originate from a model closing. Do not let an API error
        // escape another control's Dispose; retain the view for explicit cleanup.
        try { Hide(); } catch (Exception ex) { Logger.Error("Hide empty process Task Pane failed", ex); }
    }

    private void ReleaseView()
    {
        // If SW rejects DeleteView, keep the view/handle alive for a safe retry.
        if (_view != null)
        {
            if (!_view.Delete()) throw new InvalidOperationException("SOLIDWORKS 未能移除本产品工序识别选项卡。");
            var deleted = _view;
            _view = null;
            try { deleted.Dispose(); }
            finally { _surface?.Dispose(); _surface = null; }
        }
        _surface?.Dispose();
        _surface = null;
    }

    public void Dispose()
    {
        VerifyThread();
        if (_disposed) return;
        // Detach borrowed forms before disposing the managed surface, but leave
        // their own lifetime with SwAddin. Restore never reopens a floating form.
        if (_content != null)
        {
            _content.Control.Disposed -= ContentDisposed;
            _content.Restore();
            _content = null;
        }
        ReleaseView();
        _disposed = true;
    }

    private void VerifyUsable()
    {
        VerifyThread();
        if (_disposed) throw new ObjectDisposedException(nameof(ProcessRecognitionTaskPaneHost));
    }

    private void VerifyThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != _threadId || Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("工序侧边栏只能由创建它的 SOLIDWORKS STA 线程访问。");
    }

    private sealed class BorrowedContent
    {
        internal readonly Control Control;
        private readonly Control _parent;
        private readonly DockStyle _dock;
        private readonly Rectangle _bounds;
        private readonly bool _topLevel;
        private readonly FormBorderStyle _border;
        private readonly Form _owner;
        private readonly Size _minimumSize;
        private readonly Size _maximumSize;

        internal BorrowedContent(Control control)
        {
            Control = control; _parent = control.Parent; _dock = control.Dock; _bounds = control.Bounds;
            _minimumSize = control.MinimumSize; _maximumSize = control.MaximumSize;
            if (control is Form form) { _topLevel = form.TopLevel; _border = form.FormBorderStyle; _owner = form.Owner; }
        }

        internal void Embed(Control surface)
        {
            Control.Hide();
            Control.MinimumSize = Size.Empty; Control.MaximumSize = Size.Empty;
            if (Control is Form form) { form.Owner = null; form.TopLevel = false; form.FormBorderStyle = FormBorderStyle.None; }
            surface.Controls.Add(Control); Control.Dock = DockStyle.Fill;
            Control.Show(); Control.BringToFront();
        }

        internal void Restore()
        {
            if (Control.IsDisposed) return;
            Control.Hide(); Control.Parent = null;
            if (Control is Form form)
            {
                form.FormBorderStyle = _border; form.TopLevel = _topLevel;
                if (_topLevel && _owner != null && !_owner.IsDisposed) form.Owner = _owner;
            }
            if (_parent != null && !_parent.IsDisposed) Control.Parent = _parent;
            Control.Dock = _dock; Control.MinimumSize = _minimumSize; Control.MaximumSize = _maximumSize; Control.Bounds = _bounds;
        }
    }

    private sealed class NativeView : IView
    {
        private ITaskpaneView _native;
        private NativePaneSizing _sizing;
        private Control _control;
        private readonly string _icons;
        internal NativeView(ISldWorks host)
        {
            _icons = Path.Combine(Path.GetTempPath(), "XY.SolidWorksPlugin", "process-taskpane-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_icons);
            try
            {
                int[] sizes = { 20, 32, 40, 64, 96, 128 };
                string[] paths = new string[sizes.Length];
                for (int index = 0; index < sizes.Length; index++)
                {
                    paths[index] = Path.Combine(_icons, "process-" + sizes[index] + ".png");
                    using (var icon = WheelIconCatalog.Render("toolbox", sizes[index])) icon.Save(paths[index], ImageFormat.Png);
                }
                _native = host.CreateTaskpaneView3(paths, "工序识别") as ITaskpaneView;
                if (_native == null) throw new InvalidOperationException("SOLIDWORKS 未返回工序识别选项卡。");
            }
            catch { DeleteIcons(); throw; }
        }
        public bool Display(IntPtr handle)
        {
            _control = Control.FromHandle(handle);
            bool displayed = _native.DisplayWindowFromHandlex64(handle.ToInt64());
            if (displayed) SynchronizeSize();
            return displayed;
        }
        public bool Show()
        {
            bool shown = _native.ShowView();
            if (shown) SynchronizeSize();
            return shown;
        }
        private void SynchronizeSize()
        {
            if (_control == null || _control.IsDisposed) return;
            IntPtr parent = NativePaneSizing.ParentOf(_control);
            if (parent == IntPtr.Zero) return;
            if (_sizing == null || _sizing.Handle != parent)
            {
                _sizing?.Dispose();
                _sizing = new NativePaneSizing(_control, parent);
            }
            _sizing.Fit();
        }
        public bool Hide() => _native.HideView();
        public bool Delete() => _native.DeleteView();
        public void Dispose()
        {
            _sizing?.Dispose(); _sizing = null;
            var native = _native; _native = null;
            try { if (native != null && Marshal.IsComObject(native)) Marshal.ReleaseComObject(native); }
            finally { DeleteIcons(); }
        }
        private void DeleteIcons()
        {
            try { if (Directory.Exists(_icons)) Directory.Delete(_icons, true); }
            catch (Exception ex) { Logger.Error("Remove owned process Task Pane icons failed", ex); }
        }
    }

    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
