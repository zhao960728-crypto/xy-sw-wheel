using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using SolidWorks.Interop.sldworks;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class WheelContextMaintenanceTests
{
    [Fact]
    public void DocumentEventBeforeRegistrationDoesNotProbeOrConsumeTheContext()
    {
        var addin = new SwAddin();
        Set(addin, "_connected", true);
        Set(addin, "_swApp", Proxy<SldWorks>(call => throw new InvalidOperationException("Pre-registration COM probe: " + call.MethodName)));
        Call(addin, "OnActiveDocumentChangedForWheelTabs");
        Assert.Equal(-1, Field("_wheelTabContextType").GetValue(addin));
        Assert.Equal(0, Field("_wheelTabMaintenanceAttemptsRemaining").GetValue(addin));
        Assert.Null(Field("_wheelTabMaintenanceTimer").GetValue(addin));
    }

    [Fact]
    public void BlankContextDoesNotInspectOrRepairACommandTab()
    {
        var addin = Ready(null);
        Set(addin, "_commandManager", Proxy<CommandManager>(call => throw new InvalidOperationException("Blank-page tab scan: " + call.MethodName)));
        Call(addin, "OnActiveDocumentChangedForWheelTabs");
        Assert.Equal(0, Field("_wheelTabContextType").GetValue(addin));
        Assert.Equal(0, Field("_wheelTabMaintenanceAttemptsRemaining").GetValue(addin));
        Assert.Null(Field("_wheelTabMaintenanceTimer").GetValue(addin));
    }

    [Fact]
    public void RepeatedNotificationDoesNotRestartAnExhaustedPendingContext()
    {
        var document = Proxy<IModelDoc2>(call => call.MethodName == "GetType" ? 1 : null);
        var addin = Ready(document);
        Set(addin, "_wheelTabContextDocument", document); Set(addin, "_wheelTabContextType", 1);
        Set(addin, "_commandManager", Proxy<CommandManager>(call => throw new InvalidOperationException("Repeated tab scan: " + call.MethodName)));
        Call(addin, "OnActiveDocumentChangedForWheelTabs"); Call(addin, "OnActiveDocumentChangedForWheelTabs");
        Assert.Equal(0, Field("_wheelTabMaintenanceAttemptsRemaining").GetValue(addin));
        Assert.Null(Field("_wheelTabMaintenanceTimer").GetValue(addin));
        Assert.Contains(1, Pending(addin)); // Exhaustion waits for an actual context transition.
    }

    [Fact]
    public void NoRegisteredPendingTypeDoesNotReadTheDocumentOrScanCustomizedTabs()
    {
        var addin = Ready(null); Pending(addin).Clear();
        Set(addin, "_swApp", Proxy<SldWorks>(call => throw new InvalidOperationException("No pending work must not read COM.")));
        Call(addin, "OnActiveDocumentChangedForWheelTabs");
        Assert.Equal(-1, Field("_wheelTabContextType").GetValue(addin));
        Assert.Equal(0, Field("_wheelTabMaintenanceAttemptsRemaining").GetValue(addin));
        Assert.Null(Field("_wheelTabMaintenanceTimer").GetValue(addin));
    }

    [Fact]
    public void AliasOnlyPendingCleanupNeverQueriesOrRebuildsTheCurrentCustomTab()
    {
        var document = Proxy<IModelDoc2>(call => call.MethodName == "GetType" ? 1 : null);
        var addin = Ready(document);
        var names = new List<string>();
        Set(addin, "_commandManager", Proxy<CommandManager>(call =>
        {
            Assert.Equal("GetCommandTab", call.MethodName);
            names.Add((string)call.Args[1]);
            return null; // The exact old alias is already absent.
        }));
        Call(addin, "OnActiveDocumentChangedForWheelTabs");
        Assert.Single(names); Assert.Equal("小阳轮盘", names[0]);
        Assert.Empty(Pending(addin));
        Assert.Null(Field("_wheelTabMaintenanceTimer").GetValue(addin));
    }

    [Fact]
    public void MigrationMarkerRemainsPendingWhileAnyRegisteredTypeIsDeferred()
    {
        var addin = Ready(null);
        Set(addin, "_wheelTabRepairCurrentSchema", true);
        const string pendingMarker = "must-not-write\0registry-while-pending";
        Set(addin, "_wheelTabMigrationKey", pendingMarker);
        Call(addin, "CompleteWheelTabMigrationIfReady");
        Assert.Equal(pendingMarker, Field("_wheelTabMigrationKey").GetValue(addin));
        Assert.Contains(1, Pending(addin));
    }

    [Fact]
    public void DisconnectResetReleasesContextAndDisarmsQueuedWork()
    {
        var addin = Ready(null);
        Set(addin, "_wheelTabMaintenanceAttemptsRemaining", 3); Set(addin, "_wheelTabContextObservationQueued", true);
        Call(addin, "ResetWheelTabMaintenance");
        Assert.False((bool)Field("_wheelTabMaintenanceReady").GetValue(addin)!);
        Assert.False((bool)Field("_wheelTabContextObservationQueued").GetValue(addin)!);
        Assert.Equal(0, Field("_wheelTabMaintenanceAttemptsRemaining").GetValue(addin));
        Assert.Empty((int[])Field("_wheelTabRuntimeIds").GetValue(addin)!);
        Assert.Null(Field("_wheelTabContextDocument").GetValue(addin));
        Assert.Empty(Pending(addin));
    }

    private static SwAddin Ready(IModelDoc2? document)
    {
        var addin = new SwAddin(); Set(addin, "_connected", true); Set(addin, "_wheelTabMaintenanceReady", true);
        Set(addin, "_wheelTabRuntimeIds", new[] { 101, 102, 103, 104, 105, 106, 107 });
        Pending(addin).Add(1);
        Set(addin, "_swApp", Proxy<SldWorks>(call => call.MethodName == "get_ActiveDoc" ? document : throw new InvalidOperationException(call.MethodName)));
        return addin;
    }
    private static FieldInfo Field(string name) => typeof(SwAddin).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static HashSet<int> Pending(SwAddin addin) => (HashSet<int>)Field("_pendingWheelTabMaintenanceTypes").GetValue(addin)!;
    private static void Set(object addin, string name, object value) => Field(name).SetValue(addin, value);
    private static void Call(object addin, string name) => typeof(SwAddin).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(addin, null);
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
