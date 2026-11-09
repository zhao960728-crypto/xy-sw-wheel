using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public class RadialOverlayDispatchTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void TimedOutPendingDisplayIsDiscardedWhenOverlayUnblocks()
    {
        using var host = new RadialOverlayHost(_ => { });
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Start(host);
        int staleDisplays = 0;
        Post(host, _ => { entered.Set(); release.Wait(3000); });
        Assert.True(entered.Wait(2000));
        try { Send(host, _ => staleDisplays++, 20); }
        finally { release.Set(); }
        Send(host, _ => { }, 2000);
        Assert.Equal(0, staleDisplays);
    }

    [Fact]
    public void RunningDisplayCanFinishAfterTimeoutWithoutDisposedWaitHandle()
    {
        using var host = new RadialOverlayHost(_ => { });
        using var release = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        Exception? callbackError = null;
        ThreadExceptionEventHandler handler = (_, args) => callbackError = args.Exception;
        Start(host);
        Send(host, _ => Application.ThreadException += handler, 2000);
        try
        {
            Send(host, _ => { entered.Set(); release.Wait(3000); }, 100);
            Assert.True(entered.IsSet);
            release.Set();
            Send(host, _ => { }, 2000);
            Assert.Null(callbackError);
        }
        finally
        {
            release.Set();
            Send(host, _ => Application.ThreadException -= handler, 2000);
        }
    }

    private static void Start(RadialOverlayHost host)
    {
        host.Start();
        var ready = (ManualResetEventSlim)typeof(RadialOverlayHost).GetField("_ready", Flags)!.GetValue(host)!;
        Assert.True(ready.Wait(2000));
    }
    private static void Post(RadialOverlayHost host, Action<RadialMenuWindow> action) =>
        typeof(RadialOverlayHost).GetMethod("Post", Flags)!.Invoke(host, new object[] { action });
    private static void Send(RadialOverlayHost host, Action<RadialMenuWindow> action, int timeout) =>
        typeof(RadialOverlayHost).GetMethod("Send", Flags)!.Invoke(host, new object[] { action, timeout });
}
