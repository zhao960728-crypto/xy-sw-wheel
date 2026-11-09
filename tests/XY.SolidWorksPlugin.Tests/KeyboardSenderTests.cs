using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class KeyboardSenderTests
{
    [Theory]
    [InlineData("Semicolon", 0xBA)]
    [InlineData("Forward Slash", 0xBF)]
    [InlineData("Num *", 0x6A)]
    [InlineData("Num -", 0x6D)]
    [InlineData("Num 6", 0x66)]
    public void ResolveVirtualKeySupportsSolidWorksShortcutNames(string token, int expected)
    {
        Assert.Equal((byte)expected, KeyboardSender.ResolveVirtualKeyForTest(token));
    }
}
