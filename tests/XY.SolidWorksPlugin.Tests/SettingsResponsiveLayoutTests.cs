using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

[Collection("Feature cleanup UI")]
public sealed class SettingsResponsiveLayoutTests
{
    [Fact]
    public void WideButtonsStillGrowVerticallyAndRecoverAfterFontChange() => Sta(window =>
    {
        using var normal = new Font("Microsoft YaHei UI", 9);
        using var large = new Font(normal.FontFamily, 24);
        using var button = new Button { Text = "保存", Font = normal, MinimumSize = new Size(500, 0) };
        Call(window, "FitButton", button, 4);
        Size original = button.MinimumSize;
        button.Font = large;
        Call(window, "FitButton", button, 4);
        Assert.True(button.MinimumSize.Height > original.Height);
        Assert.Equal(500, button.MinimumSize.Width);
        button.Font = normal;
        Call(window, "FitButton", button, 4);
        Assert.Equal(original, button.MinimumSize);
    });

    [Fact]
    public void NumericWidthAccountsForNegativeRangeAndAllDecimalPlaces() => Sta(window =>
    {
        using var number = new NumericUpDown { Minimum = -12345678m, Maximum = 1m, DecimalPlaces = 5, ThousandsSeparator = true };
        Call(window, "FitNumeric", number, 4);
        int textWidth = TextRenderer.MeasureText(number.Minimum.ToString("N5"), number.Font).Width;
        Assert.True(number.MinimumSize.Width >= textWidth + 32);
    });

    [Fact]
    public void DropDownTracksResizingEvenWhenItemsAndFontAreUnchanged() => Sta(window =>
    {
        using var combo = new ComboBox { Width = 100 };
        combo.Items.Add("短名称");
        Call(window, "FitComboBox", combo, 4);
        combo.Width = 450;
        Call(window, "FitComboBox", combo, 4);
        Assert.True(combo.DropDownWidth >= combo.Width);
    });

    [Fact]
    public void NarrowGridFitsCaptionsAndRepeatedLayoutDoesNotGrowDockedRows() => Sta(window =>
    {
        using var table = new TableLayoutPanel { Width = 200, Height = 100, ColumnCount = 2, RowCount = 2 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        using var font = new Font("Microsoft YaHei UI", 18);
        var label = new Label { Text = "轮盘直径 px", AutoSize = true, Font = font };
        table.Controls.Add(label, 0, 0);
        table.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 0);
        table.Controls.Add(new Button { Text = "隐藏内容" }, 0, 1);
        for (int i = 0; i < 2; i++) { Call(window, "FitGridColumns", (object)new Control[] { table }); table.PerformLayout(); }
        float height = table.RowStyles[0].Height;
        float width = table.ColumnStyles[0].Width;
        for (int i = 0; i < 20; i++) { Call(window, "FitGridColumns", (object)new Control[] { table }); table.PerformLayout(); }
        Assert.Equal(height, table.RowStyles[0].Height);
        Assert.Equal(width, table.ColumnStyles[0].Width);
        Assert.True(width >= TextRenderer.MeasureText(label.Text, font).Width + label.Margin.Horizontal);
        Assert.Equal(0, table.RowStyles[1].Height);
    });

    private static void Call(SettingsWindow window, string method, params object[] arguments) =>
        typeof(SettingsWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);

    private static void Sta(Action<SettingsWindow> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var config = ConfigStore.Deserialize("{}");
                ConfigStore.Normalize(config);
                using var window = new SettingsWindow(() => config, _ => { });
                action(window);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "Settings layout did not complete.");
        if (failure != null) throw failure;
    }
}

