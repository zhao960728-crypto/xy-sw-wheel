using System.Linq;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class PropertyWatermarkConfigTests
{
	[Fact]
	public void Defaults_UseStandardPropertyNames()
	{
		var fields = PropertyWatermarkConfig.DefaultFields();

		Assert.Contains(fields, field => field.DisplayName == "材质" && field.PropertyName == "材质");
		Assert.Contains(fields, field => field.DisplayName == "厚度" && field.Source == "原生属性" && field.PropertyName == "厚度");
		Assert.Contains(fields, field => field.DisplayName == "数量" && field.PropertyName == "数量");
		Assert.Contains(fields, field => field.DisplayName == "工序" && field.PropertyName == "工序");
		Assert.DoesNotContain(fields, field => field.PropertyName == "备注" || field.PropertyName == "工序类型");
	}

	[Fact]
	public void Normalize_UsesOnlyNativeOrCustomPropertySources()
	{
		var config = new PropertyWatermarkConfig
		{
			Fields = new[]
			{
				new PropertyWatermarkField { DisplayName = "文件名", Source = "文件名", PropertyName = "" },
				new PropertyWatermarkField { DisplayName = "材质", Source = "材质", PropertyName = "材质" }
			}.ToList()
		};

		PropertyWatermarkConfig.Normalize(config);

		Assert.Equal("原生属性", config.Fields[0].Source);
		Assert.Equal("文件名", config.Fields[0].PropertyName);
		Assert.Equal("自定义属性", config.Fields[1].Source);
	}

	[Theory]
	[InlineData("材料", "材质")]
	[InlineData("单重", "质量")]
	[InlineData("重量", "质量")]
	public void Normalize_MigratesLegacyFieldNamesToStandardNames(string legacyName, string standardName)
	{
		var config = new PropertyWatermarkConfig
		{
			Fields = new[] { new PropertyWatermarkField { DisplayName = legacyName, Source = "自定义属性", PropertyName = legacyName } }.ToList()
		};

		PropertyWatermarkConfig.Normalize(config);

		Assert.Equal(standardName, Assert.Single(config.Fields).PropertyName);
	}

	[Fact]
	public void EnsureProcessField_AppendsOnlyWhenMissing()
	{
		var config = new PropertyWatermarkConfig
		{
			Fields = new[] { new PropertyWatermarkField { DisplayName = "材质", PropertyName = "材质" } }.ToList()
		};

		Assert.True(PropertyWatermarkConfig.EnsureProcessField(config));
		Assert.Contains(config.Fields, field => field.DisplayName == "工序" && field.PropertyName == "工序");
		Assert.False(PropertyWatermarkConfig.EnsureProcessField(config));
		Assert.Equal(2, config.Fields.Count);
	}

	[Fact]
	public void EnsureProcessField_PreservesExistingAliasAndSettings()
	{
		var existing = new PropertyWatermarkField { Enabled = false, DisplayName = "工艺", Source = "自定义属性", PropertyName = "工艺流程" };
		var config = new PropertyWatermarkConfig { Fields = new[] { existing }.ToList() };

		Assert.False(PropertyWatermarkConfig.EnsureProcessField(config));
		Assert.Same(existing, Assert.Single(config.Fields));
		Assert.False(existing.Enabled);
	}

	[Fact]
	public void EnsureRecognitionFieldsDoesNotForceWatermarkProperties()
	{
		var config = new PropertyWatermarkConfig
		{
			Fields = new[] { new PropertyWatermarkField { DisplayName = "板厚", PropertyName = "板厚" } }.ToList()
		};

		Assert.False(PropertyWatermarkConfig.EnsureRecognitionFields(config, new ProcessRecognitionConfig()));

		Assert.Single(config.Fields);
		Assert.Equal("板厚", config.Fields[0].PropertyName);
	}

	[Fact]
	public void EnsureRecognitionFieldsPreservesNativeCalculatedThickness()
	{
		var thickness = new PropertyWatermarkField { DisplayName = "厚度", Source = "原生属性", PropertyName = "厚度" };
		var config = new PropertyWatermarkConfig { Fields = new[] { thickness }.ToList() };

		PropertyWatermarkConfig.EnsureRecognitionFields(config, new ProcessRecognitionConfig());

		Assert.Same(thickness, config.Fields.Single(field => ModelThicknessResolver.IsThicknessProperty(field.PropertyName)));
		Assert.Equal("原生属性", thickness.Source);
		Assert.Equal("厚度", thickness.PropertyName);
	}
}
