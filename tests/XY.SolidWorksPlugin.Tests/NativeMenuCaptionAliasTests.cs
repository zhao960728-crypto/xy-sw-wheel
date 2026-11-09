using System;
using System.Collections.Generic;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class NativeMenuCaptionAliasTests
{
    [Theory]
    [InlineData(46, "ID_INSERT_CIRCLE", 32885, "圆形", "草图绘制实体", "圆")]
    [InlineData(59, "ID_SKETCH_TRIM", 32926, "剪裁实体", "草图工具", "剪裁")]
    public void ObservedUniqueNativePathUsesItsCurrentResourceUiIdOnce(int apiId, string symbol, int uiId,
        string resourceTitle, string middle, string menuTitle)
    {
        var sent = new List<int>();
        Assert.Equal(NativeMenuDispatchResult.Submitted, NativeVerifiedMenuRoute.Dispatch(
            Snapshot(uiId, "工具", middle, menuTitle), new NativeVerifiedMenuRoute.Identity(apiId, symbol, uiId, resourceTitle),
            () => true, () => true, sent.Add, out _));
        Assert.Equal(new[] { uiId }, sent);
        Assert.DoesNotContain(apiId, sent);
    }

    [Theory]
    [InlineData("其他插件", "草图绘制实体", "圆")]
    [InlineData("工具", "其他插件", "圆")]
    [InlineData("工具", "草图绘制实体", "圆形以外")]
    public void AnAliasCannotAuthorizeTheSameIdAtAnotherPluginOrMenuPath(string top, string middle, string title)
        => Assert.Equal(NativeMenuDispatchResult.NotHandled, NativeVerifiedMenuRoute.Dispatch(
            Snapshot(32885, top, middle, title), Circle(), () => true, () => true,
            _ => Assert.Fail("A menu alias must preserve every path segment."), out _));

    [Theory]
    [InlineData("ID_INSERT_CIRCLE", 49000, "圆形")]
    [InlineData("ID_INSERT_CIRCLE", 32885, "其他资源标题")]
    [InlineData("FOREIGN_CIRCLE", 32885, "圆形")]
    public void AHistoricalAliasCannotReplaceCurrentResourceIdentity(string symbol, int uiId, string resourceTitle)
    {
        Assert.False(NativeMenuRouteCaptionAliases.Matches(symbol, uiId, resourceTitle,
            new[] { "工具", "草图绘制实体", "圆" }));
        Assert.Equal(NativeMenuDispatchResult.NotHandled, NativeVerifiedMenuRoute.Dispatch(
            Snapshot(uiId, "工具", "草图绘制实体", "圆"), new NativeVerifiedMenuRoute.Identity(46, symbol, uiId, resourceTitle),
            () => true, () => true, _ => Assert.Fail("Historical metadata must not become an unverified route."), out _));
    }

    [Fact]
    public void ASecondLeafWithTheSameIdRemainsAmbiguousEvenWhenOnePathMatchesTheAlias()
    {
        var snapshot = Snapshot(32885, "工具", "草图绘制实体", "圆");
        snapshot.Leaves.Add(new NativeHostMenuRoute.Leaf
        { Id = 32885, Path = new[] { "其他插件", "圆" }, Enabled = true, SafeNativePath = true });
        Assert.Equal(NativeMenuDispatchResult.NotHandled, NativeVerifiedMenuRoute.Dispatch(snapshot, Circle(),
            () => true, () => true, _ => Assert.Fail("Duplicate UI IDs must remain rejected."), out _));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void IncompleteOrUnsafePathsDoNotAcquireTrustFromAnAlias(bool complete, bool safe)
    {
        var snapshot = Snapshot(32885, "工具", "草图绘制实体", "圆");
        snapshot.Complete = complete; snapshot.Leaves[0].SafeNativePath = safe;
        Assert.Equal(NativeMenuDispatchResult.NotHandled, NativeVerifiedMenuRoute.Dispatch(snapshot, Circle(),
            () => true, () => true, _ => Assert.Fail("Aliases only adapt verified readable paths."), out _));
    }

    [Fact]
    public void VerifiedAliasWithDisabledLeafStopsWithoutFallingBackOrSending()
    {
        var snapshot = Snapshot(32885, "工具", "草图绘制实体", "圆");
        snapshot.Leaves[0].Enabled = false;
        Assert.Equal(NativeMenuDispatchResult.Rejected, NativeVerifiedMenuRoute.Dispatch(snapshot, Circle(),
            () => true, () => true, _ => Assert.Fail("Disabled native command must not be sent."), out _));
    }

    [Fact]
    public void VerifiedAliasCannotBypassContextOrApiAvailability()
    {
        var snapshot = Snapshot(32885, "工具", "草图绘制实体", "圆");
        Assert.Equal(NativeMenuDispatchResult.Rejected, NativeVerifiedMenuRoute.Dispatch(snapshot, Circle(),
            () => false, () => true, _ => Assert.Fail("Changed document must stop the route."), out _));
        Assert.Equal(NativeMenuDispatchResult.Rejected, NativeVerifiedMenuRoute.Dispatch(snapshot, Circle(),
            () => true, () => false, _ => Assert.Fail("API-disabled native command must stop the route."), out _));
    }

    [Fact]
    public void SendFailureAfterVerifiedAliasIsTerminalAndNeverReplayed()
    {
        int calls = 0;
        Assert.Equal(NativeMenuDispatchResult.Rejected, NativeVerifiedMenuRoute.Dispatch(
            Snapshot(32885, "工具", "草图绘制实体", "圆"), Circle(), () => true, () => true,
            _ => { calls++; throw new InvalidOperationException("already attempted"); }, out _));
        Assert.Equal(1, calls);
    }

    private static NativeVerifiedMenuRoute.Identity Circle() => new(46, "ID_INSERT_CIRCLE", 32885, "圆形");

    private static NativeHostMenuRoute.Snapshot Snapshot(int uiId, params string[] path)
    {
        var snapshot = new NativeHostMenuRoute.Snapshot();
        snapshot.Leaves.Add(new NativeHostMenuRoute.Leaf { Id = uiId, Path = path, SafeNativePath = true, Enabled = true });
        return snapshot;
    }
}
