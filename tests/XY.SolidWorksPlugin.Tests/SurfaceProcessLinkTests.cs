using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using SolidWorks.Interop.sldworks;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class SurfaceProcessLinkTests
{
    [Theory]
    [InlineData("新增精整-喷塑-下料-人工终检", "新增精整-镀锌-下料-人工终检")]
    [InlineData("下料-人工指定涂层-检验", "下料-人工指定涂层-检验")]
    [InlineData("喷塑-下料-喷塑", "喷塑-下料-喷塑")]
    public void RebaseTouchesOnlyOneProvenSurfaceSlotAndKeepsManualOrder(string draft, string expected)
    {
        Assert.Equal(expected, SurfaceProcessLinkPolicy.MergeDraft(draft, "下料-喷塑-检验", "下料-镀锌-检验", "-"));
    }
    [Fact]
    public void RebaseRequiresExactlyOneModelChangeAndKeepsDraftDelimiters()
    {
        Assert.Equal("人工精整 > 镀锌 > 终检", SurfaceProcessLinkPolicy.MergeDraft("人工精整 > 喷塑 > 终检", "下料-喷塑-检验", "下料-镀锌-检验", "-"));
        Assert.Equal("人工精整-喷塑-终检", SurfaceProcessLinkPolicy.MergeDraft("人工精整-喷塑-终检", "下料-喷塑-检验", "钻孔-镀锌-检验", "-"));
    }
    [Fact]
    public void UniqueSlotFollowsRepeatedSurfaceChangesWithoutReorderingOrDeduplication()
    {
        var store = new MemoryStore("下料 → 检验 → 表面处理 → 检验", "喷塑");
        var state = new SurfaceProcessLinkState();
        Assert.True(Apply(store, state));
        Assert.Equal("下料 → 检验 → 喷塑 → 检验", store.Values["工序"]);
        store.Values["表面处理"] = "镀锌";
        Assert.True(Apply(store, state));
        Assert.Equal("下料 → 检验 → 镀锌 → 检验", store.Values["工序"]);
        Assert.False(Apply(store, state));
        Assert.Equal(2, store.Writes);
    }

    [Theory]
    [InlineData("下料-喷塑-检验")]
    [InlineData("表面处理-检验-表面处理")]
    [InlineData("下料-表面处理前清洗-检验")]
    public void FirstObservationRequiresOneExactPlaceholder(string sequence)
    {
        var store = new MemoryStore(sequence, "镀锌");
        Assert.False(Apply(store, new SurfaceProcessLinkState()));
        Assert.Equal(sequence, store.Values["工序"]);
        Assert.Equal(0, store.Writes);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("喷塑-镀锌")]
    [InlineData("喷塑\n镀锌")]
    public void EmptyOrMultiStepSurfaceNeverDeletesOrSplitsTheProcess(string surface)
    {
        var store = new MemoryStore("下料-表面处理-检验", surface);
        Assert.False(Apply(store, new SurfaceProcessLinkState()));
        Assert.Equal("下料-表面处理-检验", store.Values["工序"]);
    }

    [Fact]
    public void ManualSequenceChangeSuspendsFollowingAndPreservesCustomSteps()
    {
        var store = new MemoryStore("下料-表面处理-检验", "喷塑"); var state = new SurfaceProcessLinkState();
        Assert.True(Apply(store, state));
        store.Values["工序"] = "人工精整-喷塑-检验"; store.Values["表面处理"] = "镀锌";
        Assert.False(Apply(store, state)); Assert.False(Apply(store, state));
        Assert.Equal("人工精整-喷塑-检验", store.Values["工序"]);
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public void ClearingSurfaceKeepsLastStepAndLaterValueCanResume()
    {
        var store = new MemoryStore("下料-表面处理", "喷塑"); var state = new SurfaceProcessLinkState();
        Assert.True(Apply(store, state)); store.Values["表面处理"] = "";
        Assert.False(Apply(store, state)); Assert.Equal("下料-喷塑", store.Values["工序"]);
        store.Values["表面处理"] = "镀锌"; Assert.True(Apply(store, state));
        Assert.Equal("下料-镀锌", store.Values["工序"]);
    }

    [Fact]
    public void ChangedSurfaceSnapshotRejectsBeforeAnyProcessWrite()
    {
        var store = new MemoryStore("下料-表面处理", "喷塑"); var surface = store.Read("表面处理");
        store.Values["表面处理"] = "用户刚修改";
        Assert.Throws<InvalidOperationException>(() => SurfaceProcessLinkService.Apply(store, "工序", store.Read("工序"), store, "表面处理", surface, "-", new SurfaceProcessLinkState(), out _, out _));
        Assert.Equal("下料-表面处理", store.Values["工序"]); Assert.Equal(0, store.Writes);
    }

    [Fact]
    public void FormulaProcessIsNeverFlattened()
    {
        var store = new MemoryStore("=$PRP:工序模板", "喷塑"); var process = store.Read("工序"); process.Resolved = "下料-表面处理";
        Assert.False(SurfaceProcessLinkService.Apply(store, "工序", process, store, "表面处理", store.Read("表面处理"), "-", new SurfaceProcessLinkState(), out _, out _));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public void FailedWriteRollsBackAndDoesNotAdvanceLinkState()
    {
        var store = new MemoryStore("下料-表面处理", "喷塑") { FailWrite = true }; var state = new SurfaceProcessLinkState();
        Assert.Throws<InvalidOperationException>(() => Apply(store, state));
        Assert.Equal("下料-表面处理", store.Values["工序"]); Assert.Null(state.Sequence);
        store.FailWrite = false; Assert.True(Apply(store, state));
    }

    [Fact]
    public void ConfigurationSurfaceNeverChangesSharedDocumentProcess()
    {
        var model = new Model(); model.Scope("")["工序"] = "下料-表面处理"; model.Scope("A")["表面处理"] = "喷塑";
        var result = new SurfaceProcessLinkService().Synchronize(model.Document, "A", Config());
        Assert.False(result.Changed); Assert.Contains("共享", result.Status);
        Assert.Equal("下料-表面处理", model.Scope("")["工序"]); Assert.Equal(0, model.Writes);
    }

    [Fact]
    public void ReferenceConfigurationOnlyIsUpdatedWithoutActivationSaveOrGeometryCalls()
    {
        var model = new Model(); model.Scope("A")["工序"] = "下料-表面处理"; model.Scope("A")["表面处理"] = "喷塑";
        model.Scope("B")["工序"] = "另一个配置"; model.Scope("")["工序"] = "文档原工序";
        var service = new SurfaceProcessLinkService();
        Assert.True(service.Synchronize(model.Document, "A", Config()).Changed);
        model.Scope("A")["表面处理"] = "镀锌"; var second = service.Synchronize(model.Document, "A", Config()); Assert.True(second.Changed, second.Status);
        Assert.Equal("下料-镀锌", model.Scope("A")["工序"]);
        Assert.Equal("另一个配置", model.Scope("B")["工序"]); Assert.Equal("文档原工序", model.Scope("")["工序"]);
        Assert.Equal(2, model.Writes);
    }

    [Fact]
    public void ReadOnlyModelRemainsUntouched()
    {
        var model = new Model { ReadOnly = true }; model.Scope("")["工序"] = "下料-表面处理"; model.Scope("")["表面处理"] = "喷塑";
        Assert.False(new SurfaceProcessLinkService().Synchronize(model.Document, "A", Config()).Changed);
        Assert.Equal(0, model.Writes);
    }

    private static ProcessRecognitionConfig Config() => new ProcessRecognitionConfig { Enabled = true };
    private static bool Apply(MemoryStore store, SurfaceProcessLinkState state) => SurfaceProcessLinkService.Apply(store, "工序", store.Read("工序"), store, "表面处理", store.Read("表面处理"), "-", state, out _, out _);
    private sealed class MemoryStore : PropertyWriteBatch.Store
    {
        internal readonly Dictionary<string, string> Values = new Dictionary<string, string>(); internal int Writes; internal bool FailWrite;
        internal MemoryStore(string sequence, string surface) { Values["工序"] = sequence; Values["表面处理"] = surface; }
        public PropertyWriteBatch.Snapshot Read(string name) => new PropertyWriteBatch.Snapshot { Exists = Values.ContainsKey(name), Raw = Values[name], Resolved = Values[name], Type = 30 };
        public void Set(string name, string value, PropertyWriteBatch.Snapshot previous) { Values[name] = value; Writes++; if (FailWrite) throw new InvalidOperationException("write failed"); }
        public void Restore(string name, PropertyWriteBatch.Snapshot previous) { Values[name] = previous.Raw; }
    }
    private sealed class Model
    {
        private readonly Dictionary<string, Dictionary<string, string>> _scopes = new Dictionary<string, Dictionary<string, string>>();
        internal readonly IModelDoc2 Document; internal bool ReadOnly; internal int Writes;
        internal Dictionary<string, string> Scope(string scope) { if (!_scopes.ContainsKey(scope)) _scopes[scope] = new Dictionary<string, string>(); return _scopes[scope]; }
        internal Model()
        {
            var extension = Proxy<ModelDocExtension>(call => call.MethodName == "get_CustomPropertyManager" ? Manager((string)call.Args[0]) : throw new InvalidOperationException(call.MethodName));
            Document = Proxy<IModelDoc2>(call => call.MethodName switch {
                "GetType" => 1, "IsOpenedReadOnly" => ReadOnly, "GetActiveSketch2" => null, "get_Extension" => extension,
                _ => throw new InvalidOperationException("Unexpected host mutation or geometry read: " + call.MethodName)
            });
        }
        private CustomPropertyManager Manager(string configuration) => Proxy<CustomPropertyManager>(call =>
        {
            var values = Scope(configuration);
            switch (call.MethodName)
            {
                case "GetNames": return values.Keys.Cast<object>().ToArray();
                case "GetType2": return 30;
                case "Get4": values.TryGetValue((string)call.Args[0], out string text); return Tuple.Create(text ?? "");
                case "Set2": values[(string)call.Args[0]] = (string)call.Args[1]; Writes++; return 0;
                default: throw new InvalidOperationException(call.MethodName);
            }
        });
    }
    private static T Proxy<T>(Func<IMethodCallMessage, object?> invoke) => (T)new FakeCom(typeof(T), invoke).GetTransparentProxy();
    private sealed class FakeCom : RealProxy
    {
        private readonly Func<IMethodCallMessage, object?> _invoke;
        internal FakeCom(Type type, Func<IMethodCallMessage, object?> invoke) : base(type) { _invoke = invoke; }
        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            try { var value = _invoke(call); var args = call.Args; if (value is Tuple<string> property) { args[2] = property.Item1; args[3] = property.Item1; value = true; } return new ReturnMessage(value, args, args.Length, call.LogicalCallContext, call); }
            catch (Exception ex) { return new ReturnMessage(ex, call); }
        }
    }
}
