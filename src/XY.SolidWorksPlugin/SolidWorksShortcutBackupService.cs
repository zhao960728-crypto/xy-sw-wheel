using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;

namespace XY.SolidWorksPlugin;

internal sealed class ShortcutBackupArtifact
{
	public string RegistryFilePath { get; set; } = "";

	public string ReportFilePath { get; set; } = "";

	public string SolidWorksVersion { get; set; } = "";

	public int ShortcutCount { get; set; }
}

internal sealed class ShortcutImportResult
{
	public string ReportPath { get; set; } = "";

	public string ReportText { get; set; } = "";

	public bool Completed { get; set; }
}

internal sealed class ShortcutRegistryData
{
	public string VersionName { get; set; } = "";

	public int CustomizationVersion { get; set; }

	public List<ShortcutRegistryEntry> Entries { get; set; } = new List<ShortcutRegistryEntry>();
}

internal sealed class ShortcutRegistryEntry
{
	public string Plate { get; set; } = "tPlate0";

	public string Collection { get; set; } = "AddArray";

	public string LeafName { get; set; } = "";

	public int Flags { get; set; }

	public int Key { get; set; }

	public int CommandId { get; set; }

	public string Signature(bool includePlate)
	{
		string prefix = includePlate ? Plate + "|" : "";
		return prefix + Collection + "|" + Flags + "|" + Key + "|" + CommandId;
	}
}

internal sealed class ShortcutReportRow
{
	public string Category { get; set; } = "";

	public string Command { get; set; } = "";

	public string Hotkey { get; set; } = "";
}

internal static class SolidWorksShortcutBackupService
{
	private static readonly Regex VersionPathPattern = new Regex(@"\\(SOLIDWORKS\s+\d{4})\\", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex AcceleratorPathPattern = new Regex(@"\\Customization\\(tPlate\d+)\\Custom Accelerators\\(AddArray|SubArray)\\([^\\\]]+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly string[] PluginMarkers =
	{
		"琴剑", "QinJian", "博士", "Doctor", "Dr Sheetmetal", "Dr工具", "嘉立创", "Ican", "开拔", "盛兴", "朗乐", "启翔", "防护栏", "方通"
	};

	public static string BackupDirectory
	{
		get
		{
			string pluginDirectory = Path.GetDirectoryName(typeof(SolidWorksShortcutBackupService).Assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory;
			return Path.Combine(pluginDirectory, "快捷键备份");
		}
	}

	public static ShortcutBackupArtifact Export(ISldWorks solidWorks, IEnumerable<KeyboardShortcutRow> shortcutRows = null, string prefix = "SW快捷键")
	{
		ShortcutRegistryData data = ReadCurrent(solidWorks);
		if (data.Entries.Count == 0)
		{
			throw new InvalidOperationException("当前 SOLIDWORKS 没有读到自定义快捷键。");
		}

		Directory.CreateDirectory(BackupDirectory);
		string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
		string safePrefix = SanitizeFileName(prefix);
		string registryPath = UniquePath(Path.Combine(BackupDirectory, safePrefix + "_" + timestamp + ".sldreg"));
		string reportPath = Path.ChangeExtension(registryPath, ".txt");
		if (solidWorks == null)
		{
			throw new InvalidOperationException("没有连接到当前 SOLIDWORKS，无法导出快捷键。");
		}
		string stagingPath = NewStagingPath("export");
		try
		{
			int saveResult = solidWorks.SaveSettings(
				stagingPath,
				SystemOptions: false,
				ToolbarLayout: 0,
				KeyboardShortcuts: true,
				MouseGestures: false,
				MenuCustomization: false,
				SavedViews: false);
			if (saveResult != 0 || !File.Exists(stagingPath))
			{
				throw new InvalidOperationException("SOLIDWORKS 导出快捷键失败（SaveSettings=" + saveResult + "）。");
			}
			File.Copy(stagingPath, registryPath, overwrite: true);
		}
		finally
		{
			DeleteFileQuietly(stagingPath);
		}
		List<ShortcutReportRow> rows = NormalizeReportRows(shortcutRows).ToList();
		if (rows.Count == 0)
		{
			rows = ReadCommandNames(solidWorks, data).ToList();
		}
		File.WriteAllText(reportPath, BuildExportReport(data, rows), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
		return new ShortcutBackupArtifact
		{
			RegistryFilePath = registryPath,
			ReportFilePath = reportPath,
			SolidWorksVersion = data.VersionName,
			ShortcutCount = UniqueShortcutCount(data)
		};
	}

	public static ShortcutImportResult ImportDirect(string sourceRegistryPath, string sourceReportPath, ISldWorks solidWorks, IEnumerable<KeyboardShortcutRow> currentRows = null)
	{
		if (string.IsNullOrWhiteSpace(sourceRegistryPath) || !File.Exists(sourceRegistryPath))
		{
			throw new FileNotFoundException("找不到 SOLIDWORKS 快捷键备份。", sourceRegistryPath);
		}

		ShortcutRegistryData source = ParseRegistryFile(sourceRegistryPath);
		if (source.Entries.Count == 0)
		{
			throw new InvalidDataException("备份文件中没有 SOLIDWORKS 自定义快捷键。");
		}

		if (solidWorks == null)
		{
			throw new InvalidOperationException("没有连接到当前 SOLIDWORKS，无法直接导入快捷键。");
		}
		Directory.CreateDirectory(BackupDirectory);
		ShortcutRegistryData before = ReadCurrent(solidWorks);
		string rollbackPath = UniquePath(Path.Combine(BackupDirectory, "导入前自动回滚_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".sldreg"));
		string rollbackStagingPath = NewStagingPath("rollback");
		int saveResult = solidWorks.SaveSettings(
			rollbackStagingPath,
			SystemOptions: false,
			ToolbarLayout: 0,
			KeyboardShortcuts: true,
			MouseGestures: false,
			MenuCustomization: false,
			SavedViews: false);
		if (saveResult != 0 || !File.Exists(rollbackStagingPath))
		{
			DeleteFileQuietly(rollbackStagingPath);
			throw new InvalidOperationException("无法生成导入前快捷键回滚文件（SaveSettings=" + saveResult + "）。");
		}
		File.Copy(rollbackStagingPath, rollbackPath, overwrite: true);
		DeleteFileQuietly(rollbackStagingPath);
		File.WriteAllText(Path.ChangeExtension(rollbackPath, ".txt"), BuildExportReport(before, NormalizeReportRows(currentRows)), new UTF8Encoding(true));

		string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
		string incomingPath = UniquePath(Path.Combine(BackupDirectory, "待导入SW快捷键_" + timestamp + ".sldreg"));
		File.Copy(sourceRegistryPath, incomingPath, overwrite: true);
		string incomingReport = Path.ChangeExtension(incomingPath, ".txt");
		if (!string.IsNullOrWhiteSpace(sourceReportPath) && File.Exists(sourceReportPath))
		{
			File.Copy(sourceReportPath, incomingReport, overwrite: true);
		}
		else
		{
			File.WriteAllText(incomingReport, BuildExportReport(source, Enumerable.Empty<ShortcutReportRow>()), new UTF8Encoding(true));
		}

		string resultPath = Path.Combine(BackupDirectory, "导入结果_" + timestamp + ".txt");
		string stagingPath = NewStagingPath("import");
		File.Copy(incomingPath, stagingPath, overwrite: true);
		int restoreResult;
		try
		{
			restoreResult = solidWorks.RestoreSettings(
				stagingPath,
				SystemOptions: false,
				ToolbarLayout: false,
				KeyboardShortcuts: true,
				MouseGestures: false,
				MenuCustomization: false,
				SavedViews: false,
				CreateBackup: false);
		}
		finally
		{
			DeleteFileQuietly(stagingPath);
		}
		if (restoreResult != 0)
		{
			string reason = restoreResult == 2 ? "快捷键文件路径无效" : "SOLIDWORKS 未接受快捷键设置";
			throw new InvalidOperationException(reason + "（RestoreSettings=" + restoreResult + "）。");
		}

		ShortcutRegistryData after = ReadCurrentVersion(before.VersionName);
		string resultText = BuildImportResult(source, before, after, ReadReportRows(incomingReport));
		File.WriteAllText(resultPath, resultText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
		return new ShortcutImportResult
		{
			ReportPath = resultPath,
			ReportText = resultText,
			Completed = SourceEntriesPresent(source, after)
		};
	}

	private static string NewStagingPath(string operation)
	{
		string directory = Path.Combine(
			System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
			"XYSolidWorksRadial",
			"ShortcutTransfer");
		Directory.CreateDirectory(directory);
		return Path.Combine(directory, operation + "-" + Guid.NewGuid().ToString("N") + ".sldreg");
	}

	private static void DeleteFileQuietly(string path)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}

	internal static ShortcutRegistryData ReadCurrent(ISldWorks solidWorks)
	{
		string versionName = ResolveVersionName(solidWorks);
		return ReadCurrentVersion(versionName);
	}

	internal static ShortcutRegistryData ReadCurrentVersion(string versionName)
	{
		string customizationPath = @"Software\SolidWorks\" + versionName + @"\Customization";
		ShortcutRegistryData data = new ShortcutRegistryData { VersionName = versionName };
		using RegistryKey customization = Registry.CurrentUser.OpenSubKey(customizationPath, writable: false);
		if (customization == null)
		{
			throw new InvalidOperationException("找不到 " + versionName + " 的快捷键设置。");
		}
		data.CustomizationVersion = ConvertToInt(customization.GetValue("cusFileVersionID"));
		foreach (string plateName in customization.GetSubKeyNames().Where(name => Regex.IsMatch(name, @"^tPlate\d+$", RegexOptions.IgnoreCase)))
		{
			using RegistryKey accelerators = customization.OpenSubKey(plateName + @"\Custom Accelerators", writable: false);
			if (accelerators == null)
			{
				continue;
			}
			ReadCollection(accelerators, plateName, "AddArray", data.Entries);
			ReadCollection(accelerators, plateName, "SubArray", data.Entries);
		}
		return data;
	}

	internal static ShortcutRegistryData ParseRegistryFile(string path)
	{
		if (!File.Exists(path))
		{
			throw new FileNotFoundException("找不到快捷键文件。", path);
		}
		return ParseRegistryText(ReadRegistryText(path));
	}

	internal static ShortcutRegistryData ParseRegistryText(string text)
	{
		ShortcutRegistryData data = new ShortcutRegistryData();
		ShortcutRegistryEntry current = null;
		foreach (string rawLine in (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
		{
			string line = rawLine.Trim();
			if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
			{
				CommitEntry(current, data.Entries);
				current = null;
				string section = line.Substring(1, line.Length - 2);
				Match version = VersionPathPattern.Match(section + "\\");
				if (version.Success && data.VersionName.Length == 0)
				{
					data.VersionName = version.Groups[1].Value;
				}
				Match accelerator = AcceleratorPathPattern.Match(section);
				if (accelerator.Success)
				{
					current = new ShortcutRegistryEntry
					{
						Plate = accelerator.Groups[1].Value,
						Collection = accelerator.Groups[2].Value,
						LeafName = accelerator.Groups[3].Value
					};
				}
				continue;
			}

			if (TryParseDword(line, "cusFileVersionID", out int customizationVersion))
			{
				data.CustomizationVersion = customizationVersion;
			}
			if (current == null)
			{
				continue;
			}
			if (TryParseDword(line, "fVirt", out int flags))
			{
				current.Flags = flags;
			}
			else if (TryParseDword(line, "key", out int key))
			{
				current.Key = key;
			}
			else if (TryParseDword(line, "cmd", out int commandId))
			{
				current.CommandId = commandId;
			}
		}
		CommitEntry(current, data.Entries);
		if (data.VersionName.Length == 0)
		{
			throw new InvalidDataException("快捷键文件缺少 SOLIDWORKS 版本信息。");
		}
		return data;
	}

	internal static string BuildRegistryText(ShortcutRegistryData data)
	{
		if (data == null || string.IsNullOrWhiteSpace(data.VersionName))
		{
			throw new InvalidDataException("快捷键数据缺少 SOLIDWORKS 版本。");
		}
		StringBuilder builder = new StringBuilder(16384);
		string root = @"HKEY_CURRENT_USER\Software\SolidWorks\" + data.VersionName;
		builder.AppendLine("REGEDIT4");
		builder.AppendLine(";SolidWorks Settings Wizard");
		builder.AppendLine(";Exported by XY SOLIDWORKS 轮盘 - keyboard shortcuts only");
		builder.AppendLine();
		builder.AppendLine("[" + root + "]");
		builder.AppendLine();
		builder.AppendLine("[" + root + @"\Customization]");
		if (data.CustomizationVersion > 0)
		{
			builder.AppendLine("\"cusFileVersionID\"=dword:" + data.CustomizationVersion.ToString("x8"));
		}
		builder.AppendLine();
		foreach (IGrouping<string, ShortcutRegistryEntry> plate in data.Entries
			.OrderBy(entry => PlateNumber(entry.Plate))
			.ThenBy(entry => entry.Collection, StringComparer.OrdinalIgnoreCase)
			.ThenBy(entry => entry.LeafName, StringComparer.OrdinalIgnoreCase)
			.GroupBy(entry => entry.Plate, StringComparer.OrdinalIgnoreCase))
		{
			string acceleratorRoot = root + @"\Customization\" + plate.Key + @"\Custom Accelerators";
			builder.AppendLine("[" + root + @"\Customization\" + plate.Key + "]");
			builder.AppendLine();
			builder.AppendLine("[" + acceleratorRoot + "]");
			builder.AppendLine();
			foreach (IGrouping<string, ShortcutRegistryEntry> collection in plate.GroupBy(entry => entry.Collection, StringComparer.OrdinalIgnoreCase))
			{
				builder.AppendLine("[" + acceleratorRoot + "\\" + collection.Key + "]");
				builder.AppendLine();
				foreach (ShortcutRegistryEntry entry in collection)
				{
					string leaf = string.IsNullOrWhiteSpace(entry.LeafName) ? entry.Flags + "_" + entry.Key : entry.LeafName;
					builder.AppendLine("[" + acceleratorRoot + "\\" + collection.Key + "\\" + leaf + "]");
					builder.AppendLine("\"fVirt\"=dword:" + entry.Flags.ToString("x8"));
					builder.AppendLine("\"key\"=dword:" + entry.Key.ToString("x8"));
					builder.AppendLine("\"cmd\"=dword:" + entry.CommandId.ToString("x8"));
					builder.AppendLine();
				}
			}
		}
		return builder.ToString();
	}

	internal static string DecodeHotkey(int flags, int key)
	{
		List<string> parts = new List<string>();
		if ((flags & 0x08) != 0)
		{
			parts.Add("Ctrl");
		}
		if ((flags & 0x10) != 0)
		{
			parts.Add("Alt");
		}
		if ((flags & 0x04) != 0)
		{
			parts.Add("Shift");
		}
		parts.Add(KeyDisplay(key));
		return string.Join("+", parts);
	}

	internal static List<KeyboardShortcutRow> ReadCurrentShortcutRows(ISldWorks solidWorks)
	{
		ShortcutRegistryData data = ReadCurrent(solidWorks);
		return ReadCommandNames(solidWorks, data)
			.Select(row => new KeyboardShortcutRow { Category = row.Category, Command = row.Command, Hotkey = row.Hotkey, ActionKind = ActionKinds.Hotkey, Value = row.Hotkey })
			.OrderBy(row => row.Hotkey, StringComparer.OrdinalIgnoreCase)
			.ThenBy(row => row.Command, StringComparer.CurrentCultureIgnoreCase)
			.ToList();
	}

	internal static bool SourceEntriesPresent(ShortcutRegistryData source, ShortcutRegistryData target)
	{
		HashSet<string> targetEntries = new HashSet<string>((target?.Entries ?? new List<ShortcutRegistryEntry>()).Select(entry => entry.Signature(includePlate: false)), StringComparer.OrdinalIgnoreCase);
		return (source?.Entries ?? new List<ShortcutRegistryEntry>())
			.Where(entry => entry.Collection.Equals("AddArray", StringComparison.OrdinalIgnoreCase))
			.Select(entry => entry.Signature(includePlate: false))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.All(targetEntries.Contains);
	}

	internal static string BuildImportResult(ShortcutRegistryData source, ShortcutRegistryData before, ShortcutRegistryData after, IEnumerable<ShortcutReportRow> reportRows)
	{
		List<ShortcutRegistryEntry> sourceUnique = UniqueAddedEntries(source).ToList();
		HashSet<string> afterEntries = new HashSet<string>(UniqueAddedEntries(after).Select(entry => entry.Signature(includePlate: false)), StringComparer.OrdinalIgnoreCase);
		List<ShortcutRegistryEntry> missing = sourceUnique.Where(entry => !afterEntries.Contains(entry.Signature(includePlate: false))).ToList();
		List<ShortcutReportRow> rows = (reportRows ?? Enumerable.Empty<ShortcutReportRow>()).ToList();
		List<ShortcutReportRow> unavailablePlugins = rows.Where(row => LooksLikePlugin(row.Category) && !PluginAppearsInstalled(row.Category)).ToList();
		bool unchanged = SnapshotSignature(before).Equals(SnapshotSignature(after), StringComparison.Ordinal);

		StringBuilder builder = new StringBuilder(4096);
		builder.AppendLine("XY SOLIDWORKS 轮盘 - 快捷键导入结果");
		builder.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		builder.AppendLine("来源版本：" + source.VersionName);
		builder.AppendLine("目标版本：" + after.VersionName);
		builder.AppendLine();
		builder.AppendLine("备份快捷键：" + sourceUnique.Count + " 项");
		builder.AppendLine("已写入核对：" + (sourceUnique.Count - missing.Count) + " 项");
		builder.AppendLine("未写入：" + missing.Count + " 项");
		builder.AppendLine("未安装相应插件：" + unavailablePlugins.Count + " 项");
		if (unchanged && missing.Count > 0)
		{
			builder.AppendLine("状态：设置没有变化，源快捷键可能与当前设置相同，或 SOLIDWORKS 未写入对应命令。");
		}
		else if (missing.Count == 0)
		{
			builder.AppendLine("状态：快捷键注册表核对完成。重新启动 SOLIDWORKS 后生效。");
		}
		else
		{
			builder.AppendLine("状态：部分快捷键未写入，请查看下面的明细。");
		}

		if (unavailablePlugins.Count > 0)
		{
			builder.AppendLine();
			builder.AppendLine("[插件命令]");
			foreach (ShortcutReportRow row in unavailablePlugins.Distinct(ShortcutReportRowComparer.Instance))
			{
				builder.AppendLine(DisplayCommand(row) + " | " + row.Hotkey);
				builder.AppendLine("结果：未导入");
				builder.AppendLine("原因：未安装相应插件");
				builder.AppendLine();
			}
		}

		if (missing.Count > 0)
		{
			builder.AppendLine("[未写入的快捷键]");
			foreach (ShortcutRegistryEntry entry in missing)
			{
				string hotkey = DecodeHotkey(entry.Flags, entry.Key);
				ShortcutReportRow row = rows.FirstOrDefault(item => HotkeyEquals(item.Hotkey, hotkey));
				builder.AppendLine(row == null ? hotkey + " | 命令ID " + entry.CommandId : DisplayCommand(row) + " | " + row.Hotkey);
				builder.AppendLine("结果：未导入");
				builder.AppendLine("原因：" + (row != null && LooksLikePlugin(row.Category) ? "未安装相应插件" : "当前 SOLIDWORKS 版本未接受该命令"));
				builder.AppendLine();
			}
		}

		builder.AppendLine("回滚文件和本结果保存在：");
		builder.AppendLine(BackupDirectory);
		return builder.ToString();
	}

	private static void ReadCollection(RegistryKey accelerators, string plateName, string collectionName, ICollection<ShortcutRegistryEntry> entries)
	{
		using RegistryKey collection = accelerators.OpenSubKey(collectionName, writable: false);
		if (collection == null)
		{
			return;
		}
		foreach (string leaf in collection.GetSubKeyNames())
		{
			using RegistryKey item = collection.OpenSubKey(leaf, writable: false);
			if (item == null)
			{
				continue;
			}
			int flags = ConvertToInt(item.GetValue("fVirt"));
			int key = ConvertToInt(item.GetValue("key"));
			int commandId = ConvertToInt(item.GetValue("cmd"));
			if (key > 0 && commandId > 0)
			{
				entries.Add(new ShortcutRegistryEntry
				{
					Plate = plateName,
					Collection = collectionName,
					LeafName = leaf,
					Flags = flags,
					Key = key,
					CommandId = commandId
				});
			}
		}
	}

	private static void WriteRegistryFile(ShortcutRegistryData data, string path)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
		File.WriteAllText(path, BuildRegistryText(data), Encoding.Default);
	}

	private static string ResolveVersionName(ISldWorks solidWorks)
	{
		try
		{
			string revision = solidWorks?.RevisionNumber() ?? "";
			if (int.TryParse(revision.Split('.')[0], out int major) && major >= 8)
			{
				string expected = "SOLIDWORKS " + (1992 + major);
				using RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\SolidWorks\" + expected);
				if (key != null)
				{
					return expected;
				}
			}
		}
		catch
		{
		}
		using RegistryKey root = Registry.CurrentUser.OpenSubKey(@"Software\SolidWorks");
		string latest = root?.GetSubKeyNames()
			.Where(name => Regex.IsMatch(name, @"^SOLIDWORKS\s+\d{4}$", RegexOptions.IgnoreCase))
			.OrderByDescending(name => name, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault();
		if (string.IsNullOrWhiteSpace(latest))
		{
			throw new InvalidOperationException("没有找到当前用户的 SOLIDWORKS 设置。");
		}
		return latest;
	}

	private static IEnumerable<ShortcutReportRow> NormalizeReportRows(IEnumerable<KeyboardShortcutRow> rows)
	{
		return (rows ?? Enumerable.Empty<KeyboardShortcutRow>())
			.Where(row => row != null
				&& !string.IsNullOrWhiteSpace(row.Hotkey)
				&& !row.Hotkey.Equals("直接执行", StringComparison.OrdinalIgnoreCase)
				&& (string.IsNullOrWhiteSpace(row.ActionKind) || row.ActionKind.Equals(ActionKinds.Hotkey, StringComparison.OrdinalIgnoreCase)))
			.Select(row => new ShortcutReportRow
			{
				Category = (row.Category ?? "未分类").Trim(),
				Command = (row.Command ?? "未命名命令").Trim(),
				Hotkey = (row.Hotkey ?? "").Trim()
			})
			.Distinct(ShortcutReportRowComparer.Instance)
			.OrderBy(row => row.Category, StringComparer.CurrentCultureIgnoreCase)
			.ThenBy(row => row.Command, StringComparer.CurrentCultureIgnoreCase);
	}

	private static IEnumerable<ShortcutReportRow> ReadCommandNames(ISldWorks solidWorks, ShortcutRegistryData data)
	{
		if (solidWorks == null)
		{
			return Enumerable.Empty<ShortcutReportRow>();
		}
		List<ShortcutReportRow> result = new List<ShortcutReportRow>();
		int activeType = 0;
		try
		{
			activeType = (solidWorks.ActiveDoc as IModelDoc2)?.GetType() ?? 0;
		}
		catch
		{
		}
		int[] documentTypes = new[] { activeType, 1, 2, 3 }.Where(value => value > 0).Distinct().ToArray();
		foreach (ShortcutRegistryEntry entry in UniqueAddedEntries(data))
		{
			string command = "";
			string parent = "";
			foreach (int documentType in documentTypes)
			{
				try
				{
					string candidateParent;
					string menuText = solidWorks.GetMenuStrings(entry.CommandId, documentType, out candidateParent);
					command = CleanCommandName(menuText);
					parent = CleanCommandName(candidateParent);
					if (command.Length > 0)
					{
						break;
					}
				}
				catch
				{
				}
			}
			if (command.Length == 0)
			{
				continue;
			}
			string category = LooksLikePlugin(parent) || LooksLikePlugin(command)
				? "插件快捷键 > " + (parent.Length == 0 ? "插件命令" : parent)
				: "SOLIDWORKS 快捷键 > " + (parent.Length == 0 ? "未分类" : parent);
			result.Add(new ShortcutReportRow
			{
				Category = category,
				Command = command,
				Hotkey = DecodeHotkey(entry.Flags, entry.Key)
			});
		}
		return result.Distinct(ShortcutReportRowComparer.Instance);
	}

	private static string BuildExportReport(ShortcutRegistryData data, IEnumerable<ShortcutReportRow> reportRows)
	{
		List<ShortcutReportRow> rows = (reportRows ?? Enumerable.Empty<ShortcutReportRow>()).ToList();
		StringBuilder builder = new StringBuilder(4096);
		builder.AppendLine("XY SOLIDWORKS 轮盘 - 自定义快捷键备份清单");
		builder.AppendLine("导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		builder.AppendLine("SOLIDWORKS：" + data.VersionName);
		builder.AppendLine("快捷键数量：" + UniqueShortcutCount(data));
		builder.AppendLine();
		builder.AppendLine("类别\t命令\t快捷键");
		if (rows.Count > 0)
		{
			foreach (ShortcutReportRow row in rows)
			{
				builder.AppendLine(CleanTab(row.Category) + "\t" + CleanTab(row.Command) + "\t" + CleanTab(row.Hotkey));
			}
		}
		else
		{
			foreach (ShortcutRegistryEntry entry in UniqueAddedEntries(data))
			{
				builder.AppendLine("未分类\t命令ID " + entry.CommandId + "\t" + DecodeHotkey(entry.Flags, entry.Key));
			}
		}
		return builder.ToString();
	}

	private static IEnumerable<ShortcutReportRow> ReadReportRows(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return Enumerable.Empty<ShortcutReportRow>();
		}
		List<ShortcutReportRow> rows = new List<ShortcutReportRow>();
		foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
		{
			string[] parts = line.Split('\t');
			if (parts.Length >= 3 && !parts[0].Equals("类别", StringComparison.CurrentCultureIgnoreCase))
			{
				rows.Add(new ShortcutReportRow { Category = parts[0].Trim(), Command = parts[1].Trim(), Hotkey = parts[2].Trim() });
			}
		}
		return rows;
	}

	private static IEnumerable<ShortcutRegistryEntry> UniqueAddedEntries(ShortcutRegistryData data)
	{
		return (data?.Entries ?? new List<ShortcutRegistryEntry>())
			.Where(entry => entry.Collection.Equals("AddArray", StringComparison.OrdinalIgnoreCase))
			.GroupBy(entry => entry.Signature(includePlate: false), StringComparer.OrdinalIgnoreCase)
			.Select(group => group.First())
			.OrderBy(entry => DecodeHotkey(entry.Flags, entry.Key), StringComparer.OrdinalIgnoreCase);
	}

	private static int UniqueShortcutCount(ShortcutRegistryData data)
	{
		return UniqueAddedEntries(data).Count();
	}

	private static string SnapshotSignature(ShortcutRegistryData data)
	{
		return string.Join(";", UniqueAddedEntries(data).Select(entry => entry.Signature(includePlate: false)).OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
	}

	private static bool LooksLikePlugin(string category)
	{
		string value = category ?? "";
		return value.IndexOf("插件快捷键", StringComparison.CurrentCultureIgnoreCase) >= 0
			|| PluginMarkers.Any(marker => value.IndexOf(marker, StringComparison.CurrentCultureIgnoreCase) >= 0);
	}

	private static bool PluginAppearsInstalled(string category)
	{
		string value = category ?? "";
		List<string> titles = ReadInstalledAddinTitles();
		if (ContainsAny(value, "琴剑", "QinJian"))
		{
			return titles.Any(title => ContainsAny(title, "琴剑", "QinJian"));
		}
		if (ContainsAny(value, "博士", "Doctor", "Dr Sheetmetal", "Dr工具"))
		{
			return titles.Any(title => ContainsAny(title, "博士", "Doctor", "Dr Sheetmetal"));
		}
		if (ContainsAny(value, "嘉立创", "Ican"))
		{
			return titles.Any(title => ContainsAny(title, "嘉立创", "Ican"));
		}
		foreach (string marker in PluginMarkers)
		{
			if (value.IndexOf(marker, StringComparison.CurrentCultureIgnoreCase) >= 0
				&& titles.Any(title => title.IndexOf(marker, StringComparison.CurrentCultureIgnoreCase) >= 0))
			{
				return true;
			}
		}
		return false;
	}

	private static List<string> ReadInstalledAddinTitles()
	{
		List<string> result = new List<string>();
		foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
		foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
		{
			try
			{
				using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
				ReadAddinTitles(baseKey, hive == RegistryHive.CurrentUser ? @"Software\SolidWorks\Addins" : @"SOFTWARE\SolidWorks\Addins", result);
			}
			catch (Exception ex) { Logger.Trace("Read installed add-in titles skipped: " + hive + "/" + view + ": " + ex.Message); }
		}
		return result;
	}

	private static void ReadAddinTitles(RegistryKey hive, string path, ICollection<string> result)
	{
		try
		{
			using RegistryKey addins = hive.OpenSubKey(path, writable: false);
			if (addins == null)
			{
				return;
			}
			foreach (string id in addins.GetSubKeyNames())
			{
				using RegistryKey addin = addins.OpenSubKey(id, writable: false);
				string title = Convert.ToString(addin?.GetValue("Title")) ?? "";
				string description = Convert.ToString(addin?.GetValue("Description")) ?? "";
				result.Add(title + " " + description);
			}
		}
		catch
		{
		}
	}

	private static bool ContainsAny(string value, params string[] candidates)
	{
		return candidates.Any(candidate => (value ?? "").IndexOf(candidate, StringComparison.CurrentCultureIgnoreCase) >= 0);
	}

	private static string DisplayCommand(ShortcutReportRow row)
	{
		string category = (row?.Category ?? "插件命令").Replace("插件快捷键 > ", "").Trim();
		return category + " > " + (row?.Command ?? "未命名命令");
	}

	private static bool HotkeyEquals(string left, string right)
	{
		return NormalizeHotkey(left).Equals(NormalizeHotkey(right), StringComparison.OrdinalIgnoreCase);
	}

	private static string NormalizeHotkey(string value)
	{
		return Regex.Replace((value ?? "").Replace(" ", ""), "Control", "Ctrl", RegexOptions.IgnoreCase);
	}

	private static string CleanCommandName(string value)
	{
		string text = value ?? "";
		int shortcut = text.IndexOf('\t');
		if (shortcut >= 0)
		{
			text = text.Substring(0, shortcut);
		}
		text = Regex.Replace(text, @"\(&[^)]*\)", "").Replace("&", "");
		return text.Trim().TrimEnd('.', '…').Trim();
	}

	private static string KeyDisplay(int key)
	{
		if (key >= '0' && key <= '9' || key >= 'A' && key <= 'Z')
		{
			return ((char)key).ToString();
		}
		switch (key)
		{
		case 0x2E: return "Delete";
		case 0x2D: return "Insert";
		case 0x20: return "Space";
		case 0x09: return "Tab";
		case 0x0D: return "Enter";
		case 0x1B: return "Esc";
		case 0xBC: return ",";
		case 0xBE: return ".";
		case 0xBF: return "/";
		case 0xBA: return ";";
		case 0xBB: return "+";
		case 0xBD: return "-";
		}
		if (key >= 0x70 && key <= 0x87)
		{
			return "F" + (key - 0x6F);
		}
		return "VK_" + key.ToString("X2");
	}

	private static bool TryParseDword(string line, string name, out int value)
	{
		value = 0;
		Match match = Regex.Match(line ?? "", "^\\\"" + Regex.Escape(name) + "\\\"=dword:([0-9a-fA-F]{1,8})$", RegexOptions.IgnoreCase);
		return match.Success && int.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out value);
	}

	private static void CommitEntry(ShortcutRegistryEntry entry, ICollection<ShortcutRegistryEntry> entries)
	{
		if (entry != null && entry.Key > 0 && entry.CommandId > 0)
		{
			entries.Add(entry);
		}
	}

	private static int ConvertToInt(object value)
	{
		try
		{
			return Convert.ToInt32(value);
		}
		catch
		{
			return 0;
		}
	}

	private static int PlateNumber(string value)
	{
		Match match = Regex.Match(value ?? "", @"\d+");
		return match.Success && int.TryParse(match.Value, out int result) ? result : int.MaxValue;
	}

	private static string ReadRegistryText(string path)
	{
		byte[] bytes = File.ReadAllBytes(path);
		if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
		{
			return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
		}
		if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
		{
			return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
		}
		return Encoding.Default.GetString(bytes);
	}

	private static string SanitizeFileName(string value)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		string result = new string((value ?? "SW快捷键").Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
		return result.Length == 0 ? "SW快捷键" : result;
	}

	private static string UniquePath(string path)
	{
		if (!File.Exists(path))
		{
			return path;
		}
		string directory = Path.GetDirectoryName(path) ?? ".";
		string name = Path.GetFileNameWithoutExtension(path);
		string extension = Path.GetExtension(path);
		for (int i = 2; i < 1000; i++)
		{
			string candidate = Path.Combine(directory, name + "-" + i + extension);
			if (!File.Exists(candidate))
			{
				return candidate;
			}
		}
		return Path.Combine(directory, name + "-" + Guid.NewGuid().ToString("N") + extension);
	}

	private static string CleanTab(string value)
	{
		return (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
	}

	private sealed class ShortcutReportRowComparer : IEqualityComparer<ShortcutReportRow>
	{
		public static readonly ShortcutReportRowComparer Instance = new ShortcutReportRowComparer();

		public bool Equals(ShortcutReportRow x, ShortcutReportRow y)
		{
			return string.Equals(x?.Category, y?.Category, StringComparison.CurrentCultureIgnoreCase)
				&& string.Equals(x?.Command, y?.Command, StringComparison.CurrentCultureIgnoreCase)
				&& HotkeyEquals(x?.Hotkey, y?.Hotkey);
		}

		public int GetHashCode(ShortcutReportRow obj)
		{
			return ((obj?.Category ?? "") + "|" + (obj?.Command ?? "") + "|" + NormalizeHotkey(obj?.Hotkey)).ToUpperInvariant().GetHashCode();
		}
	}
}
