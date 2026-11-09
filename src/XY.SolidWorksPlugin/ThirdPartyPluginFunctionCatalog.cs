using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace XY.SolidWorksPlugin;

/// <summary>
/// 只读取可从已安装插件的公开入口可靠识别出的功能；无法确认的插件一律跳过。
/// </summary>
internal static class ThirdPartyPluginFunctionCatalog
{
	private static readonly HashSet<string> ExcludedAddinGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"{8D97B869-0F3D-4F13-901A-0A63D9D13A12}",
		"{e6d2bc45-beb8-4fa5-ad9a-3ef1d6bc85eb}",
		"{A4DF7A48-81F3-4797-A497-01717CA15DB2}",
		"{7550CB41-E35F-4B55-A538-276DA3969A6B}",
		JiaLiChuangFunctionCatalog.AddinGuid
	};

	public static bool TryRead(out List<KeyboardShortcutRow> rows, out string message)
	{
		rows = new List<KeyboardShortcutRow>();
		int installed = 0;
		List<AddinRegistration> registrations = ReadRegisteredAddins();
		if (registrations.Count == 0)
		{
			message = "没有找到 SOLIDWORKS 已安装插件列表。";
			return false;
		}

		foreach (AddinRegistration registration in registrations)
		{
			string guid = registration.Guid;
			if (ExcludedAddinGuids.Contains(guid)) continue;
			installed++;
			try
			{
				string title = registration.Title;
				string assemblyPath = registration.AssemblyPath;
				if (string.IsNullOrWhiteSpace(title)) title = Path.GetFileNameWithoutExtension(assemblyPath);
				if (title.Length == 0 || !File.Exists(assemblyPath)) continue;
				foreach (string command in ReadPluginCommands(assemblyPath, guid))
				{
					rows.Add(new KeyboardShortcutRow
					{
						Category = "第三方插件 > " + title,
						Command = command,
						Hotkey = "插件功能",
						ActionKind = ActionKinds.UiClick,
						Value = command
					});
				}
			}
			catch
			{
				// 非标准注册、缺少依赖或无法反射的插件不影响其它插件读取。
			}
		}

		rows = rows.GroupBy(row => row.Category + "\u001f" + row.Command, StringComparer.CurrentCultureIgnoreCase)
			.Select(group => group.First())
			.OrderBy(row => row.Category, StringComparer.CurrentCultureIgnoreCase)
			.ThenBy(row => row.Command, StringComparer.CurrentCultureIgnoreCase)
			.ToList();
		message = rows.Count > 0 ? $"已从 {installed} 个已安装第三方插件中读取 {rows.Count} 个可识别功能。" : "已扫描已安装第三方插件，没有可可靠读取的公开中文功能。";
		return rows.Count > 0;
	}

	internal static IReadOnlyList<string> ReadChineseCallbacksForTest(Type addinType)
	{
		return ReadChineseCallbacks(addinType);
	}

	private static List<AddinRegistration> ReadRegisteredAddins()
	{
		var result = new Dictionary<string, AddinRegistration>(StringComparer.OrdinalIgnoreCase);
		foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
		foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
		{
			try
			{
				using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
				using RegistryKey addins = baseKey.OpenSubKey("SOFTWARE\\SolidWorks\\Addins");
				foreach (string guid in addins?.GetSubKeyNames() ?? Array.Empty<string>())
				{
					using RegistryKey addin = addins.OpenSubKey(guid);
					string title = Convert.ToString(addin?.GetValue("Description"))?.Trim() ?? "";
					if (title.Length == 0) title = Convert.ToString(addin?.GetValue("Title"))?.Trim() ?? "";
					string assemblyPath = FindAssemblyPath(guid);
					if (!result.TryGetValue(guid, out AddinRegistration existing)
						|| (existing.AssemblyPath.Length == 0 && assemblyPath.Length > 0)
						|| (existing.Title.Length == 0 && title.Length > 0))
					{
						// AddinRegistration takes the display title before the assembly
						// path. Supplying them in reverse order made every managed add-in
						// fail File.Exists(AssemblyPath), so callbacks/manifests vanished
						// from the external-function catalog.
						result[guid] = new AddinRegistration(guid,
							title.Length > 0 ? title : existing?.Title ?? "",
							assemblyPath.Length > 0 ? assemblyPath : existing?.AssemblyPath ?? "");
					}
				}
			}
			catch (Exception ex) { Logger.Trace("Read third-party add-in registry skipped: " + hive + "/" + view + ": " + ex.Message); }
		}
		return result.Values.ToList();
	}

	private static string FindAssemblyPath(string guid)
	{
		foreach (Func<RegistryKey> open in new Func<RegistryKey>[]
		{
			() => Registry.ClassesRoot.OpenSubKey("CLSID\\" + guid + "\\InprocServer32"),
			() => OpenClassServer(RegistryHive.LocalMachine, RegistryView.Registry64, guid),
			() => OpenClassServer(RegistryHive.LocalMachine, RegistryView.Registry32, guid),
			() => OpenClassServer(RegistryHive.CurrentUser, RegistryView.Registry64, guid),
			() => OpenClassServer(RegistryHive.CurrentUser, RegistryView.Registry32, guid)
		})
		{
			try
			{
				using RegistryKey server = open();
				string codeBase = Convert.ToString(server?.GetValue("CodeBase"))?.Trim() ?? "";
				if (codeBase.Length == 0) codeBase = Convert.ToString(server?.GetValue(null))?.Trim() ?? "";
				if (codeBase.Length == 0) continue;
				string candidate = codeBase;
				try { candidate = codeBase.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(codeBase).LocalPath : codeBase; }
				catch (UriFormatException) { continue; }
				// The default InprocServer32 value is often "mscoree.dll";
				// accepting it prevents the later registry views from yielding the
				// managed add-in's real CodeBase. Only return an actual assembly file.
				if (!Path.IsPathRooted(candidate)) candidate = Path.GetFullPath(candidate);
				if (File.Exists(candidate)) return candidate;
			}
			catch (Exception ex) { Logger.Trace("Read third-party add-in assembly path skipped: " + ex.Message); }
		}
		return "";
	}

	private static RegistryKey OpenClassServer(RegistryHive hive, RegistryView view, string guid)
	{
		RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
		string root = hive == RegistryHive.CurrentUser ? "Software\\Classes" : "SOFTWARE\\Classes";
		RegistryKey key = baseKey.OpenSubKey(root + "\\CLSID\\" + guid + "\\InprocServer32");
		baseKey.Dispose();
		return key;
	}

	private sealed class AddinRegistration
	{
		public AddinRegistration(string guid, string title, string assemblyPath)
		{
			Guid = guid; Title = title; AssemblyPath = assemblyPath;
		}
		public string Guid { get; }
		public string Title { get; }
		public string AssemblyPath { get; }
	}

	private static IReadOnlyList<string> ReadChineseCallbacks(string assemblyPath, string guid)
	{
		Assembly assembly = Assembly.LoadFrom(assemblyPath);
		using RegistryKey server = Registry.ClassesRoot.OpenSubKey("CLSID\\" + guid + "\\InprocServer32");
		string typeName = Convert.ToString(server?.GetValue("Class"))?.Trim() ?? "";
		Type addinType = typeName.Length == 0 ? null : assembly.GetType(typeName, throwOnError: false);
		return ReadChineseCallbacks(addinType);
	}

	private static IReadOnlyList<string> ReadPluginCommands(string assemblyPath, string guid)
	{
		List<string> commands = new List<string>();
		string directory = Path.GetDirectoryName(assemblyPath) ?? "";
		// Vendor-distributed feature manifests remain readable even when its
		// toolbar is hidden or its implementation assembly is protected.
		string manifest = Path.Combine(directory, "tutorial-videos.default.json");
		if (File.Exists(manifest))
		{
			try { commands.AddRange(ReadFeatureManifest(File.ReadAllText(manifest))); }
			catch (Exception ex) { Logger.Trace("External feature manifest unreadable: " + ex.Message); }
		}
		string databasePath = Path.Combine(directory, "CommandMgr.db");
		if (File.Exists(databasePath))
		{
			try { commands.AddRange(ReadCommandManagerRecords(directory, databasePath)); } catch { }
		}
		try { commands.AddRange(ReadChineseCallbacks(assemblyPath, guid)); } catch { }
		return commands.Where(command => !string.IsNullOrWhiteSpace(command))
			.Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(command => command, StringComparer.CurrentCultureIgnoreCase).ToList();
	}

	internal static IReadOnlyList<string> ReadFeatureManifest(string json)
	{
		var root = new JavaScriptSerializer().DeserializeObject(json) as IDictionary<string, object>;
		if (root == null || !root.TryGetValue("videos", out object items) || !(items is IEnumerable entries)) return Array.Empty<string>();
		var names = new List<string>();
		foreach (var item in entries)
		{
			if (!(item is IDictionary<string, object> fields) || !fields.TryGetValue("featureId", out object id) || string.IsNullOrWhiteSpace(Convert.ToString(id))) continue;
			if (fields.TryGetValue("name", out object name) && !string.IsNullOrWhiteSpace(Convert.ToString(name))) names.Add(Convert.ToString(name).Trim());
		}
		// 'enabled' describes tutorial availability, not command availability.
		return names.Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();
	}

	private static IReadOnlyList<string> ReadCommandManagerRecords(string directory, string databasePath)
	{
		string sqlitePath = Path.Combine(directory, "System.Data.SQLite.dll");
		if (!File.Exists(sqlitePath)) return Array.Empty<string>();
		AppDomain domain = null;
		try
		{
			domain = AppDomain.CreateDomain("XY.ThirdPartyPluginDatabase." + Guid.NewGuid().ToString("N"));
			domain.SetData(QinJianDatabaseReaderProxy.SqlitePathKey, sqlitePath);
			domain.SetData(QinJianDatabaseReaderProxy.DatabasePathKey, databasePath);
			domain.SetData(QinJianDatabaseReaderProxy.PasswordKey, "");
			domain.SetData(QinJianDatabaseReaderProxy.TableNameKey, "CommandMgr");
			domain.DoCallBack(QinJianDatabaseReaderProxy.ReadCurrentDomain);
			if (!string.IsNullOrWhiteSpace(Convert.ToString(domain.GetData(QinJianDatabaseReaderProxy.ErrorKey)))) return Array.Empty<string>();
			return ((string[])domain.GetData(QinJianDatabaseReaderProxy.ResultKey) ?? Array.Empty<string>())
				.Select(record => record.Split(new[] { '\u001f' }, 3))
				.Where(fields => fields.Length == 3 && fields[1].Trim().Length > 0 && fields[2].Trim().Length > 0)
				.Select(fields => fields[1].Trim()).ToList();
		}
		finally
		{
			if (domain != null) AppDomain.Unload(domain);
		}
	}

	private static IReadOnlyList<string> ReadChineseCallbacks(Type addinType)
	{
		if (addinType == null) return Array.Empty<string>();
		try
		{
			return addinType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
				.Where(method => !method.IsStatic && method.ReturnType == typeof(void) && method.GetParameters().Length == 0 && ContainsChinese(method.Name))
				.Select(method => CleanDisplayName(method.Name))
				.Where(name => name.Length > 0)
				.Distinct(StringComparer.CurrentCultureIgnoreCase)
				.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
				.ToList();
		}
		catch
		{
			return Array.Empty<string>();
		}
	}

	private static string CleanDisplayName(string name)
	{
		string value = (name ?? "").Trim();
		foreach (string suffix in new[] { "Callback", "Show", "入口", "回调" })
		{
			if (value.EndsWith(suffix, StringComparison.Ordinal)) value = value.Substring(0, value.Length - suffix.Length).Trim();
		}
		return value;
	}

	private static bool ContainsChinese(string value)
	{
		return (value ?? "").Any(character => (character >= '\u3400' && character <= '\u9fff') || (character >= '\uf900' && character <= '\ufaff'));
	}
}
