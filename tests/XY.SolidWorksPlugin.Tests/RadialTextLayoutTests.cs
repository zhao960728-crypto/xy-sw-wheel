using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class RadialTextLayoutTests
{
	[Fact]
	public void AutoFitUsesCellGeometryInsteadOfLegacyEightPointCap()
	{
		float size = RadialTextLayout.MaximumFontSize(0, 1f, 80f);

		Assert.Equal(30f, size);
	}

	[Fact]
	public void FixedFontSizeIsNotReducedByCellHeight()
	{
		float size = RadialTextLayout.MaximumFontSize(24, 1f, 12f);

		Assert.Equal(24f, size);
	}

	[Fact]
	public void AutoFitKeepsReadableRuntimeMinimum()
	{
		float size = RadialTextLayout.MinimumReadableFontSize(1f, 30f);

		Assert.Equal(15f, size);
	}

	[Fact]
	public void PreviewReadableMinimumScalesWithWheel()
	{
		float size = RadialTextLayout.MinimumReadableFontSize(0.75f, 31.5f);

		Assert.Equal(11.25f, size);
	}

	[Theory]
	[InlineData(2, 2)]
	[InlineData(4, 2)]
	[InlineData(5, 4)]
	[InlineData(20, 4)]
	public void AutoFitLimitsLineCount(int labelLength, int expectedLines)
	{
		Assert.Equal(expectedLines, RadialTextLayout.MaximumAutoLines(labelLength));
	}

	[Fact]
	public void DenseInnerRingUsesConservativeTextWidth()
	{
		var area = RadialTextLayout.AvailableArea(65.75f, 83.5f, 8);

		Assert.InRange(area.Width, 49f, 50f);
		Assert.InRange(area.Height, 73f, 74f);
	}

	[Fact]
	public void WideSingleRingUsesItsMiddleWidthInsteadOfInnerEdgeWidth()
	{
		var area = RadialTextLayout.AvailableArea(149.5f, 251f, 8);

		Assert.InRange(area.Width, 111f, 113f);
		Assert.InRange(area.Height, 220f, 222f);
	}

	[Fact]
	public void MoreSlotsProduceNarrowerTextArea()
	{
		var eightSlots = RadialTextLayout.AvailableArea(180f, 80f, 8);
		var sixteenSlots = RadialTextLayout.AvailableArea(180f, 80f, 16);

		Assert.True(sixteenSlots.Width < eightSlots.Width);
	}
}
