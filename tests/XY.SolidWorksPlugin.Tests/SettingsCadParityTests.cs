using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

[Collection("Feature cleanup UI")]
public sealed class SettingsCadParityTests
{
    [Fact]
    public void RecommendationSurvivesRefreshSaveAndHiddenRingRestore() => Sta(() =>
    {
        var saved = NewConfig();
        var scene = saved.Menus[SceneKeys.Blank];
        scene.Rings = new List<RingConfig> { new RingConfig { Index = 0, Slots = 16 } };
        scene.Rings[0].Items.Add(Command(12, "overflow"));
        using var window = new SettingsWindow(() => ConfigStore.Clone(saved), c => saved = ConfigStore.Clone(c));
        Call(window, "ApplyRingPreset", new int[] { 8, 8, 16 });
        var draft = Field<PluginConfig>(window, "_config").Menus[SceneKeys.Blank];
        Assert.Equal(3, draft.Rings.Count(r => r.Enabled));
        Assert.Equal("overflow", Assert.Single(draft.UnassignedItems).Label);
        var grid = Field<DataGridView>(window, "_grid");
        grid.Rows[16].Cells["Value"].Value = "Ctrl+V";
        Call(window, "ApplyRingPreset", new int[] { 8, 8 });
        Call(window, "PersistEditorConfig");
        Assert.Equal(2, saved.Menus[SceneKeys.Blank].Rings.Count(r => r.Enabled));
        Assert.Equal("Ctrl+V", Assert.Single(saved.Menus[SceneKeys.Blank].Rings[2].Items).Value);
        Call(window, "ApplyRingPreset", new int[] { 8, 8, 16 });
        Assert.Equal("Ctrl+V", Field<DataGridView>(window, "_grid").Rows[16].Cells["Value"].Value);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShrinkingNumericControlsBeforeTimerTickKeepsDisplacedCommands(bool ringCount) => Sta(() =>
    {
        var saved = NewConfig();
        var scene = saved.Menus[SceneKeys.Blank];
        scene.Rings = new List<RingConfig> { new RingConfig { Index = 0, Slots = 8 }, new RingConfig { Index = 1, Slots = 8 } };
        scene.Rings[ringCount ? 1 : 0].Items.Add(Command(7, "keep"));
        using var window = new SettingsWindow(() => ConfigStore.Clone(saved), c => saved = ConfigStore.Clone(c));
        if (ringCount) Field<NumericUpDown>(window, "_ringCount").Value = 1;
        else Field<List<NumericUpDown>>(window, "_ringSlotInputs")[0].Value = 4;
        Call(window, "PersistEditorConfig");
        Call(window, "RefreshPreview");
        Call(window, "PersistEditorConfig");
        Assert.Equal("keep", Assert.Single(saved.Menus[SceneKeys.Blank].UnassignedItems).Label);
    });

    [Fact]
    public void RemovedComboRestoresAllStepsAndAppearanceAndKeepsReplacedBinding() => Sta(() =>
    {
        var saved = NewConfig();
        var scene = saved.Menus[SceneKeys.Blank];
        scene.Rings = new List<RingConfig> { new RingConfig { Index = 0, Slots = 8 } };
        var combo = Command(0, "combo");
        combo.Kind = ActionKinds.Combo;
        combo.ComboCommands.Add(new ComboCommandConfig { Kind = ActionKinds.Hotkey, Value = "Ctrl+S" });
        combo.ComboAdvanceEnabled = true; combo.ComboAdvanceKey = "Shift"; combo.StopOnStepError = false;
        combo.CellShape = "Heart"; combo.CellSizePercent = 75; combo.IconRatioPercent = 65;
        combo.IconData = "test-icon"; combo.ImageMode = "Image"; combo.FontFamily = "Arial";
        scene.Rings[0].Items.Add(combo);
        scene.Rings[0].Items.Add(Command(1, "replacement"));
        using var window = new SettingsWindow(() => ConfigStore.Clone(saved), c => saved = ConfigStore.Clone(c));
        Call(window, "RemoveSlotsAt", 1, new int[] { 0 });
        Call(window, "SelectCommandCell", 1, 1);
        using var choices = new ComboBox();
        Call(window, "RefreshUnassigned", choices); choices.SelectedIndex = 0;
        Assert.True((bool)Call(window, "RestoreUnassigned", choices)!);
        Call(window, "PersistEditorConfig");
        var restored = Assert.Single(saved.Menus[SceneKeys.Blank].Rings[0].Items);
        Assert.Equal("Ctrl+S", Assert.Single(restored.ComboCommands).Value);
        Assert.True(restored.ComboAdvanceEnabled); Assert.Equal("Shift", restored.ComboAdvanceKey);
        Assert.False(restored.StopOnStepError); Assert.Equal("Heart", restored.CellShape);
        Assert.Equal(75, restored.CellSizePercent); Assert.Equal(65, restored.IconRatioPercent);
        Assert.Equal("Image", restored.ImageMode); Assert.Equal("test-icon", restored.IconData);
        Assert.Equal("Arial", restored.ResolvedFontFamily());
        Assert.Equal("replacement", Assert.Single(saved.Menus[SceneKeys.Blank].UnassignedItems).Label);
    });

    [Fact]
    public void EmptyCellFormattingSurvivesReopenWithoutBecomingACommand() => Sta(() =>
    {
        var saved = NewConfig();
        saved.Menus[SceneKeys.Blank].Rings[0].Items.Clear();
        saved.Menus[SceneKeys.Blank].Rings[0].Items.Add(new MenuItemConfig { Slot = 0, CellShape = "Heart", FontBold = true });
        using (var window = new SettingsWindow(() => ConfigStore.Clone(saved), c => saved = ConfigStore.Clone(c)))
            Call(window, "PersistEditorConfig");
        using var reopened = new SettingsWindow(() => ConfigStore.Clone(saved), _ => { });
        var cell = Assert.Single(Field<PluginConfig>(reopened, "_config").Menus[SceneKeys.Blank].Rings[0].Items);
        Assert.Equal("Heart", cell.CellShape); Assert.True(cell.FontBold); Assert.Empty(cell.Value);
    });

    [Fact]
    public void NarrowWindowExposesPreviewAndPageChangeClearsEditingModes() => Sta(() =>
    {
        var config = NewConfig();
        using var window = new SettingsWindow(() => config, _ => { });
        window.Size = new Size(1040, 700); window.Show(); Application.DoEvents();
        Call(window, "RenderSection", "preview"); Application.DoEvents();
        var preview = Field<RadialMenuControl>(window, "_preview");
        Assert.True(preview.Visible); Assert.True(preview.Width > 300);
        preview.PracticeMode = true; preview.ResizeMode = true; preview.BatchSelectionMode = true;
        preview.BatchSelection.Add((0, 0));
        Call(window, "RenderSection", "commands");
        Assert.False(preview.PracticeMode); Assert.False(preview.ResizeMode);
        Assert.False(preview.BatchSelectionMode); Assert.Empty(preview.BatchSelection);
        Assert.True(Field<Panel>(window, "_editorPane").Visible);
        window.Close();
    });

    private static MenuItemConfig Command(int slot, string label) => new MenuItemConfig { Slot = slot, Label = label, Kind = ActionKinds.Hotkey, Value = "Ctrl+C" };
    private static PluginConfig NewConfig() { var c = ConfigStore.Deserialize("{}"); ConfigStore.Normalize(c); return c; }
    private static T Field<T>(object o, string n) => (T)o.GetType().GetField(n, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o);
    private static object? Call(object o, string n, params object[] args) => o.GetType().GetMethod(n, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o, args);
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) throw error;
    }
}
