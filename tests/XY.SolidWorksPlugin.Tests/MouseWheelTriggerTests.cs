using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public class MouseWheelTriggerTests
{
    [Theory]
    [InlineData(516, 517)]
    [InlineData(519, 520)]
    public void DisabledScenePassesMouseMessagesWithoutTrackingOrReplay(int down, int up) => Sta(() =>
    {
        using var f = new Fixture(TriggerModes.Disabled);
        f.Mouse(0, down);
        Assert.False(f.Tracking);
        f.Mouse(0, up); f.Activate();
        Assert.Equal(0, f.Replays); Assert.Equal(0, f.Prepared);
        Assert.Equal(0, f.Shown); Assert.Equal(0, f.Committed);
    });

    [Theory]
    [InlineData("right-hold", 516, 517)]
    [InlineData("middle-hold", 519, 520)]
    public void RepeatedShortClicksReplayOnceAndNeverPrepareOrShow(string mode, int down, int up) => Sta(() =>
    {
        using var f = new Fixture(mode);
        for (int i = 0; i < 20; i++)
        {
            f.Mouse(3, down); // PeekMessage must not start a gesture.
            Assert.False(f.Tracking);
            f.Mouse(0, down);
            f.Mouse(3, up); // Preview must not finish/replay it either.
            Assert.True(f.Tracking);
            f.Pressed = false;
            f.Mouse(0, up);
            f.Activate(); // A late timer callback after release is obsolete.
            f.Pressed = true;
        }
        Assert.Equal(20, f.Replays);
        Assert.Equal(0, f.Prepared);
        Assert.Equal(0, f.Shown);
        Assert.Equal(0, f.Committed);
    });

    [Fact]
    public void BusyHostReleaseBeforeQueuedButtonUpDoesNotBecomeHold() => Sta(() =>
    {
        using var f = new Fixture();
        f.Mouse(0, 516);
        f.Pressed = false;
        f.Activate();
        Assert.Equal(0, f.Prepared);
        Assert.Equal(0, f.Shown);
        f.Mouse(0, 517);
        Assert.Equal(1, f.Replays);
    });

    [Fact]
    public void ReleaseDuringPreparationDoesNotShowWheel() => Sta(() =>
    {
        using var f = new Fixture();
        f.OnPrepare = () => f.Pressed = false;
        f.Mouse(0, 516);
        f.Activate();
        f.Mouse(0, 517);
        Assert.Equal(1, f.Prepared);
        Assert.Equal(0, f.Shown);
        Assert.Equal(1, f.Replays);
    });

    [Fact]
    public void LeavingHostDoesNotShowOrReplayClickIntoOtherApplication() => Sta(() =>
    {
        using var f = new Fixture();
        f.Mouse(0, 516);
        f.InHost = false;
        f.Activate();
        f.Mouse(0, 517);
        Assert.Equal(0, f.Shown);
        Assert.Equal(0, f.Replays);
    });

    [Fact]
    public void OldTimerCannotActivateNewGestureAndDisabledModeCancelsPendingHold() => Sta(() =>
    {
        using var f = new Fixture();
        f.Mouse(0, 516);
        int previous = f.Gesture;
        f.Mouse(0, 517);
        f.Mouse(0, 516);
        f.Activate(previous);
        Assert.Equal(0, f.Shown);
        f.Mode = TriggerModes.Disabled;
        f.Activate();
        Assert.Equal(0, f.Shown);
        f.Mouse(0, 517);
        Assert.Equal(2, f.Replays);
    });

    [Theory]
    [InlineData("right-hold", 516, 517)]
    [InlineData("middle-hold", 519, 520)]
    public void RealTimerRunsOnOwnerThreadAndHoldCommitsOnce(string mode, int down, int up) => Sta(() =>
    {
        using var f = new Fixture(mode, 100);
        int owner = Thread.CurrentThread.ManagedThreadId;
        f.Mouse(0, down);
        var watch = Stopwatch.StartNew();
        while (f.Shown == 0 && watch.ElapsedMilliseconds < 2000)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
        Assert.Equal(1, f.Shown);
        Assert.Equal(owner, f.PrepareThread);
        f.Activate();
        f.Pressed = false;
        f.Mouse(0, up);
        Assert.Equal(1, f.Prepared);
        Assert.Equal(1, f.Shown);
        Assert.Equal(1, f.Committed);
        Assert.Equal(0, f.Replays);
    });

    [Fact]
    public void WheelEscapeConsumesDownRepeatAndUpWithoutLeakingIntoNativeTool() => Sta(() =>
    {
        using var f = new Fixture(); f.MenuVisible = true;
        Assert.Equal(IntPtr.Zero, f.Key(3, false)); // PeekMessage preview.
        Assert.True(f.MenuVisible);
        Assert.Equal((IntPtr)1, f.Key(0, false));
        Assert.False(f.MenuVisible);
        Assert.Equal((IntPtr)1, f.Key(0, false));
        Assert.Equal((IntPtr)1, f.Key(0, true));
        Assert.Equal(IntPtr.Zero, f.Key(0, false));
        Assert.Equal(IntPtr.Zero, f.Key(0, true));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EscapeCancelsPendingOrVisibleHoldWithoutReplayOrCommit(bool shown) => Sta(() =>
    {
        using var f = new Fixture();
        f.Mouse(0, 516);
        if (shown) { f.Activate(); f.MenuVisible = true; }
        int gesture = f.Gesture;
        f.Key(0, false); f.Key(0, true);
        f.Activate(gesture); f.Activate();
        f.Mouse(0, 517);
        Assert.False(f.Tracking);
        Assert.Equal(0, f.Replays); Assert.Equal(0, f.Committed);
        Assert.Equal(shown ? 1 : 0, f.Shown);
    });

    [Fact]
    public void DisposedTriggerIgnoresPendingCallbackAndMessages() => Sta(() =>
    {
        using var f = new Fixture();
        f.Mouse(0, 516);
        f.Trigger.Dispose();
        f.Activate();
        f.Mouse(0, 516);
        Assert.Equal(0, f.Shown);
        Assert.False(f.Tracking);
    });

    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(5000), "Mouse trigger test did not finish");
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    private sealed class Fixture : IDisposable
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        public readonly MouseWheelTrigger Trigger;
        public bool Pressed = true, InHost = true;
        public bool MenuVisible;
        public string Mode;
        public int Prepared, Shown, Committed, Replays, PrepareThread;
        public Action? OnPrepare;
        public Fixture(string mode = "right-hold", int delay = 3000)
        {
            Mode = mode;
            Trigger = new MouseWheelTrigger(() => Mode, () => delay, () => IntPtr.Zero,
                () => { Prepared++; PrepareThread = Thread.CurrentThread.ManagedThreadId; OnPrepare?.Invoke(); },
                (_, __) => Shown++, _ => Committed++, _ => Pressed, _ => InHost, _ => Replays++,
                () => { bool visible = MenuVisible; MenuVisible = false; return visible; });
        }
        public bool Tracking => (bool)typeof(MouseWheelTrigger).GetField("_tracking", Flags)!.GetValue(Trigger)!;
        public int Gesture => (int)typeof(MouseWheelTrigger).GetField("_gestureId", Flags)!.GetValue(Trigger)!;
        public void Activate(int? gesture = null) => typeof(MouseWheelTrigger).GetMethod("Activate", Flags)!.Invoke(Trigger, new object[] { gesture ?? Gesture });
        public IntPtr Key(int code, bool released)
        {
            var message = new KeyMessage { Message = released ? 0x0101u : 0x0100u, Key = (IntPtr)27 };
            IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<KeyMessage>());
            try
            {
                Marshal.StructureToPtr(message, memory, false);
                typeof(MouseWheelTrigger).GetMethod("KeyboardMessageCallback", Flags)!
                    .Invoke(Trigger, new object[] { code == 3 ? 0 : code, code == 3 ? IntPtr.Zero : (IntPtr)1, memory });
                return Marshal.PtrToStructure<KeyMessage>(memory).Message == 0 ? (IntPtr)1 : IntPtr.Zero;
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        public void Mouse(int code, int message)
        {
            var value = new HookData { Point = new Point(300, 300) };
            IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<HookData>());
            try
            {
                Marshal.StructureToPtr(value, memory, false);
                typeof(MouseWheelTrigger).GetMethod("HookCallback", Flags)!.Invoke(Trigger, new object[] { code, (IntPtr)message, memory });
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        public void Dispose() => Trigger.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HookData { public Point Point; public IntPtr Window; public uint HitTest; public IntPtr ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyMessage { public IntPtr Window; public uint Message; public IntPtr Key; public IntPtr Flags; public uint Time; public Point Point; public uint Private; }
}
