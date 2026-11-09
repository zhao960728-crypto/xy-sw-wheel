using System;
using System.Drawing;
using System.Reflection;
using System.Reflection.Emit;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class RuntimeSchedulingPerformanceTests
{
    [Fact]
    public void FirstHeldButtonActivationUsesPreparedSceneWithoutColdIconDiscovery()
    {
        var addin = new SwAddin();
        Field("_runtimeConfig").SetValue(addin, new PluginConfig { RadialMenuEnabled = true });
        Field("_runtimeSceneKey").SetValue(addin, "sw.assembly");
        Call(addin, "OpenWheelNow", Point.Empty, true, MouseButtons.Right);
        Assert.False((bool)Field("_iconsInitialized").GetValue(addin)!);
        Assert.Equal("sw.assembly", Field("_runtimeSceneKey").GetValue(addin));
    }

    [Fact]
    public void AssemblyLoadOnlyMarksIconRevisionAndCanBeUnsubscribed()
    {
        var addin = new SwAddin();
        try
        {
            Call(addin, "SubscribeWheelIconAssemblyChanges");
            Call(addin, "SubscribeWheelIconAssemblyChanges");
            int before = (int)Field("_wheelIconAssemblyRevision").GetValue(addin)!;
            AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("WheelIconRevision_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
            Assert.True((int)Field("_wheelIconAssemblyRevision").GetValue(addin)! > before);
            Assert.False((bool)Field("_iconsInitialized").GetValue(addin)!);
            Call(addin, "UnsubscribeWheelIconAssemblyChanges");
            int stopped = (int)Field("_wheelIconAssemblyRevision").GetValue(addin)!;
            AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("WheelIconUnsubscribed_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
            Assert.Equal(stopped, Field("_wheelIconAssemblyRevision").GetValue(addin));
        }
        finally { Call(addin, "UnsubscribeWheelIconAssemblyChanges"); }
    }
    private static FieldInfo Field(string name) => typeof(SwAddin).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static void Call(object instance, string name, params object[] args) => typeof(SwAddin).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args);
}
