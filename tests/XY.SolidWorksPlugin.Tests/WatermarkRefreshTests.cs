using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Threading;
using SolidWorks.Interop.sldworks;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class WatermarkRefreshTests
{
    [Fact]
    public void RefreshCompletionCannotDiscardNewerDocumentNotification()
    {
        var queue = new WatermarkRefreshQueue();
        queue.Request(10);
        Assert.True(queue.TryBegin(10, out long first));
        queue.Request(11);
        queue.Complete(first);
        Assert.True(queue.Pending);
        Assert.True(queue.TryBegin(11, out long latest));
        queue.Complete(latest);
        Assert.False(queue.Pending);
        queue.Request(12);
        queue.Cancel();
        Assert.False(queue.TryBegin(12, out _));
    }

    [Fact]
    public void BurstyReloadsReadOnlyLatestStateOnceOnOwnerSta()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                int reads = 0;
                using var host = new PropertyWatermarkHost(() => IntPtr.Zero, () => null!, () =>
                {
                    reads++;
                    return new PropertyWatermarkConfig();
                }, () => { });
                host.Start();
                int initial = reads;
                for (int i = 0; i < 100; i++) host.Reload();
                System.Windows.Forms.Application.DoEvents();
                Assert.Equal(initial + 1, reads);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure != null) throw failure;
    }

    [Fact]
    public void WatermarkLivesAndRefreshesOnOwnerSta()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                int owner = Thread.CurrentThread.ManagedThreadId;
                int reads = 0;
                using var host = new PropertyWatermarkHost(() => IntPtr.Zero, () => null!, () =>
                {
                    Assert.Equal(owner, Thread.CurrentThread.ManagedThreadId);
                    reads++;
                    return new PropertyWatermarkConfig();
                }, () => { });
                host.Start();
                Assert.Equal(1, reads);
                host.Reload();
                System.Windows.Forms.Application.DoEvents();
                Assert.True(reads >= 2);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure != null) throw failure;
    }

    [Fact]
    public void SelectionFollowsLatestComponentAndItsReferencedConfiguration()
    {
        var first = Model(1, "first-active");
        var last = Model(1, "last-active");
        var parts = new[] { Component(first, "first-ref"), Component(last, "last-ref") };
        var selection = Proxy<SelectionMgr>(call => call.MethodName switch
        {
            "GetSelectedObjectCount2" => 2,
            "GetSelectedObjectsComponent4" or "GetSelectedObject6" => parts[(int)call.Args[0] - 1],
            _ => null
        });
        var assembly = Model(2, "assembly", selection);
        var sw = Proxy<ISldWorks>(call => call.MethodName == "get_ActiveDoc" ? assembly : null);
        Assert.Same(last, WatermarkPropertyAccess.ResolveDocument(sw, true, out string config));
        Assert.Equal("last-ref", config);
        Assert.Same(assembly, WatermarkPropertyAccess.ResolveDocument(sw, false, out config));
        Assert.Equal("assembly", config);
    }

    [Fact]
    public void ClearingConfigurationProcessDoesNotRedisplayDocumentProcess()
    {
        var config = Manager("工序", "");
        var document = Manager("工序", "旧工序");
        var extension = Proxy<ModelDocExtension>(call => call.MethodName == "get_CustomPropertyManager" ? ((string)call.Args[0] == "引用" ? config : document) : null);
        var model = Proxy<IModelDoc2>(call => call.MethodName == "get_Extension" ? extension : null);
        var value = WatermarkPropertyAccess.Read(model, "引用", "自定义属性", "工序");
        Assert.True(value.Exists);
        Assert.Equal("", value.Text);
        Assert.Equal("引用", value.Configuration);
    }

    [Fact]
    public void ExistingProcessEditorKeepsExplicitEmptyConfigurationScope()
    {
        var configured = Manager("工序", "");
        var general = Manager("工序", "旧文档工序");
        var extension = Proxy<ModelDocExtension>(call => call.MethodName == "get_CustomPropertyManager" ? ((string)call.Args[0] == "引用" ? configured : general) : null);
        var model = Proxy<IModelDoc2>(call => call.MethodName == "get_Extension" ? extension : null);
        var target = ExistingProcessEditTarget.Effective(model, "引用", "工序");
        Assert.NotNull(target);
        Assert.Equal("引用", target.Configuration);
        Assert.True(target.Snapshot.Exists);
        Assert.Equal("", target.Snapshot.Raw);
        Assert.Equal("", target.Snapshot.Resolved);
    }

    [Fact]
    public void MissingConfigurationProcessStillUsesActualDocumentAlias()
    {
        var config = Manager(null, "");
        var document = Manager("工艺流程", "折弯-包装");
        var extension = Proxy<ModelDocExtension>(call => call.MethodName == "get_CustomPropertyManager" ? ((string)call.Args[0] == "引用" ? config : document) : null);
        var model = Proxy<IModelDoc2>(call => call.MethodName == "get_Extension" ? extension : null);
        var value = WatermarkPropertyAccess.Read(model, "引用", "自定义属性", "工序");
        Assert.Equal("折弯-包装", value.Text);
        Assert.Equal("工艺流程", value.Name);
        Assert.Equal("", value.Configuration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BlankHostClearsPreviousWatermarkDocumentAndText(bool editing)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var owner = new System.Windows.Forms.Form();
                owner.Show();
                var config = new PropertyWatermarkConfig { Enabled = true };
                var sw = Proxy<ISldWorks>(call => null);
                using var window = new PropertyWatermarkWindow(() => owner.Handle, () => sw, () => config, () => { });
                void Set(string name, object value) => typeof(PropertyWatermarkWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);
                object? Get(string name) => typeof(PropertyWatermarkWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
                Set("_editing", editing);
                Set("_displayedDocument", Model(1, "old"));
                Set("_displayedConfiguration", "old");
                Set("_lastText", "旧零件\r\n旧工序");
                window.Show();
                window.RefreshNow();
                Assert.False(window.Visible);
                Assert.Null(Get("_displayedDocument"));
                Assert.Equal("", Get("_displayedConfiguration"));
                Assert.Equal("", Get("_lastText"));
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure != null) throw failure;
    }

    private static ICustomPropertyManager Manager(string? name, string text) => Proxy<CustomPropertyManager>(call =>
    {
        if (call.MethodName == "GetNames") return name == null ? Array.Empty<object>() : new object[] { name };
        if (call.MethodName == "Get4") return new PropertyReply(text);
        if (call.MethodName == "GetType2") return 30;
        return null;
    });
    private static IComponent2 Component(IModelDoc2 doc, string config) => Proxy<Component2>(call => call.MethodName switch
    {
        "GetModelDoc2" => doc, "get_ReferencedConfiguration" => config, _ => null
    });
    private static IModelDoc2 Model(int type, string config, ISelectionMgr? selection = null)
    {
        var configuration = Proxy<Configuration>(call => call.MethodName == "get_Name" ? config : null);
        var manager = Proxy<ConfigurationManager>(call => call.MethodName == "get_ActiveConfiguration" ? configuration : null);
        return Proxy<IModelDoc2>(call => call.MethodName switch
        {
            "GetType" => type, "get_ConfigurationManager" => manager, "get_ISelectionManager" => selection, _ => null
        });
    }
    private static T Proxy<T>(Func<IMethodCallMessage, object?> invoke) => (T)new FakeCom(typeof(T), invoke).GetTransparentProxy();
    private sealed class PropertyReply
    {
        internal PropertyReply(string text) { Text = text; }
        internal string Text { get; }
    }
    private sealed class FakeCom : RealProxy
    {
        private readonly Func<IMethodCallMessage, object?> _invoke;
        public FakeCom(Type type, Func<IMethodCallMessage, object?> invoke) : base(type) { _invoke = invoke; }
        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            try
            {
                var value = _invoke(call);
                var args = call.Args;
                if (value is PropertyReply property) { args[2] = property.Text; args[3] = property.Text; value = true; }
                return new ReturnMessage(value, args, args.Length, call.LogicalCallContext, call);
            }
            catch (Exception ex) { return new ReturnMessage(ex, call); }
        }
    }
}
