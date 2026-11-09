using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Threading;
using SolidWorks.Interop.sldworks;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class CommandRouteIdentityTests
{
    [Fact]
    public void ToolbarPathRoundTripsWithoutReinterpretingOldPathlessData()
    {
        string value = ExternalToolbarCommandReference.Encode(2, 41001, "设置/A", "第三方插件 > 插件A > 子菜单");
        Assert.True(ExternalToolbarCommandReference.TryDecode(value, out int type, out int id));
        Assert.Equal(2, type); Assert.Equal(41001, id); Assert.Equal("设置/A", ExternalToolbarCommandReference.Name(value));
        Assert.Equal("第三方插件 > 插件A > 子菜单", ExternalToolbarCommandReference.Path(value));
        Assert.Equal("", ExternalToolbarCommandReference.Path(ExternalToolbarCommandReference.Encode(2, 41001, "设置/A")));
    }

    [Fact]
    public void MissingPluginOrSubmenuNeverFallsBackToAnotherUniqueCaption()
    {
        var b = Row(1, 41002, "第三方插件 > 插件B");
        Assert.False(ExternalPluginCommandCatalog.TryResolveLiveRows(new[] { b }, 1, "第三方插件 > 插件A", "设置", out _));
        Assert.False(ExternalPluginCommandCatalog.TryResolveLiveRows(new[] { Row(1, 41003, "第三方插件 > 插件A扩展") }, 1, "第三方插件 > 插件A", "设置", out _));
        Assert.False(ExternalPluginCommandCatalog.TryResolveLiveRows(new[] { Row(1, 41004, "第三方插件 > 插件A > 其他") }, 1, "第三方插件 > 插件A > 原菜单", "设置", out _));
    }

    [Fact]
    public void IdMayChangeOnlyWithinTheSameFullCurrentPathAndDocument()
    {
        var a = Row(1, 41021, "第三方插件 > 插件A > 原菜单");
        Assert.True(ExternalPluginCommandCatalog.TryResolveLiveRows(new[] { a, Row(1, 41022, "第三方插件 > 插件B") }, 1, a.Category, "设置", out var actual));
        Assert.Same(a, actual);
        Assert.False(ExternalPluginCommandCatalog.TryResolveLiveRows(new[] { a }, 2, a.Category, "设置", out _));
        Assert.False(ExternalPluginCommandCatalog.TryResolveLiveRows(new[] { a, Row(1, 41023, a.Category) }, 1, a.Category, "设置", out _));
        a.Value = ExternalToolbarCommandReference.Encode(1, 41021, "设置", "第三方插件 > 插件B");
        Assert.False(ExternalPluginCommandCatalog.TryResolveLiveRows(new[] { a }, 1, a.Category, "设置", out _));
    }

    [Fact]
    public void LegacyNativeCategoryCannotBeDroppedWhenOnlyOneOtherCommandRemains()
    {
        var row = new KeyboardShortcutRow { Category = "SOLIDWORKS > 特征", Command = "阵列", Value = NativeCommandReference.Encode(43) };
        Assert.False(SolidWorksNativeCommandCatalog.TryResolveLegacyRows(new[] { row }, "草图", "阵列", out _));
        Assert.True(SolidWorksNativeCommandCatalog.TryResolveLegacyRows(new[] { row }, "特征", "阵列", out int id));
        Assert.Equal(43, id);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void WrongEnvironmentAndMissingPathStopBeforeAnyWindowOrCommandLookup(int currentType, bool withPath)
    {
        var document = Proxy<IModelDoc2>(call => call.MethodName == "GetType" ? currentType : null);
        var host = Proxy<ISldWorks>(call => call.MethodName == "get_ActiveDoc" ? document : throw new InvalidOperationException("Unexpected host dispatch: " + call.MethodName));
        string value = withPath ? ExternalToolbarCommandReference.Encode(1, 41001, "设置", "第三方插件 > 原插件") : ExternalToolbarCommandReference.Encode(1, 41001, "设置");
        Assert.False(SolidWorksNativeCommandCatalog.TryExecute(host, value, out string message));
        Assert.Contains(withPath ? "文档类型" : "完整插件路径", message);
    }

    [Fact]
    public void UnknownKindAndEncodedFunctionCannotBecomeKeyboardInput()
    {
        var addin = new SwAddin();
        var execute = typeof(SwAddin).GetMethod("ExecuteComboCommand", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var error = Assert.Throws<TargetInvocationException>(() => execute.Invoke(addin, new object[] { new ComboCommandConfig { Kind = "wrong-route", Value = "Ctrl+A" }, CancellationToken.None }));
        Assert.Contains("未知组合功能类型", error.InnerException!.Message);
        error = Assert.Throws<TargetInvocationException>(() => execute.Invoke(addin, new object[] { new ComboCommandConfig { Kind = ActionKinds.Hotkey, Value = NativeCommandReference.Encode(43) }, CancellationToken.None }));
        Assert.Contains("快捷键格式无效", error.InnerException!.Message);
    }

    private static KeyboardShortcutRow Row(int type, int id, string path) => new KeyboardShortcutRow { Category = path, Command = "设置", ActionKind = ActionKinds.ExternalPlugin, Value = ExternalToolbarCommandReference.Encode(type, id, "设置", path) };
    private static T Proxy<T>(Func<IMethodCallMessage, object?> call) => (T)new FakeCom(typeof(T), call).GetTransparentProxy();
    private sealed class FakeCom : RealProxy
    {
        private readonly Func<IMethodCallMessage, object?> _call;
        internal FakeCom(Type type, Func<IMethodCallMessage, object?> call) : base(type) => _call = call;
        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            try { return new ReturnMessage(_call(call), call.Args, call.Args.Length, call.LogicalCallContext, call); }
            catch (Exception ex) { return new ReturnMessage(ex, call); }
        }
    }
}
