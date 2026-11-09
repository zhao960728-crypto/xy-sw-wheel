using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

// Adding and removing cells used to be possible only at the end of a ring.
// These cover editing at a chosen position and keeping the commands that fall
// out of the grid, which is what the CAD build already does.
public sealed class WheelCellPositionTests
{
    private const string SceneKey = "sw.blank";

    private static PluginConfig Config()
    {
        PluginConfig config = ConfigStore.Deserialize("{}");
        ConfigStore.Normalize(config);
        var ring = new RingConfig { Index = 0, Slots = 4, DiameterPx = 520, Enabled = true };
        ring.Items.AddRange(new[]
        {
            new MenuItemConfig { Slot = 0, Label = "甲", Kind = ActionKinds.Hotkey, Value = "Ctrl+1" },
            new MenuItemConfig { Slot = 1, Label = "乙", Kind = ActionKinds.Hotkey, Value = "Ctrl+2" },
            new MenuItemConfig { Slot = 2, Label = "丙", Kind = ActionKinds.Hotkey, Value = "Ctrl+3" },
            new MenuItemConfig { Slot = 3, Label = "丁", Kind = ActionKinds.Hotkey, Value = "Ctrl+4" }
        });
        config.Menus[SceneKey] = new SceneMenuConfig { Key = SceneKey, MenuSizePx = 520, CenterRadiusPx = 40, Rings = new List<RingConfig> { ring } };
        return config;
    }

    private static void Sta(Action work)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { work(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        // A bare rethrow loses the frames inside the STA thread, which is where
        // the real failure lives.
        if (failure != null) throw new InvalidOperationException("STA 线程内异常: " + failure, failure);
    }

    private static object Field(object target, string name)
    {
        FieldInfo? field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field!.GetValue(target)!;
    }

    private static void SetField(object target, string name, object? value)
    {
        FieldInfo? field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static void Call(object target, string name, params object[] args)
    {
        MethodInfo? method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(target, args);
    }

    private static DataGridView Grid(SettingsWindow settings) => (DataGridView)Field(settings, "_grid");

    private static List<NumericUpDown> SlotInputs(SettingsWindow settings) => (List<NumericUpDown>)Field(settings, "_ringSlotInputs");

    private static void SelectCell(SettingsWindow settings, int ring, int slot)
    {
        SetField(settings, "_selectedCommandRing", ring);
        SetField(settings, "_selectedCommandSlot", slot);
    }

    private static string Label(DataGridView grid, int slot)
    {
        DataGridViewRow row = grid.Rows.Cast<DataGridViewRow>()
            .First(item => !item.IsNewRow && Convert.ToString(item.Cells["Slot"].Value) == slot.ToString());
        return Convert.ToString(row.Cells["Label"].Value)?.Trim() ?? "";
    }

    [Fact]
    public void InsertingBeforeTheSelectedCellShiftsTheLaterCommandsDown() => Sta(() =>
    {
        PluginConfig config = Config();
        using var settings = new SettingsWindow(() => config, _ => { });
        settings.Location = new Point(-32000, -32000);
        settings.Show();
        Application.DoEvents();
        DataGridView grid = Grid(settings);
        Assert.Equal(4, grid.Rows.Cast<DataGridViewRow>().Count(item => !item.IsNewRow));

        SelectCell(settings, 1, 2);
        Call(settings, "InsertSlotsAt", 1, 1, 1);
        Application.DoEvents();

        Assert.Equal(5m, SlotInputs(settings)[0].Value);
        Assert.Equal("甲", Label(grid, 1));
        Assert.Equal("", Label(grid, 2));
        Assert.Equal("乙", Label(grid, 3));
        Assert.Equal("丙", Label(grid, 4));
        Assert.Equal("丁", Label(grid, 5));
    });

    [Fact]
    public void InsertingAfterTheSelectedCellLeavesTheEarlierCommandsAlone() => Sta(() =>
    {
        PluginConfig config = Config();
        using var settings = new SettingsWindow(() => config, _ => { });
        settings.Location = new Point(-32000, -32000);
        settings.Show();
        Application.DoEvents();
        DataGridView grid = Grid(settings);

        SelectCell(settings, 1, 2);
        Call(settings, "InsertSlotsAt", 1, 2, 1);
        Application.DoEvents();

        Assert.Equal(5m, SlotInputs(settings)[0].Value);
        Assert.Equal("甲", Label(grid, 1));
        Assert.Equal("乙", Label(grid, 2));
        Assert.Equal("", Label(grid, 3));
        Assert.Equal("丙", Label(grid, 4));
    });

    [Fact]
    public void RemovingACellKeepsItsCommandInTheUnassignedList() => Sta(() =>
    {
        PluginConfig config = Config();
        using var settings = new SettingsWindow(() => config, _ => { });
        settings.Location = new Point(-32000, -32000);
        settings.Show();
        Application.DoEvents();
        DataGridView grid = Grid(settings);

        SelectCell(settings, 1, 2);
        Call(settings, "RemoveSlotsAt", 1, new[] { 1 });
        Application.DoEvents();

        Assert.Equal(3m, SlotInputs(settings)[0].Value);
        Assert.Equal("甲", Label(grid, 1));
        Assert.Equal("丙", Label(grid, 2));
        Assert.Equal("丁", Label(grid, 3));

        SceneMenuConfig scene = config.Menus[SceneKey];
        Assert.Single(scene.UnassignedItems);
        Assert.Equal("乙", scene.UnassignedItems[0].Label);
        Assert.Equal("Ctrl+2", scene.UnassignedItems[0].Value);
    });

    [Fact]
    public void AnUnassignedCommandCanBePutBackIntoTheSelectedCell() => Sta(() =>
    {
        PluginConfig config = Config();
        using var settings = new SettingsWindow(() => config, _ => { });
        settings.Location = new Point(-32000, -32000);
        settings.Show();
        Application.DoEvents();
        DataGridView grid = Grid(settings);

        SelectCell(settings, 1, 2);
        Call(settings, "RemoveSlotsAt", 1, new[] { 1 });
        Application.DoEvents();

        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        Call(settings, "RefreshUnassigned", combo);
        Assert.Single(combo.Items);
        combo.SelectedIndex = 0;

        SelectCell(settings, 1, 1);
        Call(settings, "RestoreUnassigned", combo);
        Application.DoEvents();

        SceneMenuConfig scene = config.Menus[SceneKey];
        Assert.Equal("甲", Assert.Single(scene.UnassignedItems).Label);
        Assert.Equal("乙", Label(grid, 1));
    });

    // The question this answers: press a cell in the preview, drag it onto
    // another one, and the two commands actually trade places - in the grid,
    // and in what gets saved.
    [Fact]
    public void DraggingACellOntoAnotherSwapsTheirCommands() => Sta(() =>
    {
        PluginConfig config = Config();
        using var settings = new SettingsWindow(() => config, _ => { });
        settings.Location = new Point(-32000, -32000);
        settings.Show();
        Application.DoEvents();
        DataGridView grid = Grid(settings);
        var preview = (RadialMenuControl)Field(settings, "_preview");
        // The preview lives on the appearance page: it only gets a real size
        // and a scene once that page has been laid out.
        Descendants(settings).OfType<Button>().First(control => control.Text == "外观").PerformClick();
        Application.DoEvents();
        settings.PerformLayout();
        preview.SetMenu(config, config.Menus[SceneKey]);
        Assert.True(preview.Width > 0 && preview.Height > 0, "预览控件未完成布局: " + preview.Size);

        Point first = FindSlot(preview, 0), third = FindSlot(preview, 2);
        Assert.Equal("甲", Label(grid, 1));
        Assert.Equal("丙", Label(grid, 3));

        Call(preview, "BeginCellDrag", first);
        Call(preview, "UpdateCellDrag", new MouseEventArgs(MouseButtons.Left, 1, third.X, third.Y, 0));
        Call(preview, "EndCellDrag", third);
        Application.DoEvents();

        Assert.Equal("丙", Label(grid, 1));
        Assert.Equal("甲", Label(grid, 3));
        Assert.Equal("Ctrl+3", Value(grid, 1));
        Assert.Equal("Ctrl+1", Value(grid, 3));

        // What the user actually keeps: the swap has to reach the saved scene.
        // ApplyRows has two overloads, so the parameterless one is picked by type.
        typeof(SettingsWindow).GetMethod("ApplyRows", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!
            .Invoke(settings, null);
        SceneMenuConfig scene = config.Menus[SceneKey];
        Assert.Equal("丙", scene.Rings[0].Items.First(item => item.Slot == 0).Label);
        Assert.Equal("甲", scene.Rings[0].Items.First(item => item.Slot == 2).Label);
    });

    private static string Snapshot(DataGridView grid) => string.Join(" | ", grid.Rows.Cast<DataGridViewRow>()
        .Where(item => !item.IsNewRow)
        .Select(item => $"{item.Cells["Slot"].Value}/{item.Cells["Label"].Value}/{item.Cells["FontSize"].Value}"));

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in Descendants(child)) yield return nested;
        }
    }

    private static Point FindSlot(RadialMenuControl preview, int slot)
    {
        for (int y = 4; y < preview.Height - 4; y += 4)
            for (int x = 4; x < preview.Width - 4; x += 4)
            {
                var hit = preview.HitCell(new Point(x, y));
                if (hit != null && hit.RingIndex == 0 && hit.Slot == slot) return new Point(x, y);
            }
        throw new InvalidOperationException("预览里找不到第 " + slot + " 格");
    }

    private static string Value(DataGridView grid, int slot)
    {
        DataGridViewRow row = grid.Rows.Cast<DataGridViewRow>()
            .First(item => !item.IsNewRow && Convert.ToString(item.Cells["Slot"].Value) == slot.ToString());
        return Convert.ToString(row.Cells["Value"].Value)?.Trim() ?? "";
    }

    // Dragging a cell onto another one is the gesture users reach for first.
    // It has to move the whole cell - command, caption and the look that goes
    // with it - and it has to survive the save.
    [Fact]
    public void DraggingACellOntoAnotherSwapsTheCommandAndItsLook() => Sta(() =>
    {
        PluginConfig config = Config();
        using var settings = new SettingsWindow(() => config, _ => { });
        settings.Location = new Point(-32000, -32000);
        settings.Show();
        Application.DoEvents();
        DataGridView grid = Grid(settings);

        DataGridViewRow first = Row(grid, 1);
        first.Cells["FontSize"].Value = "18";
        first.Cells["TextColor"].Value = "#FF0000";
        grid.EndEdit();
        Application.DoEvents();
        Assert.True(Convert.ToString(Row(grid, 1).Cells["FontSize"].Value) == "18", "设置字号未生效。网格=" + Snapshot(grid));
        object? firstTag = first.Tag, thirdTag = Row(grid, 3).Tag;
        Assert.NotSame(firstTag, thirdTag);

        Call(settings, "SwapWheelCells", null!, new RadialCellsSwappedEventArgs(0, 0, 0, 2));
        Application.DoEvents();

        Assert.Equal("丙", Label(grid, 1));
        Assert.Equal("甲", Label(grid, 3));
        Assert.Equal("Ctrl+3", Convert.ToString(Row(grid, 1).Cells["Value"].Value));
        Assert.Equal("Ctrl+1", Convert.ToString(Row(grid, 3).Cells["Value"].Value));
        // The look travels with the cell it was set on: the first cell took the
        // third cell's (empty) size, the third cell took the first cell's 18.
        Assert.Equal("", Convert.ToString(Row(grid, 1).Cells["FontSize"].Value));
        Assert.Equal("18", Convert.ToString(Row(grid, 3).Cells["FontSize"].Value));
        // The per-cell state travels as one object, so shape, size and icon
        // cannot end up attached to the wrong command.
        Assert.Same(thirdTag, Row(grid, 1).Tag);
        Assert.Same(firstTag, Row(grid, 3).Tag);

        // ApplyRows has an overload, so the parameterless one is picked by hand.
        typeof(SettingsWindow).GetMethod("ApplyRows", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!
            .Invoke(settings, null);
        List<MenuItemConfig> items = config.Menus[SceneKey].Rings[0].Items;
        Assert.Equal("丙", items.First(item => item.Slot == 0).Label);
        MenuItemConfig moved = items.First(item => item.Slot == 2);
        Assert.Equal("甲", moved.Label);
        Assert.Equal(18, moved.FontSizePx);
        Assert.Equal("#FF0000", moved.TextColor);
    });

    [Fact]
    public void DroppingACellBackOntoItselfChangesNothing() => Sta(() =>
    {
        PluginConfig config = Config();
        using var settings = new SettingsWindow(() => config, _ => { });
        settings.Location = new Point(-32000, -32000);
        settings.Show();
        Application.DoEvents();
        DataGridView grid = Grid(settings);

        Call(settings, "SwapWheelCells", null!, new RadialCellsSwappedEventArgs(0, 1, 0, 1));
        Application.DoEvents();

        Assert.Equal("甲", Label(grid, 1));
        Assert.Equal("乙", Label(grid, 2));
    });

    private static DataGridViewRow Row(DataGridView grid, int slot) =>
        grid.Rows.Cast<DataGridViewRow>()
            .First(item => !item.IsNewRow && Convert.ToString(item.Cells["Slot"].Value) == slot.ToString());

    [Fact]
    public void ARingNeverDropsBelowOneCell() => Sta(() =>
    {
        PluginConfig config = Config();
        config.Menus[SceneKey].Rings[0].Slots = 1;
        using var settings = new SettingsWindow(() => config, _ => { });
        settings.Location = new Point(-32000, -32000);
        settings.Show();
        Application.DoEvents();

        SelectCell(settings, 1, 1);
        var result = typeof(SettingsWindow)
            .GetMethod("RemoveSlotsAt", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(settings, new object[] { 1, new[] { 0 } });
        Assert.Equal(false, result);
        Assert.Equal(1m, SlotInputs(settings)[0].Value);
    });
}
