using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class DoctorTaskPaneCompletionGateTests
{
	[Fact]
	public void BlankTaskPaneShellNeverCompletes()
	{
		DoctorTaskPaneCompletionGate gate = new DoctorTaskPaneCompletionGate();

		Assert.False(gate.Observe(false, true, false));
	}

	[Fact]
	public void FloatingSelectionWindowMustFinishHandOffFirst()
	{
		DoctorTaskPaneCompletionGate gate = new DoctorTaskPaneCompletionGate();

		Assert.False(gate.Observe(true, true, true));
		Assert.True(gate.Observe(false, true, true));
	}

	[Fact]
	public void DirectlyOpenedPopulatedTaskPaneCanComplete()
	{
		DoctorTaskPaneCompletionGate gate = new DoctorTaskPaneCompletionGate();

		Assert.True(gate.Observe(false, true, true));
	}

	[Fact]
	public void ContentWithoutTaskPaneShellNeverCompletes()
	{
		DoctorTaskPaneCompletionGate gate = new DoctorTaskPaneCompletionGate();

		Assert.False(gate.Observe(false, false, true));
	}
}
