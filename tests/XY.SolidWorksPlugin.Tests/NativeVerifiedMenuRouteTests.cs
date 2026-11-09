using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class NativeVerifiedMenuRouteTests
{
    [Fact]
    public void ExplicitReferenceMappingsPreserveEveryNonNullPairWithoutInventingApiNames()
    {
        Assert.Equal(652, NativeCommandRouteMappings.Entries.Length);
        Assert.Equal(650, NativeCommandRouteMappings.SymbolsByApi.Count);
        Assert.Equal(648, NativeCommandRouteMappings.SymbolsByApi.Count(pair => pair.Value.Length == 1));
        Assert.Equal(new[] { "ID_INSERT_DIMENSION" }, NativeCommandRouteMappings.SymbolsByApi[38]);
        Assert.Equal(new[] { "ID_INSERT_LINE" }, NativeCommandRouteMappings.SymbolsByApi[43]);
        Assert.Equal(new[] { "ID_VIEW_FRONT" }, NativeCommandRouteMappings.SymbolsByApi[161]);
        Assert.Equal(new[] { "ID_FILE_OPEN" }, NativeCommandRouteMappings.SymbolsByApi[0]);
    }

    [Fact]
    public void DuplicateApiSymbolsAndUnknownOrNumericApiNamesAreNeverGuessed()
    {
        var groups = NativeCommandRouteMappings.Build(new[]
        {
            new KeyValuePair<string, string>("ID_ONE", "swCommands_SmartDimension"),
            new KeyValuePair<string, string>("ID_TWO", "swCommands_SmartDimension"),
            new KeyValuePair<string, string>("ID_NUMERIC", "38"),
            new KeyValuePair<string, string>("ID_UNKNOWN", "swCommands_NotARealCommand")
        });
        Assert.Single(groups); Assert.Equal(new[] { "ID_ONE", "ID_TWO" }, groups[38]);
    }

    [Fact]
    public void AFullResourcePassIndexesAllUnambiguousApisAndRefusesBothMultiSymbolGroups()
    {
        int nextId = 40000;
        var xml = new StringBuilder("<tooltips><toolbar>");
        foreach (var pair in NativeCommandRouteMappings.SymbolsByApi)
            foreach (string symbol in pair.Value) xml.Append(Node(++nextId, symbol, "原生命令" + pair.Key));
        xml.Append("</toolbar></tooltips>");
        var index = Read(xml.ToString());
        Assert.NotNull(index); Assert.Equal(648, index!.Count);
        Assert.False(index.ContainsKey(341)); Assert.False(index.ContainsKey(3438));
        Assert.All(index, pair => Assert.NotEqual(pair.Key, pair.Value.UiId));
    }

    [Theory]
    [InlineData(0, "ID_FILE_OPEN", 32801, "打开")]
    [InlineData(38, "ID_INSERT_DIMENSION", 32862, "智能尺寸")]
    [InlineData(43, "ID_INSERT_LINE", 32873, "直线")]
    [InlineData(46, "ID_INSERT_CIRCLE", 32885, "圆形")]
    [InlineData(59, "ID_SKETCH_TRIM", 32926, "剪裁实体")]
    [InlineData(332, "ID_VIEW_ZOOMTOFIT", 34100, "整屏显示全图")]
    [InlineData(161, "ID_VIEW_FRONT", 33579, "前视")]
    [InlineData(167, "ID_VIEW_ISOMETRIC", 33585, "等轴测")]
    public void EachMappedFunctionUsesItsOwnVerifiedUiIdentityExactlyOnce(int apiId, string symbol, int uiId, string title)
    {
        var index = Read("<tooltips><toolbar>" + Node(uiId, symbol, title) + "</toolbar></tooltips>");
        Assert.NotNull(index); var identity = Assert.Single(index!).Value;
        Assert.Equal(apiId, identity.ApiId); Assert.Equal(uiId, identity.UiId); Assert.Equal(symbol, identity.Symbol);
        var sent = new List<int>();
        Assert.Equal(NativeMenuDispatchResult.Submitted, NativeVerifiedMenuRoute.Dispatch(Snapshot(uiId, title), identity,
            () => true, () => true, sent.Add, out _));
        Assert.Equal(new[] { uiId }, sent); Assert.DoesNotContain(apiId, sent);
    }

    [Fact]
    public void ForeignCommandsWithTheSameTitleCannotCreateAnApiMapping()
    {
        var index = Read("<tooltips><toolbar>" + Node(49000, "FOREIGN_PLUGIN_LINE", "直线") + "</toolbar></tooltips>");
        Assert.NotNull(index); Assert.Empty(index!);
        Assert.Equal(NativeMenuDispatchResult.NotHandled, NativeVerifiedMenuRoute.Dispatch(Snapshot(49000, "直线"),
            new NativeVerifiedMenuRoute.Identity(43, "FOREIGN_PLUGIN_LINE", 49000, "直线"), () => true, () => true,
            _ => Assert.Fail("Foreign caption must not become a native route."), out _));
    }

    [Fact]
    public void AUiIdSharedByDifferentSymbolsInvalidatesBothMappingsWhileOtherCommandsStayAvailable()
    {
        var index = Read("<tooltips><toolbar>" + Node(32873, "ID_INSERT_LINE", "直线")
            + Node(32873, "ID_INSERT_CIRCLE", "圆形") + Node(32862, "ID_INSERT_DIMENSION", "智能尺寸") + "</toolbar></tooltips>");
        Assert.NotNull(index); Assert.False(index!.ContainsKey(43)); Assert.False(index.ContainsKey(46)); Assert.True(index.ContainsKey(38));
    }

    [Fact]
    public void AResourceTitleConflictInvalidatesOnlyItsApiWithoutPickingTheFirstCaption()
    {
        var index = Read("<tooltips><toolbar>" + Node(32873, "ID_INSERT_LINE", "直线")
            + Node(32873, "ID_INSERT_LINE", "其他功能") + Node(32862, "ID_INSERT_DIMENSION", "智能尺寸") + "</toolbar></tooltips>");
        Assert.NotNull(index); Assert.False(index!.ContainsKey(43)); Assert.True(index.ContainsKey(38));
    }

    [Theory]
    [InlineData(341, "ID_VIEW_SELECT_NAMED_VIEW")]
    [InlineData(3438, "ID_FLYOUT_PEN_PROPS")]
    public void AnAmbiguousApiDoesNotBecomeRoutableWhenOnlyOneOfItsSymbolsIsPresent(int apiId, string symbol)
    {
        var index = Read("<tooltips><toolbar>" + Node(49000, symbol, "唯一可见标题") + "</toolbar></tooltips>");
        Assert.NotNull(index); Assert.False(index!.ContainsKey(apiId));
        Assert.Equal(NativeMenuDispatchResult.NotHandled, NativeVerifiedMenuRoute.Dispatch(Snapshot(49000, "唯一可见标题"),
            new NativeVerifiedMenuRoute.Identity(apiId, symbol, 49000, "唯一可见标题"), () => true, () => true,
            _ => Assert.Fail("Multi-symbol API must not pick a preferred alias."), out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VerifiedNonDimensionCommandsRejectDisabledOrChangedContextWithoutRetry(bool changedContext)
    {
        var identity = new NativeVerifiedMenuRoute.Identity(43, "ID_INSERT_LINE", 32873, "直线");
        int sends = 0;
        Assert.Equal(NativeMenuDispatchResult.Rejected, NativeVerifiedMenuRoute.Dispatch(Snapshot(32873, "直线"), identity,
            () => !changedContext, () => changedContext, _ => sends++, out _)); Assert.Equal(0, sends);
    }

    [Fact]
    public void AContextReadFailureStopsUnverifiedFallbackForAnyNativeCommand()
        => Assert.Equal(NativeMenuDispatchResult.Rejected, NativeVerifiedMenuRoute.GuardUnverifiedContext(
            () => throw new InvalidOperationException("document changed"), out _));

    [Fact]
    public void NativeSendErrorsRemainTerminalForNonDimensionFunctions()
    {
        int sends = 0;
        var identity = new NativeVerifiedMenuRoute.Identity(46, "ID_INSERT_CIRCLE", 32885, "圆形");
        Assert.Equal(NativeMenuDispatchResult.Rejected, NativeVerifiedMenuRoute.Dispatch(Snapshot(32885, "圆形"), identity,
            () => true, () => true, _ => { sends++; throw new InvalidOperationException("already submitted"); }, out _));
        Assert.Equal(1, sends);
    }

    private static string Node(int id, string symbol, string title) => "<tooltip id='" + id + "' name='" + symbol + "'><title>" + title + "</title></tooltip>";
    private static IReadOnlyDictionary<int, NativeVerifiedMenuRoute.Identity>? Read(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return NativeVerifiedMenuRoute.ReadIdentities(stream);
    }
    private static NativeHostMenuRoute.Snapshot Snapshot(int uiId, string title)
    {
        var snapshot = new NativeHostMenuRoute.Snapshot();
        snapshot.Leaves.Add(new NativeHostMenuRoute.Leaf { Id = uiId, Path = new[] { "工具", title }, Enabled = true, SafeNativePath = true });
        return snapshot;
    }
}
