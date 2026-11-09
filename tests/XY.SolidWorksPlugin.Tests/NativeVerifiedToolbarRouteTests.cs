using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class NativeVerifiedToolbarRouteTests
{
    private static readonly IntPtr Root = (IntPtr)11;
    private const uint Process = 123, Thread = 456;

    [Theory]
    [InlineData(161, "ID_VIEW_FRONT", 33579, "前视")]
    [InlineData(167, "ID_VIEW_ISOMETRIC", 33585, "等轴测")]
    public void VerifiedStandardViewButtonSendsTheCurrentResourceUiIdExactlyOnce(int api, string symbol, int ui, string title)
    {
        var snapshot = Snapshot(ui, title); var sent = new List<int>();
        Assert.Equal(NativeMenuDispatchResult.Submitted, Dispatch(snapshot, new NativeVerifiedMenuRoute.Identity(api, symbol, ui, title), sent.Add));
        Assert.Equal(new[] { ui }, sent); Assert.DoesNotContain(api, sent);
    }

    [Fact]
    public void ARealIconOnlyButtonCanUseTheResourceIdentityWithoutInventingATitle()
    {
        var snapshot = Snapshot(33579, ""); int sends = 0;
        Assert.Equal(NativeMenuDispatchResult.Submitted, Dispatch(snapshot, Front(), _ => sends++)); Assert.Equal(1, sends);
    }

    [Theory]
    [InlineData(43, "ID_INSERT_LINE", 32873)]
    [InlineData(161, "FOREIGN_FRONT", 33579)]
    [InlineData(161, "ID_VIEW_FRONT", 161)]
    public void UnmappedApisForeignSymbolsAndApiIdsUsedAsUiIdsCannotAcquireTheToolbarRoute(int api, string symbol, int ui)
    {
        var identity = new NativeVerifiedMenuRoute.Identity(api, symbol, ui, "前视");
        Assert.False(NativeVerifiedToolbarRoute.Eligible(identity));
        Assert.Equal(NativeMenuDispatchResult.NotHandled, Dispatch(Snapshot(ui, "前视"), identity, _ => Assert.Fail("Unverified identity cannot send.")));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(128)]
    public void SeparatorsAndDropdownsOnlyDisableTheOptionalFastRoute(int style)
    {
        var snapshot = Snapshot(33579, "前视"); snapshot.Buttons[0].Style = (byte)style;
        AssertFallback(snapshot);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(20)]
    public void HiddenDisabledAndIndeterminateToolbarButtonsDoNotDisableTheOriginalNativeApi(int state)
    {
        var snapshot = Snapshot(33579, "前视"); snapshot.Buttons[0].State = (byte)state;
        AssertFallback(snapshot);
    }

    [Fact]
    public void DuplicateUiIdsAndDifferentNonemptyCaptionsRemainUnverified()
    {
        var snapshot = Snapshot(33579, "前视"); snapshot.Buttons.Add(Button(Window(3), 0, 33579, "前视"));
        AssertFallback(snapshot);
        AssertFallback(Snapshot(33579, "其他插件前视"));
    }

    [Fact]
    public void MissingButtonAndIncompleteSnapshotFallBackWithoutSendingAnything()
    {
        AssertFallback(Snapshot(49000, "前视"));
        var incomplete = Snapshot(33579, "前视"); incomplete.Complete = false; AssertFallback(incomplete);
    }

    [Fact]
    public void AnExplicitlyDisabledApiIsTerminalEvenWhenTheToolbarLooksEnabled()
        => Assert.Equal(NativeMenuDispatchResult.Rejected, NativeVerifiedToolbarRoute.Dispatch(Snapshot(33579, "前视"), Front(),
            () => true, () => false, _ => true, _ => Assert.Fail("Disabled API must not send."), out _));

    [Fact]
    public void AChangedDocumentIsRejectedBeforeDispatchAndAfterTheEnabledQuery()
    {
        Assert.Equal(NativeMenuDispatchResult.Rejected, NativeVerifiedToolbarRoute.Dispatch(Snapshot(33579, "前视"), Front(),
            () => false, () => true, _ => true, _ => Assert.Fail("Changed document cannot send."), out _));
        bool stable = true;
        Assert.Equal(NativeMenuDispatchResult.Rejected, NativeVerifiedToolbarRoute.Dispatch(Snapshot(33579, "前视"), Front(),
            () => stable, () => { stable = false; return true; }, _ => true, _ => Assert.Fail("No retargeting after query."), out _));
    }

    [Fact]
    public void ARebuiltOrUnreadableButtonFallsBackOnlyInTheSameContext()
    {
        Assert.Equal(NativeMenuDispatchResult.NotHandled, NativeVerifiedToolbarRoute.Dispatch(Snapshot(33579, "前视"), Front(),
            () => true, () => true, _ => false, _ => Assert.Fail("Rebuilt button cannot send."), out _));
        Assert.Equal(NativeMenuDispatchResult.NotHandled, NativeVerifiedToolbarRoute.Dispatch(Snapshot(33579, "前视"), Front(),
            () => true, () => throw new InvalidOperationException("read failed"), _ => true, _ => Assert.Fail("Read failure cannot send."), out _));
        int checks = 0;
        Assert.Equal(NativeMenuDispatchResult.Rejected, NativeVerifiedToolbarRoute.Dispatch(Snapshot(33579, "前视"), Front(),
            () => ++checks == 1, () => throw new InvalidOperationException("read failed"), _ => true,
            _ => Assert.Fail("Unconfirmed new document cannot receive API fallback."), out _));
    }

    [Fact]
    public void UncertainNativeSendIsTerminalAndNeverReplayed()
    {
        int sends = 0;
        Assert.Equal(NativeMenuDispatchResult.Rejected, NativeVerifiedToolbarRoute.Dispatch(Snapshot(33579, "前视"), Front(),
            () => true, () => true, _ => true, _ => { sends++; throw new InvalidOperationException("already attempted"); }, out string message));
        Assert.Equal(1, sends); Assert.Contains("未重复", message);
    }

    [Fact]
    public void ForeignProcessesThreadsRootsAndOtherControlsAreNeverReadWithToolbarMessages()
    {
        var own = Window(2); var foreignProcess = Window(3); foreignProcess.Process++;
        var foreignThread = Window(4); foreignThread.Thread++;
        var otherRoot = Window(5); otherRoot.Root = (IntPtr)999;
        var otherClass = Window(6); otherClass.ClassName = "CustomControl";
        int reads = 0;
        var snapshot = NativeVerifiedToolbarRoute.Capture(Root, Process, Thread,
            visit => new[] { foreignProcess, foreignThread, otherRoot, otherClass, own }.All(visit),
            window => { Assert.Same(own, window); return 1; },
            (window, position) => { reads++; Assert.Same(own, window); return Button(window, position, 33579, "前视"); });
        Assert.True(snapshot.Complete); Assert.Single(snapshot.Buttons); Assert.Equal(1, reads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheEntireBoundedSnapshotIsRequiredBeforeUniquenessCanBeClaimed(bool readFailure)
    {
        var window = Window(2); int reads = 0;
        var snapshot = NativeVerifiedToolbarRoute.Capture(Root, Process, Thread, visit => visit(window), _ => 2,
            (owned, position) => { reads++; return readFailure && position == 1 ? null! : Button(owned, position, position == 0 ? 33579 : 49000, ""); },
            maxButtons: readFailure ? 2 : 1);
        Assert.False(snapshot.Complete); Assert.Equal(readFailure ? 2 : 0, reads); AssertFallback(snapshot);
    }

    [Fact]
    public void AToolbarRebuildEnumerationFailureAndTimeBoundRemainOptionalFallbacks()
    {
        var window = Window(2); int counts = 0;
        var rebuilt = NativeVerifiedToolbarRoute.Capture(Root, Process, Thread, visit => visit(window),
            _ => ++counts == 1 ? 1 : 2, (owned, position) => Button(owned, position, 33579, "前视"));
        Assert.False(rebuilt.Complete); AssertFallback(rebuilt);
        var enumerationFailed = NativeVerifiedToolbarRoute.Capture(Root, Process, Thread, visit => { visit(window); return false; },
            _ => 1, (owned, position) => Button(owned, position, 33579, "前视"));
        Assert.False(enumerationFailed.Complete); AssertFallback(enumerationFailed);
        var timedOut = NativeVerifiedToolbarRoute.Capture(Root, Process, Thread, visit => visit(window),
            _ => 1, (owned, position) => Button(owned, position, 33579, "前视"), withinBudget: () => false);
        Assert.False(timedOut.Complete); AssertFallback(timedOut);
    }

    [Fact]
    public void NativeButtonLayoutsMatchWin32OnBothArchitectures()
    {
        Assert.Equal(20, Marshal.SizeOf(typeof(NativeVerifiedToolbarRoute.Button32)));
        Assert.Equal(32, Marshal.SizeOf(typeof(NativeVerifiedToolbarRoute.Button64)));
    }

    private static NativeMenuDispatchResult Dispatch(NativeVerifiedToolbarRoute.Snapshot snapshot, NativeVerifiedMenuRoute.Identity identity, Action<int> send)
        => NativeVerifiedToolbarRoute.Dispatch(snapshot, identity, () => true, () => true, _ => true, send, out _);
    private static void AssertFallback(NativeVerifiedToolbarRoute.Snapshot snapshot)
        => Assert.Equal(NativeMenuDispatchResult.NotHandled, Dispatch(snapshot, Front(), _ => Assert.Fail("Unverified toolbar cannot send or disable the original API.")));
    private static NativeVerifiedMenuRoute.Identity Front() => new(161, "ID_VIEW_FRONT", 33579, "前视");
    private static NativeVerifiedToolbarRoute.Window Window(int handle) => new()
        { Handle = (IntPtr)handle, Root = Root, Process = Process, Thread = Thread, ClassName = "ToolbarWindow32" };
    private static NativeVerifiedToolbarRoute.Button Button(NativeVerifiedToolbarRoute.Window window, int position, int id, string title) => new()
        { Window = window, Position = position, UiId = id, Caption = title, State = 4 };
    private static NativeVerifiedToolbarRoute.Snapshot Snapshot(int id, string title)
    {
        var snapshot = new NativeVerifiedToolbarRoute.Snapshot(); snapshot.Buttons.Add(Button(Window(2), 0, id, title)); return snapshot;
    }
}
