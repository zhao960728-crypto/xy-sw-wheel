using System.Linq;
using XY.SolidWorksPlugin;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public class WheelIconLibraryTests
{
	[Fact]
	public void LibraryOffersThousandsOfStyles()
	{
		// shapes x operation badges x render styles
		Assert.True(WheelIconCatalog.Entries.Count > 5000,
			"Expected several thousand icon styles, got " + WheelIconCatalog.Entries.Count + ".");
	}

	[Fact]
	public void HistoricIdsStillResolve()
	{
		// A configuration saved before badges and styles existed must not break.
		Assert.Contains(WheelIconCatalog.Entries, e => e.Id == "line");
		Assert.Contains(WheelIconCatalog.Entries, e => e.Id == "line.add");
		Assert.Contains(WheelIconCatalog.Entries, e => e.Id == "circle.delete");
	}

	[Fact]
	public void NewDomainIdsResolve()
	{
		Assert.Contains(WheelIconCatalog.Entries, e => e.Id == "pressrivet");
		Assert.Contains(WheelIconCatalog.Entries, e => e.Id == "squaretube");
		Assert.Contains(WheelIconCatalog.Entries, e => e.Id == "footpad");
		Assert.Contains(WheelIconCatalog.Entries, e => e.Id == "weldrivet");
	}

	[Fact]
	public void StyleAndBadgeVariantsResolve()
	{
		Assert.Contains(WheelIconCatalog.Entries, e => e.Id == "gear.solid");
		Assert.Contains(WheelIconCatalog.Entries, e => e.Id == "gear.duo");
		Assert.Contains(WheelIconCatalog.Entries, e => e.Id == "gear.add.solid");
		Assert.Contains(WheelIconCatalog.Entries, e => e.Id == "motor.warning.duo");
	}

	[Fact]
	public void EveryIdHasANonEmptyLabel()
	{
		Assert.All(WheelIconCatalog.Entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Name)));
		Assert.All(WheelIconCatalog.Entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Id)));
	}

	[Fact]
	public void RenderingEveryShapeSucceeds()
	{
		// Draws one icon per shape with the plain style to prove the new
		// geometry cases do not throw.
		for (int shape = 0; shape < WheelIconCatalog.ShapeCount; shape++)
		{
			using var bitmap = WheelIconCatalog.Render(WheelIconCatalog.Entries[shape * 20 * 3].Id, 32);
			Assert.Equal(32, bitmap.Width);
		}
	}

	[Fact]
	public void DensePreviewClampsTinyIconCanvasInsteadOfThrowing()
	{
		using var bitmap = WheelIconCatalog.Render("export", 1);
		Assert.Equal(16, bitmap.Width);
		Assert.Equal(16, bitmap.Height);
	}

	[Theory]
	[InlineData("pressrivet")]
	[InlineData("squaretube")]
	[InlineData("footpad")]
	[InlineData("lasercut")]
	public void ExactCommandTokenMatchesItsIcon(string command)
	{
		Assert.True(WheelIconCatalog.TryGetCommandIconId(command, out string id));
		Assert.Equal(command, id);
	}

	[Fact]
	public void UnknownCommandHasNoIcon()
	{
		Assert.False(WheelIconCatalog.TryGetCommandIconId("MY-OWN-CUSTOM-TOOL", out _));
		Assert.False(WheelIconCatalog.TryGetCommandIconId("", out _));
	}

	[Theory]
	[InlineData("折弯", "sheetbend")]
	[InlineData("压铆螺母", "pressrivet")]
	[InlineData("焊接螺母", "weldnut")]
	[InlineData("脚垫", "footpad")]
	[InlineData("方通", "squaretube")]
	[InlineData("去毛刺", "deburr")]
	[InlineData("激光下料", "lasercut")]
	[InlineData("表面处理", "surfacetreatment")]
	[InlineData("切割清单", "cutlist")]
	[InlineData("三坐标", "cmm")]
	public void ChineseLabelsMatchTheirProcessIcon(string label, string expected)
	{
		Assert.True(WheelIconCatalog.TryGetSemanticIconId("", label, "", "", out string id));
		Assert.Equal(expected, id);
	}

	[Fact]
	public void CommandTokenWinsOverLooseLabelMatch()
	{
		// The executed token is authoritative; descriptive text only fills gaps.
		Assert.True(WheelIconCatalog.TryGetSemanticIconId("turning", "随便写的名字", "", "", out string id));
		Assert.Equal("turning", id);
	}

	[Fact]
	public void AutoMatchLeavesUserImagesAlone()
	{
		// Mirrors the guard in SettingsWindow.AutoMatchIcons: a chosen picture
		// is never replaced by a matched catalogue icon.
		Assert.False(string.IsNullOrWhiteSpace("user-pick.png"));
		Assert.True(WheelIconCatalog.TryGetSemanticIconId("", "折弯", "", "", out _));
	}

	[Theory]
	[InlineData("batch-sheet-metal-correction", "sheetbend")]
	[InlineData("hole-process", "drill")]
	[InlineData("panel-gap-adjustment", "door")]
	[InlineData("new-part", "part")]
	public void BasicActionValueKeepsItsIconWhenCaptionChanges(string value, string expected)
	{
		Assert.True(WheelIconCatalog.TryGetBasicActionIconId(value, out string id));
		Assert.Equal(expected, id);
		using var bitmap = WheelIconCatalog.Render(id, 32);
		Assert.Equal(32, bitmap.Width);
	}
}
