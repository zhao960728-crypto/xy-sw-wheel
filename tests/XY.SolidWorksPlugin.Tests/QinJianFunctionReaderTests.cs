using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class QinJianFunctionReaderTests
{
	[Fact]
	public void ActionValueRoundTripsTabAndChineseCommandName()
	{
		string value = QinJianActionValue.Format("琴剑钣金", "批量处理属性");

		Assert.Equal("琴剑钣金 > 批量处理属性", value);
		Assert.True(QinJianActionValue.TryParse(value, out string tabName, out string commandName));
		Assert.Equal("琴剑钣金", tabName);
		Assert.Equal("批量处理属性", commandName);
	}

	[Fact]
	public void ActionValueAcceptsLegacyCategoryPrefixAndCompactSeparator()
	{
		Assert.True(QinJianActionValue.TryParse("琴剑系列 > 琴剑钣金>门板切口", out string tabName, out string commandName));
		Assert.Equal("琴剑钣金", tabName);
		Assert.Equal("门板切口", commandName);
	}

	[Theory]
	[InlineData("")]
	[InlineData("琴剑钣金")]
	[InlineData("琴剑钣金 > ")]
	[InlineData("琴剑系列 > 琴剑钣金 > ")]
	public void ActionValueRejectsIncompleteValues(string value)
	{
		Assert.False(QinJianActionValue.TryParse(value, out _, out _));
	}

	[Theory]
	[InlineData("琴剑SW工具", true)]
	[InlineData("琴剑钣金 选项卡", true)]
	[InlineData("选择 琴剑型材", true)]
	[InlineData("启翔铝型材", true)]
	public void IsQinJianTabNameRecognizesQinJianAndQiXiangTabs(string value, bool expected)
	{
		Assert.Equal(expected, QinJianFunctionReader.IsQinJianTabNameForTest(value));
	}

	[Fact]
	public void CommandCatalogRecognizesEveryQinJianSeriesTable()
	{
		Assert.True(QinJianCommandCatalog.IsQinJianSeriesTableNameForTest("琴剑小菜鸟"));
		Assert.True(QinJianCommandCatalog.IsQinJianSeriesTableNameForTest("琴剑小工具"));
		Assert.False(QinJianCommandCatalog.IsQinJianSeriesTableNameForTest("启翔铝型材"));
	}

	[Theory]
	[InlineData("通用BOM\r\n生成清单", "通用BOM")]
	[InlineData("&快速改名...", "快速改名")]
	[InlineData(" 批量处理属性 ", "批量处理属性")]
	public void CleanCommandNameKeepsChineseButtonName(string value, string expected)
	{
		Assert.Equal(expected, QinJianFunctionReader.CleanCommandNameForTest(value));
	}

	[Theory]
	[InlineData("琴剑钣金 选项卡", "琴剑钣金", true)]
	[InlineData("QinJian琴剑钣金", "琴剑钣金", true)]
	[InlineData("启翔铝型材", "启翔铝型材", true)]
	public void TabAliasesResolveToTheSamePersistedIdentity(string discovered, string persisted, bool expected)
	{
		Assert.Equal(expected, QinJianFunctionReader.TabNameEqualsForTest(discovered, persisted));
	}

	[Theory]
	[InlineData("DoctorSheetMetal_Plus AddIn", "博士钣金", true)]
	[InlineData("Dr工具", "Doctor Tools", true)]
	[InlineData("琴剑钣金", "DoctorSheetMetal_Plus AddIn", false)]
	public void ExternalPluginTabAliasesResolveAcrossDoctorLocalizedCaptions(string discovered, string persisted, bool expected)
	{
		Assert.Equal(expected, QinJianFunctionReader.PluginTabNamesMatchForTest(discovered, persisted));
	}

	[Theory]
	[InlineData("批量处理属性...", "批量处理属性", true)]
	[InlineData("&快速改名", "快速改名", true)]
	public void CatalogFunctionMatchingIgnoresUiDecorations(string discovered, string persisted, bool expected)
	{
		Assert.Equal(expected, QinJianCommandCatalog.FunctionNameEqualsForTest(discovered, persisted));
	}
}
