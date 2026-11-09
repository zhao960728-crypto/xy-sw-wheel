using System;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class LoggerTests
{
	[Fact]
	public void ErrorRecordContainsOperationEnvironmentAndExceptionChain()
	{
		InvalidOperationException inner = new InvalidOperationException("inner failure");
		Exception error = new Exception("outer failure", inner);

		string record = Logger.BuildErrorRecord(
			"execute combo",
			error,
			new DateTimeOffset(2026, 7, 14, 12, 34, 56, TimeSpan.FromHours(8)));

		Assert.Contains("XY SOLIDWORKS 轮盘错误报告", record);
		Assert.Contains("Operation: execute combo", record);
		Assert.Contains("PluginVersion:", record);
		Assert.Contains("Process:", record);
		Assert.Contains("Thread:", record);
		Assert.Contains("outer failure", record);
		Assert.Contains("inner failure", record);
		Assert.DoesNotContain("LoadedThirdPartyAssemblies", record);
	}

	[Fact]
	public void ComErrorsAreMarkedAsPossibleAddinConflicts()
	{
		Assert.Equal("COM_INTEROP_OR_ADDIN_CONFLICT", Logger.Classify(new COMException("COM failed")));
	}

	[Fact]
	public void DependencyErrorsAreMarkedForRegistrationDiagnostics()
	{
		Assert.Equal("ADDIN_DEPENDENCY_OR_REGISTRATION", Logger.Classify(new FileNotFoundException("missing")));
	}

	[Fact]
	public void TimeoutInsideWrapperIsMarkedAsPossiblePluginConflict()
	{
		Exception wrapped = new InvalidOperationException("step failed", new TimeoutException("blocked"));

		Assert.Equal("COMMAND_TIMEOUT_OR_PLUGIN_CONFLICT", Logger.Classify(wrapped));
	}

	[Fact]
	public void FailureWithoutExceptionHasDedicatedCategory()
	{
		string record = Logger.BuildErrorRecord("shortcut conflict", null, DateTimeOffset.Now);

		Assert.Contains("Category: FUNCTION_FAILURE", record);
		Assert.Contains("Exception: none", record);
		Assert.DoesNotContain("LoadedThirdPartyAssemblies", record);
	}

	[Fact]
	public void ConflictErrorIncludesClearlyLabelledAssemblySnapshot()
	{
		string record = Logger.BuildErrorRecord("combo timeout", new TimeoutException("blocked"), DateTimeOffset.Now);

		Assert.Contains("LoadedThirdPartyAssemblies (diagnostic snapshot; entries below are not individual errors):", record);
	}

	[Fact]
	public void SqliteDependencyIsKeptInConflictSnapshotFilter()
	{
		Assert.False(Logger.IsFrameworkAssembly("System.Data.SQLite"));
	}

	[Fact]
	public void ErrorRecordSurvivesExceptionWhoseToStringFails()
	{
		string record = Logger.BuildErrorRecord("third-party failure", new BrokenToStringException(), DateTimeOffset.Now);

		Assert.Contains("third-party failure", record);
		Assert.Contains("exception text unavailable", record);
	}

	[Fact]
	public void ErrorRecordWriterCreatesUtf8LogFile()
	{
		string directory = Path.Combine(Path.GetTempPath(), "xy-error-log-test-" + Guid.NewGuid().ToString("N"));
		string path = Path.Combine(directory, "error.log");
		try
		{
			Logger.WriteErrorRecordToPath(path, "错误日志测试" + Environment.NewLine);

			Assert.True(File.Exists(path));
			Assert.Contains("错误日志测试", File.ReadAllText(path));
		}
		finally
		{
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, recursive: true);
			}
		}
	}

	[Fact]
	public void PrimaryErrorLogLivesBesidePluginAssembly()
	{
		string path = Logger.ResolvePrimaryErrorLogPath(@"E:\Plugins\XY\XY.SolidWorksPlugin.SwAddin.dll");

		Assert.Equal(@"E:\Plugins\XY\error.log", path);
	}

	private sealed class BrokenToStringException : Exception
	{
		public override string ToString()
		{
			throw new InvalidOperationException("broken exception formatter");
		}
	}
}
