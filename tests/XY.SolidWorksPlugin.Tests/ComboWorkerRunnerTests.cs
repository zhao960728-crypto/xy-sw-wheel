using System;
using System.Threading;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ComboWorkerRunnerTests
{
	[Fact]
	public void PreCancelledRunDoesNotInvokeCommandExecutor()
	{
		using CancellationTokenSource cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		bool executed = false;

		ComboRunOutcome outcome = ComboWorkerRunner.Run(null, cancellation.Token, _ =>
		{
			executed = true;
			return ComboRunOutcome.Completed;
		}, _ => { });

		Assert.Equal(ComboRunOutcome.Cancelled, outcome);
		Assert.False(executed);
	}

	[Fact]
	public void CancellationThrownByExecutorIsContained()
	{
		ComboRunOutcome outcome = ComboWorkerRunner.Run(null, CancellationToken.None, _ =>
		{
			throw new OperationCanceledException();
		}, _ => { });

		Assert.Equal(ComboRunOutcome.Cancelled, outcome);
	}

	[Fact]
	public void UnexpectedExecutorFailureIsReportedAndContained()
	{
		Exception? reported = null;

		ComboRunOutcome outcome = ComboWorkerRunner.Run(null, CancellationToken.None, _ =>
		{
			throw new InvalidOperationException("test failure");
		}, ex => reported = ex);

		Assert.Equal(ComboRunOutcome.Failed, outcome);
		Assert.IsType<InvalidOperationException>(reported);
	}

	[Fact]
	public void ReportingFailureCannotEscapeWorker()
	{
		ComboRunOutcome outcome = ComboWorkerRunner.Run(null, CancellationToken.None, _ =>
		{
			throw new InvalidOperationException("test failure");
		}, _ => throw new Exception("reporting failure"));

		Assert.Equal(ComboRunOutcome.Failed, outcome);
	}
}
