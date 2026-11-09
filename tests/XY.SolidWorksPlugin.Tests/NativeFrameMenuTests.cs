using System;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using SolidWorks.Interop.sldworks;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class NativeFrameMenuTests
{
    [Fact]
    public void BackingMenuUsesTyped64BitApiWithoutConsultingWindowMenu()
    {
        long expected = 0x100000123L;
        var frame = Proxy<IFrame>(call => call.MethodName == "GetMenux64" ? expected : throw new InvalidOperationException("Unexpected frame lookup: " + call.MethodName));
        IntPtr actual = ExternalMenuCommandCatalog.ResolveMenuHandle(frame, _ => throw new InvalidOperationException("The actual backing menu was discarded."));
        Assert.Equal(new IntPtr(expected), actual);
    }

    [Fact]
    public void MissingBackingMenuFallsBackToTheActual64BitFrameWindow()
    {
        long expectedWindow = 0x200000345L;
        var frame = Proxy<IFrame>(call => call.MethodName switch { "GetMenux64" => 0L, "GetHWndx64" => expectedWindow, _ => throw new InvalidOperationException(call.MethodName) });
        int fallbacks = 0;
        IntPtr actual = ExternalMenuCommandCatalog.ResolveMenuHandle(frame, window => { Assert.Equal(new IntPtr(expectedWindow), window); fallbacks++; return new IntPtr(83); });
        Assert.Equal(new IntPtr(83), actual); Assert.Equal(1, fallbacks);
    }

    [Fact]
    public void UnavailableApiMayFallBackButNeverRequiresAHostForEmptyResults()
    {
        var frame = Proxy<IFrame>(call => call.MethodName == "GetMenux64" ? throw new NotSupportedException() : 12L);
        Assert.Equal(new IntPtr(29), ExternalMenuCommandCatalog.ResolveMenuHandle(frame, _ => new IntPtr(29)));
        Assert.Empty(ExternalMenuCommandCatalog.Read((ISldWorks)null!));
    }

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
