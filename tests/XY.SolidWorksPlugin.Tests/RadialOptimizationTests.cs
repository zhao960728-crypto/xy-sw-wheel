using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Xunit;
using Xunit.Abstractions;

namespace XY.SolidWorksPlugin.Tests;

[Collection("Feature cleanup UI")]
public sealed class RadialOptimizationTests
{
    private readonly ITestOutputHelper _output;
    public RadialOptimizationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void IconLookupBenchmarkPreservesExactCommandIdentity()
    {
        string directory = Path.Combine(Path.GetTempPath(), "xy-icon-lookup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "RadialIcons"));
        try
        {
            using (var icon = new Bitmap(24, 24))
            {
                using (var graphics = Graphics.FromImage(icon)) graphics.Clear(Color.Red);
                icon.Save(Path.Combine(directory, "RadialIcons", "red.png"));
            }
            string path = Path.Combine(directory, "radial-icons.json");
            var item = new MenuItemConfig { Slot = 0, Kind = ActionKinds.Hotkey, Value = "Ctrl+1|\"中文" };
            using var icons = new RadialIconOverrides(path);
            using var output = new Bitmap(24, 24);
            using var canvas = Graphics.FromImage(output);
            var bounds = new RectangleF(0, 0, 24, 24);
            const int iterations = 50000;
            double empty = Measure(() => { for (int i = 0; i < iterations; i++) icons.Draw(canvas, SceneKeys.Part, 0, item, bounds); });
            File.WriteAllText(path, new JavaScriptSerializer().Serialize(new RadialIconOverrides.Document
            {
                Icons = new List<RadialIconOverrides.Entry>
                {
                    new RadialIconOverrides.Entry { Scene = SceneKeys.Part, Ring = 0, Slot = 0, Kind = item.Kind, Value = item.Value, Image = "red.png" }
                }
            }));
            icons.Reload();
            double missing = Measure(() => { for (int i = 0; i < iterations; i++) icons.Draw(canvas, SceneKeys.Assembly, 0, item, bounds); });
            Assert.True(icons.Draw(canvas, SceneKeys.Part, 0, item, bounds));
            Assert.Equal(Color.Red.ToArgb(), output.GetPixel(12, 12).ToArgb());
            Assert.False(icons.Draw(canvas, SceneKeys.Assembly, 0, item, bounds));
            Assert.False(icons.Draw(canvas, SceneKeys.Part, 1, item, bounds));
            item.Slot = 1; Assert.False(icons.Draw(canvas, SceneKeys.Part, 0, item, bounds));
            item.Slot = 0; item.Kind = ActionKinds.NativeCommand; Assert.False(icons.Draw(canvas, SceneKeys.Part, 0, item, bounds));
            item.Kind = ActionKinds.Hotkey; item.Value = "ctrl+1|\"中文"; Assert.False(icons.Draw(canvas, SceneKeys.Part, 0, item, bounds));
            File.WriteAllText(path, "{\"Version\":1,\"Icons\":[]}"); icons.Reload();
            item.Value = "Ctrl+1|\"中文"; Assert.False(icons.Draw(canvas, SceneKeys.Part, 0, item, bounds));
            _output.WriteLine("Icon lookup, {0} iterations: empty={1:F3} ms; missing={2:F3} ms", iterations, empty, missing);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void StationaryCursorBenchmarkKeepsHoverAndCommandSelection() => Sta(() =>
    {
        var config = ConfigStore.Deserialize("{}"); ConfigStore.Normalize(config); config.RadialMenuEnabled = true;
        var scene = config.Menus[SceneKeys.Part]; scene.StyleId = "classic"; scene.MenuSizePx = 520;
        scene.Rings = new List<RingConfig> { new RingConfig { Index = 0, Enabled = true, Slots = 8, DiameterPx = 520,
            Items = new List<MenuItemConfig> { new MenuItemConfig { Slot = 0, Label = "测试命令", Kind = ActionKinds.Hotkey, Value = "Ctrl+1" } } } };
        MenuItemConfig? selected = null;
        using var window = new RadialMenuWindow(item => selected = item);
        var control = (RadialMenuControl)typeof(RadialMenuWindow).GetField("_control", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        window.ShowMenu(config, SceneKeys.Part, Screen.PrimaryScreen.WorkingArea.Location + new Size(350, 350), false, MouseButtons.None);
        Point cursor = window.PointToScreen(new Point(window.Width / 2, window.Height / 2 - 210));
        window.UpdateCursor(cursor);
        Assert.Equal("测试命令", control.AccessibleDescription);
        const int iterations = 10000;
        double elapsed = Measure(() => { for (int i = 0; i < iterations; i++) window.UpdateCursor(cursor); });
        Assert.Equal("测试命令", control.AccessibleDescription);
        scene.Rings[0].Items[0].Label = "改名后的命令";
        window.ReloadConfig(); window.UpdateCursor(cursor);
        Assert.Equal("改名后的命令", control.AccessibleDescription);
        window.CommitCursor(cursor);
        Assert.Same(scene.Rings[0].Items[0], selected);
        Assert.False(window.Visible);
        _output.WriteLine("Stationary cursor, {0} updates: {1:F3} ms", iterations, elapsed);
    });

    private static double Measure(Action action)
    {
        action(); // Keep process/JIT warmup outside the reported sample.
        var watch = Stopwatch.StartNew(); action(); watch.Stop(); return watch.Elapsed.TotalMilliseconds;
    }

    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(15000), "Wheel optimization check timed out.");
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
