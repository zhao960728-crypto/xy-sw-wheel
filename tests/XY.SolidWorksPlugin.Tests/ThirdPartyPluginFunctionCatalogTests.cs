using System;
using System.Linq;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ThirdPartyPluginFunctionCatalogTests
{
	[Fact]
	public void ReadsOnlyChineseParameterlessVoidPluginMethods()
	{
		string[] names = ThirdPartyPluginFunctionCatalog.ReadChineseCallbacksForTest(typeof(FakeAddin)).ToArray();

		Assert.Equal(new[] { "打开面板", "生成报表" }, names);
	}

	private sealed class FakeAddin
	{
		public void 打开面板Callback() { }
		public void 生成报表() { }
		public void EnglishMethod() { }
		public int 返回数字() => 1;
		public void 带参数(string value) { }
	}
}
