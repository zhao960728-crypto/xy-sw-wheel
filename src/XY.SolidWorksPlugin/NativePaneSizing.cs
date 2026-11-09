using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

// SW reparents the HWND without assigning Control.Parent. WinForms Dock cannot
// follow that native parent, so observe only our pane's native sizing messages.
internal sealed class NativePaneSizing : NativeWindow, IDisposable
{
    private readonly Control _content;
    private bool _fitting;
    internal NativePaneSizing(Control content, IntPtr parent)
    {
        _content = content;
        AssignHandle(parent);
        Fit();
    }
    internal static IntPtr ParentOf(Control control)
    {
        IntPtr parent = GetParent(control.Handle);
        if (parent == IntPtr.Zero) return IntPtr.Zero;
        var name = new StringBuilder(256);
        GetClassName(parent, name, name.Capacity);
        var title = new StringBuilder(256);
        GetWindowText(parent, title, title.Capacity);
        return (name.ToString().StartsWith("WindowsFormsParkingWindow", StringComparison.Ordinal)
            || title.ToString().StartsWith("WindowsFormsParkingWindow", StringComparison.Ordinal))
            ? IntPtr.Zero : parent;
    }
    internal void Fit()
    {
        if (_fitting || Handle == IntPtr.Zero || _content.IsDisposed || GetParent(_content.Handle) != Handle) return;
        if (!GetClientRect(Handle, out var rect) || rect.Right <= 0 || rect.Bottom <= 0) return;
        var bounds = new Rectangle(0, 0, rect.Right, rect.Bottom);
        if (_content.Bounds == bounds) return;
        _fitting = true;
        try { _content.Bounds = bounds; }
        finally { _fitting = false; }
    }
    protected override void WndProc(ref Message message)
    {
        int code = message.Msg;
        base.WndProc(ref message);
        if (code == 0x0005 || code == 0x0047 || code == 0x0018) Fit(); // size, window position, show
    }
    public void Dispose() { if (Handle != IntPtr.Zero) ReleaseHandle(); }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect bounds);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
}
