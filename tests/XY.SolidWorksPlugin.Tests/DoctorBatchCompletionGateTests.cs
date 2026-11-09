using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class DoctorBatchCompletionGateTests
{
	[Fact]
	public void HiddenWindowAfterStartCompletesBatch()
	{
		DoctorBatchCompletionGate gate = new DoctorBatchCompletionGate();

		Assert.False(gate.Observe(windowVisible: true, startButtonFound: true, startButtonEnabled: true, startActionReleased: false));
		Assert.False(gate.Observe(windowVisible: true, startButtonFound: true, startButtonEnabled: true, startActionReleased: true));
		Assert.True(gate.Observe(windowVisible: false, startButtonFound: true, startButtonEnabled: true, startActionReleased: false));
	}

	[Fact]
	public void DisabledThenEnabledStartButtonCompletesBatch()
	{
		DoctorBatchCompletionGate gate = new DoctorBatchCompletionGate();

		Assert.False(gate.Observe(windowVisible: true, startButtonFound: true, startButtonEnabled: false, startActionReleased: false));
		Assert.True(gate.Observe(windowVisible: true, startButtonFound: true, startButtonEnabled: true, startActionReleased: false));
	}

	[Fact]
	public void MerelyOpeningWindowNeverCompletesBatch()
	{
		DoctorBatchCompletionGate gate = new DoctorBatchCompletionGate();

		Assert.False(gate.Observe(windowVisible: true, startButtonFound: true, startButtonEnabled: true, startActionReleased: false));
		Assert.False(gate.Observe(windowVisible: true, startButtonFound: true, startButtonEnabled: true, startActionReleased: false));
	}

	[Fact]
	public void ClosingWithoutStartingIsReported()
	{
		DoctorBatchCompletionGate gate = new DoctorBatchCompletionGate();

		Assert.False(gate.Observe(windowVisible: true, startButtonFound: true, startButtonEnabled: true, startActionReleased: false));
		Assert.False(gate.Observe(windowVisible: false, startButtonFound: true, startButtonEnabled: true, startActionReleased: false));
		Assert.True(gate.ClosedWithoutStarting);
	}
}
