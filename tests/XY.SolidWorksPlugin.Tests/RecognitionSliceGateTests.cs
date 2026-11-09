using System;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class RecognitionSliceGateTests
{
	[Fact]
	public void TimerAndIdleShareBudgetButDoNotStarveLaterSlices()
	{
		var gate = new RecognitionSliceGate(250);
		var start = new DateTime(2026, 9, 29);
		for (int index = 0; index < 100; index++)
		{
			DateTime now = start.AddMilliseconds(index * 250);
			Assert.True(gate.TryEnter(now));
			gate.Exit();
			Assert.False(gate.TryEnter(now.AddMilliseconds(100)));
		}
	}

	[Fact]
	public void NestedMessagePumpCannotStartAnotherComSlice()
	{
		var gate = new RecognitionSliceGate(250);
		var now = DateTime.UtcNow;
		Assert.True(gate.TryEnter(now));
		Assert.False(gate.TryEnter(now.AddSeconds(5)));
		gate.Exit();
		Assert.True(gate.TryEnter(now.AddSeconds(5)));
	}

	[Fact]
	public void ClockCorrectionDoesNotStallQueue()
	{
		var gate = new RecognitionSliceGate(250);
		var now = DateTime.UtcNow;
		Assert.True(gate.TryEnter(now));
		gate.Exit();
		Assert.True(gate.TryEnter(now.AddMinutes(-1)));
	}
}
