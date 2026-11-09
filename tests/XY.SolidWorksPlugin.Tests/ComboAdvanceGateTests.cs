using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ComboAdvanceGateTests
{
	[Fact]
	public void RequiresFreshPressAfterArm()
	{
		ComboAdvanceGate gate = new ComboAdvanceGate();

		gate.Arm(true);
		Assert.False(gate.Observe(true, true));
		Assert.False(gate.Observe(false, true));
		Assert.True(gate.Observe(true, true));
	}

	[Fact]
	public void PressOutsideSolidWorksDoesNotTriggerWhenFocusReturnsWhileHeld()
	{
		ComboAdvanceGate gate = new ComboAdvanceGate();

		gate.Arm(false);
		Assert.False(gate.Observe(true, false));
		Assert.False(gate.Observe(true, true));
		Assert.False(gate.Observe(false, true));
		Assert.True(gate.Observe(true, true));
	}

	[Theory]
	[InlineData(null, "Ctrl")]
	[InlineData("Control", "Ctrl")]
	[InlineData("f8", "F8")]
	[InlineData("z", "Z")]
	[InlineData("unknown", "Ctrl")]
	public void NormalizesConfiguredKey(string? value, string expected)
	{
		Assert.Equal(expected, ComboAdvanceKeys.Normalize(value));
	}
}
