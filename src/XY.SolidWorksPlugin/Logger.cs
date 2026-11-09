using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

internal static class Logger
{
	private const long MaxLogBytes = 2L * 1024L * 1024L;

	private static readonly object Sync = new object();

	private static readonly string LogPath = System.IO.Path.Combine(ConfigStore.BaseDirectory, "self-plugin.log");

	private static readonly string AppDataErrorPath = System.IO.Path.Combine(ConfigStore.BaseDirectory, "error.log");

	private static readonly string ErrorPath = ResolvePrimaryErrorLogPath(typeof(Logger).Assembly.Location);

	private static readonly bool TraceEnabled = string.Equals(Environment.GetEnvironmentVariable("XY_RADIAL_TRACE"), "1", StringComparison.OrdinalIgnoreCase);

	private static bool _exceptionHooksInstalled;

	private static string _hostContext = "SOLIDWORKS=unknown";

	internal static string ErrorLogPath => ErrorPath;

	public static void Initialize(string hostContext = null)
	{
		lock (Sync)
		{
			UpdateHostContextUnsafe(hostContext);
			if (_exceptionHooksInstalled)
			{
				return;
			}
			AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
			Application.ThreadException += OnThreadException;
			TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
			_exceptionHooksInstalled = true;
		}
	}

	public static void UpdateHostContext(string hostContext)
	{
		lock (Sync)
		{
			UpdateHostContextUnsafe(hostContext);
		}
	}

	public static void Shutdown()
	{
		lock (Sync)
		{
			if (!_exceptionHooksInstalled)
			{
				return;
			}
			AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
			Application.ThreadException -= OnThreadException;
			TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
			_exceptionHooksInstalled = false;
			_hostContext = "SOLIDWORKS=unknown";
		}
	}

	public static void Info(string message)
	{
		WriteRuntime("INFO", message);
	}

	public static void Error(string message)
	{
		Error(message, null);
	}

	public static void Error(string message, Exception ex)
	{
		string runtimeMessage = ex == null ? message : message + Environment.NewLine + SafeExceptionText(ex);
		WriteRuntime("ERROR", runtimeMessage);
		try
		{
			WriteErrorRecord(BuildErrorRecord(message, ex, DateTimeOffset.Now));
		}
		catch (Exception diagnosticError)
		{
			WriteErrorRecord(
				new string('=', 88) + Environment.NewLine
				+ "XY SOLIDWORKS 轮盘错误报告（诊断信息生成失败）" + Environment.NewLine
				+ $"TimeLocal: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}" + Environment.NewLine
				+ "Operation: " + Normalize(message) + Environment.NewLine
				+ "OriginalException: " + SafeExceptionText(ex) + Environment.NewLine
				+ "DiagnosticException: " + SafeExceptionText(diagnosticError) + Environment.NewLine);
		}
	}

	public static void Trace(string message)
	{
		if (TraceEnabled)
		{
			WriteRuntime("TRACE", message);
		}
	}

	internal static string BuildErrorRecord(string operation, Exception ex, DateTimeOffset timestamp)
	{
		StringBuilder builder = new StringBuilder(4096);
		builder.AppendLine(new string('=', 88));
		builder.AppendLine("XY SOLIDWORKS 轮盘错误报告");
		builder.AppendLine("TimeLocal: " + timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"));
		builder.AppendLine("TimeUtc: " + timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'"));
		builder.AppendLine("Operation: " + Normalize(operation));
		string category = Classify(ex);
		builder.AppendLine("Category: " + category);
		builder.AppendLine("PluginVersion: " + SafePluginVersion());
		builder.AppendLine("Host: " + SafeHostContext());
		builder.AppendLine("Process: " + SafeProcessDescription());
		builder.AppendLine($"Thread: ManagedId={Thread.CurrentThread.ManagedThreadId}; Name={Normalize(Thread.CurrentThread.Name)}; Apartment={SafeApartmentState()}; Background={Thread.CurrentThread.IsBackground}");
		builder.AppendLine($"Runtime: CLR={Environment.Version}; OS={Environment.OSVersion}; 64BitProcess={Environment.Is64BitProcess}");
		if (ex != null)
		{
			builder.AppendLine($"Exception: {ex.GetType().FullName}; HResult=0x{ex.HResult:X8}");
			builder.AppendLine("ExceptionDetail:");
			builder.AppendLine(SafeExceptionText(ex));
			AppendLoaderExceptions(builder, ex);
		}
		else
		{
			builder.AppendLine("Exception: none (function reported a failure without throwing)");
		}
		if (ShouldIncludeAssemblySnapshot(category, ex))
		{
			builder.AppendLine("LoadedThirdPartyAssemblies (diagnostic snapshot; entries below are not individual errors):");
			foreach (string assembly in LoadedThirdPartyAssemblies())
			{
				builder.AppendLine("  " + assembly);
			}
		}
		return builder.ToString();
	}

	internal static bool ShouldIncludeAssemblySnapshot(string category, Exception ex)
	{
		if (ex == null)
		{
			return false;
		}
		string value = category ?? "";
		return value.IndexOf("CONFLICT", StringComparison.OrdinalIgnoreCase) >= 0
			|| value.IndexOf("ADDIN", StringComparison.OrdinalIgnoreCase) >= 0
			|| value.IndexOf("DEPENDENCY", StringComparison.OrdinalIgnoreCase) >= 0
			|| value.IndexOf("COM_INTEROP", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	internal static string Classify(Exception ex)
	{
		for (Exception current = ex; current != null; current = current.InnerException)
		{
			if (current is COMException)
			{
				return "COM_INTEROP_OR_ADDIN_CONFLICT";
			}
			if (current is ReflectionTypeLoadException || current is TypeLoadException || current is MissingMethodException || current is MissingFieldException || current is BadImageFormatException)
			{
				return "ADDIN_OR_ASSEMBLY_COMPATIBILITY";
			}
			if (current is FileLoadException || current is FileNotFoundException || current is DllNotFoundException)
			{
				return "ADDIN_DEPENDENCY_OR_REGISTRATION";
			}
			if (current is UnauthorizedAccessException || current is SecurityException)
			{
				return "PERMISSION_OR_SECURITY";
			}
			if (current is TimeoutException)
			{
				return "COMMAND_TIMEOUT_OR_PLUGIN_CONFLICT";
			}
			if (current is InvalidDataException || current is FormatException)
			{
				return "CONFIG_OR_DATA";
			}
		}
		return ex == null ? "FUNCTION_FAILURE" : "UNEXPECTED_PLUGIN_ERROR";
	}

	private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
	{
		Exception ex = e.ExceptionObject as Exception ?? new Exception(Convert.ToString(e.ExceptionObject));
		Error("AppDomain unhandled exception. IsTerminating=" + e.IsTerminating, ex);
	}

	private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
	{
		Error("WinForms UI thread unhandled exception (may originate from SOLIDWORKS or another add-in)", e.Exception);
	}

	private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
	{
		Error("Unobserved background task exception", e.Exception);
	}

	private static void WriteRuntime(string level, string message)
	{
		try
		{
			lock (Sync)
			{
				WriteFile(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
			}
		}
		catch
		{
		}
	}

	private static void WriteErrorRecord(string record)
	{
		if (TryWriteErrorRecord(ErrorPath, record))
		{
			return;
		}
		if (!ErrorPath.Equals(AppDataErrorPath, StringComparison.OrdinalIgnoreCase)
			&& TryWriteErrorRecord(AppDataErrorPath, record))
		{
			return;
		}
		TryWriteErrorRecord(FallbackErrorLogPath(), record);
	}

	internal static void WriteErrorRecordToPath(string path, string record)
	{
		TryWriteErrorRecord(path, record);
	}

	private static bool TryWriteErrorRecord(string path, string record)
	{
		try
		{
			lock (Sync)
			{
				WriteFile(path, record);
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void WriteFile(string path, string content)
	{
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
		RotateIfNeeded(path);
		File.AppendAllText(path, content, Encoding.UTF8);
	}

	private static void RotateIfNeeded(string path)
	{
		if (!File.Exists(path) || new FileInfo(path).Length < MaxLogBytes)
		{
			return;
		}
		string previous = path + ".previous";
		if (File.Exists(previous))
		{
			File.Delete(previous);
		}
		File.Move(path, previous);
	}

	private static void UpdateHostContextUnsafe(string hostContext)
	{
		if (!string.IsNullOrWhiteSpace(hostContext))
		{
			_hostContext = hostContext.Trim();
		}
	}

	private static string SafeHostContext()
	{
		lock (Sync)
		{
			return _hostContext;
		}
	}

	private static string SafePluginVersion()
	{
		try
		{
			return typeof(Logger).Assembly.GetName().Version?.ToString() ?? "unknown";
		}
		catch
		{
			return "unknown";
		}
	}

	private static string SafeProcessDescription()
	{
		try
		{
			using Process process = Process.GetCurrentProcess();
			return $"{process.ProcessName}.exe; Id={process.Id}; Started={process.StartTime:yyyy-MM-dd HH:mm:ss}";
		}
		catch
		{
			return "unknown";
		}
	}

	private static string SafeApartmentState()
	{
		try
		{
			return Thread.CurrentThread.GetApartmentState().ToString();
		}
		catch
		{
			return "unknown";
		}
	}

	private static void AppendLoaderExceptions(StringBuilder builder, Exception ex)
	{
		ReflectionTypeLoadException loader = null;
		for (Exception current = ex; current != null && loader == null; current = current.InnerException)
		{
			loader = current as ReflectionTypeLoadException;
		}
		if (loader == null)
		{
			return;
		}
		builder.AppendLine("LoaderExceptions:");
		foreach (Exception item in loader.LoaderExceptions ?? Array.Empty<Exception>())
		{
			builder.AppendLine(SafeExceptionText(item));
		}
	}

	private static string[] LoadedThirdPartyAssemblies()
	{
		try
		{
			return AppDomain.CurrentDomain.GetAssemblies()
				.Select(DescribeAssembly)
				.Where(item => item != null)
				.OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
				.Take(80)
				.ToArray();
		}
		catch
		{
			return new[] { "unavailable" };
		}
	}

	private static string DescribeAssembly(Assembly assembly)
	{
		try
		{
			AssemblyName name = assembly.GetName();
			if (IsFrameworkAssembly(name.Name))
			{
				return null;
			}
			string location;
			try
			{
				location = string.IsNullOrWhiteSpace(assembly.Location) ? "dynamic/in-memory" : assembly.Location;
			}
			catch
			{
				location = "location unavailable";
			}
			return $"{name.Name}, Version={name.Version}; {location}";
		}
		catch
		{
			return null;
		}
	}

	internal static bool IsFrameworkAssembly(string name)
	{
		string value = name ?? "";
		if (value.Equals("System.Data.SQLite", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return value.Equals("mscorlib", StringComparison.OrdinalIgnoreCase)
			|| value.Equals("netstandard", StringComparison.OrdinalIgnoreCase)
			|| value.Equals("WindowsBase", StringComparison.OrdinalIgnoreCase)
			|| value.Equals("Accessibility", StringComparison.OrdinalIgnoreCase)
			|| value.Equals("stdole", StringComparison.OrdinalIgnoreCase)
			|| value.StartsWith("System", StringComparison.OrdinalIgnoreCase)
			|| value.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)
			|| value.StartsWith("UIAutomation", StringComparison.OrdinalIgnoreCase)
			|| value.StartsWith("Presentation", StringComparison.OrdinalIgnoreCase);
	}

	private static string Normalize(string value)
	{
		return string.IsNullOrWhiteSpace(value) ? "(empty)" : value.Trim();
	}

	private static string SafeExceptionText(Exception ex)
	{
		if (ex == null)
		{
			return "none";
		}
		try
		{
			return ex.ToString();
		}
		catch
		{
			try
			{
				return $"{ex.GetType().FullName}; HResult=0x{ex.HResult:X8}; exception text unavailable";
			}
			catch
			{
				return "exception text unavailable";
			}
		}
	}

	private static string FallbackErrorLogPath()
	{
		try
		{
			return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "XY SOLIDWORKS 轮盘-error.log");
		}
		catch
		{
			return System.IO.Path.Combine(Environment.CurrentDirectory, "XY SOLIDWORKS 轮盘-error.log");
		}
	}

	internal static string ResolvePrimaryErrorLogPath(string assemblyLocation)
	{
		try
		{
			string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(assemblyLocation ?? ""));
			return string.IsNullOrWhiteSpace(directory)
				? AppDataErrorPath
				: System.IO.Path.Combine(directory, "error.log");
		}
		catch
		{
			return AppDataErrorPath;
		}
	}
}
