using System;
using System.Collections.Generic;
using System.Linq;

namespace XY.SolidWorksPlugin;

internal static class ProcessRecognitionModes
{
	public const string Quick = "quick";
	public const string Deep = "deep";

	public static string Normalize(string value)
	{
		return string.Equals((value ?? "").Trim(), Quick, StringComparison.OrdinalIgnoreCase) ? Quick : Deep;
	}
}

internal static class SurfaceRecognitionModes
{
	public const string Material = "material";
	public const string Property = "property";
	public const string Fixed = "fixed";

	public static string Normalize(string value)
	{
		string normalized = (value ?? "").Trim().ToLowerInvariant();
		if (normalized == Property) return Property;
		if (normalized == Fixed) return Fixed;
		return Material;
	}
}

internal sealed class ProcessRecognitionConfig
{
	public bool Enabled { get; set; } = true;

	public string DefaultMode { get; set; } = ProcessRecognitionModes.Deep;

	public string SummaryPropertyName { get; set; } = "工序";

	public string Separator { get; set; } = "-";

	public bool SyncWatermark { get; set; } = true;

	public bool WriteCraftDescription { get; set; } = true;

	public string CraftDescriptionPropertyName { get; set; } = "工艺说明";

	public bool WriteGeometryProperties { get; set; } = true;

	public string ThicknessPropertyName { get; set; } = "板厚";

	public string BendCountPropertyName { get; set; } = "折弯下刀数";

	public string CutoutCountPropertyName { get; set; } = "内轮廓数";

	public string PartTypePropertyName { get; set; } = "类型";

	public string ProcessTypePropertyName { get; set; } = "工序类型";

	public string RemarkPropertyName { get; set; } = "备注";

	public string OutsourcePropertyName { get; set; } = "外加工";

	public int AssemblyBatchSize { get; set; } = 1;

	public int AssemblyBatchDelayMs { get; set; } = 800;

	public bool AssemblyAutoRecognition { get; set; } = true;

	public bool IncludeDeburr { get; set; } = true;

	public string DeburrProcessName { get; set; } = "去毛刺";

	public bool IncludeInspection { get; set; } = true;

	public string InspectionProcessName { get; set; } = "检验";

	public bool IncludePackaging { get; set; } = true;

	public string PackagingProcessName { get; set; } = "包装";

	public bool IncludeShipping { get; set; } = true;

	public string ShippingProcessName { get; set; } = "出货";

	public string AdditionalProcesses { get; set; } = "";

	public string ProcessNameMappings { get; set; } = "";

	public string ProcessCombinations { get; set; } = ProcessPresets.DefaultCombinations;
	// A selection catalogue is separate from AdditionalProcesses, which appends to every route.
	public string AvailableProcessNames { get; set; } = "";
	public List<ProcessRecognitionRule> CustomRules { get; set; } = new List<ProcessRecognitionRule>();
	// References ProcessCombinations by name; does not duplicate process sequences.
	public string ProcessTypeGroups { get; set; } = ProcessPresets.DefaultTypeGroups;
	public string ProcessCategories { get; set; } = ProcessPresets.DefaultCategories;

	// 类型=工序1>工序2 . Used when a model gives no machining evidence; the
	// declared type alone then produces a sensible starting chain.
	public string PartTypeChains { get; set; } = PartTypeCatalog.DefaultChains;

	public static IReadOnlyList<string> ResolveTypeChain(ProcessRecognitionConfig config, string type)
	{
		string key = (type ?? "").Trim();
		if (key.Length == 0) return Array.Empty<string>();
		string sequence = ParseCombinations(config?.PartTypeChains)
			.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
		IReadOnlyList<string> steps = CombinationSteps(sequence);
		return steps.Count > 0 ? steps : PartTypeCatalog.DefaultChain(key);
	}

	public static IReadOnlyList<KeyValuePair<string, string>> ParseCombinations(string value)
	{
		return (value ?? "").Split(new[] { '|', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(item => item.Split(new[] { '=' }, 2))
			.Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && CombinationSteps(parts[1]).Count > 0)
			.Select(parts => new KeyValuePair<string, string>(Limit(parts[0]), string.Join(">", CombinationSteps(parts[1]))))
			.GroupBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase).Select(group => group.Last()).Take(50).ToList();
	}
	public static IReadOnlyList<string> CombinationSteps(string value)
		=> (value ?? "").Split(new[] { '>', '→' }, StringSplitOptions.RemoveEmptyEntries).Select(Limit)
			.Where(item => item.Length > 0).Take(30).ToList();
	public static IReadOnlyList<RecognizedProcess> ResolveCombination(ProcessRecognitionConfig config, string name)
	{
		string sequence = ParseCombinations(config?.ProcessCombinations)
			.FirstOrDefault(pair => string.Equals(pair.Key, (name ?? "").Trim(), StringComparison.CurrentCultureIgnoreCase)).Value;
		return CombinationSteps(sequence).Select(step => new RecognizedProcess { Name = step, Confidence = 100, Evidence = "用户自定义工序组合：" + name }).ToList();
	}

	public bool InspectionAfterSurface { get; set; }

	public string SurfaceMode { get; set; } = SurfaceRecognitionModes.Material;

	public string SurfacePropertyName { get; set; } = "表面处理";

	public string DefaultSurfaceProcess { get; set; } = "表面处理";

	public string SurfaceMaterialRules { get; set; } = "AISI=拉丝|SUS=拉丝|SS=拉丝|不锈钢=拉丝|stainless=拉丝|Q235=烤漆|SPCC=烤漆";

	public static void Normalize(ProcessRecognitionConfig config)
	{
		if (config == null) return;
		config.DefaultMode = ProcessRecognitionModes.Normalize(config.DefaultMode);
		config.SummaryPropertyName = Text(config.SummaryPropertyName, "工序", 64);
		config.Separator = Text(config.Separator, "-", 8);
		config.CraftDescriptionPropertyName = Text(config.CraftDescriptionPropertyName, "工艺说明", 64);
		config.ThicknessPropertyName = Text(config.ThicknessPropertyName, "板厚", 64);
		config.BendCountPropertyName = Text(config.BendCountPropertyName, "折弯下刀数", 64);
		config.CutoutCountPropertyName = Text(config.CutoutCountPropertyName, "内轮廓数", 64);
		config.PartTypePropertyName = Text(config.PartTypePropertyName, "类型", 64);
		config.ProcessTypePropertyName = Text(config.ProcessTypePropertyName, "工序类型", 64);
		config.RemarkPropertyName = Text(config.RemarkPropertyName, "备注", 64);
		config.OutsourcePropertyName = Text(config.OutsourcePropertyName, "外加工", 64);
		config.AssemblyBatchSize = Math.Max(1, Math.Min(10, config.AssemblyBatchSize));
		config.AssemblyBatchDelayMs = Math.Max(800, Math.Min(5000, config.AssemblyBatchDelayMs));
		config.DeburrProcessName = Text(config.DeburrProcessName, "去毛刺", 64);
		config.InspectionProcessName = Text(config.InspectionProcessName, "检验", 64);
		config.PackagingProcessName = Text(config.PackagingProcessName, "包装", 64);
		config.ShippingProcessName = Text(config.ShippingProcessName, "出货", 64);
		config.AdditionalProcesses = string.Join("|", ParseAdditionalProcesses(config.AdditionalProcesses));
		config.ProcessNameMappings = string.Join("|", ParseProcessNameMappings(config.ProcessNameMappings).Select(pair => pair.Key + "=" + pair.Value));
		config.ProcessCombinations = string.Join("|", ParseCombinations(config.ProcessCombinations).Select(pair => pair.Key + "=" + pair.Value));
		config.ProcessTypeGroups = string.Join("|", ParseProcessNameMappings(config.ProcessTypeGroups).Select(pair => pair.Key + "=" + pair.Value));
		config.ProcessCategories = string.Join("|", ParseProcessNameMappings(config.ProcessCategories).Select(pair => pair.Key + "=" + pair.Value));
		config.PartTypeChains = string.Join("|", ParseCombinations(config.PartTypeChains).Select(pair => pair.Key + "=" + pair.Value));
		if (config.PartTypeChains.Length == 0) config.PartTypeChains = PartTypeCatalog.DefaultChains;
		config.SurfaceMode = SurfaceRecognitionModes.Normalize(config.SurfaceMode);
		config.SurfacePropertyName = Text(config.SurfacePropertyName, "表面处理", 64);
		config.DefaultSurfaceProcess = Text(config.DefaultSurfaceProcess, "表面处理", 64);
		config.SurfaceMaterialRules = NormalizeRules(config.SurfaceMaterialRules);
	}

	public static IReadOnlyList<KeyValuePair<string, string>> ParseProcessNameMappings(string value)
	{
		return (value ?? "")
			.Split(new[] { '|', '\r', '\n', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(item => item.Split(new[] { '=' }, 2))
			.Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
			.Select(parts => new KeyValuePair<string, string>(Limit(parts[0]), Limit(parts[1])))
			.Where(pair => pair.Key.Length > 0 && pair.Value.Length > 0)
			.GroupBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase)
			.Select(group => group.Last())
			.Take(50)
			.ToList();
	}

	public static IReadOnlyList<string> ParseAdditionalProcesses(string value)
	{
		return (value ?? "")
			.Split(new[] { '|', '\r', '\n', ';', '；', '，', ',' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(item => item.Trim())
			.Where(item => item.Length > 0)
			.Select(item => item.Length <= 64 ? item : item.Substring(0, 64))
			.Distinct(StringComparer.CurrentCultureIgnoreCase)
			.Take(20)
			.ToList();
	}

	public static IReadOnlyList<KeyValuePair<string, string>> ParseSurfaceMaterialRules(string rules)
	{
		return (rules ?? "")
			.Split(new[] { '|', '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(item => item.Split(new[] { '=' }, 2))
			.Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
			.Select(parts => new KeyValuePair<string, string>(parts[0].Trim(), parts[1].Trim()))
			.GroupBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase)
			.Select(group => group.Last())
			.ToList();
	}

	private static string NormalizeRules(string rules)
	{
		IReadOnlyList<KeyValuePair<string, string>> parsed = ParseSurfaceMaterialRules(rules);
		if (parsed.Count == 0)
		{
			parsed = ParseSurfaceMaterialRules("AISI=拉丝|SUS=拉丝|SS=拉丝|不锈钢=拉丝|stainless=拉丝|Q235=烤漆|SPCC=烤漆");
		}
		return string.Join("|", parsed.Select(pair => pair.Key + "=" + pair.Value));
	}

	private static string Text(string value, string fallback, int maxLength)
	{
		string normalized = (value ?? "").Trim();
		if (normalized.Length == 0) normalized = fallback;
		return normalized.Length <= maxLength ? normalized : normalized.Substring(0, maxLength);
	}

	private static string Limit(string value)
	{
		string normalized = (value ?? "").Trim();
		return normalized.Length <= 64 ? normalized : normalized.Substring(0, 64);
	}
}
