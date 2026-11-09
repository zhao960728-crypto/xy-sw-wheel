using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

[Collection("Feature cleanup UI")]
public sealed class DisabledWheelVisibilityTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void DisabledBackgroundPreparationCannotDisplayEmptyWheel() => Sta(() =>
    {
        var config = Config(); config.RadialMenuEnabled = false;
        using var window = new RadialMenuWindow(_ => throw new Exception("Disabled command executed"));
        window.Warmup(config, SceneKeys.Part);
        window.Show(); // A delayed/framework show must not expose a bare popup.
        Application.DoEvents();
        AssertHidden(window);
    });

    [Fact]
    public void DisablingAnOpenWheelHidesSurfaceAndCaptionsAndAllowsExplicitReenable() => Sta(() =>
    {
        var config = Config();
        using var window = new RadialMenuWindow(_ => { });
        Show(window, config);
        Assert.True(window.Visible); Assert.True(Foreground(window).Visible);
        config.RadialMenuEnabled = false;
        Show(window, config);
        Application.DoEvents();
        AssertHidden(window);
        window.Warmup(config, SceneKeys.Part); window.Show();
        AssertHidden(window);
        config.RadialMenuEnabled = true;
        Show(window, config);
        Assert.True(window.Visible); Assert.True(Foreground(window).Visible);
    });

    [Fact]
    public void DisabledWarmupAndTriggerDoNotStartAnOverlayThread()
    {
        var config = Config(); config.RadialMenuEnabled = false;
        using var host = new RadialOverlayHost(_ => throw new Exception("Disabled command executed"));
        host.Warmup(config, SceneKeys.Part);
        host.Show(config, SceneKeys.Part, new Point(350, 350), false, MouseButtons.None);
        Assert.Null(typeof(RadialOverlayHost).GetField("_thread", Fields)!.GetValue(host));
    }

    [Fact]
    public void QueuedDisplayCannotReshowAWheelDisabledBeforeDispatch()
    {
        using var host = new RadialOverlayHost(_ => throw new Exception("Disabled command executed"));
        var config = Config();
        var post = typeof(RadialOverlayHost).GetMethod("Post", Fields)!;
        var send = typeof(RadialOverlayHost).GetMethod("Send", Fields)!;
        host.Start();
        Assert.True(((ManualResetEventSlim)typeof(RadialOverlayHost).GetField("_ready", Fields)!.GetValue(host)!).Wait(3000));
        host.Show(config, SceneKeys.Part, new Point(350, 350), false, MouseButtons.None);
        bool shown = false;
        send.Invoke(host, new object[] { (Action<RadialMenuWindow>)(window => shown = window.Visible), 2000 });
        Assert.True(shown);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        post.Invoke(host, new object[] { (Action<RadialMenuWindow>)(_ => { entered.Set(); release.Wait(3000); }) });
        Assert.True(entered.Wait(2000));
        try
        {
            host.Show(config, SceneKeys.Part, new Point(350, 350), false, MouseButtons.None);
            host.Warmup(config, SceneKeys.Part);
            config.RadialMenuEnabled = false;
        }
        finally { release.Set(); }
        bool hidden = false;
        send.Invoke(host, new object[] { (Action<RadialMenuWindow>)(window => hidden = !window.Visible && !Foreground(window).Visible), 2000 });
        Assert.True(hidden, "A queued show left a disabled surface or caption layer visible.");
    }

    [Fact]
    public void OpeningAndSwitchingDisabledSettingsDoesNotOpenAStandaloneWheel() => Sta(() =>
    {
        var config = Config(); config.RadialMenuEnabled = false;
        using var settings = new SettingsWindow(() => ConfigStore.Clone(config), _ => { });
        settings.Show(); Application.DoEvents();
        foreach (string section in new[] { "features", "basic", "features", "appearance", "features" })
        {
            typeof(SettingsWindow).GetMethod("RenderSection", Fields)!.Invoke(settings, new object[] { section });
            Application.DoEvents();
            Assert.False(((CheckBox)typeof(SettingsWindow).GetField("_radialMenuEnabled", Fields)!.GetValue(settings)!).Checked);
            foreach (Form form in Application.OpenForms)
                if (form is RadialMenuWindow || form is RadialForegroundWindow) Assert.False(form.Visible);
        }
    });

    private static PluginConfig Config() { var config = ConfigStore.Deserialize("{}"); ConfigStore.Normalize(config); config.RadialMenuEnabled = true; return config; }
    private static Form Foreground(RadialMenuWindow window) => (Form)typeof(RadialMenuWindow).GetField("_foreground", Fields)!.GetValue(window)!;
    private static void AssertHidden(RadialMenuWindow window) { Assert.False(window.Visible); Assert.False(Foreground(window).Visible); }
    private static void Show(RadialMenuWindow window, PluginConfig config) => window.ShowMenu(config, SceneKeys.Part, new Point(350, 350), false, MouseButtons.None);
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(12000), "Disabled-wheel verification did not finish.");
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
