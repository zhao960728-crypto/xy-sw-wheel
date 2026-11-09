using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using SolidWorks.Interop.sldworks;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class LegacyNativeSessionCacheTests
{
    private static readonly string LegacyDimension = NativeCommandReference.Encode("草图 > 尺寸", "智能尺寸");
    private static int ScanSize => ((ICollection<int>)typeof(SolidWorksNativeCommandCatalog)
        .GetField("NativeCommandIds", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Count;

    [Fact]
    public void LegacyColdScanBecomesZeroMenuReadsForTheSameVerifiedIdentity()
    {
        var fixture = new HostFixture(); fixture.Menu(1, 38, "智能尺寸", "尺寸");
        Assert.True(SolidWorksNativeCommandCatalog.TryExecute(fixture.Host, LegacyDimension, out var issue), issue);
        Assert.Equal(ScanSize, fixture.MenuReads);
        Assert.True(ScanSize > 3000);
        Assert.True(SolidWorksNativeCommandCatalog.TryExecute(fixture.Host, LegacyDimension, out issue), issue);
        Assert.Equal(ScanSize, fixture.MenuReads);
        Assert.Equal(new[] { 38, 38 }, fixture.Runs);
    }

    [Fact]
    public void DirectNativeIdentityNeverReadsTheCatalogOrDocument()
    {
        var fixture = new HostFixture { RejectDocumentRead = true };
        Assert.True(SolidWorksNativeCommandCatalog.TryExecute(fixture.Host, NativeCommandReference.Encode(38), out var issue), issue);
        Assert.Equal(0, fixture.MenuReads); Assert.Equal(0, fixture.DocumentReads);
        Assert.Equal(new[] { 38 }, fixture.Runs);
    }

    [Fact]
    public void CompleteExplicitReadPrewarmsAnImmutableIdentitySnapshot()
    {
        var fixture = new HostFixture(); fixture.Menu(1, 38, "智能尺寸", "尺寸");
        Assert.True(SolidWorksNativeCommandCatalog.TryRead(fixture.Host, out var rows, out var issue), issue);
        var row = Assert.Single(rows);
        row.Category = "SOLIDWORKS > 其他"; row.Command = "被外部编辑"; row.Value = NativeCommandReference.Encode(43);
        Assert.True(SolidWorksNativeCommandCatalog.TryExecute(fixture.Host, LegacyDimension, out issue), issue);
        Assert.Equal(ScanSize, fixture.MenuReads); Assert.Equal(new[] { 38 }, fixture.Runs);
    }

    [Fact]
    public void HostAndDocumentTypesHaveIndependentVerifiedIdentities()
    {
        var first = new HostFixture(); first.Menu(1, 38, "智能尺寸", "尺寸"); first.Menu(2, 43, "智能尺寸", "尺寸");
        var second = new HostFixture(); second.Menu(1, 44, "智能尺寸", "尺寸");
        Assert.True(SolidWorksNativeCommandCatalog.TryExecute(first.Host, LegacyDimension, out _));
        Assert.True(SolidWorksNativeCommandCatalog.TryExecute(second.Host, LegacyDimension, out _));
        first.DocumentType = 2;
        Assert.True(SolidWorksNativeCommandCatalog.TryExecute(first.Host, LegacyDimension, out _));
        first.DocumentType = 1;
        Assert.True(SolidWorksNativeCommandCatalog.TryExecute(first.Host, LegacyDimension, out _));
        Assert.Equal(ScanSize * 2, first.MenuReads); Assert.Equal(ScanSize, second.MenuReads);
        Assert.Equal(new[] { 38, 43, 38 }, first.Runs); Assert.Equal(new[] { 44 }, second.Runs);
    }

    [Fact]
    public void CategoryAndNameBothRemainPartOfTheHotIdentity()
    {
        var fixture = new HostFixture(); fixture.Menu(1, 38, "智能尺寸", "尺寸"); fixture.Menu(1, 43, "同一类别的其他功能", "尺寸");
        Assert.True(SolidWorksNativeCommandCatalog.TryExecute(fixture.Host, LegacyDimension, out _));
        Assert.False(SolidWorksNativeCommandCatalog.TryExecute(fixture.Host, NativeCommandReference.Encode("特征", "智能尺寸"), out _));
        Assert.False(SolidWorksNativeCommandCatalog.TryExecute(fixture.Host, NativeCommandReference.Encode("草图 > 尺寸", "不存在功能"), out _));
        Assert.Equal(new[] { 38 }, fixture.Runs);
    }

    [Fact]
    public void ACompleteRereadInvalidatesOldUniqueTargetsAndAmbiguityCanRecover()
    {
        var fixture = new HostFixture(); fixture.Menu(1, 38, "智能尺寸", "尺寸");
        Assert.True(SolidWorksNativeCommandCatalog.TryExecute(fixture.Host, LegacyDimension, out _));
        fixture.Menu(1, 43, "智能尺寸", "尺寸");
        Assert.True(SolidWorksNativeCommandCatalog.TryRead(fixture.Host, out var rows, out _)); Assert.Equal(2, rows.Count);
        Assert.False(SolidWorksNativeCommandCatalog.TryExecute(fixture.Host, LegacyDimension, out _));
        Assert.Equal(new[] { 38 }, fixture.Runs);
        fixture.RemoveMenu(1, 38);
        Assert.True(SolidWorksNativeCommandCatalog.TryExecute(fixture.Host, LegacyDimension, out _));
        Assert.Equal(ScanSize * 4, fixture.MenuReads); Assert.Equal(new[] { 38, 43 }, fixture.Runs);
        Assert.True(SolidWorksNativeCommandCatalog.TryExecute(fixture.Host, LegacyDimension, out _));
        Assert.Equal(ScanSize * 4, fixture.MenuReads);
    }

    [Fact]
    public void MissingCommandsAreNotPermanentlyNegativeCached()
    {
        var fixture = new HostFixture();
        Assert.False(SolidWorksNativeCommandCatalog.TryExecute(fixture.Host, LegacyDimension, out _));
        fixture.Menu(1, 38, "智能尺寸", "尺寸");
        Assert.True(SolidWorksNativeCommandCatalog.TryExecute(fixture.Host, LegacyDimension, out _));
        Assert.True(SolidWorksNativeCommandCatalog.TryExecute(fixture.Host, LegacyDimension, out _));
        Assert.Equal(ScanSize * 2, fixture.MenuReads); Assert.Equal(new[] { 38, 38 }, fixture.Runs);
    }

    [Fact]
    public void PartialReadInvalidatesPreviousSnapshotAndCannotExecuteAPartialUniqueMatch()
    {
        var fixture = new HostFixture(); fixture.Menu(1, 38, "智能尺寸", "尺寸");
        Assert.True(SolidWorksNativeCommandCatalog.TryExecute(fixture.Host, LegacyDimension, out _));
        fixture.RemoveMenu(1, 38); fixture.Menu(1, 43, "智能尺寸", "尺寸"); fixture.FailMenuId = 0;
        Assert.True(SolidWorksNativeCommandCatalog.TryRead(fixture.Host, out var partial, out _)); Assert.Single(partial);
        Assert.False(SolidWorksNativeCommandCatalog.TryExecute(fixture.Host, LegacyDimension, out _));
        Assert.Equal(new[] { 38 }, fixture.Runs);
        fixture.FailMenuId = null;
        Assert.True(SolidWorksNativeCommandCatalog.TryExecute(fixture.Host, LegacyDimension, out _));
        Assert.Equal(ScanSize * 4, fixture.MenuReads); Assert.Equal(new[] { 38, 43 }, fixture.Runs);
    }

    [Fact]
    public void FailedOptionalCommandManagerReadDoesNotPublishAPrewarmedSnapshot()
    {
        var fixture = new HostFixture(); fixture.Menu(1, 38, "智能尺寸", "尺寸");
        var manager = Proxy<ICommandManager>(_ => throw new InvalidOperationException("temporary CM failure"));
        Assert.True(SolidWorksNativeCommandCatalog.TryRead(fixture.Host, manager, out _, out _));
        Assert.True(SolidWorksNativeCommandCatalog.TryExecute(fixture.Host, LegacyDimension, out _));
        Assert.Equal(ScanSize * 2, fixture.MenuReads);
    }

    [Fact]
    public void ContextChangeDuringScanCannotPublishOrExecuteTheOldContext()
    {
        var fixture = new HostFixture(); fixture.Menu(1, 38, "智能尺寸", "尺寸");
        fixture.OnMenuRead = () => fixture.DocumentType = 2;
        Assert.False(SolidWorksNativeCommandCatalog.TryExecute(fixture.Host, LegacyDimension, out _));
        Assert.Empty(fixture.Runs);
        fixture.OnMenuRead = null; fixture.DocumentType = 1;
        Assert.True(SolidWorksNativeCommandCatalog.TryExecute(fixture.Host, LegacyDimension, out _));
        Assert.Equal(ScanSize * 2, fixture.MenuReads);
    }

    [Fact]
    public void NativeAvailabilityFailuresAreNeitherRetriedNorPermanentlyRemembered()
    {
        var fixture = new HostFixture { AcceptRun = false }; fixture.Menu(1, 38, "智能尺寸", "尺寸");
        Assert.False(SolidWorksNativeCommandCatalog.TryExecute(fixture.Host, LegacyDimension, out _));
        Assert.Equal(new[] { 38 }, fixture.Runs);
        fixture.AcceptRun = true;
        Assert.True(SolidWorksNativeCommandCatalog.TryExecute(fixture.Host, LegacyDimension, out _));
        Assert.Equal(ScanSize, fixture.MenuReads); Assert.Equal(new[] { 38, 38 }, fixture.Runs);
    }

    private sealed class HostFixture
    {
        private readonly Dictionary<string, string[]> _menus = new();
        internal readonly ISldWorks Host;
        internal int DocumentType = 1, MenuReads, DocumentReads;
        internal int? FailMenuId;
        internal bool AcceptRun = true, RejectDocumentRead;
        internal Action? OnMenuRead;
        internal readonly List<int> Runs = new();
        internal void Menu(int type, int id, string name, string category) => _menus[type + ":" + id] = new[] { name, category };
        internal void RemoveMenu(int type, int id) => _menus.Remove(type + ":" + id);
        internal HostFixture()
        {
            var document = Proxy<IModelDoc2>(call => call.MethodName == "GetType" ? DocumentType : null);
            Host = Proxy<ISldWorks>(call =>
            {
                switch (call.MethodName)
                {
                    case "get_ActiveDoc":
                        DocumentReads++;
                        if (RejectDocumentRead) throw new InvalidOperationException("Direct native ID must not query ActiveDoc.");
                        return document;
                    case "GetMenuStrings":
                        MenuReads++; OnMenuRead?.Invoke();
                        int id = (int)call.Args[0], type = (int)call.Args[1];
                        if (FailMenuId == id) throw new InvalidOperationException("temporary menu failure");
                        bool found = _menus.TryGetValue(type + ":" + id, out var entry);
                        return new MenuText(found ? entry![0] : "", found ? entry![1] : "");
                    case "RunCommand": Runs.Add((int)call.Args[0]); return AcceptRun;
                    default: return null;
                }
            });
        }
    }
    private sealed class MenuText
    {
        internal readonly string Name, Parent;
        internal MenuText(string name, string parent) { Name = name; Parent = parent; }
    }
    private static T Proxy<T>(Func<IMethodCallMessage, object?> handler) => (T)new FakeCom(typeof(T), handler).GetTransparentProxy();
    private sealed class FakeCom : RealProxy
    {
        private readonly Func<IMethodCallMessage, object?> _handler;
        internal FakeCom(Type type, Func<IMethodCallMessage, object?> handler) : base(type) => _handler = handler;
        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            try
            {
                object? result = _handler(call);
                var args = call.Args;
                if (result is MenuText menu) { args[2] = menu.Parent; result = menu.Name; }
                return new ReturnMessage(result, args, args.Length, call.LogicalCallContext, call);
            }
            catch (Exception ex) { return new ReturnMessage(ex, call); }
        }
    }
}
