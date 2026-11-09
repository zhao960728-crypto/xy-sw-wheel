using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class SwCommandDispatcherTests
{
    [Fact]
    public void UserCommandOvertakesQueuedMetadataAndRemainsOnTheOwnerThread() => Sta(() =>
    {
        using var dispatcher = new SwCommandDispatcher();
        var order = new List<string>();
        int owner = Thread.CurrentThread.ManagedThreadId;
        dispatcher.Post(() => order.Add("metadata1"));
        dispatcher.Post(() => order.Add("metadata2"));
        dispatcher.PostPriority(() =>
        {
            Assert.Equal(owner, Thread.CurrentThread.ManagedThreadId);
            Assert.True(dispatcher.HasPriorityWork);
            order.Add("command");
        });
        Application.DoEvents();
        Assert.Equal(new[] { "command", "metadata1", "metadata2" }, order);
        Assert.False(dispatcher.HasPriorityWork);
    });

    [Fact]
    public void CommandQueuedDuringMetadataRunsBeforeTheNextMetadataAction() => Sta(() =>
    {
        using var dispatcher = new SwCommandDispatcher();
        var order = new List<string>();
        dispatcher.Post(() =>
        {
            order.Add("metadata1");
            dispatcher.PostPriority(() => order.Add("command"));
        });
        dispatcher.Post(() => order.Add("metadata2"));
        Application.DoEvents();
        Assert.Equal(new[] { "metadata1", "command", "metadata2" }, order);
    });

    [Theory]
    [InlineData(ApartmentState.MTA)]
    [InlineData(ApartmentState.STA)]
    public void DisconnectUnblocksAnInvokeWaitingForTheHostWithoutExecutingItOnTheWorker(ApartmentState apartment) => Sta(() =>
    {
        var dispatcher = new SwCommandDispatcher();
        bool executed = false; Exception? failure = null;
        var worker = new Thread(() =>
        {
            try { dispatcher.Invoke(() => executed = true); }
            catch (Exception error) { failure = error; }
        });
        worker.SetApartmentState(apartment); worker.Start();
        var wait = Stopwatch.StartNew();
        while (!dispatcher.HasPriorityWork && wait.ElapsedMilliseconds < 2000) Thread.Sleep(1);
        Assert.True(dispatcher.HasPriorityWork);
        dispatcher.Dispose();
        Assert.True(worker.Join(2000));
        Assert.IsType<ObjectDisposedException>(failure);
        Assert.False(executed);
    });

    [Fact]
    public void DisconnectSynchronouslyReleasesEveryQueuedInvokeWithoutRunningAnyAction() => Sta(() =>
    {
        using var posted = new CountdownEvent(4);
        using var dispatcher = new SwCommandDispatcher((window, message, wParam, lParam) =>
        {
            bool sent = NativeMethods.PostMessage(window, message, wParam, lParam);
            if (sent) posted.Signal();
            return sent;
        });
        var failures = new Exception?[4];
        var workers = new Thread[4]; int executed = 0;
        for (int index = 0; index < workers.Length; index++)
        {
            int slot = index;
            workers[index] = new Thread(() =>
            {
                try { dispatcher.Invoke(() => Interlocked.Increment(ref executed)); }
                catch (Exception error) { failures[slot] = error; }
            });
            workers[index].SetApartmentState(index % 2 == 0 ? ApartmentState.MTA : ApartmentState.STA);
            workers[index].Start();
        }
        // Waiting on the owner deliberately does not pump the queued UI work.
        Assert.True(posted.Wait(2000), "Not all callers reached the UI queue.");
        dispatcher.Dispose();
        foreach (var worker in workers) Assert.True(worker.Join(2000), "Cancelled dispatcher caller stayed blocked: " + worker.ThreadState);
        Assert.All(failures, error => Assert.IsType<ObjectDisposedException>(error));
        Assert.Equal(0, executed);
    });

    [Fact]
    public void InvokedActionRunsOnTheOwnerAndReturnsItsOriginalExceptionToTheCaller() => Sta(() =>
    {
        using var dispatcher = new SwCommandDispatcher();
        int owner = Thread.CurrentThread.ManagedThreadId, ranOn = -1;
        var expected = new InvalidOperationException("exact host command failure");
        Exception? received = null; bool returned = false;
        var worker = new Thread(() =>
        {
            try { dispatcher.Invoke(() => { ranOn = Thread.CurrentThread.ManagedThreadId; throw expected; }); }
            catch (Exception error) { received = error; }
            finally { Volatile.Write(ref returned, true); }
        });
        worker.SetApartmentState(ApartmentState.STA); worker.Start();
        var clock = Stopwatch.StartNew();
        while (!Volatile.Read(ref returned) && clock.ElapsedMilliseconds < 2000) { Application.DoEvents(); Thread.Sleep(1); }
        Assert.True(worker.Join(2000));
        Assert.Equal(owner, ranOn); Assert.Same(expected, received);
        Assert.Contains(nameof(SwCommandDispatcher.Invoke), received!.StackTrace);
    });

    [Fact]
    public void FailedPriorityPostCannotConsumeAnEarlierBackgroundWakeup() => Sta(() =>
    {
        int calls = 0;
        using var dispatcher = new SwCommandDispatcher((window, message, wParam, lParam) =>
            ++calls == 2 ? false : NativeMethods.PostMessage(window, message, wParam, lParam));
        var order = new List<string>();
        dispatcher.Post(() => order.Add("metadata"));
        Assert.Throws<System.ComponentModel.Win32Exception>(() => dispatcher.PostPriority(() => order.Add("failed")));
        Application.DoEvents();
        Assert.Equal(new[] { "metadata" }, order);
        Assert.False(dispatcher.HasPriorityWork);
    });

    [Fact]
    public void PostingAfterDisconnectNeverRunsAnActionInlineOnTheCaller() => Sta(() =>
    {
        var dispatcher = new SwCommandDispatcher(); dispatcher.Dispose();
        bool executed = false;
        Assert.Throws<ObjectDisposedException>(() => dispatcher.Post(() => executed = true));
        Assert.Throws<ObjectDisposedException>(() => dispatcher.PostPriority(() => executed = true));
        Assert.Throws<ObjectDisposedException>(() => dispatcher.Invoke(() => executed = true));
        Assert.False(executed);
    });

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(10000));
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
