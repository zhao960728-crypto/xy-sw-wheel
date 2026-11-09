using System;
using System.Reflection;
using System.Threading;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class OverlayRequestLifecycleTests
{
    [Fact]
    public void ReleaseBeforeQueuedShowPreventsDisplayAndSelection()
    {
        var lifecycle = new OverlayRequestLifecycle();
        var request = lifecycle.Begin(true)!;
        Assert.Same(request, lifecycle.ReleaseCurrentDrag());
        Assert.False(lifecycle.CanShow(request));
        Assert.False(lifecycle.MarkShown(request));
        Assert.False(lifecycle.CanCommit(request));
        Assert.False(lifecycle.CanSelect);
    }

    [Fact]
    public void NewHoldCannotResurrectOrCommitThePreviousGesture()
    {
        var lifecycle = new OverlayRequestLifecycle();
        var first = lifecycle.Begin(true)!;
        Assert.True(lifecycle.MarkShown(first));
        Assert.Same(first, lifecycle.ReleaseCurrentDrag());
        var second = lifecycle.Begin(true)!;
        Assert.False(lifecycle.CanShow(first));
        Assert.False(lifecycle.CanCommit(first));
        Assert.False(lifecycle.CanSelect);
        Assert.True(lifecycle.CanShow(second));
        Assert.True(lifecycle.MarkShown(second));
        Assert.Same(second, lifecycle.ReleaseCurrentDrag());
        Assert.True(lifecycle.CanCommit(second));
        Assert.True(lifecycle.CanSelect);
    }

    [Fact]
    public void ReleaseDuringColdRenderCannotPublishASelectableWheel()
    {
        var lifecycle = new OverlayRequestLifecycle();
        var request = lifecycle.Begin(true)!;
        Assert.True(lifecycle.CanShow(request));
        lifecycle.ReleaseCurrentDrag();
        Assert.False(lifecycle.MarkShown(request));
        Assert.False(lifecycle.CanCommit(request));
    }

    [Fact]
    public void ButtonOpenedWheelIsNotCommittedByAnOldMouseRelease()
    {
        var lifecycle = new OverlayRequestLifecycle();
        var hold = lifecycle.Begin(true)!;
        lifecycle.MarkShown(hold);
        var button = lifecycle.Begin(false)!;
        lifecycle.MarkShown(button);
        Assert.Null(lifecycle.ReleaseCurrentDrag());
        Assert.False(lifecycle.CanCommit(hold));
        Assert.True(lifecycle.CanSelect);
        lifecycle.Dispose();
        Assert.False(lifecycle.CanSelect);
        Assert.False(lifecycle.MarkShown(button));
        Assert.Null(lifecycle.Begin(false));
    }

    [Fact]
    public void EscapeCancelsColdShowAndPublishedSelectionBeforeAnotherRequest()
    {
        var lifecycle = new OverlayRequestLifecycle();
        var cold = lifecycle.Begin(false)!;
        Assert.True(lifecycle.HasPending);
        Assert.True(lifecycle.CancelCurrent());
        Assert.False(lifecycle.HasPending);
        Assert.False(lifecycle.CanShow(cold));
        Assert.False(lifecycle.MarkShown(cold));
        var shown = lifecycle.Begin(true)!;
        Assert.True(lifecycle.MarkShown(shown));
        Assert.True(lifecycle.CancelCurrent());
        Assert.False(lifecycle.CanCommit(shown));
        Assert.False(lifecycle.CanSelect);
        Assert.Null(lifecycle.ReleaseCurrentDrag());
        Assert.False(lifecycle.CancelCurrent());
    }

    [Fact]
    public void DisposeEventuallyClosesHiddenOverlayAfterBusyCallbackReturns()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var host = new RadialOverlayHost(_ => throw new InvalidOperationException("No selection was requested."));
        host.Start();
        var ready = (ManualResetEventSlim)typeof(RadialOverlayHost).GetField("_ready", flags)!.GetValue(host)!;
        Assert.True(ready.Wait(3000));
        var thread = (Thread)typeof(RadialOverlayHost).GetField("_thread", flags)!.GetValue(host)!;
        var post = typeof(RadialOverlayHost).GetMethod("Post", flags)!;
        try
        {
            post.Invoke(host, new object[] { (Action<RadialMenuWindow>)(_ => { entered.Set(); release.Wait(6000); }) });
            Assert.True(entered.Wait(3000));
            host.Dispose();
        }
        finally { release.Set(); }
        Assert.True(thread.Join(3000), "A timed-out shutdown was cancelled instead of being processed after the render.");
        var window = (RadialMenuWindow)typeof(RadialOverlayHost).GetField("_window", flags)!.GetValue(host)!;
        Assert.True(window.IsDisposed);
    }
}
