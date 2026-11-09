using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ManualInteractionGateTests
{
	[Fact]
	public void CommandCloseWithoutInteractionDoesNotAdvance()
	{
		ManualInteractionGate gate = new ManualInteractionGate();

		Assert.False(gate.Observe(targetVisible: false, windowVisible: false));
		Assert.False(gate.HasObservedInteraction);
	}

	[Fact]
	public void NewWindowMustCloseBeforeAdvancing()
	{
		ManualInteractionGate gate = new ManualInteractionGate();

		Assert.False(gate.Observe(targetVisible: false, windowVisible: true));
		Assert.False(gate.Observe(targetVisible: false, windowVisible: true));
		Assert.True(gate.Observe(targetVisible: false, windowVisible: false));
	}

	[Fact]
	public void PropertyPanelMustDisappearBeforeAdvancing()
	{
		ManualInteractionGate gate = new ManualInteractionGate();

		Assert.False(gate.Observe(targetVisible: true, windowVisible: false));
		Assert.False(gate.Observe(targetVisible: true, windowVisible: false));
		Assert.True(gate.Observe(targetVisible: false, windowVisible: false));
	}

	[Fact]
	public void WindowLifecycleTakesPriorityOverPersistentMatchingText()
	{
		ManualInteractionGate gate = new ManualInteractionGate();

		Assert.False(gate.Observe(targetVisible: true, windowVisible: true));
		Assert.False(gate.Observe(targetVisible: true, windowVisible: true));
		Assert.True(gate.Observe(targetVisible: true, windowVisible: false));
	}

	[Fact]
	public void PrimaryActionCommitAdvancesEvenWhenToolWindowStaysOpen()
	{
		ManualInteractionGate gate = new ManualInteractionGate();

		Assert.False(gate.Observe(targetVisible: false, windowVisible: true));
		Assert.True(gate.Observe(targetVisible: false, windowVisible: true, actionCommitted: true));
	}
}
