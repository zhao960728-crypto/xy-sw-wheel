using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

[Collection("Feature cleanup UI")]
public sealed class SettingsPersistenceTests
{
	[Fact]
	public void SceneSwitchesSurvivePageChangesAndSaveWithoutChangingBindings() => Sta(() =>
	{
		var current = NewConfig();
		string originalMenus = ConfigStore.Serialize(current);
		using (var window = new SettingsWindow(() => ConfigStore.Clone(current), value => current = ConfigStore.Clone(value)))
		{
			var switches = Field<System.Collections.Generic.Dictionary<string, CheckBox>>(window, "_sceneSwitches");
			Assert.Equal(5, switches.Count);
			switches[SceneKeys.Part].Checked = false;
			switches[SceneKeys.Sketch].Checked = true;
			var selector = Field<ComboBox>(window, "_scene");
			selector.SelectedIndex = 1; selector.SelectedIndex = 4; selector.SelectedIndex = 0;
			Assert.False(switches[SceneKeys.Part].Checked);
			Assert.True(switches[SceneKeys.Sketch].Checked);
			Assert.Equal(originalMenus, ConfigStore.Serialize(current));
			Call(window, "PersistEditorConfig");
		}
		using var reopened = new SettingsWindow(() => ConfigStore.Clone(current), _ => { });
		var savedSwitches = Field<System.Collections.Generic.Dictionary<string, CheckBox>>(reopened, "_sceneSwitches");
		Assert.False(savedSwitches[SceneKeys.Part].Checked);
		Assert.True(savedSwitches[SceneKeys.Sketch].Checked);
		Assert.True(current.SketchWheelEnabled);
	});

	[Fact]
	public void SceneSwitchGroupsFitAtMinimumWindowWidth() => Sta(() =>
	{
		var current = NewConfig();
		using var window = new SettingsWindow(() => ConfigStore.Clone(current), _ => { });
		window.Size = window.MinimumSize; window.Show(); Application.DoEvents();
		var switches = Field<System.Collections.Generic.Dictionary<string, CheckBox>>(window, "_sceneSwitches");
		var bar = Field<Control>(window, "_sceneBar");
		foreach (var entry in switches)
		{
			Control page = entry.Value.Parent!;
			Assert.True(bar.RectangleToScreen(bar.ClientRectangle).Contains(page.RectangleToScreen(page.ClientRectangle)), entry.Key + " page group is clipped");
			Assert.All(page.Controls.Cast<Control>(), control => Assert.True(page.ClientRectangle.Contains(control.Bounds), entry.Key + " control is clipped"));
		}
	});

	[Fact]
	public void FeatureSettingsSaveDoesNotChangeLegacyPageChoices() => Sta(() =>
	{
		var current = NewConfig();
		using var window = new SettingsWindow(() => ConfigStore.Clone(current), value => current = ConfigStore.Clone(value), mode: SettingsWindowMode.FeatureTools);
		Call(window, "ApplyEditorToConfig"); Call(window, "PersistEditorConfig");
		Assert.Null(current.Menus[SceneKeys.Sketch].Enabled);
		Assert.False(current.SketchWheelEnabled);
		Assert.Equal(SceneKeys.Part, SceneResolver.Resolve(current, 1, true));
	});

	[Fact]
	public void SavePreservesNewIndependentToolSettingsAndLocalFeatureChoices() => Sta(() =>
	{
		PluginConfig current = NewConfig();
		using var window = new SettingsWindow(() => ConfigStore.Clone(current), value => current = ConfigStore.Clone(value));

		current.PropertyWatermark.DisplayScalePercent = 145;
		Field<CheckBox>(window, "_propertyWatermarkEnabled").Checked = false;
		Field<CheckBox>(window, "_radialMenuEnabled").Checked = false;
		try
		{
			Call(window, "SaveConfig");

			Assert.Equal(145, current.PropertyWatermark.DisplayScalePercent);
			Assert.False(current.PropertyWatermark.Enabled);
			Assert.False(current.RadialMenuEnabled);
		}
		finally { foreach (Form notice in window.OwnedForms) notice.Close(); }
	});

	[Fact]
	public void ProcessRulesMergePerFieldAndRebaseAfterEachSave() => Sta(() =>
	{
		PluginConfig current = NewConfig();
		using var window = new SettingsWindow(() => ConfigStore.Clone(current), value => current = ConfigStore.Clone(value));
		current.ProcessRecognition.ProcessCategories = "外部类别=外部工序";
		current.ProcessRecognition.SummaryPropertyName = "外部汇总";
		Field<TextBox>(window, "_processSummaryProperty").Text = "本页汇总";
		Call(window, "PersistEditorConfig");
		Assert.Equal("外部类别=外部工序", current.ProcessRecognition.ProcessCategories);
		Assert.Equal("本页汇总", current.ProcessRecognition.SummaryPropertyName);
		Assert.Equal("外部类别=外部工序", Field<TextBox>(window, "_processCategories").Text);

		current.ProcessRecognition.SummaryPropertyName = "后续外部汇总";
		Call(window, "PersistEditorConfig");
		Assert.Equal("后续外部汇总", current.ProcessRecognition.SummaryPropertyName);
		Assert.Equal("后续外部汇总", Field<TextBox>(window, "_processSummaryProperty").Text);
	});

	[Fact]
	public void AutomaticSavePreservesSynchronizedBindingsAndLatestWatermark() => Sta(() =>
	{
		PluginConfig current = NewConfig();
		current.Menus[SceneKeys.Part].Rings[0].Items.Add(new MenuItemConfig
		{
			Slot = 0, Label = "复制", Kind = ActionKinds.Hotkey, Value = "Ctrl+C"
		});
		using var window = new SettingsWindow(() => ConfigStore.Clone(current), value => current = ConfigStore.Clone(value));
		PluginConfig draft = Field<PluginConfig>(window, "_config");
		Assert.Equal(1, ShortcutChangeDetector.Synchronize(draft, new[]
		{
			new KeyboardShortcutRow { Command = "复制", Hotkey = "Ctrl+Shift+C" }
		}));
		current.PropertyWatermark.DisplayScalePercent = 160;
		
		Call(window, "PersistEditorConfig");
		Assert.Equal("Ctrl+Shift+C", current.Menus[SceneKeys.Part].Rings[0].Items.Single().Value);
		Assert.Equal(160, current.PropertyWatermark.DisplayScalePercent);
		
	});

    [Fact]
    public void TextDialogAppliesCheckedFieldsToMultipleSelectedRows() => Sta(() =>
    {
        var current = NewConfig(); var ring = current.Menus[SceneKeys.Blank].Rings[0]; ring.Items.Clear();
        ring.Items.Add(new MenuItemConfig { Slot = 0, Label = "首格", Value = "Ctrl+C", FontSizePx = 15 });
        ring.Items.Add(new MenuItemConfig { Slot = 1, Label = "次格", Value = "Ctrl+V", FontSizePx = 23 });
        using var window = new SettingsWindow(() => ConfigStore.Clone(current), value => current = ConfigStore.Clone(value));
        var grid = Field<DataGridView>(window, "_grid"); grid.ClearSelection(); grid.Rows[0].Selected = true; grid.Rows[1].Selected = true;
        Exception? dialogError = null;
        using var timer = new System.Windows.Forms.Timer { Interval = 50 };
        timer.Tick += (_, __) => {
            var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Text == "文字设置与应用范围");
            if (dialog == null) return;
            timer.Stop();
            try {
                var body = dialog.Controls.OfType<TableLayoutPanel>().Single();
                ((ComboBox)body.GetControlFromPosition(1, 0)!).SelectedIndex = 1;
                for (int row = 1; row <= 5; row++) ((CheckBox)body.GetControlFromPosition(0, row)!).Checked = row == 3;
                ((ComboBox)body.GetControlFromPosition(1, 3)!).SelectedIndex = 2;
                dialog.DialogResult = DialogResult.OK;
            } catch (Exception ex) { dialogError = ex; dialog.DialogResult = DialogResult.Cancel; }
            dialog.Close();
        };
        timer.Start(); Call(window, "EditWheelTextRules");
        if (dialogError != null) throw dialogError;
        Call(window, "PersistEditorConfig");
        var saved = current.Menus[SceneKeys.Blank].Rings[0].Items;
        Assert.Equal(2, saved.Count); Assert.All(saved, item => Assert.True(item.FontBold));
        Assert.Equal(15, saved[0].FontSizePx); Assert.Equal(23, saved[1].FontSizePx);
        Assert.Equal("Ctrl+C", saved[0].Value); Assert.Equal("Ctrl+V", saved[1].Value);
    });

    [Fact]
    public void WheelAppearanceSurvivesPreviewSceneReloadAndPositionSwap() => Sta(() =>
    {
        var current = NewConfig();
        var scene = current.Menus[SceneKeys.Blank];
        scene.StyleId = "orbit"; scene.ContentMode = "Auto"; scene.IconColor = "Purple";
        scene.Rings[0].Items.Clear();
        scene.Rings[0].Items.Add(new MenuItemConfig { Slot = 0, Label = "组合名称", Kind = ActionKinds.Combo,
            Value = "组合", IconId = "gear", IconPath = "", ContentMode = "TextOnly", FontBold = true,
            TextEffect = "outline", FontName = "SimSun", FontSizePx = 21, TextColor = "#7038A0",
            ComboAdvanceEnabled = true, ComboAdvanceKey = "Ctrl", ComboCommands = new System.Collections.Generic.List<ComboCommandConfig> {
                new ComboCommandConfig { Kind = ActionKinds.Hotkey, Value = "Ctrl+C" } } });
        using var window = new SettingsWindow(() => ConfigStore.Clone(current), value => current = ConfigStore.Clone(value));
        Call(window, "RefreshPreview");
        Call(window, "LoadSceneRows");
        var grid = Field<DataGridView>(window, "_grid");
        // Exchange values exactly as a drag notification does, including complete row metadata.
        window.GetType().GetMethod("SwapWheelCells", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window,
            new object[] { window, new RadialCellsSwappedEventArgs(0, 0, 0, 1) });
        Call(window, "PersistEditorConfig");
        var saved = current.Menus[SceneKeys.Blank];
        var item = Assert.Single(saved.Rings[0].Items);
        Assert.Equal(1, item.Slot); Assert.Equal("gear", item.IconId); Assert.Equal("TextOnly", item.ContentMode);
        Assert.True(item.FontBold); Assert.Equal("outline", item.TextEffect); Assert.Equal(21, item.FontSizePx);
        Assert.Equal("#7038A0", item.TextColor); Assert.True(item.ComboAdvanceEnabled);
        Assert.Equal("Ctrl+C", Assert.Single(item.ComboCommands).Value);
        Assert.Equal("orbit", saved.StyleId); Assert.Equal("Auto", saved.ContentMode); Assert.Equal("Purple", saved.IconColor);
    });

	private static PluginConfig NewConfig()
	{
		PluginConfig config = ConfigStore.Deserialize("{}");
		ConfigStore.Normalize(config);
		return config;
	}

	private static T Field<T>(object target, string name) =>
		(T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

	private static void Call(object target, string name) =>
		target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);

	private static void Sta(Action action)
	{
		Exception? error = null;
		var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();
		if (error != null) throw error;
	}
}
