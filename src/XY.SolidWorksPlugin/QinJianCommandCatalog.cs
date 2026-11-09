using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;

namespace XY.SolidWorksPlugin;

internal static class QinJianCommandCatalog
{
	private const string QinJianAddinGuid = "{e6d2bc45-beb8-4fa5-ad9a-3ef1d6bc85eb}";
	private const string DatabasePassword = "FengJiaXi20120728";
	private static readonly AddinSource[] StandaloneSources =
	{
		new AddinSource("琴剑小菜鸟", "{A4DF7A48-81F3-4797-A497-01717CA15DB2}", "NewBird", "NewBird.SwAddin", "KaiBa230917"),
		new AddinSource("启翔铝型材", "{7550CB41-E35F-4B55-A538-276DA3969A6B}", "QinJianAlum", "QinJianAlum.SwAddin", "Alum250305")
	};

	internal static bool IsQinJianSeriesTableNameForTest(string tableName)
	{
		return IsQinJianSeriesTableName(tableName);
	}

	public static bool TryRead(out List<KeyboardShortcutRow> rows, out string message)
	{
		rows = new List<KeyboardShortcutRow>();
		if (!TryReadDefinitions(out List<QinJianCommandDefinition> definitions, out message))
		{
			return false;
		}
		rows = definitions
			.GroupBy(command => NormalizeFunctionKey(command.TabName, command.CommandName), StringComparer.OrdinalIgnoreCase)
			.Select(group => UniqueDefinition(group))
			.Where(command => command != null)
			.Select((QinJianCommandDefinition command) => new KeyboardShortcutRow
		{
			Category = FormatCategory(command.TabName),
			Command = command.CommandName,
			Hotkey = "琴剑功能",
			ActionKind = ActionKinds.QinJian,
			Value = QinJianActionValue.Format(command.TabName, command.CommandName)
		}).ToList();
		return rows.Count > 0;
	}

	internal static string FormatCategory(string tabName)
	{
		return "琴剑系列 > " + (tabName ?? "").Trim();
	}

	public static bool TryExecute(ISldWorks solidWorks, string actionValue, out string message)
	{
		return TryExecute(solidWorks, actionValue, out message, out _);
	}

	public static bool TryExecute(ISldWorks solidWorks, string actionValue, out string message, out bool callbackInvoked)
	{
		return TryExecute(solidWorks, actionValue, out message, out callbackInvoked, out _);
	}

	public static bool TryExecute(ISldWorks solidWorks, string actionValue, out string message, out bool callbackInvoked, out bool identityRejected)
	{
		message = "";
		callbackInvoked = false;
		identityRejected = false;
		if (solidWorks == null)
		{
			message = "SOLIDWORKS 未连接。";
			return false;
		}
		if (!QinJianActionValue.TryParse(actionValue, out string tabName, out string commandName))
		{
			identityRejected = true;
			message = "琴剑功能配置无效：" + actionValue;
			return false;
		}
		if (!TryReadDefinitions(out List<QinJianCommandDefinition> definitions, out message))
		{
			return false;
		}
		if (!TryResolveDefinition(definitions, tabName, commandName, out QinJianCommandDefinition definition, out message))
		{
			identityRejected = definitions.Any(command => FunctionNameEquals(command.TabName, tabName) && FunctionNameEquals(command.CommandName, commandName));
			return false;
		}
		identityRejected = true;
		try
		{
			Assembly assembly = Assembly.LoadFrom(definition.AssemblyPath);
			Type addinType = FindAddinType(assembly, definition.AddinTypeName, definition.CallbackFunction);
			if (addinType == null) throw new TypeLoadException("未找到琴剑插件回调宿主类型：" + definition.AddinTypeName);
			object adapter = Activator.CreateInstance(addinType);
			SetSolidWorksField(adapter, addinType, solidWorks);
			MethodInfo callback = FindCallback(addinType, definition.CallbackFunction);
			if (callback == null)
			{
				message = "琴剑回调不可用：" + definition.CallbackFunction;
				return false;
			}
			callbackInvoked = true;
			callback.Invoke(adapter, null);
			return true;
		}
		catch (TargetInvocationException ex)
		{
			Exception inner = ex.InnerException ?? ex;
			message = $"执行琴剑功能“{commandName}”失败：{inner.Message}";
			return false;
		}
		catch (Exception ex)
		{
			message = $"执行琴剑功能“{commandName}”失败：{ex.Message}";
			return false;
		}
	}

	private static bool TryReadDefinitions(out List<QinJianCommandDefinition> definitions, out string message)
	{
		definitions = new List<QinJianCommandDefinition>();
		message = "";
		try
		{
			string addinPath = FindQinJianAssemblyPath();
			string directory = Path.GetDirectoryName(addinPath) ?? "";
			string databasePath = Path.Combine(directory, "QinJianFile", "CommandMgr.db");
			if (!File.Exists(databasePath))
			{
				message = "没有找到琴剑命令数据库：" + databasePath;
				return false;
			}
			foreach (string record in ReadDatabaseRecordsIsolated(directory, databasePath))
			{
				string[] fields = record.Split(new[] { '\u001f' }, 3);
				if (fields.Length == 3)
				{
					string tableName = fields[0].Trim();
					string commandName = fields[1].Trim();
					string callback = fields[2].Trim();
					if (commandName.Length > 0 && callback.Length > 0)
					{
						definitions.Add(new QinJianCommandDefinition(tableName, commandName, callback, addinPath, "QinJianSwTool.SwAddin"));
					}
				}
			}
			foreach (AddinSource source in StandaloneSources)
			{
				TryReadStandaloneDefinitions(source, definitions);
			}
			if (definitions.Count == 0)
			{
				message = "琴剑命令数据库中没有可见功能。";
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			message = "读取琴剑命令数据库失败：" + ex.Message;
			return false;
		}
	}

	private static void TryReadStandaloneDefinitions(AddinSource source, List<QinJianCommandDefinition> definitions)
	{
		try
		{
			string assemblyPath = FindRegisteredAssemblyPath(source.AddinGuid, source.AssemblyName);
			string directory = Path.GetDirectoryName(assemblyPath) ?? "";
			string databasePath = Path.Combine(directory, "CommandMgr.db");
			if (!File.Exists(databasePath)) return;
			foreach (string record in ReadDatabaseRecordsIsolated(directory, databasePath, source.DatabasePassword, "CommandMgr"))
			{
				string[] fields = record.Split(new[] { '\u001f' }, 3);
				if (fields.Length == 3 && fields[1].Trim().Length > 0 && fields[2].Trim().Length > 0)
				{
					definitions.Add(new QinJianCommandDefinition(source.TabName, fields[1].Trim(), fields[2].Trim(), assemblyPath, source.AddinTypeName));
				}
			}
		}
		catch
		{
			// 独立插件未安装、版本不兼容或数据库不可读时，不影响其它琴剑系列读取。
		}
	}

	private static string[] ReadDatabaseRecordsIsolated(string qinJianDirectory, string databasePath, string password = DatabasePassword, string tableName = "")
	{
		string sqlitePath = Path.Combine(qinJianDirectory, "System.Data.SQLite.dll");
		if (!File.Exists(sqlitePath))
		{
			throw new FileNotFoundException("琴剑的 SQLite 组件不存在。", sqlitePath);
		}

		AppDomain domain = null;
		try
		{
			AppDomainSetup setup = new AppDomainSetup
			{
				ApplicationBase = Path.GetDirectoryName(typeof(QinJianCommandCatalog).Assembly.Location),
				ShadowCopyFiles = "false"
			};
			domain = AppDomain.CreateDomain("XY.QinJianDatabase." + Guid.NewGuid().ToString("N"), null, setup);
			domain.SetData(QinJianDatabaseReaderProxy.SqlitePathKey, sqlitePath);
			domain.SetData(QinJianDatabaseReaderProxy.DatabasePathKey, databasePath);
			domain.SetData(QinJianDatabaseReaderProxy.PasswordKey, password);
			domain.SetData(QinJianDatabaseReaderProxy.TableNameKey, tableName);
			domain.DoCallBack(QinJianDatabaseReaderProxy.ReadCurrentDomain);
			string error = Convert.ToString(domain.GetData(QinJianDatabaseReaderProxy.ErrorKey)) ?? "";
			if (error.Length > 0)
			{
				throw new InvalidOperationException(error);
			}
			return (string[])domain.GetData(QinJianDatabaseReaderProxy.ResultKey) ?? Array.Empty<string>();
		}
		finally
		{
			if (domain != null)
			{
				AppDomain.Unload(domain);
			}
		}
	}

	private static string FindQinJianAssemblyPath()
	{
		Assembly loaded = AppDomain.CurrentDomain.GetAssemblies()
			.FirstOrDefault((Assembly assembly) => assembly.GetName().Name.Equals("QinJianSwTool", StringComparison.OrdinalIgnoreCase));
		if (loaded != null && !string.IsNullOrWhiteSpace(loaded.Location) && File.Exists(loaded.Location))
		{
			return loaded.Location;
		}
		string codeBase = ReadRegisteredCodeBase(QinJianAddinGuid);
		if (codeBase.Length == 0)
		{
			throw new FileNotFoundException("琴剑SW工具没有注册到 Windows COM。", "QinJianSwTool.dll");
		}
		string path = NormalizeRegisteredPath(codeBase);
		if (!File.Exists(path))
		{
			throw new FileNotFoundException("琴剑SW工具 DLL 不存在。", path);
		}
		return path;
	}

	private static string FindRegisteredAssemblyPath(string addinGuid, string assemblyName)
	{
		Assembly loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault((Assembly assembly) => assembly.GetName().Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase));
		if (loaded != null && !string.IsNullOrWhiteSpace(loaded.Location) && File.Exists(loaded.Location)) return loaded.Location;
		string codeBase = ReadRegisteredCodeBase(addinGuid);
		if (codeBase.Length == 0) throw new FileNotFoundException("未注册琴剑系列插件：" + assemblyName, assemblyName + ".dll");
		string path = NormalizeRegisteredPath(codeBase);
		if (!File.Exists(path)) throw new FileNotFoundException("琴剑系列插件 DLL 不存在。", path);
		return path;
	}

	// SOLIDWORKS add-ins are commonly registered per-user or in the opposite
	// registry view from this process. Registry.ClassesRoot only exposes the
	// merged view for the current bitness, so probe both hives and both views
	// before declaring a QinJian assembly unavailable.
	private static string ReadRegisteredCodeBase(string addinGuid)
	{
		try
		{
			using RegistryKey key = Registry.ClassesRoot.OpenSubKey($"CLSID\\{addinGuid}\\InprocServer32");
			string value = ReadCodeBaseValue(key);
			if (value.Length > 0) return value;
		}
		catch (Exception ex) { Logger.Trace("Read merged QinJian COM registration skipped: " + ex.Message); }

		foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
		foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
		{
			try
			{
				using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
				string root = hive == RegistryHive.CurrentUser ? "Software\\Classes" : "SOFTWARE\\Classes";
				using RegistryKey key = baseKey.OpenSubKey($"{root}\\CLSID\\{addinGuid}\\InprocServer32");
				string value = ReadCodeBaseValue(key);
				if (value.Length > 0) return value;
			}
			catch (Exception ex) { Logger.Trace("Read QinJian COM registration skipped: " + hive + "/" + view + ": " + ex.Message); }
		}
		return "";
	}

	private static string ReadCodeBaseValue(RegistryKey key)
	{
		if (key == null) return "";
		string codeBase = Convert.ToString(key.GetValue("CodeBase"))?.Trim() ?? "";
		if (codeBase.Length > 0) return codeBase;
		return Convert.ToString(key.GetValue(null))?.Trim() ?? "";
	}

	private static string NormalizeRegisteredPath(string value)
	{
		string text = (value ?? "").Trim();
		try
		{
			if (text.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
			{
				return Path.GetFullPath(new Uri(text).LocalPath);
			}
		}
		catch (Exception ex) { Logger.Trace("Normalize QinJian COM CodeBase skipped: " + ex.Message); }
		return Path.GetFullPath(text);
	}

	private static Assembly LoadQinJianAssembly()
	{
		Assembly loaded = AppDomain.CurrentDomain.GetAssemblies()
			.FirstOrDefault((Assembly assembly) => assembly.GetName().Name.Equals("QinJianSwTool", StringComparison.OrdinalIgnoreCase));
		return loaded ?? Assembly.LoadFrom(FindQinJianAssemblyPath());
	}

	internal static Type FindAddinType(Assembly assembly, string preferredName, string callbackName)
	{
		if (assembly == null) return null;
		Type preferred = assembly.GetType(preferredName ?? "", throwOnError: false);
		// The database records its callback host. A similarly named method in a
		// different type is not the same command and must never be substituted.
		return preferred != null && !preferred.IsAbstract && FindCallback(preferred, callbackName) != null
			&& FindSolidWorksField(preferred) != null ? preferred : null;
	}

	private static MethodInfo FindCallback(Type addinType, string callbackName)
	{
		if (addinType == null || string.IsNullOrWhiteSpace(callbackName)) return null;
		MethodInfo[] methods = addinType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
			.Where(method => method.Name.Equals(callbackName, StringComparison.Ordinal)
				&& method.ReturnType == typeof(void) && !method.ContainsGenericParameters
				&& method.GetParameters().Length == 0).ToArray();
		return methods.Length == 1 ? methods[0] : null;
	}

	internal static void SetSolidWorksField(object adapter, Type addinType, ISldWorks solidWorks)
	{
		FieldInfo field = FindSolidWorksField(addinType)
			?? throw new MissingFieldException(addinType.FullName, "iSwApp");
		if (solidWorks == null) throw new InvalidOperationException("SOLIDWORKS COM 对象不可用。");
		// Always bind the instance that owns the wheel/recording. The process-wide
		// ROT entry can refer to another open SOLIDWORKS process.
		try
		{
			field.SetValue(adapter, solidWorks);
			return;
		}
		catch (ArgumentException ex)
		{
			Logger.Trace("QinJian supplied proxy needs interface conversion: " + ex.Message);
		}
		IntPtr unknown = Marshal.GetIUnknownForObject(solidWorks);
		try { field.SetValue(adapter, Marshal.GetTypedObjectForIUnknown(unknown, field.FieldType)); }
		finally { Marshal.Release(unknown); }
	}

	private static FieldInfo FindSolidWorksField(Type addinType)
	{
		if (addinType == null) return null;
		BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
		string[] names = { "iSwApp", "swApp", "ISwApp", "m_swApp", "solidWorks" };
		foreach (string name in names)
		{
			FieldInfo field = addinType.GetField(name, flags);
			if (field != null) return field;
		}
		return addinType.GetFields(flags).FirstOrDefault(field =>
			field.FieldType == typeof(ISldWorks) || field.Name.IndexOf("swApp", StringComparison.OrdinalIgnoreCase) >= 0);
	}

	private static bool IsQinJianSeriesTableName(string tableName)
	{
		return (tableName ?? "").Trim().StartsWith("琴剑", StringComparison.CurrentCultureIgnoreCase);
	}

	internal static bool FunctionNameEqualsForTest(string left, string right)
		=> FunctionNameEquals(left, right);

	private static bool FunctionNameEquals(string left, string right)
		=> NormalizeFunctionName(left).Equals(NormalizeFunctionName(right), StringComparison.CurrentCultureIgnoreCase);

	private static string NormalizeFunctionName(string value)
	{
		string text = (value ?? "").Trim().Replace("&", "");
		text = text.Replace("选项卡", "").Replace("标签页", "").Replace("已选中", "").Trim();
		while (text.EndsWith(".", StringComparison.Ordinal) || text.EndsWith("…", StringComparison.Ordinal) || text.EndsWith("。", StringComparison.Ordinal))
			text = text.Substring(0, text.Length - 1).Trim();
		return string.Join(" ", text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
	}

	private static string NormalizeFunctionKey(string tabName, string commandName)
		=> NormalizeFunctionName(tabName) + "\u001f" + NormalizeFunctionName(commandName);

	internal static bool TryResolveDefinition(IEnumerable<QinJianCommandDefinition> definitions, string tabName, string commandName,
		out QinJianCommandDefinition definition, out string message)
	{
		var matches = (definitions ?? Enumerable.Empty<QinJianCommandDefinition>())
			.Where(command => FunctionNameEquals(command.TabName, tabName) && FunctionNameEquals(command.CommandName, commandName)).ToList();
		definition = UniqueDefinition(matches);
		message = definition != null ? "" : matches.Count == 0
			? $"琴剑当前版本中没有找到“{tabName} > {commandName}”。请重新读取琴剑功能。"
			: $"琴剑功能“{tabName} > {commandName}”对应多个不同回调，已停止执行。请重新读取并确认插件版本。";
		return definition != null;
	}

	private static QinJianCommandDefinition UniqueDefinition(IEnumerable<QinJianCommandDefinition> definitions)
	{
		QinJianCommandDefinition[] unique = definitions.GroupBy(command => (command.AssemblyPath ?? "").Trim()
			+ "\u001f" + command.AddinTypeName + "\u001f" + command.CallbackFunction, StringComparer.Ordinal)
			.Select(group => group.First()).Take(2).ToArray();
		return unique.Length == 1 ? unique[0] : null;
	}

	internal sealed class QinJianCommandDefinition
	{
	public QinJianCommandDefinition(string tabName, string commandName, string callbackFunction, string assemblyPath, string addinTypeName)
		{
			TabName = tabName;
			CommandName = commandName;
			CallbackFunction = callbackFunction;
			AssemblyPath = assemblyPath;
			AddinTypeName = addinTypeName;
		}

		public string TabName { get; }
		public string CommandName { get; }
		public string CallbackFunction { get; }
		public string AssemblyPath { get; }
		public string AddinTypeName { get; }
	}

	private sealed class AddinSource
	{
		public AddinSource(string tabName, string addinGuid, string assemblyName, string addinTypeName, string databasePassword)
		{
			TabName = tabName; AddinGuid = addinGuid; AssemblyName = assemblyName; AddinTypeName = addinTypeName; DatabasePassword = databasePassword;
		}
		public string TabName { get; }
		public string AddinGuid { get; }
		public string AssemblyName { get; }
		public string AddinTypeName { get; }
		public string DatabasePassword { get; }
	}
}

[ComVisible(false)]
public static class QinJianDatabaseReaderProxy
{
	public const string SqlitePathKey = "XY.QinJian.SqlitePath";
	public const string DatabasePathKey = "XY.QinJian.DatabasePath";
	public const string PasswordKey = "XY.QinJian.Password";
	public const string ResultKey = "XY.QinJian.Result";
	public const string ErrorKey = "XY.QinJian.Error";
	public const string TableNameKey = "XY.QinJian.TableName";

	public static void ReadCurrentDomain()
	{
		AppDomain domain = AppDomain.CurrentDomain;
		try
		{
			string sqlitePath = Convert.ToString(domain.GetData(SqlitePathKey));
			string databasePath = Convert.ToString(domain.GetData(DatabasePathKey));
			string password = Convert.ToString(domain.GetData(PasswordKey));
			string requestedTableName = Convert.ToString(domain.GetData(TableNameKey));
			Assembly sqliteAssembly = Assembly.LoadFrom(sqlitePath);
			Type connectionType = sqliteAssembly.GetType("System.Data.SQLite.SQLiteConnection", throwOnError: true);
			string connectionString = $"Data Source={databasePath};Password={password};Read Only=True;";
			List<string> records = new List<string>();
			using DbConnection connection = (DbConnection)Activator.CreateInstance(connectionType, connectionString);
			connection.Open();
			List<string> tableNames = ReadTableNames(connection);
			if (!string.IsNullOrWhiteSpace(requestedTableName))
			{
				string requested = requestedTableName.Trim();
				tableNames = tableNames
					.Where(name => name.Equals(requested, StringComparison.OrdinalIgnoreCase))
					.ToList();
				if (tableNames.Count == 0) tableNames.Add(requested);
			}
			foreach (string tableName in tableNames)
			{
				try { ReadTableRecords(connection, tableName, records); }
				catch (Exception) { /* One legacy table must not hide all other functions. */ }
			}
			domain.SetData(ResultKey, records.ToArray());
			domain.SetData(ErrorKey, "");
		}
		catch (Exception ex)
		{
			domain.SetData(ResultKey, Array.Empty<string>());
			domain.SetData(ErrorKey, ex.ToString());
		}
	}

	private static List<string> ReadTableNames(DbConnection connection)
	{
		List<string> names = new List<string>();
		using DbCommand command = connection.CreateCommand();
		command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
		using DbDataReader reader = command.ExecuteReader();
		while (reader.Read())
		{
			string name = Convert.ToString(reader.GetValue(0))?.Trim() ?? "";
			if (name.Length > 0) names.Add(name);
		}
		return names;
	}

	private static void ReadTableRecords(DbConnection connection, string tableName, List<string> records)
	{
		if (connection == null || string.IsNullOrWhiteSpace(tableName)) return;
		HashSet<string> columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		using (DbCommand schema = connection.CreateCommand())
		{
			schema.CommandText = "PRAGMA table_info(" + QuoteIdentifier(tableName) + ")";
			using DbDataReader reader = schema.ExecuteReader();
			while (reader.Read() && reader.FieldCount > 1)
			{
				string column = Convert.ToString(reader.GetValue(1))?.Trim() ?? "";
				if (column.Length > 0) columns.Add(column);
			}
		}
		string nameColumn = FindColumn(columns, new[] { "Name", "CommandName", "ButtonName", "DisplayName", "Title", "Text" });
		string callbackColumn = FindColumn(columns, new[] { "CallbackFunction", "Callback", "CallBack", "FunctionName", "MethodName", "Method", "Function", "Action" });
		if (nameColumn.Length == 0 || callbackColumn.Length == 0) return;
		string visibleColumn = FindColumn(columns, new[] { "Visible", "IsVisible", "Enabled", "Enable" });
		string projection = QuoteIdentifier(nameColumn) + ", " + QuoteIdentifier(callbackColumn);
		if (visibleColumn.Length > 0) projection += ", " + QuoteIdentifier(visibleColumn);
		using DbCommand command = connection.CreateCommand();
		command.CommandText = "SELECT " + projection + " FROM " + QuoteIdentifier(tableName) + " ORDER BY rowid";
		using DbDataReader data = command.ExecuteReader();
		while (data.Read())
		{
			if (visibleColumn.Length > 0 && !IsVisibleValue(data.GetValue(2))) continue;
			string commandName = Convert.ToString(data.GetValue(0))?.Trim() ?? "";
			string callback = Convert.ToString(data.GetValue(1))?.Trim() ?? "";
			if (commandName.Length == 0 || callback.Length == 0) continue;
			records.Add(tableName.Trim() + '\u001f' + commandName + '\u001f' + callback);
		}
	}

	private static string FindColumn(HashSet<string> columns, IEnumerable<string> candidates)
	{
		foreach (string candidate in candidates ?? Array.Empty<string>())
			if (columns.Contains(candidate)) return columns.First(column => column.Equals(candidate, StringComparison.OrdinalIgnoreCase));
		return "";
	}

	private static bool IsVisibleValue(object value)
	{
		if (value == null || value == DBNull.Value) return true;
		string text = Convert.ToString(value)?.Trim() ?? "";
		if (text.Length == 0) return true;
		return text.Equals("ON", StringComparison.OrdinalIgnoreCase)
			|| text.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
			|| text.Equals("YES", StringComparison.OrdinalIgnoreCase)
			|| text.Equals("1", StringComparison.OrdinalIgnoreCase)
			|| text.Equals("显示", StringComparison.OrdinalIgnoreCase)
			|| text.Equals("可见", StringComparison.OrdinalIgnoreCase);
	}

	private static string QuoteIdentifier(string value)
		=> "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
}
