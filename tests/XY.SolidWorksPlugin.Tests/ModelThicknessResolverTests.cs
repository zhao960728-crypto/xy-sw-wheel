using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ModelThicknessResolverTests
{
	[Theory]
	[InlineData("厚度")]
	[InlineData("板厚")]
	[InlineData("料厚")]
	[InlineData("SW-Sheet Metal Thickness")]
	public void ThicknessAliasesAreNativeCalculatedProperties(string name)
	{
		Assert.True(ModelThicknessResolver.IsThicknessProperty(name));
	}

	[Fact]
	public void ThicknessResolutionUsesStablePriority()
	{
		Assert.Equal(2.5, ModelThicknessResolver.SelectThickness(2.5, 3, 4, 5));
		Assert.Equal(3, ModelThicknessResolver.SelectThickness(0, 3, 4, 5));
		Assert.Equal(4, ModelThicknessResolver.SelectThickness(0, 0, 4, 5));
		Assert.Equal(5, ModelThicknessResolver.SelectThickness(0, 0, 0, 5));
	}

	[Theory]
	[InlineData("2.5 mm", 2.5)]
	[InlineData("0.2 cm", 2)]
	[InlineData("0.002 m", 2)]
	[InlineData("0.125 in", 3.175)]
	public void PropertyLengthUnitsAreNormalizedToMillimeters(string value, double expected)
	{
		Assert.Equal(expected, ModelThicknessResolver.ParseLengthMillimeters(value), 3);
	}

	[Fact]
	public void NonSheetFallbackUsesSmallestBodyDimension()
	{
		double thickness = ModelThicknessResolver.MinimumBodyDimensionMillimeters(
			new[] { -0.05, -0.025, 0d, 0.05, 0.025, 0.008 });

		Assert.Equal(8, thickness, 3);
	}
}
