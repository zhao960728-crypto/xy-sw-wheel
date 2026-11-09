using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace XY.SolidWorksPlugin;

internal sealed class ConfigBackupExportOptions
{
	public bool IncludeConfig { get; set; } = true;

	public string ShortcutRegistryFile { get; set; } = "";

	public string ShortcutReportFile { get; set; } = "";

	public bool IncludeShortcuts => !string.IsNullOrWhiteSpace(ShortcutRegistryFile);
}

internal sealed class ConfigBackupContents
{
	public bool HasConfig { get; set; }

	public bool HasShortcuts { get; set; }
}

internal sealed class ExtractedShortcutBackup
{
	public string RegistryFilePath { get; set; } = "";

	public string ReportFilePath { get; set; } = "";
}

internal static class ConfigBackupService
{
	private const string ManifestName = "manifest.json";
	private const string ConfigName = "config.json";
	private const string AssetsPrefix = "assets/";
	private const string ShortcutRegistryName = "shortcuts/solidworks-shortcuts.sldreg";
	private const string ShortcutReportName = "shortcuts/shortcut-list.txt";

	public static void Export(PluginConfig config, string packagePath)
	{
		Export(config, packagePath, new ConfigBackupExportOptions { IncludeConfig = true });
	}

	public static void Export(PluginConfig config, string packagePath, ConfigBackupExportOptions options)
	{
		if (string.IsNullOrWhiteSpace(packagePath))
		{
			throw new ArgumentException("导出路径不能为空。", nameof(packagePath));
		}
		options = options ?? new ConfigBackupExportOptions();
		if (!options.IncludeConfig && !options.IncludeShortcuts)
		{
			throw new InvalidOperationException("至少选择一项备份内容。");
		}
		if (options.IncludeShortcuts && !File.Exists(options.ShortcutRegistryFile))
		{
			throw new FileNotFoundException("找不到要打包的 SOLIDWORKS 快捷键文件。", options.ShortcutRegistryFile);
		}
		PluginConfig pluginConfig = options.IncludeConfig ? ConfigStore.Clone(config) : null;
		if (pluginConfig != null)
		{
			ConfigStore.Normalize(pluginConfig);
		}
		string fullPackagePath = Path.GetFullPath(packagePath);
		Directory.CreateDirectory(Path.GetDirectoryName(fullPackagePath) ?? ".");
		string text = CreateTempDirectory();
		string temporaryPackagePath = fullPackagePath + ".tmp-" + Guid.NewGuid().ToString("N");
		try
		{
			string configJson = "";
			if (pluginConfig != null)
			{
				string text2 = Path.Combine(text, "assets");
					Directory.CreateDirectory(text2);
					RewriteBackgroundsForExport(pluginConfig, text2);
					RewriteIconsForExport(pluginConfig, text2);
					configJson = ConfigStore.Serialize(pluginConfig);
				File.WriteAllText(Path.Combine(text, ConfigName), configJson, Encoding.UTF8);
			}
			string shortcutHash = "";
			if (options.IncludeShortcuts)
			{
				string shortcutRoot = Path.Combine(text, "shortcuts");
				Directory.CreateDirectory(shortcutRoot);
				string shortcutTarget = Path.Combine(text, ShortcutRegistryName.Replace('/', Path.DirectorySeparatorChar));
				File.Copy(options.ShortcutRegistryFile, shortcutTarget, overwrite: true);
				shortcutHash = Sha256Hex(File.ReadAllBytes(shortcutTarget));
				if (!string.IsNullOrWhiteSpace(options.ShortcutReportFile) && File.Exists(options.ShortcutReportFile))
				{
					File.Copy(options.ShortcutReportFile, Path.Combine(text, ShortcutReportName.Replace('/', Path.DirectorySeparatorChar)), overwrite: true);
				}
			}
			File.WriteAllText(Path.Combine(text, ManifestName), Serialize(new BackupManifest
			{
				HasConfig = options.IncludeConfig,
				HasShortcuts = options.IncludeShortcuts,
				ConfigSha256 = options.IncludeConfig ? Sha256Hex(configJson) : "",
				ShortcutSha256 = shortcutHash
			}), Encoding.UTF8);
			ZipFile.CreateFromDirectory(text, temporaryPackagePath, CompressionLevel.Optimal, includeBaseDirectory: false, Encoding.UTF8);
			ValidatePackage(temporaryPackagePath, options.IncludeConfig ? configJson : null, options.IncludeConfig, options.IncludeShortcuts);
			if (File.Exists(fullPackagePath))
			{
				File.Replace(temporaryPackagePath, fullPackagePath, null);
			}
			else
			{
				File.Move(temporaryPackagePath, fullPackagePath);
			}
		}
		finally
		{
			DeleteQuietly(text);
			DeleteFileQuietly(temporaryPackagePath);
		}
	}

	public static PluginConfig Import(string packagePath, string assetDirectory)
	{
		if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
		{
			throw new FileNotFoundException("找不到要导入的配置备份。", packagePath);
		}
		if (string.IsNullOrWhiteSpace(assetDirectory))
		{
			throw new ArgumentException("资源目录不能为空。", nameof(assetDirectory));
		}
		Directory.CreateDirectory(assetDirectory);
		string extension = Path.GetExtension(packagePath);
		if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
		{
				PluginConfig pluginConfig = ConfigStore.Deserialize(File.ReadAllText(packagePath, Encoding.UTF8));
				RewriteBackgroundsForImport(pluginConfig, Path.GetDirectoryName(Path.GetFullPath(packagePath)) ?? ".", assetDirectory);
				RewriteIconsForImport(pluginConfig, Path.GetDirectoryName(Path.GetFullPath(packagePath)) ?? ".", assetDirectory);
				ConfigStore.Normalize(pluginConfig);
			return pluginConfig;
		}
		string text = CreateTempDirectory();
		try
		{
			ValidatePackage(packagePath, null, requireConfig: true, requireShortcuts: false);
			ZipFile.ExtractToDirectory(packagePath, text, Encoding.UTF8);
			string text2 = Path.Combine(text, ConfigName);
			if (!File.Exists(text2))
			{
				throw new InvalidDataException("备份包缺少 config.json。");
			}
				PluginConfig pluginConfig2 = ConfigStore.Deserialize(File.ReadAllText(text2, Encoding.UTF8));
				RewriteBackgroundsForImport(pluginConfig2, text, assetDirectory);
				RewriteIconsForImport(pluginConfig2, text, assetDirectory);
				ConfigStore.Normalize(pluginConfig2);
			return pluginConfig2;
		}
		finally
		{
			DeleteQuietly(text);
		}
	}

	public static ConfigBackupContents Inspect(string packagePath)
	{
		if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
		{
			throw new FileNotFoundException("找不到要检查的备份文件。", packagePath);
		}
		string extension = Path.GetExtension(packagePath);
		if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
		{
			return new ConfigBackupContents { HasConfig = true };
		}
		if (extension.Equals(".sldreg", StringComparison.OrdinalIgnoreCase))
		{
			return new ConfigBackupContents { HasShortcuts = true };
		}
		using ZipArchive archive = ZipFile.OpenRead(packagePath);
		return new ConfigBackupContents
		{
			HasConfig = FindEntry(archive, ConfigName) != null,
			HasShortcuts = FindEntry(archive, ShortcutRegistryName) != null
		};
	}

	public static ExtractedShortcutBackup ExtractShortcuts(string packagePath, string destinationDirectory)
	{
		if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
		{
			throw new FileNotFoundException("找不到要导入的快捷键备份。", packagePath);
		}
		if (string.IsNullOrWhiteSpace(destinationDirectory))
		{
			throw new ArgumentException("快捷键解压目录不能为空。", nameof(destinationDirectory));
		}
		Directory.CreateDirectory(destinationDirectory);
		string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
		string registryTarget = Path.Combine(destinationDirectory, "SW快捷键导入源_" + timestamp + ".sldreg");
		string reportTarget = Path.ChangeExtension(registryTarget, ".txt");
		if (Path.GetExtension(packagePath).Equals(".sldreg", StringComparison.OrdinalIgnoreCase))
		{
			File.Copy(packagePath, registryTarget, overwrite: true);
			string adjacentReport = Path.ChangeExtension(packagePath, ".txt");
			if (File.Exists(adjacentReport))
			{
				File.Copy(adjacentReport, reportTarget, overwrite: true);
			}
			return new ExtractedShortcutBackup { RegistryFilePath = registryTarget, ReportFilePath = File.Exists(reportTarget) ? reportTarget : "" };
		}

		ValidatePackage(packagePath, null, requireConfig: false, requireShortcuts: true);
		using (ZipArchive archive = ZipFile.OpenRead(packagePath))
		{
			ExtractEntry(FindEntry(archive, ShortcutRegistryName), registryTarget);
			ZipArchiveEntry report = FindEntry(archive, ShortcutReportName);
			if (report != null)
			{
				ExtractEntry(report, reportTarget);
			}
		}
		return new ExtractedShortcutBackup { RegistryFilePath = registryTarget, ReportFilePath = File.Exists(reportTarget) ? reportTarget : "" };
	}

	private static void RewriteBackgroundsForExport(PluginConfig config, string assetDirectory)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (SceneMenuConfig item in config.Menus.Values)
		{
			string text = (item.BackgroundImagePath ?? "").Trim();
			if (string.IsNullOrWhiteSpace(text) || !File.Exists(text))
			{
				item.BackgroundImagePath = "";
				continue;
			}
			string fullPath = Path.GetFullPath(text);
			if (!dictionary.TryGetValue(fullPath, out string value))
			{
				string text2 = UniqueAssetName(fullPath);
				File.Copy(fullPath, Path.Combine(assetDirectory, text2), overwrite: true);
				value = AssetsPrefix + text2;
				dictionary[fullPath] = value;
			}
			item.BackgroundImagePath = value;
		}
	}

	private static void RewriteBackgroundsForImport(PluginConfig config, string packageRoot, string assetDirectory)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (SceneMenuConfig item in config.Menus.Values)
		{
			string text = (item.BackgroundImagePath ?? "").Trim();
			if (string.IsNullOrWhiteSpace(text))
			{
				item.BackgroundImagePath = "";
				continue;
			}
			string text2 = ResolveImportAssetPath(text, packageRoot);
			if (!File.Exists(text2))
			{
				item.BackgroundImagePath = "";
				continue;
			}
			string fullPath = Path.GetFullPath(text2);
			if (!dictionary.TryGetValue(fullPath, out string value))
			{
				value = Path.Combine(assetDirectory, UniqueAssetName(fullPath));
				File.Copy(fullPath, value, overwrite: true);
				dictionary[fullPath] = value;
			}
			item.BackgroundImagePath = value;
		}
	}

	private static IEnumerable<MenuItemConfig> IconItems(PluginConfig config)
	{
		return config.Menus.Values.Where(scene => scene != null)
			.SelectMany(scene => scene.Rings ?? new List<RingConfig>())
			.Where(ring => ring != null)
			.SelectMany(ring => ring.Items ?? new List<MenuItemConfig>())
			.Where(item => item != null);
	}

	private static void RewriteIconsForExport(PluginConfig config, string assetDirectory)
	{
		var copied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (MenuItemConfig item in IconItems(config))
		{
			string path = (item.IconPath ?? "").Trim();
			if (path.Length == 0 || !File.Exists(path))
			{
				item.IconPath = "";
				continue;
			}
			string fullPath = Path.GetFullPath(path);
			if (!copied.TryGetValue(fullPath, out string packagedPath))
			{
				string name = UniqueAssetName(fullPath);
				File.Copy(fullPath, Path.Combine(assetDirectory, name), overwrite: true);
				packagedPath = AssetsPrefix + name;
				copied[fullPath] = packagedPath;
			}
			item.IconPath = packagedPath;
		}
	}

	private static void RewriteIconsForImport(PluginConfig config, string packageRoot, string assetDirectory)
	{
		var copied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (MenuItemConfig item in IconItems(config))
		{
			string path = (item.IconPath ?? "").Trim();
			if (path.Length == 0)
			{
				item.IconPath = "";
				continue;
			}
			string fullPath = Path.GetFullPath(ResolveImportAssetPath(path, packageRoot));
			// Relative references must stay inside the backup. Absolute paths remain
			// supported for standalone JSON exported by an older local configuration.
			if (!Path.IsPathRooted(path) && !fullPath.StartsWith(
				Path.GetFullPath(packageRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
				StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("图标资源路径超出备份目录。");
			}
			if (!File.Exists(fullPath))
			{
				item.IconPath = "";
				continue;
			}
			if (!copied.TryGetValue(fullPath, out string importedPath))
			{
				importedPath = Path.Combine(assetDirectory, UniqueAssetName(fullPath));
				File.Copy(fullPath, importedPath, overwrite: true);
				copied[fullPath] = importedPath;
			}
			item.IconPath = importedPath;
		}
	}

	private static string ResolveImportAssetPath(string path, string packageRoot)
	{
		string text = path.Replace('\\', '/');
		if (text.StartsWith(AssetsPrefix, StringComparison.OrdinalIgnoreCase))
		{
			return Path.Combine(packageRoot, text.Replace('/', Path.DirectorySeparatorChar));
		}
		return Path.IsPathRooted(path) ? path : Path.Combine(packageRoot, path);
	}

	private static string UniqueAssetName(string path)
	{
		string extension = Path.GetExtension(path);
		if (string.IsNullOrWhiteSpace(extension))
		{
			extension = ".dat";
		}
		using SHA256 sHA = SHA256.Create();
		byte[] bytes = sHA.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant()));
		string text = BitConverter.ToString(bytes, 0, 8).Replace("-", "").ToLowerInvariant();
		string fileNameWithoutExtension = SanitizeFileName(Path.GetFileNameWithoutExtension(path));
		if (fileNameWithoutExtension.Length > 32)
		{
			fileNameWithoutExtension = fileNameWithoutExtension.Substring(0, 32);
		}
		return fileNameWithoutExtension + "-" + text + extension.ToLowerInvariant();
	}

	private static string SanitizeFileName(string value)
	{
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		string text = new string((value ?? "asset").Select((char c) => invalidFileNameChars.Contains(c) ? '_' : c).ToArray()).Trim();
		return string.IsNullOrWhiteSpace(text) ? "asset" : text;
	}

	private static string CreateTempDirectory()
	{
		string text = Path.Combine(Path.GetTempPath(), "xy-radial-backup-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		return text;
	}

	private static void ValidatePackage(string packagePath, string expectedConfigJson, bool requireConfig, bool requireShortcuts)
	{
		using ZipArchive archive = ZipFile.OpenRead(packagePath);
		ZipArchiveEntry manifestEntry = FindEntry(archive, ManifestName)
			?? throw new InvalidDataException("备份包缺少 manifest.json。");
		BackupManifest manifest = Deserialize<BackupManifest>(ReadEntry(manifestEntry));
		ZipArchiveEntry configEntry = FindEntry(archive, ConfigName);
		ZipArchiveEntry shortcutEntry = FindEntry(archive, ShortcutRegistryName);
		if (requireConfig && configEntry == null)
		{
			throw new InvalidDataException("备份包不包含轮盘配置。");
		}
		if (requireShortcuts && shortcutEntry == null)
		{
			throw new InvalidDataException("备份包不包含 SOLIDWORKS 快捷键。");
		}
		if (configEntry != null)
		{
			string configJson = ReadEntry(configEntry);
			if (!string.IsNullOrWhiteSpace(manifest.ConfigSha256)
				&& !string.Equals(manifest.ConfigSha256, Sha256Hex(configJson), StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("备份包配置校验失败。");
			}
			if (expectedConfigJson != null && !string.Equals(expectedConfigJson, configJson, StringComparison.Ordinal))
			{
				throw new InvalidDataException("备份包中的配置与导出内容不一致。");
			}

			PluginConfig packagedConfig = ConfigStore.Deserialize(configJson);
			foreach (MenuItemConfig item in IconItems(packagedConfig))
			{
				string path = (item.IconPath ?? "").Replace('\\', '/');
				if (path.StartsWith(AssetsPrefix, StringComparison.OrdinalIgnoreCase)
					&& FindEntry(archive, path) == null)
				{
					throw new InvalidDataException("备份包缺少图标资源：" + path);
				}
			}
			foreach (SceneMenuConfig scene in packagedConfig.Menus.Values)
			{
				string path = (scene.BackgroundImagePath ?? "").Replace('\\', '/');
				if (path.StartsWith(AssetsPrefix, StringComparison.OrdinalIgnoreCase)
					&& FindEntry(archive, path) == null)
				{
					throw new InvalidDataException("备份包缺少背景资源：" + path);
				}
			}
		}
		if (shortcutEntry != null && !string.IsNullOrWhiteSpace(manifest.ShortcutSha256))
		{
			using Stream stream = shortcutEntry.Open();
			using MemoryStream buffer = new MemoryStream();
			stream.CopyTo(buffer);
			if (!string.Equals(manifest.ShortcutSha256, Sha256Hex(buffer.ToArray()), StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("备份包快捷键校验失败。");
			}
		}
	}

	private static ZipArchiveEntry FindEntry(ZipArchive archive, string path)
	{
		string expected = NormalizeEntryPath(path);
		return archive?.Entries.FirstOrDefault(entry =>
			NormalizeEntryPath(entry.FullName).Equals(expected, StringComparison.OrdinalIgnoreCase));
	}

	private static string NormalizeEntryPath(string path)
	{
		return (path ?? "").Replace('\\', '/').TrimStart('/');
	}

	private static void ExtractEntry(ZipArchiveEntry entry, string destinationPath)
	{
		if (entry == null)
		{
			throw new InvalidDataException("备份包缺少快捷键文件。");
		}
		using Stream source = entry.Open();
		using FileStream target = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
		source.CopyTo(target);
	}

	private static string ReadEntry(ZipArchiveEntry entry)
	{
		using Stream stream = entry.Open();
		using StreamReader reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
		return reader.ReadToEnd();
	}

	private static string Sha256Hex(string value)
	{
		using SHA256 sha = SHA256.Create();
		return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""))).Replace("-", "").ToLowerInvariant();
	}

	private static string Sha256Hex(byte[] value)
	{
		using SHA256 sha = SHA256.Create();
		return BitConverter.ToString(sha.ComputeHash(value ?? Array.Empty<byte>())).Replace("-", "").ToLowerInvariant();
	}

	private static string Serialize(object value)
	{
		return new JavaScriptSerializer
		{
			MaxJsonLength = int.MaxValue
		}.Serialize(value);
	}

	private static T Deserialize<T>(string value)
	{
		return new JavaScriptSerializer
		{
			MaxJsonLength = int.MaxValue
		}.Deserialize<T>(value);
	}

	private static void DeleteQuietly(string directory)
	{
		try
		{
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, recursive: true);
			}
		}
		catch
		{
		}
	}

	private static void DeleteFileQuietly(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}

	private sealed class BackupManifest
	{
		public string Schema { get; set; } = "xiao-yang-radial-backup/v3";

		public string Plugin { get; set; } = "XY SOLIDWORKS 轮盘";

		public string ConfigSha256 { get; set; } = "";

		public string ShortcutSha256 { get; set; } = "";

		public bool HasConfig { get; set; } = true;

		public bool HasShortcuts { get; set; }

		public string Version { get; set; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";

		public string ExportedAt { get; set; } = DateTimeOffset.Now.ToString("O");
	}
}
