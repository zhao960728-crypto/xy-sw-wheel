using System.Collections.Generic;

namespace XY.SolidWorksPlugin;

internal sealed class MenuItemConfig
{
	public int Slot { get; set; }

	public string IconId { get; set; } = "";
	public string IconPath { get; set; } = "";

	// Imported wheel data may carry the catalog reference inline. It mirrors
	// IconId and is only used when neither IconId nor IconPath resolves.
	public string IconData { get; set; } = "";

	// "Icon" scales a symbol into the cell; "Image" fits a user picture.
	public string ImageMode { get; set; } = "Icon";

	// Cell sizing relative to the ring band, and the share of the cell height
	// given to the symbol when both a symbol and a caption are drawn.
	public int CellSizePercent { get; set; } = 100;
	public string CellShape { get; set; } = "Circle";
	public double? FreeX { get; set; }
	public double? FreeY { get; set; }
	public int IconRatioPercent { get; set; } = 55;

	public string ContentMode { get; set; } = "";

	public string Label { get; set; } = "";

	public string Kind { get; set; } = "hotkey";

	public string Value { get; set; } = "";

	public int FontSizePx { get; set; }

	// FontName is the historical field; FontFamily is the shared name used by
	// the CAD wheel. Either one may be populated; FontFamily wins when set.
	public string FontName { get; set; } = "";
	public string FontFamily { get; set; } = "";

	public bool? FontBold { get; set; }

	public string TextEffect { get; set; } = "";

	public string TextColor { get; set; } = "";

	public bool ComboAdvanceEnabled { get; set; }

	public string ComboAdvanceKey { get; set; } = "Ctrl";

	// Steps are no longer exclusive to the combo kind: any wheel cell may carry
	// an ordered sequence, which is how a single function records its hand-off.
	public List<ComboCommandConfig> ComboCommands { get; set; } = new List<ComboCommandConfig>();

	// False lets a failing step be skipped instead of stopping the sequence.
	public bool StopOnStepError { get; set; } = true;

	internal string ResolvedFontFamily()
	{
		string family = (FontFamily ?? "").Trim();
		return family.Length > 0 ? family : (FontName ?? "").Trim();
	}

	internal bool HasSteps() => ComboCommands != null && ComboCommands.Count > 0;
}
