using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class NativeSmartDimensionRouteTests
{
    private const int UiId = 32862;
    private static NativeSmartDimensionRoute.Identity Dimension => new(UiId, "智能尺寸");

    [Fact]
    public void RuntimeResourceIdentityAllowsIdenticalTooltipDuplicatesButNeverUsesApiIdAsUiId()
    {
        string node = "<tooltip id='32862' name='ID_INSERT_DIMENSION'><title>智能尺寸</title></tooltip>";
        var identity = Read("<tooltips><toolbar>" + node + node + "</toolbar></tooltips>");
        Assert.NotNull(identity); Assert.Equal(UiId, identity!.UiId); Assert.Equal("智能尺寸", identity.Title);
        Assert.Null(Read("<tooltips><toolbar><tooltip id='38' name='ID_INSERT_DIMENSION'><title>智能尺寸</title></tooltip></toolbar></tooltips>"));
    }

    [Theory]
    [InlineData("<tooltip id='32862' name='ANOTHER_COMMAND'><title>智能尺寸</title></tooltip>")]
    [InlineData("<tooltip id='32863' name='ID_INSERT_DIMENSION'><title>智能尺寸</title></tooltip>")]
    [InlineData("<tooltip id='32862' name='ID_INSERT_DIMENSION'><title>其他功能</title></tooltip>")]
    public void ConflictingResourceSymbolsIdsAndTitlesCannotEnableTheFastRoute(string conflictingNode)
    {
        string original = "<tooltip id='32862' name='ID_INSERT_DIMENSION'><title>智能尺寸</title></tooltip>";
        Assert.Null(Read("<tooltips><toolbar>" + original + conflictingNode + "</toolbar></tooltips>"));
    }

    [Theory]
    [InlineData("<other><toolbar><tooltip id='32862' name='ID_INSERT_DIMENSION'><title>智能尺寸</title></tooltip></toolbar></other>")]
    [InlineData("<tooltips><toolbar><tooltip id='70000' name='ID_INSERT_DIMENSION'><title>智能尺寸</title></tooltip></toolbar></tooltips>")]
    [InlineData("<tooltips><toolbar><tooltip id='32862' name='ID_INSERT_DIMENSION'><title> </title></tooltip></toolbar></tooltips>")]
    public void UnsupportedOrIncompleteResourceStructuresRemainUnhandled(string xml) => Assert.Null(Read(xml));

    [Fact]
    public void ResourceXmlCannotResolveExternalEntities()
    {
        Assert.Throws<XmlException>(() => Read("<!DOCTYPE tooltips [<!ENTITY x SYSTEM 'file:///C:/missing'>]><tooltips>&x;</tooltips>"));
    }

    [Fact]
    public void VerifiedPathUsesTheResourceUiIdExactlyOnceEvenWithAnotherPluginsSameCaption()
    {
        var menu = Menu();
        menu[(IntPtr)1].Add(new NativeHostMenuRoute.Item { Caption = "插件B", SubMenu = (IntPtr)4 });
        menu[(IntPtr)4] = new() { new NativeHostMenuRoute.Item { Id = 49001, Caption = "智能尺寸" } };
        var sent = new List<int>();
        Assert.Equal(NativeMenuDispatchResult.Submitted, Dispatch(Capture(menu), sent.Add));
        Assert.Equal(new[] { UiId }, sent); Assert.DoesNotContain(38, sent);
    }

    [Fact]
    public void RebuildingNativeMenuIdRequiresFreshResourceAndMenuProof()
    {
        var menu = Menu(); menu[(IntPtr)3][0].Id = 32863;
        var sent = new List<int>();
        Assert.Equal(NativeMenuDispatchResult.NotHandled, Dispatch(Capture(menu), sent.Add));
        Assert.Empty(sent);
        Assert.Equal(NativeMenuDispatchResult.Submitted, NativeSmartDimensionRoute.Dispatch(Capture(menu),
            new NativeSmartDimensionRoute.Identity(32863, "智能尺寸"), () => true, () => true, sent.Add, out _));
        Assert.Equal(new[] { 32863 }, sent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DuplicateIdsOrAncestorCaptionsNeverChooseTheFirstMatchingMenu(bool duplicateId)
    {
        var menu = Menu();
        menu[(IntPtr)1].Add(new NativeHostMenuRoute.Item { Caption = duplicateId ? "插件B" : "工具", SubMenu = (IntPtr)4 });
        menu[(IntPtr)4] = new() { new NativeHostMenuRoute.Item { Id = duplicateId ? (uint)UiId : 49001u, Caption = "智能尺寸" } };
        var sent = new List<int>();
        Assert.Equal(NativeMenuDispatchResult.NotHandled, Dispatch(Capture(menu), sent.Add)); Assert.Empty(sent);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void DisabledVerifiedParentOrLeafIsRejectedInsteadOfFallingBack(int disabledMenu)
    {
        var menu = Menu(); menu[(IntPtr)disabledMenu][0].State = 3;
        int sent = 0;
        Assert.Equal(NativeMenuDispatchResult.Rejected, Dispatch(Capture(menu), _ => sent++)); Assert.Equal(0, sent);
    }

    [Fact]
    public void OwnerDrawnLeafRequiresTheVerifiedIdentityWhileOwnerDrawnParentsStayUnsupported()
    {
        var menu = Menu(); menu[(IntPtr)3][0].Type = 0x100;
        var sent = new List<int>();
        Assert.Equal(NativeMenuDispatchResult.Submitted, Dispatch(Capture(menu), sent.Add));
        menu[(IntPtr)1][0].Type = 0x100;
        Assert.Equal(NativeMenuDispatchResult.NotHandled, Dispatch(Capture(menu), sent.Add)); Assert.Single(sent);
    }

    [Fact]
    public void NotifyByPositionMenusCannotBecomeWmCommandRoutes()
    {
        var menu = Menu(); menu[(IntPtr)3][0].Style = 0x08000000;
        Assert.Equal(NativeMenuDispatchResult.NotHandled, Dispatch(Capture(menu), _ => Assert.Fail("Must not send.")));
    }

    [Fact]
    public void ContextChangeDuringEnableQueryRejectsWithoutSendingOrFallback()
    {
        int checks = 0, sent = 0;
        Assert.Equal(NativeMenuDispatchResult.Rejected, NativeSmartDimensionRoute.Dispatch(Capture(Menu()), Dimension,
            () => ++checks == 1, () => true, _ => sent++, out string message));
        Assert.Equal(2, checks); Assert.Equal(0, sent); Assert.Contains("变化", message);
    }

    [Fact]
    public void OfficialDisabledStateRejectsTheVerifiedRoute()
    {
        int sent = 0;
        Assert.Equal(NativeMenuDispatchResult.Rejected, NativeSmartDimensionRoute.Dispatch(Capture(Menu()), Dimension,
            () => true, () => false, _ => sent++, out _)); Assert.Equal(0, sent);
    }

    [Fact]
    public void SendFailureIsTerminalEvenIfTheNativeCommandMayAlreadyHaveStarted()
    {
        int attempts = 0;
        Assert.Equal(NativeMenuDispatchResult.Rejected, Dispatch(Capture(Menu()), _ => { attempts++; throw new InvalidOperationException("provider error"); }));
        Assert.Equal(1, attempts);
    }

    [Theory]
    [InlineData(2, 16)]
    [InlineData(4096, 2)]
    public void PartialOrOverBoundMenuSnapshotsNeverDispatch(int maxItems, int maxDepth)
    {
        var menu = Menu();
        var snapshot = NativeHostMenuRoute.Capture((IntPtr)1, handle => menu[handle].Count,
            (handle, index) => menu[handle][index], maxItems, maxDepth);
        Assert.False(snapshot.Complete);
        Assert.Equal(NativeMenuDispatchResult.NotHandled, Dispatch(snapshot, _ => Assert.Fail("Must not send.")));
    }

    [Fact]
    public void AReadFailureElsewhereCannotLeaveAPartialMatchingRouteExecutable()
    {
        var menu = Menu(); menu[(IntPtr)1].Add(new NativeHostMenuRoute.Item { Caption = "其他", SubMenu = (IntPtr)4 });
        var snapshot = NativeHostMenuRoute.Capture((IntPtr)1, handle => handle == (IntPtr)4 ? -1 : menu[handle].Count, (handle, index) => menu[handle][index]);
        Assert.Single(snapshot.Leaves); Assert.False(snapshot.Complete);
        Assert.Equal(NativeMenuDispatchResult.NotHandled, Dispatch(snapshot, _ => Assert.Fail("Must not send.")));
    }

    [Fact]
    public void MenuCaptionMismatchCannotExecuteByIdAlone()
    {
        var menu = Menu(); menu[(IntPtr)3][0].Caption = "智能尺寸插件版";
        Assert.Equal(NativeMenuDispatchResult.NotHandled, Dispatch(Capture(menu), _ => Assert.Fail("Must not send.")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnverifiedDiscoveryCanFallBackOnlyInTheSameCapturedContext(bool unchanged)
    {
        Assert.Equal(unchanged ? NativeMenuDispatchResult.NotHandled : NativeMenuDispatchResult.Rejected,
            NativeSmartDimensionRoute.GuardUnverifiedContext(() => unchanged, out _));
    }

    [Fact]
    public void UnverifiableFallbackContextIsRejectedWithoutLaunchingAnotherRoute()
        => Assert.Equal(NativeMenuDispatchResult.Rejected,
            NativeSmartDimensionRoute.GuardUnverifiedContext(() => throw new InvalidOperationException("document closed"), out _));

    private static NativeMenuDispatchResult Dispatch(NativeHostMenuRoute.Snapshot snapshot, Action<int> send)
        => NativeSmartDimensionRoute.Dispatch(snapshot, Dimension, () => true, () => true, send, out _);
    private static NativeSmartDimensionRoute.Identity? Read(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return NativeSmartDimensionRoute.ReadIdentity(stream);
    }
    private static NativeHostMenuRoute.Snapshot Capture(Dictionary<IntPtr, List<NativeHostMenuRoute.Item>> menu)
        => NativeHostMenuRoute.Capture((IntPtr)1, handle => menu[handle].Count, (handle, index) => menu[handle][index]);
    private static Dictionary<IntPtr, List<NativeHostMenuRoute.Item>> Menu() => new()
    {
        [(IntPtr)1] = new() { new NativeHostMenuRoute.Item { Caption = "工具(&T)", SubMenu = (IntPtr)2 } },
        [(IntPtr)2] = new() { new NativeHostMenuRoute.Item { Caption = "尺寸", SubMenu = (IntPtr)3 } },
        [(IntPtr)3] = new() { new NativeHostMenuRoute.Item { Caption = "智能尺寸(&D)...\tD", Id = UiId } }
    };
}
