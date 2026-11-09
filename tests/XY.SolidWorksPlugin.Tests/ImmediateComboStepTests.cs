using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ImmediateComboStepTests
{
    [Theory]
    [InlineData(161)] // verified installed API: swCommands_Front
    [InlineData(163)] // verified installed API: swCommands_Top
    [InlineData(167)] // verified installed API: swCommands_Isometric
    public void ConfirmedNativeViewsWithoutExplicitWaitSkipCompletionDiscovery(int id)
    {
        var command = new ComboCommandConfig { Kind = ActionKinds.NativeCommand, Value = NativeCommandReference.Encode(id), Label = "任意显示名称" };
        var step = new ComboExecutionStep(command, waitForManualCompletion: true);
        Assert.True(SwAddin.CanExecuteImmediateComboStep(step));
    }

    [Theory]
    [InlineData(ComboWaitModes.Delay)]
    [InlineData(ComboWaitModes.WindowClosed)]
    [InlineData(ComboWaitModes.PropertyPane)]
    [InlineData(ComboWaitModes.ManualConfirm)]
    public void ExplicitWaitModesRetainTheirCompletionPathEvenForViews(string waitMode)
    {
        var command = View(); command.WaitMode = waitMode; command.DelayMs = 250;
        var step = Assert.Single(ComboExecutionPlanner.Build(new[] { command }));
        Assert.False(SwAddin.CanExecuteImmediateComboStep(step));
        Assert.Equal(waitMode, step.WaitMode);
        Assert.Equal(250, step.DelayMs);
    }

    [Fact]
    public void ExplicitAdvanceKeyNeverTakesTheNoWaitShortcut()
    {
        var command = View(); command.RequireAdvanceKey = true;
        Assert.False(SwAddin.CanExecuteImmediateComboStep(new ComboExecutionStep(command, false)));
    }

    [Fact]
    public void SelectionCaptureAndRestoreRemainConfiguredForImmediateViews()
    {
        var command = View(); command.SaveSelectionBefore = command.RestoreSelectionAfter = true;
        var step = Assert.Single(ComboExecutionPlanner.Build(new[] { command }));
        Assert.True(SwAddin.CanExecuteImmediateComboStep(step));
        Assert.True(step.SaveSelectionBefore); Assert.True(step.RestoreSelectionAfter);
    }

    [Theory]
    [InlineData(ActionKinds.Hotkey, "F")]
    [InlineData(ActionKinds.UiClick, "前视")]
    [InlineData(ActionKinds.ExternalPlugin, "sw-external-named:1/Plugin/Front")]
    public void ViewLikeLabelsCannotPromoteOtherRoutesToImmediateIdentity(string kind, string value)
    {
        var command = new ComboCommandConfig { Kind = kind, Value = value, Label = "前视/Front" };
        Assert.False(SwAddin.CanExecuteImmediateComboStep(new ComboExecutionStep(command, false)));
        Assert.False(SwAddin.CanExecuteImmediateComboStep(new ComboExecutionStep(new ComboCommandConfig { Kind = ActionKinds.NativeCommand,
            Value = NativeCommandReference.Encode(9), Label = "前视" }, false)));
    }

    [Fact]
    public void PureDelayStillExecutesItsOwnDelayWithoutCompletionDiscovery()
    {
        var command = new ComboCommandConfig { Kind = ActionKinds.Delay, Value = "100" };
        Assert.True(SwAddin.CanExecuteImmediateComboStep(new ComboExecutionStep(command, false)));
        Assert.False(SwAddin.CanExecuteImmediateComboStep(null!));
    }

    private static ComboCommandConfig View() => new ComboCommandConfig { Kind = ActionKinds.NativeCommand,
        Value = NativeCommandReference.Encode(161), AutoConfirm = true };
}
