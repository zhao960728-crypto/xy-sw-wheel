using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace XY.SolidWorksPlugin;

// Wheel surface colours. Kept separate from PluginConfig so a scene can carry
// its own appearance and the plugin can supply a global fallback appearance.
// The wheel renderer reads every colour from here: nothing about the surface,
// border, hover, selection or caption is hard coded in the paint paths any more.
internal sealed class WheelAppearance
{
	// Preset ids. A scene keeps the id it was created from so the settings
	// dialog can show which preset it is on; "Custom" means the user edited
	// colours away from the preset.
	public const string PresetDarkGlass = "darkGlass";
	public const string PresetClassic = "classic";
	public const string PresetNative = "native";
	public const string PresetMidnight = "midnight";
	public const string PresetCustom = "custom";

	// The palette the CAD wheel ships with. It is the default: the wheel is
	// expected to read the same as its CAD counterpart, and every other look is
	// an explicit choice the user makes in the palette dialog.
	public string PresetId { get; set; } = PresetClassic;
	// Missing in historical configuration files. An explicit choice in today's
	// palette must not be mistaken for an old default during migration.
	public bool ExplicitPreset { get; set; }

	public string FillMode { get; set; } = "Solid";
	public string StartColor { get; set; } = "#FFFFFF";
	public string EndColor { get; set; } = "#DDF4F5";
	public int GradientAngle { get; set; } = 90;
	public string BorderColor { get; set; } = "#D7DFE6";
	public string HoverColor { get; set; } = "#008CA2";
	public string SelectedColor { get; set; } = "#008CA2";
	public string TextColor { get; set; } = "#1C2631";

	// Glass adds the frosted rim and top sheen; ShadowPercent drives the soft
	// drop shadow drawn under the disc. 0 disables the shadow entirely. Off by
	// default because the CAD wheel has neither.
	public bool Glass { get; set; }
	public int ShadowPercent { get; set; }

	public void Normalize()
	{
		PresetId = NormalizePresetId(PresetId);
		FillMode = string.Equals(FillMode, "Gradient", StringComparison.OrdinalIgnoreCase) ? "Gradient" : "Solid";
		StartColor = NormalizeColor(StartColor, "#FFFFFF");
		EndColor = NormalizeColor(EndColor, "#DDF4F5");
		BorderColor = NormalizeColor(BorderColor, "#D7DFE6");
		HoverColor = NormalizeColor(HoverColor, "#008CA2");
		SelectedColor = NormalizeColor(SelectedColor, "#008CA2");
		TextColor = string.IsNullOrWhiteSpace(TextColor) ? "" : NormalizeColor(TextColor, "#1C2631");
		GradientAngle = ((GradientAngle % 360) + 360) % 360;
		ShadowPercent = Math.Max(0, Math.Min(100, ShadowPercent));
	}

	public WheelAppearance Clone() => new WheelAppearance
	{
		PresetId = PresetId,
		ExplicitPreset = ExplicitPreset,
		FillMode = FillMode,
		StartColor = StartColor,
		EndColor = EndColor,
		GradientAngle = GradientAngle,
		BorderColor = BorderColor,
		HoverColor = HoverColor,
		SelectedColor = SelectedColor,
		TextColor = TextColor,
		Glass = Glass,
		ShadowPercent = ShadowPercent
	};

	// --- Presets ---

	public static WheelAppearance DarkGlass() => new WheelAppearance
	{
		PresetId = PresetDarkGlass,
		FillMode = "Gradient",
		StartColor = "#2A3846",
		EndColor = "#131B24",
		GradientAngle = 90,
		BorderColor = "#63C9E2",
		HoverColor = "#38C8E9",
		SelectedColor = "#8FE6F8",
		TextColor = "#EAF4FA",
		Glass = true,
		ShadowPercent = 55
	};

	public static WheelAppearance Classic() => new WheelAppearance
	{
		PresetId = PresetClassic,
		FillMode = "Solid",
		StartColor = "#FFFFFF",
		EndColor = "#DDF4F5",
		GradientAngle = 90,
		BorderColor = "#D7DFE6",
		HoverColor = "#008CA2",
		SelectedColor = "#008CA2",
		TextColor = "#1C2631",
		Glass = false,
		ShadowPercent = 0
	};

	public static WheelAppearance Native() => new WheelAppearance
	{
		PresetId = PresetNative,
		FillMode = "Solid",
		StartColor = "#FCFCFC",
		EndColor = "#F2F4F6",
		GradientAngle = 90,
		BorderColor = "#A6A6A6",
		HoverColor = "#45709D",
		SelectedColor = "#45709D",
		TextColor = "#1C2631",
		Glass = false,
		ShadowPercent = 0
	};

	public static WheelAppearance Midnight() => new WheelAppearance
	{
		PresetId = PresetMidnight,
		FillMode = "Gradient",
		StartColor = "#1F2A44",
		EndColor = "#0B1020",
		GradientAngle = 135,
		BorderColor = "#7C9CFF",
		HoverColor = "#5C7CFA",
		SelectedColor = "#A5B8FF",
		TextColor = "#E8EDFF",
		Glass = true,
		ShadowPercent = 60
	};

	public static WheelAppearance FromPreset(string presetId)
	{
		WheelAppearance result;
		switch (NormalizePresetId(presetId))
		{
			case PresetDarkGlass: result = DarkGlass(); break;
			case PresetNative: result = Native(); break;
			case PresetMidnight: result = Midnight(); break;
			default: result = Classic(); break;
		}
		result.ExplicitPreset = true;
		return result;
	}

	// True when this appearance is still exactly what the dark-glass default
	// produced. Those scenes were never hand-tuned - the visual refresh put them
	// there - so they can follow the wheel back to the CAD palette.
	public bool IsUntouchedDarkGlass() =>
		!ExplicitPreset && string.Equals(PresetId, PresetDarkGlass, StringComparison.OrdinalIgnoreCase)
		&& string.Equals(FillMode, "Gradient", StringComparison.OrdinalIgnoreCase)
		&& ((GradientAngle % 360) + 360) % 360 == 90
		&& Glass && ShadowPercent == 55
		&& IsColor(StartColor, "#2A3846") && IsColor(EndColor, "#131B24")
		&& IsColor(BorderColor, "#63C9E2") && IsColor(HoverColor, "#38C8E9")
		&& IsColor(SelectedColor, "#8FE6F8") && IsColor(TextColor, "#EAF4FA");

	public static string NormalizePresetId(string value)
	{
		string id = (value ?? "").Trim();
		if (string.Equals(id, PresetClassic, StringComparison.OrdinalIgnoreCase)) return PresetClassic;
		if (string.Equals(id, PresetNative, StringComparison.OrdinalIgnoreCase)) return PresetNative;
		if (string.Equals(id, PresetMidnight, StringComparison.OrdinalIgnoreCase)) return PresetMidnight;
		if (string.Equals(id, PresetCustom, StringComparison.OrdinalIgnoreCase)) return PresetCustom;
		return PresetDarkGlass;
	}

	// True while the appearance still carries the pre-visual-refresh defaults,
	// which is how a scene written before colours became live is recognised.
	// Such a scene is upgraded to the new default instead of staying stale.
	// TextColor being empty is part of the signature too: the new presets all
	// set an explicit caption colour, so a blank one means the value came from
	// the old code path.
	public bool IsLegacyDefault()
	{
		return !ExplicitPreset && !string.Equals(PresetId, PresetCustom, StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(FillMode, "Gradient", StringComparison.OrdinalIgnoreCase)
			&& IsColor(StartColor, "#FFFFFF")
			&& IsColor(EndColor, "#DDF4F5")
			&& IsColor(BorderColor, "#D7DFE6")
			&& IsColor(HoverColor, "#008CA2")
			&& IsColor(SelectedColor, "#008CA2")
			&& string.IsNullOrWhiteSpace(TextColor);
	}

	public static bool IsColor(string value, string hex) =>
		string.Equals((value ?? "").Trim(), hex, StringComparison.OrdinalIgnoreCase);

	// --- Colour accessors ---

	// Caller owns the returned brush. Colours stay opaque; wheel opacity is applied separately.
	public Brush CreateBrush(RectangleF bounds) => CreateBrush(bounds, 100);

	public Brush CreateBrush(RectangleF bounds, int opacityPercent)
	{
		Color start = ParseColor(StartColor, Color.FromArgb(42, 56, 70));
		Color end = ParseColor(EndColor, Color.FromArgb(19, 27, 36));
		Brush brush;
		if (!string.Equals(FillMode, "Gradient", StringComparison.OrdinalIgnoreCase) || bounds.Width <= 0 || bounds.Height <= 0)
		{
			brush = new SolidBrush(start);
		}
		else
		{
			brush = new LinearGradientBrush(bounds, start, end, GradientAngle);
		}
		return brush;
	}

	public Color GetSurfaceColor() => ParseColor(StartColor, Color.FromArgb(42, 56, 70));

	public Color GetBorderColor() => ParseColor(BorderColor, Color.FromArgb(99, 201, 226));

	public Color GetHoverColor() => ParseColor(HoverColor, Color.FromArgb(56, 200, 233));

	public Color GetSelectedColor() => ParseColor(SelectedColor, Color.FromArgb(143, 230, 248));

	// An empty TextColor means "pick the readable one for this surface", which
	// keeps legacy configurations legible after the surface turns dark.
	public Color GetTextColor()
	{
		if (!string.IsNullOrWhiteSpace(TextColor)) return ParseColor(TextColor, Color.FromArgb(234, 244, 250));
		return IsDarkSurface() ? Color.FromArgb(238, 245, 250) : Color.FromArgb(28, 38, 49);
	}

	public bool IsDarkSurface()
	{
		Color a = ParseColor(StartColor, Color.FromArgb(42, 56, 70));
		if (!string.Equals(FillMode, "Gradient", StringComparison.OrdinalIgnoreCase)) return 0.299 * a.R + 0.587 * a.G + 0.114 * a.B < 140;
		Color b = ParseColor(EndColor, Color.FromArgb(19, 27, 36));
		double luminance = (0.299 * a.R + 0.587 * a.G + 0.114 * a.B) * 0.5
			+ (0.299 * b.R + 0.587 * b.G + 0.114 * b.B) * 0.5;
		return luminance < 140;
	}

	public static string ToHex(Color color) => "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");

	public static Color ParseColor(string value, Color fallback)
	{
		string text = (value ?? "").Trim();
		if (text.Length == 7 && text[0] == '#'
			&& int.TryParse(text.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
		{
			return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
		}
		return fallback;
	}

	private static string NormalizeColor(string value, string fallback) =>
		ToHex(ParseColor(value, ParseColor(fallback, Color.White)));
}

// Layout identity is independent of surface colours. Old XY choices keep their
// IDs and interaction; reference names resolve only to an equivalent layout.
internal sealed class WheelLayoutStyle
{
	internal WheelLayoutStyle(string id, string name, string description, bool ringPresets = false, bool cellShapes = false)
	{
		Id = id; Name = name; Description = description; SupportsRingPresets = ringPresets; SupportsCellShapes = cellShapes;
	}
	internal string Id { get; }
	internal string Name { get; }
	internal string Description { get; }
	internal bool SupportsRingPresets { get; }
	internal bool SupportsCellShapes { get; }
}

internal static class WheelLayoutStyles
{
	internal static IReadOnlyList<WheelLayoutStyle> All { get; } = Array.AsReadOnly(new[]
	{
		new WheelLayoutStyle("classic", "经典轮盘", "多圈完整扇区；顶部为扇区分界，保留原来的分隔线与内容位置。", true),
		new WheelLayoutStyle("01", "图标轮盘", "多圈扇区；顶部正对第一个命令，所有格子直接选择，支持图标和文字。", true),
		new WheelLayoutStyle("11", "分组面板", "先用顶部标签切换命令组，再选择当前组的矩形格子。"),
		new WheelLayoutStyle("06", "节点轮盘", "先选择圈节点，再显示该圈命令；中心按钮返回上一级。"),
		new WheelLayoutStyle("orbit", "独立格轮盘", "各圈命令为彼此分离的按钮；保留格子自己的圆形、圆角方或方形。", true, true),
		new WheelLayoutStyle("free", "自由布局", "格子按保存的坐标摆放；保留各格的独立形状与命令身份。", false, true),
		new WheelLayoutStyle("grid", "矩形布局", "所有启用圈的格子直接铺成矩形网格；命令仍保留原圈与位置。", true),
		new WheelLayoutStyle("honeycomb", "蜂窝布局", "所有启用圈的格子铺成错行六边形；点击按六边形实际轮廓命中。", true)
	});

	internal static string NormalizeStyleId(string value)
	{
		string id = (value ?? "").Trim();
		if (id.Length == 0 || id.Equals("Circle", StringComparison.OrdinalIgnoreCase)) return "01";
		if (id.Equals("Ring", StringComparison.OrdinalIgnoreCase)) return "orbit";
		if (id.Equals("List", StringComparison.OrdinalIgnoreCase)) return "grid";
		foreach (WheelLayoutStyle style in All) if (id.Equals(style.Id, StringComparison.OrdinalIgnoreCase)) return style.Id;
		// Unknown legacy IDs remain recoverable instead of becoming a different
		// layout merely because the user opens a colour dialog.
		return id;
	}

	internal static WheelLayoutStyle Find(string value)
	{
		string id = NormalizeStyleId(value);
		foreach (WheelLayoutStyle style in All) if (style.Id == id) return style;
		return new WheelLayoutStyle(id, "保留当前样式（" + id + "）", "保留历史样式标识；选择其他布局才会替换它。");
	}
}
