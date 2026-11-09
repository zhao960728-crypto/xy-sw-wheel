using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class WheelFluidityTests
{
    [Theory]
    [InlineData("classic")]
    [InlineData("01")]
    [InlineData("grid")]
    [InlineData("honeycomb")]
    [InlineData("orbit")]
    public void HoverRepaintReusesGeometryAndStillTracksEditedLayouts(string style) => Sta(() =>
    {
        var config = ConfigStore.Deserialize("{}"); ConfigStore.Normalize(config);
        var scene = config.Menus[SceneKeys.Part]; scene.StyleId = style;
        using var control = new RadialMenuControl(Color.Fuchsia) { Size = new Size(520, 520), DrawLabels = false };
        control.SetMenu(config, scene);
        using var bitmap = new Bitmap(520, 520);
        using var graphics = Graphics.FromImage(bitmap);
        using var args = new PaintEventArgs(graphics, control.ClientRectangle);
        var paint = typeof(RadialMenuControl).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!;
        control.UpdateCursor(new Point(260, 80)); paint.Invoke(control, new object[] { args });
        int prepared = control.StyleGeometryBuildCount;
        for (int frame = 0; frame < 80; frame++)
        {
            double angle = frame * Math.PI / 20;
            control.UpdateCursor(new Point(260 + (int)(180 * Math.Cos(angle)), 260 + (int)(180 * Math.Sin(angle))));
            paint.Invoke(control, new object[] { args });
        }
        Assert.Equal(prepared, control.StyleGeometryBuildCount);
        scene.Rings[0].Slots++;
        control.UpdateCursor(new Point(260, 80)); paint.Invoke(control, new object[] { args });
        Assert.True(control.StyleGeometryBuildCount > prepared);
    });

    [Theory]
    [InlineData("第三方插件 > 原插件 > 工具", "原插件 > 工具", true)]
    [InlineData("第三方插件 > 原插件 > 工具", "原插件 > 其他", false)]
    [InlineData("第三方插件 > 原插件", "原插件扩展", false)]
    [InlineData("第三方插件 > 原插件", "其他插件", false)]
    public void TargetedReaderRequiresTheSameCompletePath(string path, string title, bool expected)
        => Assert.Equal(expected, ExternalPluginCommandCatalog.MatchesCategory(path, "第三方插件 > " + title));

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(10000));
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
