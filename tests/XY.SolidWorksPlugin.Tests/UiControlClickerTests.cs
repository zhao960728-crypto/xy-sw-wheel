using System;
using System.Threading;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class UiControlClickerTests
{
	[Fact]
	public void ClickByTextStopsImmediatelyWhenCancelled()
	{
		using CancellationTokenSource source = new CancellationTokenSource();
		source.Cancel();

		Assert.Throws<OperationCanceledException>(() =>
			UiControlClicker.ClickByText(IntPtr.Zero, "确定", TimeSpan.FromSeconds(5), source.Token, out _));
	}
}
