using System.Linq;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class JiaLiChuangFunctionCatalogTests
{
	[Fact]
	public void ReadDefinitionsKeepsOnlyPublicChineseParameterlessVoidCallbacks()
	{
		JiaLiChuangCommandDefinition[] definitions = JiaLiChuangFunctionCatalog
			.ReadDefinitionsForTest(typeof(FakeIcanAddin))
			.ToArray();

		Assert.Equal(3, definitions.Length);
		Assert.Contains(definitions, item => item.CallbackName == "快速倒角Show" && item.DisplayName == "快速倒角");
		Assert.Contains(definitions, item => item.CallbackName == "钣金功能Callback" && item.DisplayName == "钣金功能");
		Assert.Contains(definitions, item => item.CallbackName == "表面粗糙度回调1" && item.DisplayName == "表面粗糙度入口1");
	}

	[Theory]
	[InlineData("自动装螺丝Show", "自动装螺丝")]
	[InlineData("机械社区Callback", "机械社区")]
	[InlineData("便捷选择回调2", "便捷选择入口2")]
	public void CleanDisplayNameRemovesTechnicalSuffixes(string callback, string expected)
	{
		Assert.Equal(expected, JiaLiChuangFunctionCatalog.CleanDisplayNameForTest(callback));
	}

	private sealed class FakeIcanAddin
	{
		public void 快速倒角Show()
		{
		}

		public void 钣金功能Callback()
		{
		}

		public void 表面粗糙度回调1()
		{
		}

		public void EnglishOnlyShow()
		{
		}

		public int 中文返回值Show()
		{
			return 1;
		}

		public void 中文带参数Show(string value)
		{
		}

		public static void 中文静态Show()
		{
		}
	}
}
