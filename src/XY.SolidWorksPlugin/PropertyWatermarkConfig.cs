using System;
using System.Collections.Generic;
using System.Linq;

namespace XY.SolidWorksPlugin;

internal sealed class PropertyWatermarkConfig
{
	internal PropertyWatermarkConfig Clone() => new System.Web.Script.Serialization.JavaScriptSerializer()
		.Deserialize<PropertyWatermarkConfig>(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(this));
	public bool Enabled { get; set; }
	public string FontName { get; set; } = "SimSun";
	public float FontSize { get; set; } = 9f;
	public int OpacityPercent { get; set; } = 82;
	public int DisplayScalePercent { get; set; } = 100;
	public string TextColor { get; set; } = "#1E293B";
	public bool MoveEnabled { get; set; }
	public bool ReadSelectedPartProperties { get; set; }
	public string PositionMode { get; set; } = "bottom-left";
	public int OffsetX { get; set; } = 18;
	public int OffsetY { get; set; } = 50;
	public List<PropertyWatermarkField> Fields { get; set; } = DefaultFields();

	public static List<PropertyWatermarkField> DefaultFields()
	{
		return new List<PropertyWatermarkField>
		{
			new PropertyWatermarkField { DisplayName = "文件名", Source = "原生属性", PropertyName = "文件名" },
			new PropertyWatermarkField { DisplayName = "材质", Source = "自定义属性", PropertyName = "材质" },
			new PropertyWatermarkField { DisplayName = "厚度", Source = "原生属性", PropertyName = "厚度" },
			new PropertyWatermarkField { DisplayName = "数量", Source = "自定义属性", PropertyName = "数量" },
			new PropertyWatermarkField { DisplayName = "工序", Source = "自定义属性", PropertyName = "工序" }
		};
	}

	// Merge only edits made by this settings window. A later move, toggle or
	// independent settings change must not be replaced by an old draft.
	internal static PropertyWatermarkConfig Merge(PropertyWatermarkConfig baseline, PropertyWatermarkConfig draft, PropertyWatermarkConfig current)
	{
		PropertyWatermarkConfig result = current.Clone();
		result.FontName = MergeValue("字体", baseline.FontName, draft.FontName, current.FontName);
		result.FontSize = MergeValue("字号", baseline.FontSize, draft.FontSize, current.FontSize);
		result.OpacityPercent = MergeValue("透明度", baseline.OpacityPercent, draft.OpacityPercent, current.OpacityPercent);
		result.DisplayScalePercent = MergeValue("显示大小", baseline.DisplayScalePercent, draft.DisplayScalePercent, current.DisplayScalePercent);
		result.TextColor = MergeValue("文字颜色", baseline.TextColor, draft.TextColor, current.TextColor);
		result.MoveEnabled = MergeValue("移动开关", baseline.MoveEnabled, draft.MoveEnabled, current.MoveEnabled);
		result.ReadSelectedPartProperties = MergeValue("读取选择零件参数", baseline.ReadSelectedPartProperties, draft.ReadSelectedPartProperties, current.ReadSelectedPartProperties);
		var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
		string fields = MergeValue("显示字段", serializer.Serialize(baseline.Fields), serializer.Serialize(draft.Fields), serializer.Serialize(current.Fields));
		result.Fields = serializer.Deserialize<List<PropertyWatermarkField>>(fields);
		return result;
	}

	private static T MergeValue<T>(string label, T baseline, T draft, T current)
	{
		if (EqualityComparer<T>.Default.Equals(draft, baseline)) return current;
		if (EqualityComparer<T>.Default.Equals(current, baseline) || EqualityComparer<T>.Default.Equals(current, draft)) return draft;
		throw new InvalidOperationException("水印“" + label + "”已在其他窗口改变，请重新打开设置后再修改；未覆盖最新设置。");
	}

	public static void Normalize(PropertyWatermarkConfig config)
	{
		if (config == null) return;
		config.FontName = "SimSun";
		config.FontSize = Math.Max(7f, Math.Min(22f, config.FontSize));
		config.OpacityPercent = Math.Max(20, Math.Min(100, config.OpacityPercent));
		config.DisplayScalePercent = Math.Max(50, Math.Min(200, config.DisplayScalePercent));
		config.TextColor = NormalizeColor(config.TextColor, "#1E293B");
		config.PositionMode = string.Equals(config.PositionMode, "custom", StringComparison.OrdinalIgnoreCase) ? "custom" : "bottom-left";
		config.OffsetX = Math.Max(-2000, Math.Min(2000, config.OffsetX));
		config.OffsetY = Math.Max(-2000, Math.Min(2000, config.OffsetY));
		if (config.Fields == null || config.Fields.Count == 0) config.Fields = DefaultFields();
		config.Fields = config.Fields.Where(f => f != null).Take(20).Select(f =>
		{
			f.DisplayName = (f.DisplayName ?? "").Trim();
			bool legacyFileName = string.Equals(f.Source, "文件名", StringComparison.OrdinalIgnoreCase);
			f.Source = string.Equals(f.Source, "原生属性", StringComparison.OrdinalIgnoreCase) || legacyFileName ? "原生属性" : "自定义属性";
			f.PropertyName = CanonicalPropertyName((f.PropertyName ?? "").Trim());
			if (legacyFileName && f.PropertyName.Length == 0) f.PropertyName = "文件名";
			return f;
		}).Where(f => f.DisplayName.Length > 0 && f.PropertyName.Length > 0).ToList();
	}

	public static bool EnsureProcessField(PropertyWatermarkConfig config, string propertyName = "工序")
	{
		if (config == null) return false;
		config.Fields ??= new List<PropertyWatermarkField>();
		string resolvedPropertyName = string.IsNullOrWhiteSpace(propertyName) ? "工序" : propertyName.Trim();
		bool exists = config.Fields.Any(field => field != null &&
			(string.Equals(field.PropertyName, resolvedPropertyName, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(field.PropertyName, "工序", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(field.PropertyName, "工艺流程", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(field.PropertyName, "工艺", StringComparison.OrdinalIgnoreCase)));
		if (exists || config.Fields.Count >= 20) return false;
		config.Fields.Add(new PropertyWatermarkField { DisplayName = "工序", Source = "自定义属性", PropertyName = resolvedPropertyName });
		return true;
	}

	public static bool EnsureRecognitionFields(PropertyWatermarkConfig config, ProcessRecognitionConfig process)
	{
		// Watermark fields are user-owned. Process recognition may refresh the
		// watermark after a confirmed write, but must never append fields such as
		// remark or process type behind the user's back.
		return false;
	}

	private static bool EnsureNativeThicknessField(PropertyWatermarkConfig config)
	{
		config.Fields ??= new List<PropertyWatermarkField>();
		if (config.Fields.Any(field => field != null && ModelThicknessResolver.IsThicknessProperty(field.PropertyName))) return false;
		if (config.Fields.Count >= 20) return false;
		config.Fields.Add(new PropertyWatermarkField { DisplayName = "厚度", Source = "原生属性", PropertyName = "厚度" });
		return true;
	}

	private static bool EnsureConfiguredField(PropertyWatermarkConfig config, string displayName, string propertyName, params string[] aliases)
	{
		config.Fields ??= new List<PropertyWatermarkField>();
		string resolvedPropertyName = string.IsNullOrWhiteSpace(propertyName) ? displayName : propertyName.Trim();
		PropertyWatermarkField existing = config.Fields.FirstOrDefault(field => field != null &&
			(string.Equals(field.PropertyName, resolvedPropertyName, StringComparison.OrdinalIgnoreCase)
			|| aliases.Any(alias => string.Equals(field.PropertyName, alias, StringComparison.OrdinalIgnoreCase))));
		if (existing != null)
		{
			if (string.Equals(existing.PropertyName, resolvedPropertyName, StringComparison.OrdinalIgnoreCase)) return false;
			existing.PropertyName = resolvedPropertyName;
			existing.Source = "自定义属性";
			return true;
		}
		if (config.Fields.Count >= 20) return false;
		config.Fields.Add(new PropertyWatermarkField { DisplayName = displayName, Source = "自定义属性", PropertyName = resolvedPropertyName });
		return true;
	}

	private static string CanonicalPropertyName(string name)
	{
		if (string.Equals(name, "材料", StringComparison.OrdinalIgnoreCase)) return "材质";
		if (string.Equals(name, "单重", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "重量", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Weight", StringComparison.OrdinalIgnoreCase)) return "质量";
		return name;
	}

	private static string NormalizeColor(string value, string fallback)
	{
		try { return System.Drawing.ColorTranslator.ToHtml(System.Drawing.ColorTranslator.FromHtml(value ?? fallback)); }
		catch { return fallback; }
	}
}

internal sealed class PropertyWatermarkField
{
	public bool Enabled { get; set; } = true;
	public string DisplayName { get; set; } = "属性";
	public string Source { get; set; } = "自定义属性";
	public string PropertyName { get; set; } = "";
}
