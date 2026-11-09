using System;
using System.Collections;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ShortcutPaletteInteractionTests
{
    [Fact]
    public void TextAndImeInputRemainTextAndNativeNameShowsWithoutHotkey() => Sta(() =>
    {
        var native = Native("边线法兰", 359);
        KeyboardShortcutRow? executed = null;
        using var window = new ShortcutPaletteWindow(row => executed = row);
        window.ShowPalette(new[] { native }, new Point(50, 50));
        var input = Field<TextBox>(window, "_input");
        Assert.False(Route(window, Keys.A));
        Assert.False(Route(window, Keys.ProcessKey));
        Assert.False(Route(window, Keys.OemPeriod));
        Assert.False(Route(window, Keys.ControlKey));
        Assert.False(Route(window, Keys.Control | Keys.V));
        input.Text = "边线法兰";
        var list = Field<ListView>(window, "_list");
        Assert.Single(list.Items.Cast<ListViewItem>());
        Assert.True(window.Height > input.Height + 30);
        Assert.Contains("359", list.Items[0].SubItems[2].Text);
        Assert.Empty(Field<System.Collections.Generic.List<string>>(window, "_tokens"));
        Key(window, Keys.Enter);
        Assert.Same(native, executed);
    });

    [Fact]
    public void SameNamedSourcesRequireExplicitChoiceAndKeepExternalIdentity() => Sta(() =>
    {
        var first = External("插件 A", 41001); var second = External("插件 B", 41002);
        KeyboardShortcutRow? executed = null;
        using var window = new ShortcutPaletteWindow(row => executed = row);
        window.ShowPalette(new[] { first, second }, new Point(50, 50));
        Field<TextBox>(window, "_input").Text = "自动出图";
        var list = Field<ListView>(window, "_list");
        Assert.Equal(2, list.Items.Count);
        Assert.Contains("插件 A", list.Items[0].SubItems[2].Text);
        Assert.Contains("插件 B", list.Items[1].SubItems[2].Text);
        Key(window, Keys.Enter); Assert.Null(executed);
        Key(window, Keys.Down); Key(window, Keys.Down); Key(window, Keys.Enter);
        Assert.Same(second, executed);
        Assert.Equal(ExternalToolbarCommandReference.Encode(1, 41002, "自动出图"), executed!.Value);
    });

    [Fact]
    public void TypingAfterShortcutModeReturnsToNameInput() => Sta(() =>
    {
        using var window = new ShortcutPaletteWindow(_ => { });
        window.ShowPalette(new[] { new KeyboardShortcutRow { Command = "Copy", Hotkey = "Ctrl+C" } }, new Point(50, 50));
        Assert.True(Route(window, Keys.F2));
        Key(window, Keys.ControlKey);
        Assert.NotEmpty(Field<System.Collections.Generic.List<string>>(window, "_tokens"));
        Assert.True(Route(window, Keys.F2));
        Assert.False(Route(window, Keys.C));
        Assert.Empty(Field<System.Collections.Generic.List<string>>(window, "_tokens"));
        Field<TextBox>(window, "_input").Text = "Copy";
        Assert.False(Route(window, Keys.Control | Keys.A));
        Assert.Single(Field<ListView>(window, "_list").Items.Cast<ListViewItem>());
    });

    [Fact]
    public void DisplayOnlyNamesAndInvalidHotkeysNeverBecomeExecutableCandidates()
    {
        var rows = new[] {
            new KeyboardShortcutRow { Command = "边线法兰" },
            new KeyboardShortcutRow { Command = "边线法兰", Hotkey = "Ctrl+边线法兰" },
            new KeyboardShortcutRow { Command = "边线法兰", ActionKind = ActionKinds.NativeCommand, Value = "边线法兰" },
            new KeyboardShortcutRow { Command = "边线法兰", ActionKind = "unknown", Hotkey = "Ctrl+E", Value = "边线法兰" }
        };
        Assert.Empty(ShortcutPaletteMatcher.MatchByName(rows, "边线法兰"));
        Assert.All(rows, row => Assert.False(ShortcutPaletteMatcher.TryGetAction(row, out _, out _)));
    }

    [Fact]
    public void QualifiedSourceResolvesSameNameAndAliasRetainsIdentity()
    {
        var rows = new[] { External("插件 A", 41001), External("插件 B", 41002),
            new KeyboardShortcutRow { Command = "导出图纸", Category = "插件 A", ActionKind = ActionKinds.ExternalPlugin,
                Value = ExternalToolbarCommandReference.Encode(1, 41001, "自动出图") } };
        Assert.True(FunctionNameResolver.TryResolve(rows, "插件 B 自动出图", out var selected, out _));
        Assert.Equal(rows[1].Value, selected.Value);
        Assert.True(FunctionNameResolver.TryResolve(rows, "导出图纸", out selected, out _));
        Assert.Equal(rows[0].Value, selected.Value);
    }

    [Fact]
    public void ComboSlotEnterConnectsNativeFunctionAndBasicFunction() => Sta(() =>
    {
        using var editor = new ComboCommandEditor("组合", Array.Empty<ComboCommandConfig>(), new[] { Native("边线法兰", 359) });
        object slot = ((IList)Field<object>(editor, "_slots"))[0]!;
        var value = Field<TextBox>(slot, "_value");
        value.Text = "边线法兰"; PressEnter(value);
        var command = (ComboCommandConfig)slot.GetType().GetMethod("ReadCommand")!.Invoke(slot, null)!;
        Assert.Equal(ActionKinds.NativeCommand, command.Kind);
        Assert.Equal(NativeCommandReference.Encode(359), command.Value);
        value.Text = "工序识别"; PressEnter(value);
        command = (ComboCommandConfig)slot.GetType().GetMethod("ReadCommand")!.Invoke(slot, null)!;
        Assert.Equal(ActionKinds.Basic, command.Kind);
        Assert.Equal("process-recognition", command.Value);
    });

    [Fact]
    public void ComboNameAmbiguityShowsSourcesBeforeFillingSlot() => Sta(() =>
    {
        var rows = new[] { External("插件 A", 41001), External("插件 B", 41002) };
        using var editor = new ComboCommandEditor("组合", Array.Empty<ComboCommandConfig>(), rows);
        object slot = ((IList)Field<object>(editor, "_slots"))[0]!;
        var value = Field<TextBox>(slot, "_value"); value.Text = "自动出图"; PressEnter(value);
        Assert.Equal("自动出图", value.Text);
        Assert.Equal(2, Field<DataGridView>(editor, "_shortcutGrid").Rows.Count);
        Assert.Contains("来源", Field<Label>(editor, "_searchHint").Text);
        typeof(ComboCommandEditor).GetMethod("ApplyShortcutToActiveSlot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, new object[] { rows[1] });
        var command = (ComboCommandConfig)slot.GetType().GetMethod("ReadCommand")!.Invoke(slot, null)!;
        Assert.Equal(ActionKinds.ExternalPlugin, command.Kind);
        Assert.Equal(rows[1].Value, command.Value);
    });

    private static KeyboardShortcutRow Native(string name, int id) => new KeyboardShortcutRow
    { Category = "SOLIDWORKS > 钣金", Command = name, ActionKind = ActionKinds.NativeCommand, Value = NativeCommandReference.Encode(id) };
    private static KeyboardShortcutRow External(string source, int id) => new KeyboardShortcutRow
    { Category = source, Command = "自动出图", ActionKind = ActionKinds.ExternalPlugin, Value = ExternalToolbarCommandReference.Encode(1, id, "自动出图") };
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static bool Route(ShortcutPaletteWindow window, Keys key)
    {
        object[] arguments = { Message.Create(IntPtr.Zero, 0x100, IntPtr.Zero, IntPtr.Zero), key };
        return (bool)typeof(ShortcutPaletteWindow).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments)!;
    }
    private static void Key(ShortcutPaletteWindow window, Keys key) => typeof(ShortcutPaletteWindow).GetMethod("HandleKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { key });
    private static void PressEnter(Control control) => typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, new object[] { new KeyEventArgs(Keys.Enter) });
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(15000));
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
