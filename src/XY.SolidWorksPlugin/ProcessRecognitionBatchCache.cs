using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace XY.SolidWorksPlugin;

internal sealed class ProcessRecognitionBatchCache
{
	internal static string SettingsKey(ProcessRecognitionConfig config) => CanonicalJson(config);
	// .NET Framework reflection property order can change after individual
	// properties are cached. Compare values, while keeping rule/list order.
	internal static string CanonicalJson(object value)
	{
		var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
		string Encode(object item)
		{
			// Settings are compared on every background resume. Read known
			// persisted settings directly instead of serializing and parsing the
			// whole large preset catalogue before serializing it a second time.
			// Cache metadata only: mutable rules and values are read on each call.
			if (item is ProcessRecognitionConfig || item is ProcessRecognitionRule)
			{
				PropertyInfo[] properties = CanonicalProperties.GetOrAdd(item.GetType(), type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
					.Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
					.OrderBy(property => property.Name, StringComparer.Ordinal).ToArray());
				return "{" + string.Join(",", properties.Select(property => serializer.Serialize(property.Name) + ":" + Encode(property.GetValue(item, null)))) + "}";
			}
			if (item is IDictionary<string, object> map)
				return "{" + string.Join(",", map.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => serializer.Serialize(pair.Key) + ":" + Encode(pair.Value))) + "}";
			if (item is System.Collections.IEnumerable list && !(item is string))
				return "[" + string.Join(",", list.Cast<object>().Select(Encode)) + "]";
			if (item != null && !(item is string) && !item.GetType().IsValueType)
				return Encode(serializer.DeserializeObject(serializer.Serialize(item)));
			return serializer.Serialize(item);
		}
		// Preserve the general-purpose serializer semantics for other callers.
		// The hot settings path is the known configuration object above.
		return Encode(value is ProcessRecognitionConfig || value is ProcessRecognitionRule
			? value : serializer.DeserializeObject(serializer.Serialize(value)));
	}
	private static readonly ConcurrentDictionary<Type, PropertyInfo[]> CanonicalProperties = new ConcurrentDictionary<Type, PropertyInfo[]>();
	private const int MaxEntries = 10000;
	private const string CacheSchemaVersion = "process-recognition-v17-configuration-property-scope";
	private readonly string _path;
	private readonly string _fingerprint;
	private readonly string _separator;
	private readonly HashSet<string> _compatibleFingerprints;
	private readonly Dictionary<string, CacheEntry> _entries;
	private int _pendingWrites;

	public ProcessRecognitionBatchCache(ProcessRecognitionConfig config)
		: this(config, Path.Combine(ConfigStore.BaseDirectory, "process-recognition-cache.json"))
	{
	}

	internal ProcessRecognitionBatchCache(ProcessRecognitionConfig config, string cachePath)
	{
		_path = cachePath ?? throw new ArgumentNullException(nameof(cachePath));
		_fingerprint = Fingerprint(config);
		_separator = config?.Separator ?? "-";
		_compatibleFingerprints = new HashSet<string>(StringComparer.Ordinal)
		{
			_fingerprint
		};
		_entries = Load(_path);
	}

	public bool TryGet(string path, string configuration, out string classification, out string process)
	{
		classification = "";
		process = "";
		if (!TryFile(path, out FileInfo file)) return false;
		if (!_entries.TryGetValue(Key(path, configuration), out CacheEntry entry)) return false;
		if (entry.Length != file.Length || entry.LastWriteUtcTicks != file.LastWriteTimeUtc.Ticks || !_compatibleFingerprints.Contains(entry.Fingerprint ?? "")) return false;
		entry.LastUsedUtcTicks = DateTime.UtcNow.Ticks;
		classification = entry.Classification ?? "";
		process = entry.Process ?? "";
		return process.Length > 0;
	}

	public void Put(string path, string configuration, string classification, string process)
	{
		if (!TryFile(path, out FileInfo file) || string.IsNullOrWhiteSpace(process)) return;
		_entries[Key(path, configuration)] = new CacheEntry
		{
			Length = file.Length,
			LastWriteUtcTicks = file.LastWriteTimeUtc.Ticks,
			LastUsedUtcTicks = DateTime.UtcNow.Ticks,
			Fingerprint = _fingerprint,
			Classification = classification ?? "",
			Process = process.Trim()
		};
		if (++_pendingWrites >= 100) Save();
	}

	internal bool TryGetSnapshot(string path, string configuration, bool requireDeep, out ProcessRecognitionInput input, out IReadOnlyList<RecognizedProcess> processes)
		=> TryGetSnapshotInfo(path, configuration, requireDeep, out input, out processes, out _);
	internal bool TryGetSnapshotInfo(string path, string configuration, bool requireDeep, out ProcessRecognitionInput input, out IReadOnlyList<RecognizedProcess> processes, out bool snapshotDeep)
	{
		snapshotDeep = false;
		input = null; processes = Array.Empty<RecognizedProcess>();
		if (!TryGet(path, configuration, out _, out _)) return false;
		var entry = _entries[Key(path, configuration)];
		if (string.IsNullOrWhiteSpace(entry.SnapshotJson)) return false;
		try {
			var snapshot = ProcessRecognitionSnapshot.Decode(entry.SnapshotJson);
			if (snapshot?.Input == null || (requireDeep && !snapshot.Deep)) return false;
			input = snapshot.Input.Restore(); processes = snapshot.Processes ?? Array.Empty<RecognizedProcess>(); snapshotDeep = snapshot.Deep;
			return processes.Count > 0;
		} catch (Exception ex) { Logger.Trace("Process snapshot could not be restored: " + ex.Message); return false; }
	}

	internal void PutSnapshot(string path, string configuration, string classification, ProcessRecognitionInput input, IEnumerable<RecognizedProcess> processes, bool deep)
	{
		if (input == null || processes == null || !TryFile(path, out _)) return;
		var rows = processes.ToArray();
		string json = ProcessRecognitionSnapshot.Create(input, rows, deep).Encode();
		Put(path, configuration, classification, ProcessRecognitionService.Join(rows.Select(p => p.Name), _separator));
		if (_entries.TryGetValue(Key(path, configuration), out var entry)) { entry.SnapshotJson = json; _pendingWrites++; }
	}

	public void Save()
	{
		if (_pendingWrites == 0) return;
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_path));
			Dictionary<string, CacheEntry> trimmed = _entries
				.OrderByDescending(pair => pair.Value?.LastUsedUtcTicks ?? 0)
				.Take(MaxEntries)
				.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
			JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 20 };
			File.WriteAllText(_path, serializer.Serialize(trimmed), new UTF8Encoding(false));
			_pendingWrites = 0;
		}
		catch (Exception ex)
		{
			Logger.Trace("Process recognition cache save skipped: " + ex.Message);
		}
	}

	private static Dictionary<string, CacheEntry> Load(string path)
	{
		try
		{
			if (!File.Exists(path)) return new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
			JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 20 };
			Dictionary<string, CacheEntry> entries = serializer.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(path, Encoding.UTF8));
			return entries == null
				? new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase)
				: new Dictionary<string, CacheEntry>(entries, StringComparer.OrdinalIgnoreCase);
		}
		catch
		{
			return new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
		}
	}

	private static string Fingerprint(ProcessRecognitionConfig config)
	{
		config ??= new ProcessRecognitionConfig();
		ProcessRecognitionConfig.Normalize(config);
		// Functional releases should not invalidate thousands of unchanged parts.
		// Bump this schema only when recognition semantics or cached fields change.
		return FingerprintValue(config, CacheSchemaVersion);
	}

	private static string LegacyFingerprint(ProcessRecognitionConfig config, string assemblyVersion)
	{
		config ??= new ProcessRecognitionConfig();
		ProcessRecognitionConfig.Normalize(config);
		return FingerprintValue(config, assemblyVersion);
	}

	private static string FingerprintValue(ProcessRecognitionConfig config, string version)
	{
		string value = version + "|"
			+ config.SummaryPropertyName + "|" + config.Separator + "|" + config.WriteCraftDescription + "|" + config.CraftDescriptionPropertyName + "|" + config.WriteGeometryProperties + "|"
			+ config.ThicknessPropertyName + "|" + config.BendCountPropertyName + "|" + config.CutoutCountPropertyName + "|"
			+ config.IncludeDeburr + "|" + config.IncludeInspection + "|" + config.InspectionAfterSurface + "|" + config.IncludePackaging + "|" + config.IncludeShipping + "|"
			+ config.DeburrProcessName + "|" + config.InspectionProcessName + "|" + config.PackagingProcessName + "|" + config.ShippingProcessName + "|"
			+ config.AdditionalProcesses + "|" + config.ProcessNameMappings + "|" + config.ProcessCombinations + "|"
			+ config.SurfaceMode + "|" + config.SurfacePropertyName + "|" + config.DefaultSurfaceProcess + "|" + config.SurfaceMaterialRules + "|"
			+ config.PartTypePropertyName + "|" + config.OutsourcePropertyName;
		value += "|" + config.ProcessTypePropertyName + "|" + config.RemarkPropertyName;
		value += "|" + config.ProcessTypeGroups + "|" + config.PartTypeChains + "|" + config.ProcessCategories;
		value += "|" + CanonicalJson(config.CustomRules);
		using SHA256 sha = SHA256.Create();
		return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
	}

	private static bool TryFile(string path, out FileInfo file)
	{
		file = null;
		try
		{
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
			file = new FileInfo(path);
			return true;
		}
		catch { return false; }
	}

	internal static string KeyForConfiguration(string path, string configuration) => Key(path, configuration);

	private static string Key(string path, string configuration)
	{
		string file;
		try { file = Path.GetFullPath(path ?? "").Trim().ToUpperInvariant(); }
		catch { file = (path ?? "").Trim().ToUpperInvariant(); }
		return file + "\n" + (configuration ?? "").Trim().ToUpperInvariant();
	}

	private sealed class CacheEntry
	{
		public long Length { get; set; }
		public long LastWriteUtcTicks { get; set; }
		public long LastUsedUtcTicks { get; set; }
		public string Fingerprint { get; set; } = "";
		public string Classification { get; set; } = "";
		public string Process { get; set; } = "";
		public string SnapshotJson { get; set; } = "";
	}
}
