using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class DoctorPropertyTaskPaneCompletionGateTests
{
	[Fact]
	public void DoesNotCompleteBeforeUserRunsUpdate()
	{
		DoctorPropertyTaskPaneCompletionGate gate = new DoctorPropertyTaskPaneCompletionGate();

		Assert.False(gate.Observe(true, true, true, false, false, 10));
		Assert.False(gate.Observe(true, true, true, false, false, 20));
		Assert.False(gate.SawAction);
	}

	[Fact]
	public void CompletesAfterButtonDisabledAndEnabledAgain()
	{
		DoctorPropertyTaskPaneCompletionGate gate = new DoctorPropertyTaskPaneCompletionGate();

		Assert.False(gate.Observe(true, true, true, true, false, 10));
		Assert.False(gate.Observe(true, true, false, false, true, 11));
		Assert.True(gate.Observe(true, true, true, false, false, 12));
	}

	[Fact]
	public void CompletesAfterRunningCommandReturnsIdle()
	{
		DoctorPropertyTaskPaneCompletionGate gate = new DoctorPropertyTaskPaneCompletionGate();

		Assert.False(gate.Observe(true, true, true, true, true, 10));
		Assert.True(gate.Observe(true, true, true, false, false, 11));
	}

	[Fact]
	public void CompletesFromTwoHostIdleEventsWhenButtonNeverDisables()
	{
		DoctorPropertyTaskPaneCompletionGate gate = new DoctorPropertyTaskPaneCompletionGate();

		Assert.False(gate.Observe(true, true, true, true, false, 10));
		Assert.False(gate.Observe(true, true, true, false, false, 11));
		Assert.True(gate.Observe(true, true, true, false, false, 12));
	}

	[Fact]
	public void ClosingPaneBeforeUpdateIsNotCompletion()
	{
		DoctorPropertyTaskPaneCompletionGate gate = new DoctorPropertyTaskPaneCompletionGate();

		Assert.False(gate.Observe(true, true, true, false, false, 10));
		Assert.False(gate.Observe(false, false, false, false, false, 11));
		Assert.True(gate.ClosedWithoutAction);
	}

	[Fact]
	public void ImmediatePaneCloseAfterActionStillCompletes()
	{
		DoctorPropertyTaskPaneCompletionGate gate = new DoctorPropertyTaskPaneCompletionGate();

		Assert.False(gate.Observe(true, true, true, false, false, 10));
		Assert.True(gate.Observe(false, false, false, true, false, 11));
		Assert.True(gate.SawAction);
	}
}
