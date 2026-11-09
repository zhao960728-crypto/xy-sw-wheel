using System;
using System.Threading;

namespace XY.SolidWorksPlugin;

internal static class ComboWorkerRunner
{
	public static ComboRunOutcome Run(
		Thread predecessor,
		CancellationToken token,
		Func<CancellationToken, ComboRunOutcome> execute,
		Action<Exception> reportUnexpectedFailure)
	{
		try
		{
			if (predecessor != null
				&& !ReferenceEquals(predecessor, Thread.CurrentThread)
				&& predecessor.IsAlive)
			{
				predecessor.Join();
			}
			if (token.IsCancellationRequested)
			{
				return ComboRunOutcome.Cancelled;
			}
			return execute(token);
		}
		catch (OperationCanceledException)
		{
			return ComboRunOutcome.Cancelled;
		}
		catch (Exception ex)
		{
			try
			{
				reportUnexpectedFailure?.Invoke(ex);
			}
			catch
			{
				// A reporting failure must never escape the SOLIDWORKS combo worker.
			}
			return ComboRunOutcome.Failed;
		}
	}
}
