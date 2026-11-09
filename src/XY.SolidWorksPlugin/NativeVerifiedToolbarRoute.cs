using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using SolidWorks.Interop.swcommands;

namespace XY.SolidWorksPlugin;

// The reference catalog uses this route for these ten standard views only.
// Resource identities are supplied by NativeVerifiedMenuRoute's current-host
// XML index. This class never invents UI IDs, initializes a menu, or retains HWNDs.
internal static class NativeVerifiedToolbarRoute
{
    private static readonly HashSet<int> EligibleApis = BuildEligibleApis();
    private static readonly uint ThisProcess = (uint)Process.GetCurrentProcess().Id;
    private const int TbGetButton = 0x417, TbButtonCount = 0x418, TbGetButtonText = 0x44b;

    internal sealed class Window
    {
        internal IntPtr Handle, Root;
        internal uint Process, Thread;
        internal string ClassName;
    }

    internal sealed class Button
    {
        internal Window Window;
        internal int Position, UiId;
        internal byte State, Style;
        internal string Caption;
    }

    internal sealed class Snapshot
    {
        internal readonly List<Button> Buttons = new List<Button>();
        internal bool Complete = true;
        internal string Failure = "";

        internal Button Resolve(NativeVerifiedMenuRoute.Identity identity)
        {
            if (!Complete || !Eligible(identity)) return null;
            var matches = Buttons.Where(button => button.UiId == identity.UiId).Take(2).ToArray();
            if (matches.Length != 1) return null;
            var target = matches[0];
            // Match reference ToolbarEnabled: no separator/dropdown/whole dropdown,
            // and enabled without hidden/indeterminate. A checked view is legal.
            if ((target.Style & 0x89) != 0 || (target.State & 0x1c) != 4) return null;
            if (!string.IsNullOrWhiteSpace(target.Caption)
                && NativeHostMenuRoute.Caption(target.Caption) != NativeHostMenuRoute.Caption(identity.Title)) return null;
            return target;
        }
    }

    internal static bool Eligible(NativeVerifiedMenuRoute.Identity identity)
        => identity != null && EligibleApis.Contains(identity.ApiId) && identity.UiId > 0 && identity.UiId <= ushort.MaxValue
            && identity.UiId != identity.ApiId && !string.IsNullOrWhiteSpace(identity.Title)
            && NativeCommandRouteMappings.SymbolsByApi.TryGetValue(identity.ApiId, out string[] symbols)
            && symbols.Length == 1 && string.Equals(symbols[0], identity.Symbol, StringComparison.Ordinal);

    private static HashSet<int> BuildEligibleApis()
    {
        var result = new HashSet<int>();
        foreach (string name in new[] { "swCommands_Back", "swCommands_Bottom", "swCommands_Dimetric", "swCommands_Front",
            "swCommands_Isometric", "swCommands_Left", "swCommands_NormalTo", "swCommands_Right", "swCommands_Top", "swCommands_Trimetric" })
            if (Enum.TryParse(name, out swCommands_e command) && Enum.IsDefined(typeof(swCommands_e), command)) result.Add((int)command);
        return result;
    }

    internal static NativeMenuDispatchResult TryExecute(IntPtr root, NativeVerifiedMenuRoute.Identity identity,
        Func<bool> sameContext, Func<bool> apiEnabled, out string message)
    {
        message = "";
        if (!Eligible(identity)) return NativeMenuDispatchResult.NotHandled;
        uint process = ThisProcess, thread = NativeMethods.GetCurrentThreadId();
        Func<bool> context = () => IsOwnedRoot(root, process, thread) && NativeMethods.IsWindowEnabled(root) && sameContext();
        Snapshot snapshot;
        try
        {
            if (!context()) return ChangedContext(out message);
            snapshot = Capture(root, process, thread,
                visit => EnumerateWindows(root, process, thread, visit),
                CountButtons, (window, position) => ReadButton(window, position, identity.UiId));
        }
        catch (Exception error)
        {
            Logger.Trace("Verified native toolbar discovery unavailable: " + error.Message);
            return Unverified(context, identity, "discovery-failed", out message);
        }
        string command = NativeCommandReference.Encode(identity.ApiId);
        var result = Dispatch(snapshot, identity, context, apiEnabled,
            button => IsOwnedToolbar(button.Window, root, process, thread) && SameButton(button, ReadButton(button.Window, button.Position, identity.UiId)),
            uiId =>
            {
                NativeCommandLaunchTrace.Mark("native-toolbar-dispatch", command);
                try { NativeMethods.SendMessage(root, 0x0111, new IntPtr(uiId), IntPtr.Zero); }
                finally { NativeCommandLaunchTrace.Mark("native-toolbar-return", command); }
            }, out message);
        return result == NativeMenuDispatchResult.NotHandled
            ? Unverified(context, identity, snapshot.Complete ? "button-identity-unverified" : "snapshot-incomplete", out message) : result;
    }

    internal static NativeMenuDispatchResult Dispatch(Snapshot snapshot, NativeVerifiedMenuRoute.Identity identity,
        Func<bool> sameContext, Func<bool> apiEnabled, Func<Button, bool> stillVerified, Action<int> send, out string message)
    {
        message = "";
        if (!Eligible(identity)) return NativeMenuDispatchResult.NotHandled;
        bool attempted = false;
        try
        {
            if (!sameContext()) return ChangedContext(out message);
            var button = snapshot?.Resolve(identity);
            if (button == null) return NativeMenuDispatchResult.NotHandled;
            if (!apiEnabled())
            {
                message = "原生命令当前未启用，未从工具栏或其他路径重复激发。";
                return NativeMenuDispatchResult.Rejected;
            }
            if (!sameContext()) return ChangedContext(out message);
            if (!stillVerified(button)) return NativeMenuDispatchResult.NotHandled;
            if (!sameContext()) return ChangedContext(out message);
            attempted = true;
            send(identity.UiId);
            return NativeMenuDispatchResult.Submitted;
        }
        catch (Exception error)
        {
            if (attempted)
            {
                message = "原生命令工具栏调用已尝试提交，结果不确定；未重复激发：" + error.Message;
                return NativeMenuDispatchResult.Rejected;
            }
            // Read/state failures only invalidate this optional optimization.
            // The original verified native API remains available in the same doc.
            return NativeVerifiedMenuRoute.GuardUnverifiedContext(sameContext, out message);
        }
    }

    private static NativeMenuDispatchResult ChangedContext(out string message)
    {
        message = "工具栏发现或激发期间活动文档/宿主线程已变化，未改投其他上下文。";
        return NativeMenuDispatchResult.Rejected;
    }

    private static NativeMenuDispatchResult Unverified(Func<bool> sameContext, NativeVerifiedMenuRoute.Identity identity, string reason, out string message)
    {
        var result = NativeVerifiedMenuRoute.GuardUnverifiedContext(sameContext, out message);
        if (result == NativeMenuDispatchResult.NotHandled)
            NativeCommandLaunchTrace.Mark("native-toolbar-unverified:" + reason, NativeCommandReference.Encode(identity.ApiId));
        return result;
    }

    internal static Snapshot Capture(IntPtr root, uint process, uint thread,
        Func<Func<Window, bool>, bool> enumerate, Func<Window, int> count, Func<Window, int, Button> read,
        int maxWindows = 8192, int maxButtons = 4096, Func<bool> withinBudget = null)
    {
        var snapshot = new Snapshot(); var clock = Stopwatch.StartNew();
        withinBudget ??= () => clock.ElapsedMilliseconds <= 150;
        int windows = 0, remaining = maxButtons;
        Func<string, bool> fail = reason => { snapshot.Complete = false; snapshot.Failure = reason; return false; };
        try
        {
            bool complete = enumerate(window =>
            {
                if (++windows > maxWindows || !withinBudget()) return fail("window-or-time-bound");
                // Foreign process/thread controls are never read with TB_GETBUTTON.
                if (window == null) return fail("window-unreadable");
                if (!Owned(window, root, process, thread)) return true;
                int total = count(window);
                if (total < 0 || total > remaining) return fail("button-bound");
                remaining -= total;
                for (int position = 0; position < total; position++)
                {
                    if (!withinBudget()) return fail("time-bound");
                    Button button = read(window, position);
                    if (button == null || button.Window != window || button.Position != position) return fail("button-unreadable");
                    snapshot.Buttons.Add(button);
                }
                return count(window) == total || fail("toolbar-rebuilt");
            });
            if (!complete && snapshot.Complete) fail("window-enumeration-failed");
        }
        catch { fail("snapshot-read-failed"); }
        return snapshot;
    }

    private static bool Owned(Window window, IntPtr root, uint process, uint thread)
        => window.Handle != IntPtr.Zero && window.Root == root && window.Process == process && window.Thread == thread
            && string.Equals(window.ClassName, "ToolbarWindow32", StringComparison.Ordinal);

    private static bool IsOwnedRoot(IntPtr root, uint process, uint thread)
        => root != IntPtr.Zero && NativeMethods.GetWindowThreadProcessId(root, out uint owner) == thread && owner == process;

    private static bool IsOwnedToolbar(Window window, IntPtr root, uint process, uint thread)
    {
        if (window == null || !Owned(window, root, process, thread)) return false;
        return NativeMethods.GetWindowThreadProcessId(window.Handle, out uint owner) == thread && owner == process
            && GetAncestor(window.Handle, 2) == root && ClassName(window.Handle) == "ToolbarWindow32";
    }

    private static bool EnumerateWindows(IntPtr root, uint process, uint thread, Func<Window, bool> visit)
    {
        bool complete = true;
        EnumChildProc callback = (handle, _) =>
        {
            try
            {
                uint ownerThread = NativeMethods.GetWindowThreadProcessId(handle, out uint ownerProcess);
                if (ownerThread == 0 || ownerProcess == 0) { complete = false; return false; }
                bool own = ownerProcess == process && ownerThread == thread;
                string className = own ? ClassName(handle) : "";
                if (own && className.Length == 0) { complete = false; return false; }
                if (!visit(new Window { Handle = handle, Process = ownerProcess, Thread = ownerThread,
                    Root = own ? GetAncestor(handle, 2) : IntPtr.Zero, ClassName = className })) complete = false;
                return complete;
            }
            catch { complete = false; return false; }
        };
        // Win32 does not define a useful EnumChildWindows return value. Our
        // callback's bounded completion flag is authoritative, with root/context
        // validity checked again immediately before any dispatch.
        EnumChildWindows(root, callback, IntPtr.Zero); GC.KeepAlive(callback);
        return complete;
    }

    private static bool IsLiveOwnedToolbar(Window window)
        => window != null && window.Process == ThisProcess && window.Thread == NativeMethods.GetCurrentThreadId()
            && IsOwnedToolbar(window, window.Root, window.Process, window.Thread);

    private static int CountButtons(Window window)
        => IsLiveOwnedToolbar(window) ? unchecked((int)NativeMethods.SendMessage(window.Handle, TbButtonCount, IntPtr.Zero, IntPtr.Zero).ToInt64()) : -1;

    private static Button ReadButton(Window window, int position, int expectedUiId)
    {
        if (!IsLiveOwnedToolbar(window)) return null;
        int size = IntPtr.Size == 8 ? Marshal.SizeOf(typeof(Button64)) : Marshal.SizeOf(typeof(Button32));
        IntPtr data = Marshal.AllocHGlobal(size);
        try
        {
            if (NativeMethods.SendMessage(window.Handle, TbGetButton, new IntPtr(position), data) == IntPtr.Zero) return null;
            var button = new Button { Window = window, Position = position };
            if (IntPtr.Size == 8)
            {
                var value = (Button64)Marshal.PtrToStructure(data, typeof(Button64)); button.UiId = value.Command; button.State = value.State; button.Style = value.Style;
            }
            else
            {
                var value = (Button32)Marshal.PtrToStructure(data, typeof(Button32)); button.UiId = value.Command; button.State = value.State; button.Style = value.Style;
            }
            // IDs/states are read for every button to prove global uniqueness.
            // Text is relevant only for the resource-qualified target ID.
            if (button.UiId != expectedUiId) { button.Caption = ""; return IsLiveOwnedToolbar(window) ? button : null; }
            if (!IsLiveOwnedToolbar(window)) return null;
            long length = NativeMethods.SendMessage(window.Handle, TbGetButtonText, new IntPtr(button.UiId), IntPtr.Zero).ToInt64();
            if (length < -1 || length > 2048) return null;
            if (length <= 0) button.Caption = "";
            else
            {
                var text = new StringBuilder((int)length + 1);
                if (!IsLiveOwnedToolbar(window)) return null;
                long copied = SendMessageText(window.Handle, TbGetButtonText, new IntPtr(button.UiId), text).ToInt64();
                if (copied != length) return null;
                button.Caption = text.ToString();
            }
            return IsLiveOwnedToolbar(window) ? button : null;
        }
        finally { Marshal.FreeHGlobal(data); }
    }

    private static bool SameButton(Button before, Button after)
        => after != null && before.Window.Handle == after.Window.Handle && before.Position == after.Position && before.UiId == after.UiId
            && before.State == after.State && before.Style == after.Style && string.Equals(before.Caption, after.Caption, StringComparison.Ordinal);

    private static string ClassName(IntPtr window)
    {
        var text = new StringBuilder(256); return GetClassName(window, text, text.Capacity) > 0 ? text.ToString() : "";
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Button32
    {
        internal int Bitmap, Command;
        internal byte State, Style;
        internal ushort Reserved;
        internal uint Data, Text;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Button64
    {
        internal int Bitmap, Command;
        internal byte State, Style;
        internal ushort Reserved1;
        internal uint Reserved2;
        internal ulong Data, Text;
    }
    private delegate bool EnumChildProc(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr root, EnumChildProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageText(IntPtr window, uint message, IntPtr wParam, StringBuilder text);
}
