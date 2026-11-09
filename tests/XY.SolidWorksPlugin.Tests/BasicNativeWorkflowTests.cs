using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class BasicNativeWorkflowTests
{
    [Theory]
    [InlineData("new-part")]
    [InlineData("new-assembly")]
    [InlineData("new-drawing")]
    [InlineData("rebuild")]
    [InlineData("force-rebuild")]
    [InlineData("normal-to")]
    [InlineData("zoom-fit")]
    [InlineData("view-front")]
    [InlineData("view-back")]
    [InlineData("view-left")]
    [InlineData("view-right")]
    [InlineData("view-top")]
    [InlineData("view-bottom")]
    [InlineData("view-isometric")]
    public void BuiltInActionsHaveExecutableNativeIdentities(string action)
    {
        Assert.True(BasicActions.TryGetNativeCommandId(action, out int id));
        Assert.True(NativeCommandReference.TryDecodeId(NativeCommandReference.Encode(id), out int decoded));
        Assert.Equal(id, decoded);
    }

    [Theory]
    [InlineData("explode-all")]
    [InlineData("settings")]
    [InlineData("watermark-settings")]
    [InlineData("process-recognition")]
    [InlineData("clear-selection")]
    public void PluginWorkflowsKeepTheirOwnExecution(string action)
        => Assert.False(BasicActions.TryGetNativeCommandId(action, out _));
}
