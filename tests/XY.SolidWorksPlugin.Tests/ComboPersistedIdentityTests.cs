using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

[Collection("Command capture interaction")]
public sealed class ComboPersistedIdentityTests
{
    public static IEnumerable<object[]> PersistedCommands()
    {
        yield return new object[] { ActionKinds.NativeCommand, NativeCommandReference.Encode(1) };
        yield return new object[] { ActionKinds.ExternalPlugin, ExternalNamedCommandReference.Encode(1, "第三方插件 > 已绑定插件", "既有功能") };
        yield return new object[] { ActionKinds.ExternalPlugin, ExternalToolbarCommandReference.Encode(2, 41001, "既有工具栏功能") };
        yield return new object[] { ActionKinds.ExternalPlugin, ExternalPluginActionValue.Format("已绑定插件", "既有界面功能") };
        yield return new object[] { ActionKinds.QinJian, QinJianActionValue.Format("原命令组", "既有功能") };
        yield return new object[] { ActionKinds.JiaLiChuang, "读取零件属性Callback" };
    }

    [Theory]
    [MemberData(nameof(PersistedCommands))]
    public void ExistingEncodedOrCallbackCommandsCanBeSavedWithoutRereadingThePluginCatalog(string kind, string value) => Sta(() =>
    {
        var original = new ComboCommandConfig
        {
            Kind = kind, Value = value, Label = "既有绑定", WaitMode = ComboWaitModes.ManualConfirm,
            RequireAdvanceKey = true, AdvanceKey = "Shift", TimeoutMs = 2200, OnError = ComboErrorPolicies.Retry, RetryCount = 2
        };
        using var editor = new ComboCommandEditor("既有组合", new[] { original }, Array.Empty<KeyboardShortcutRow>());
        typeof(ComboCommandEditor).GetMethod("Accept", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null);
        Assert.Equal(DialogResult.OK, editor.DialogResult);
        var actual = Assert.Single(editor.Commands);
        Assert.Equal(kind, actual.Kind);
        Assert.Equal(value, actual.Value);
        Assert.Equal("Shift", actual.AdvanceKey);
        Assert.Equal(2200, actual.TimeoutMs);
        Assert.Equal(ComboWaitModes.ManualConfirm, actual.WaitMode);
        Assert.Equal(2, actual.RetryCount);
    });

    [Theory]
    [InlineData(ActionKinds.ExternalPlugin, "sw-external-named:9/plugin/function")]
    [InlineData(ActionKinds.ExternalPlugin, "sw-external-toolbar:1/not-a-number/function")]
    [InlineData(ActionKinds.ExternalPlugin, "external-plugin-ui:/missing-tab")]
    [InlineData(ActionKinds.ExternalPlugin, "未识别的任意功能名称")]
    [InlineData(ActionKinds.QinJian, "未识别的任意功能名称")]
    [InlineData(ActionKinds.JiaLiChuang, "回调:不是合法标识")]
    [InlineData(ActionKinds.Hotkey, "未识别的任意功能名称")]
    public void MalformedIdentitiesAndUnresolvedNamesStillRequireAValidCatalogMatch(string kind, string value) => Sta(() =>
    {
        Type slotType = typeof(ComboCommandEditor).GetNestedType("ComboSlotControl", BindingFlags.NonPublic)!;
        using var slot = (Control)Activator.CreateInstance(slotType, 1)!;
        slotType.GetMethod("SetCommand")!.Invoke(slot, new object[] { new ComboCommandConfig { Kind = kind, Value = value } });
        object?[] arguments = { Array.Empty<KeyboardShortcutRow>(), null };
        Assert.False((bool)slotType.GetMethod("TryResolveTypedFunction")!.Invoke(slot, arguments)!);
        Assert.False(string.IsNullOrWhiteSpace(Convert.ToString(arguments[1])));
    });

    [Fact]
    public void TypingAnUncataloguedCallbackDoesNotCreateANewPluginIdentity() => Sta(() =>
    {
        Type slotType = typeof(ComboCommandEditor).GetNestedType("ComboSlotControl", BindingFlags.NonPublic)!;
        using var slot = (Control)Activator.CreateInstance(slotType, 1)!;
        var kind = (ComboBox)slotType.GetField("_kind", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(slot)!;
        var value = (TextBox)slotType.GetField("_value", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(slot)!;
        kind.SelectedItem = "扩展功能";
        value.Text = "读取零件属性Callback";
        object?[] arguments = { Array.Empty<KeyboardShortcutRow>(), null };
        Assert.False((bool)slotType.GetMethod("TryResolveTypedFunction")!.Invoke(slot, arguments)!);
    });

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
