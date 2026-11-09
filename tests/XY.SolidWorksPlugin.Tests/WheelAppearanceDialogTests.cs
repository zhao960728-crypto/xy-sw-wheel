using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

[Collection("Feature cleanup UI")]
public sealed class WheelAppearanceDialogTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppearanceAppliesShapeOnlyWhenRequestedAndPreviewUsesDraft(bool changeShape) => Sta(() =>
    {
        using var window = Window();
        var scene = Field<PluginConfig>(window, "_config").Menus[SceneKeys.Blank];
        Edit(window, "EditWheelAppearance", "轮盘样式 · 配色 · 排版", dialog =>
        {
            var combos = Descendants(dialog).OfType<ComboBox>().ToList();
            var shape = combos.Single(c => c.Items.Contains("保留各格形状"));
            if (changeShape) shape.SelectedItem = "方形";
            combos.Single(c => c.Items.Contains("经典轮盘")).SelectedItem = "独立格轮盘";
            var wheel = Descendants(dialog).OfType<RadialMenuControl>().Single();
            var draft = Field<SceneMenuConfig>(wheel, "_scene");
            Assert.Equal("orbit", draft.StyleId);
            Assert.Equal("01", scene.StyleId);
            Assert.NotSame(scene, draft);
            Assert.Equal(changeShape ? "Square" : "Rounded", draft.Rings[0].Items[0].CellShape);
            dialog.DialogResult = DialogResult.OK;
        });
        Call(window, "RefreshPreview");
        Assert.Equal("orbit", scene.StyleId);
        Assert.Equal(changeShape ? "Square" : "Rounded", scene.Rings[0].Items[0].CellShape);
        Assert.Equal(changeShape ? "Square" : "Circle", scene.Rings[0].Items[1].CellShape);
        Assert.Equal("Ctrl+C", scene.Rings[0].Items[0].Value);
    });

    [Fact]
    public void CancelDoesNotApplyAppearanceOrLayoutChanges() => Sta(() =>
    {
        using var window = Window();
        var scene = Field<PluginConfig>(window, "_config").Menus[SceneKeys.Blank];
        string originalColor = scene.Appearance.StartColor;
        Edit(window, "EditWheelAppearance", "轮盘样式 · 配色 · 排版", dialog =>
        {
            Descendants(dialog).OfType<ComboBox>().Single(c => c.Items.Contains("午夜蓝")).SelectedItem = "午夜蓝";
            Descendants(dialog).OfType<ComboBox>().Single(c => c.Items.Contains("经典轮盘")).SelectedItem = "自由布局";
            dialog.DialogResult = DialogResult.Cancel;
        });
        Assert.Equal("01", scene.StyleId);
        Assert.Equal(originalColor, scene.Appearance.StartColor);
        Edit(window, "EditFreeLayout", "自由布局", dialog =>
        {
            Descendants(dialog).OfType<CheckBox>().Single().Checked = true;
            dialog.DialogResult = DialogResult.Cancel;
        });
        Assert.Equal("01", scene.StyleId);
        Assert.Null(scene.Rings[0].Items[0].FreeX);
    });

    [Fact]
    public void PresetResetsAutomaticTextAndManualChangesMarkCustom() => Sta(() =>
    {
        using var window = Window();
        Edit(window, "EditWheelAppearance", "轮盘样式 · 配色 · 排版", dialog =>
        {
            var preset = Descendants(dialog).OfType<ComboBox>().Single(c => c.Items.Contains("午夜蓝"));
            var autoText = Descendants(dialog).OfType<CheckBox>().Single(c => c.Text == "文字色跟随盘面");
            autoText.Checked = !autoText.Checked;
            Assert.Equal("自定义", preset.SelectedItem);
            preset.SelectedItem = "午夜蓝";
            var preview = Descendants(dialog).OfType<RadialMenuControl>().Single();
            var draft = Field<SceneMenuConfig>(preview, "_scene");
            Assert.Equal(string.IsNullOrWhiteSpace(draft.Appearance.TextColor), autoText.Checked);
            var glass = Descendants(dialog).OfType<CheckBox>().Single(c => c.Text == "玻璃高光");
            glass.Checked = !glass.Checked;
            Assert.Equal("自定义", preset.SelectedItem);
            dialog.DialogResult = DialogResult.OK;
        });
        Assert.Equal(WheelAppearance.PresetCustom, Field<PluginConfig>(window, "_config").Menus[SceneKeys.Blank].Appearance.PresetId);
    });

    private static SettingsWindow Window()
    {
        var config = ConfigStore.Deserialize("{}");
        ConfigStore.Normalize(config);
        var scene = config.Menus[SceneKeys.Blank];
        scene.StyleId = "01";
        scene.Rings[0].Items = new List<MenuItemConfig>
        {
            new MenuItemConfig { Slot = 0, Label = "复制", Value = "Ctrl+C", CellShape = "Rounded" },
            new MenuItemConfig { Slot = 1, Label = "粘贴", Value = "Ctrl+V", CellShape = "Circle" }
        };
        return new SettingsWindow(() => ConfigStore.Clone(config), _ => { });
    }

    private static void Edit(SettingsWindow owner, string method, string title, Action<Form> edit)
    {
        Exception? failure = null;
        using var timer = new System.Windows.Forms.Timer { Interval = 50 };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Text == title);
            if (dialog == null) return;
            timer.Stop();
            try { edit(dialog); }
            catch (Exception ex) { failure = ex; dialog.DialogResult = DialogResult.Cancel; }
            finally { dialog.Close(); }
        };
        timer.Start();
        Call(owner, method);
        if (failure != null) throw failure;
    }

    private static IEnumerable<Control> Descendants(Control root) =>
        root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value)!;
    private static void Call(object value, string name) => value.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(value, null);
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure != null) throw failure;
    }
}
