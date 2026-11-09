using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

[Collection("Feature cleanup UI")]
public sealed class WheelStyleCatalogTests
{
    [Theory]
    [InlineData("Circle", "01")]
    [InlineData("Ring", "orbit")]
    [InlineData("Free", "free")]
    [InlineData("List", "grid")]
    [InlineData("Grid", "grid")]
    [InlineData("Honeycomb", "honeycomb")]
    [InlineData(" classic ", "classic")]
    [InlineData("legacy-layout-23", "legacy-layout-23")]
    public void ReferenceAliasesHaveAnExplicitEquivalentAndUnknownIdsRemainRecoverable(string value, string expected)
        => Assert.Equal(expected, WheelLayoutStyles.NormalizeStyleId(value));

    [Fact]
    public void CatalogPreservesExistingInteractionsAndAddsOnlyTwoMissingReferenceGeometries()
    {
        string[] ids = WheelLayoutStyles.All.Select(style => style.Id).ToArray();
        Assert.Equal(new[] { "classic", "01", "11", "06", "orbit", "free", "grid", "honeycomb" }, ids);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("切换命令组", WheelLayoutStyles.Find("11").Description);
        Assert.Contains("返回上一级", WheelLayoutStyles.Find("06").Description);
        Assert.False(WheelLayoutStyles.Find("honeycomb").SupportsCellShapes);
        Assert.True(WheelLayoutStyles.Find("orbit").SupportsCellShapes);
    }

    [Theory]
    [InlineData("Ring")]
    [InlineData("grid")]
    [InlineData("Honeycomb")]
    public void RingRecommendationsPreserveDisplacedBindingsAndCustomAppearance(string style)
    {
        var scene = new SceneMenuConfig
        {
            StyleId = style, MenuSizePx = 520,
            Appearance = new WheelAppearance { PresetId = WheelAppearance.PresetCustom, StartColor = "#456789", TextColor = "" },
            Rings = new List<RingConfig> { new RingConfig { Index = 0, Slots = 32, Items = new List<MenuItemConfig>
            { new MenuItemConfig { Slot = 31, Label = "保留的命令", Kind = ActionKinds.Hotkey, Value = "Ctrl+C" } } } }
        };
        WheelRingPresets.Apply(scene, WheelRingPresets.ForStyle(style)[0]);
        Assert.Equal(2, scene.Rings.Count(ring => ring.Enabled));
        Assert.Equal("Ctrl+C", Assert.Single(scene.UnassignedItems).Value);
        Assert.Equal("#456789", scene.Appearance.StartColor);
        Assert.Equal("", scene.Appearance.TextColor);
        Assert.Equal(style, scene.StyleId);
    }

    [Fact]
    public void ChoosingDarkGlassExplicitlySurvivesSaveAndReload()
    {
        var config = ConfigStore.Deserialize("{}"); ConfigStore.Normalize(config);
        config.Menus[SceneKeys.Part].Appearance = WheelAppearance.FromPreset(WheelAppearance.PresetDarkGlass);
        var reloaded = ConfigStore.Deserialize(ConfigStore.Serialize(config)); ConfigStore.Normalize(reloaded);
        var actual = reloaded.Menus[SceneKeys.Part].Appearance;
        Assert.Equal(WheelAppearance.PresetDarkGlass, actual.PresetId);
        Assert.True(actual.ExplicitPreset);
        Assert.Equal("#2A3846", actual.StartColor);
        Assert.True(actual.Glass);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangingAnOldDarkGlassAngleOrFillIsPreserved(bool changeFill)
    {
        var config = ConfigStore.Deserialize("{}"); ConfigStore.Normalize(config);
        var appearance = WheelAppearance.DarkGlass();
        if (changeFill) appearance.FillMode = "Solid"; else appearance.GradientAngle = 135;
        config.Menus[SceneKeys.Part].Appearance = appearance;
        ConfigStore.Normalize(config);
        var actual = config.Menus[SceneKeys.Part].Appearance;
        Assert.Equal(WheelAppearance.PresetDarkGlass, actual.PresetId);
        Assert.Equal(changeFill ? "Solid" : "Gradient", actual.FillMode);
        Assert.Equal(changeFill ? 90 : 135, actual.GradientAngle);
    }

    [Fact]
    public void ACustomClassicPaletteWithAutomaticTextIsNotMistakenForAnOldDefault()
    {
        var config = ConfigStore.Deserialize("{}"); ConfigStore.Normalize(config);
        var appearance = WheelAppearance.Classic(); appearance.PresetId = WheelAppearance.PresetCustom; appearance.TextColor = "";
        config.Menus[SceneKeys.Part].Appearance = appearance;
        ConfigStore.Normalize(config);
        Assert.Equal(WheelAppearance.PresetCustom, config.Menus[SceneKeys.Part].Appearance.PresetId);
        Assert.Equal("", config.Menus[SceneKeys.Part].Appearance.TextColor);
    }

    [Fact]
    public void SolidSurfaceAutomaticTextIgnoresTheUnusedGradientEndColor()
    {
        var appearance = new WheelAppearance { FillMode = "Solid", StartColor = "#FFFFFF", EndColor = "#000000", TextColor = "" };
        Assert.False(appearance.IsDarkSurface());
        Assert.True(appearance.GetTextColor().R < 100);
        appearance.StartColor = "#000000"; appearance.EndColor = "#FFFFFF";
        Assert.True(appearance.IsDarkSurface());
        Assert.True(appearance.GetTextColor().R > 200);
    }

    [Fact]
    public void EditingOnlyThePaletteKeepsAnUnknownLegacyLayoutId() => Sta(() =>
    {
        using var settings = Settings("legacy-layout-23");
        var scene = Field<PluginConfig>(settings, "_config").Menus[SceneKeys.Blank];
        Edit(settings, dialog =>
        {
            var style = Descendants(dialog).OfType<ComboBox>().Single(box => box.Name == "WheelLayoutStyle");
            Assert.Contains("legacy-layout-23", Convert.ToString(style.SelectedItem));
            Assert.Contains("矩形布局", style.Items.Cast<string>());
            Assert.Contains("蜂窝布局", style.Items.Cast<string>());
            Descendants(dialog).OfType<ComboBox>().Single(box => box.Items.Contains("午夜蓝")).SelectedItem = "午夜蓝";
            dialog.DialogResult = DialogResult.OK;
        });
        Assert.Equal("legacy-layout-23", scene.StyleId);
        Assert.Equal(WheelAppearance.PresetMidnight, scene.Appearance.PresetId);
    });

    [Fact]
    public void SelectingHoneycombKeepsCommandsAndTheUsersCustomAppearance() => Sta(() =>
    {
        using var settings = Settings("01");
        var scene = Field<PluginConfig>(settings, "_config").Menus[SceneKeys.Blank];
        scene.Appearance = new WheelAppearance { PresetId = WheelAppearance.PresetCustom, StartColor = "#456789", EndColor = "#ABCDEF", TextColor = "" };
        Edit(settings, dialog =>
        {
            Descendants(dialog).OfType<ComboBox>().Single(box => box.Name == "WheelLayoutStyle").SelectedItem = "蜂窝布局";
            Assert.False(Descendants(dialog).OfType<ComboBox>().Single(box => box.Items.Contains("保留各格形状")).Enabled);
            var preview = Descendants(dialog).OfType<RadialMenuControl>().Single();
            var draft = Field<SceneMenuConfig>(preview, "_scene");
            Assert.Equal("honeycomb", draft.StyleId);
            Assert.Equal("#456789", draft.Appearance.StartColor);
            dialog.DialogResult = DialogResult.OK;
        });
        Assert.Equal("honeycomb", scene.StyleId);
        Assert.Equal("Ctrl+C", Assert.Single(scene.Rings[0].Items).Value);
        Assert.Equal("#456789", scene.Appearance.StartColor);
        Assert.Equal(WheelAppearance.PresetCustom, scene.Appearance.PresetId);
        Assert.Equal("", scene.Appearance.TextColor);
    });

    private static SettingsWindow Settings(string style)
    {
        var config = ConfigStore.Deserialize("{}"); ConfigStore.Normalize(config);
        var scene = config.Menus[SceneKeys.Blank]; scene.StyleId = style;
        scene.Rings[0].Items = new List<MenuItemConfig> { new MenuItemConfig { Slot = 0, Kind = ActionKinds.Hotkey, Value = "Ctrl+C", Label = "复制" } };
        return new SettingsWindow(() => ConfigStore.Clone(config), _ => { });
    }
    private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(control => new[] { control }.Concat(Descendants(control)));
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static void Edit(SettingsWindow owner, Action<Form> edit)
    {
        Exception? failure = null;
        using var timer = new System.Windows.Forms.Timer { Interval = 50 };
        timer.Tick += (_, __) =>
        {
            Form? dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.Text == "轮盘样式 · 配色 · 排版");
            if (dialog == null) return;
            timer.Stop();
            try { edit(dialog); } catch (Exception error) { failure = error; dialog.DialogResult = DialogResult.Cancel; }
            finally { dialog.Close(); }
        };
        timer.Start();
        typeof(SettingsWindow).GetMethod("EditWheelAppearance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, null);
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
