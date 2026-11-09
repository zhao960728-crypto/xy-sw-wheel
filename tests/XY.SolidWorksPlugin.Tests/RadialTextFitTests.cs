using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class RadialTextFitTests
{
	[Theory]
	[InlineData("SimSun")]
	[InlineData("SimHei")]
	public void FixedFontShrinksWhenLiteralSizeOverflowsCell(string fontName)
	{
		// User saved a literal 24 px font on a 16-slot outer ring band —
		// neither width nor height fits at the requested size.
		float requestedSize = 24f;
		SizeF requestedSizeText = new SizeF(180f, 32f); // wider + taller than 60×40 available
		SizeF available = new SizeF(60f, 40f);

		float shrunk = RadialTextLayout.ShrinkFixedFontSizeToFit(requestedSize, requestedSizeText, available.Width, available.Height);

		// The shrink is the smaller of width/height ratios with 5% slack.
		float expectedWidthScale = available.Width / requestedSizeText.Width; // 60 / 180 = 0.333
		float expectedHeightScale = available.Height / requestedSizeText.Height; // 40 / 32 = 1.25
		float expectedShrink = Math.Min(expectedWidthScale, expectedHeightScale) * 0.95f;
		Assert.Equal(expectedShrink * requestedSize, shrunk, 2);
		Assert.True(shrunk < requestedSize, "Should shrink when oversized");
	}

	[Fact]
	public void FixedFontDoesNotShrinkWhenItAlreadyFits()
	{
		// In-bounds case: returning the original size preserves the user's
		// literal preference rather than rounding it down by accident.
		float requestedSize = 14f;
		SizeF text = new SizeF(40f, 18f);
		SizeF available = new SizeF(80f, 80f);
		Assert.Equal(requestedSize, RadialTextLayout.ShrinkFixedFontSizeToFit(requestedSize, text, available.Width, available.Height));
	}

	[Fact]
	public void FixedFontShrinkFloorIsOne()
	{
		// Pathologically small cell + giant font: the floor protects against
		// going to 0 (which would crash Font construction).
		float shrunk = RadialTextLayout.ShrinkFixedFontSizeToFit(72f, new SizeF(1000f, 1000f), 10f, 10f);
		Assert.True(shrunk >= 1f);
	}

	[Theory]
	[InlineData(1.0f, 4.0f)]
	[InlineData(0.5f, 2.5f)] // 4*0.5 = 2, but 2.5 floor wins.
	[InlineData(0.25f, 2.5f)] // 4*0.25 = 1, but 2.5 floor wins.
	[InlineData(0.1f, 2.5f)] // 4*0.1 = 0.4, 2.5 floor wins.
	[InlineData(2.0f, 8.0f)] // High-DPI preview: 4 * 2 = 8.
	public void MinimumFallbackFontSizeScalesWithPreview(float previewScale, float expected)
	{
		Assert.Equal(expected, RadialTextLayout.MinimumFallbackFontSize(previewScale));
	}
	[Theory]
	[InlineData("SimSun")]
	[InlineData("SimHei")]
	public void DenseInnerRingLabelsRemainCompleteAndReadable(string fontName)
	{
		using Bitmap bitmap = new Bitmap(600, 600);
		using Graphics graphics = Graphics.FromImage(bitmap);
		SizeF area = RadialTextLayout.AvailableArea(65.75f, 83.5f, 8);
		string[] labels =
		{
			"设定固定面..",
			"压铆拉铆[牙孔]..",
			"零件转[子]装配..",
			"快速工程图..",
			"设计树材质..",
			"QuickSplit.."
		};

		for (int slot = 0; slot < labels.Length; slot++)
		{
			string label = labels[slot];
			float start = -90f + slot * 45f;
			float angle = start + 22.5f;
			PointF wheelCenter = new PointF(300f, 300f);
			PointF labelCenter = PointOnCircle(wheelCenter, 65.75f, angle);
			using GraphicsPath path = RadialMenuControl.CreateSlotPath(wheelCenter, 24f, 107.5f, start, 45f);
			RadialTextFitResult result = RadialMenuControl.FitAutoTextInCell(graphics, label, fontName, path, labelCenter, angle, 83.5f, area.Width, area.Height, 1f);
			string rendered = string.Concat(result.Lines);
			string expected = label.Trim().TrimEnd('.', '…').Replace("[", "").Replace("]", "");

			Assert.True(result.FullText, label);
			Assert.Equal(expected, rendered);
			Assert.DoesNotContain("...", rendered);
			Assert.True(result.FontSize >= 3.5f && result.FontSize <= 30f, Describe(label, result, path));
			Assert.True(
				RadialMenuControl.LineBoundsInsidePath(path, result.LineBounds, Math.Max(2f, result.FontSize * 0.08f)),
				Describe(label, result, path));
		}
	}

	[Fact]
	public void SixteenSlotOuterRingKeepsFullChineseCommand()
	{
		using Bitmap bitmap = new Bitmap(600, 600);
		using Graphics graphics = Graphics.FromImage(bitmap);
		SizeF area = RadialTextLayout.AvailableArea(233.25f, 83.5f, 16);

		PointF wheelCenter = new PointF(300f, 300f);
		float start = -90f;
		float angle = start + 11.25f;
		PointF labelCenter = PointOnCircle(wheelCenter, 233.25f, angle);
		using GraphicsPath path = RadialMenuControl.CreateSlotPath(wheelCenter, 191.5f, 275f, start, 22.5f);
		RadialTextFitResult result = RadialMenuControl.FitAutoTextInCell(graphics, "从选择生成草图..", "SimSun", path, labelCenter, angle, 83.5f, area.Width, area.Height, 1f);

		Assert.True(result.FullText);
		Assert.Equal("从选择生成草图", string.Concat(result.Lines));
		Assert.InRange(result.FontSize, 3.5f, 30f);
		Assert.True(RadialMenuControl.LineBoundsInsidePath(path, result.LineBounds, Math.Max(2f, result.FontSize * 0.08f)));
	}

	[Fact]
	public void CurrentConfiguredAutoLabelsAreCompleteInsideTheirOwnCellsAndUseVariableSizes()
	{
		string? configPath = Environment.GetEnvironmentVariable("XY_UI_REAL_CONFIG_PATH");
		if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
		{
			return;
		}
		PluginConfig config = ConfigStore.Deserialize(File.ReadAllText(configPath));
		ConfigStore.Normalize(config);
		using Bitmap bitmap = new Bitmap(900, 900);
		using Graphics graphics = Graphics.FromImage(bitmap);
		List<float> autoFontSizes = new List<float>();
		foreach (SceneMenuConfig scene in config.Menus.Values)
		{
			int menuSize = Math.Max(260, Math.Min(900, scene.MenuSizePx > 0 ? scene.MenuSizePx : config.MenuSizePx));
			float innerRadius = Math.Max(20, Math.Min(menuSize / 3, scene.CenterRadiusPx > 0 ? scene.CenterRadiusPx : config.CenterRadiusPx));
			float centerRadius = innerRadius;
			List<RingConfig> rings = scene.Rings.Where(ring => ring.Enabled).OrderBy(ring => ring.Index).ToList();
			int totalWeight = Math.Max(1, rings.Sum(ring => Math.Max(1, ring.SizeWeight)));
			int cumulativeWeight = 0;
			for (int index = 0; index < rings.Count; index++)
			{
				RingConfig ring = rings[index];
				cumulativeWeight += Math.Max(1, ring.SizeWeight);
				float configuredOuter = ring.DiameterPx > 0
					? ring.DiameterPx * 0.5f
					: centerRadius + (menuSize * 0.5f - centerRadius) * cumulativeWeight / totalWeight;
				float outerRadius = index == rings.Count - 1
					? menuSize * 0.5f
					: Math.Min(menuSize * 0.5f, Math.Max(innerRadius + 1f, configuredOuter));
				float ringWidth = outerRadius - innerRadius;
				float middleRadius = (innerRadius + outerRadius) * 0.5f;
				SizeF area = RadialTextLayout.AvailableArea(middleRadius, ringWidth, ring.Slots);
				foreach (MenuItemConfig item in ring.Items.Where(item => item.FontSizePx <= 0))
				{
					string fontName = string.IsNullOrWhiteSpace(item.FontName) ? config.FontName : item.FontName;
					float sweep = 360f / ring.Slots;
					float start = -90f + item.Slot * sweep;
					float angle = start + sweep * 0.5f;
					PointF wheelCenter = new PointF(menuSize * 0.5f, menuSize * 0.5f);
					PointF labelCenter = PointOnCircle(wheelCenter, middleRadius, angle);
					using GraphicsPath path = RadialMenuControl.CreateSlotPath(wheelCenter, innerRadius, outerRadius, start, sweep);
					RadialTextFitResult result = RadialMenuControl.FitAutoTextInCell(graphics, item.Label, fontName, path, labelCenter, angle, ringWidth, area.Width, area.Height, 1f);
					Assert.True(result.FullText, scene.Key + " / " + item.Label);
					Assert.Equal(NormalizeLabel(item.Label), string.Concat(result.Lines));
					Assert.True(result.FontSize >= 3.5f, Describe(scene.Key + " / " + item.Label, result, path));
					Assert.True(
						RadialMenuControl.LineBoundsInsidePath(path, result.LineBounds, Math.Max(2f, result.FontSize * 0.08f)),
						Describe(scene.Key + " / " + item.Label, result, path));
					autoFontSizes.Add(result.FontSize);
				}
				innerRadius = outerRadius;
			}
		}
		Assert.NotEmpty(autoFontSizes);
		Assert.True(autoFontSizes.Select(size => Math.Round(size, 1)).Distinct().Count() >= 3, "Auto-fit should choose sizes per cell instead of forcing one global size.");
	}

	private static string NormalizeLabel(string label)
	{
		return (label ?? "").Trim().TrimEnd('.', '…').Replace("[", "").Replace("]", "");
	}

	private static string Describe(string label, RadialTextFitResult result, GraphicsPath path)
	{
		return label
			+ " | size=" + result.FontSize
			+ " | lines=" + string.Join("/", result.Lines)
			+ " | center=" + result.Center
			+ " | block=" + result.BlockBounds
			+ " | path=" + path.GetBounds();
	}

	private static PointF PointOnCircle(PointF center, float radius, float degrees)
	{
		double radians = degrees * Math.PI / 180.0;
		return new PointF(center.X + (float)Math.Cos(radians) * radius, center.Y + (float)Math.Sin(radians) * radius);
	}
}
