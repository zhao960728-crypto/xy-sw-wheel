using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Xunit;
using Xunit.Abstractions;

namespace XY.SolidWorksPlugin.Tests;

public sealed class RadialRenderCacheTests
{
	private readonly ITestOutputHelper _output;

	public RadialRenderCacheTests(ITestOutputHelper output)
	{
		_output = output;
	}

	[Fact]
	public void NativeIconPaintingAvoidsTextFitAndRetainsUpdatedCommandLabels()
	{
		Exception? failure = null;
		long firstPaintMilliseconds = 0;
		long repeatedPaintMilliseconds = 0;
		int firstComputationCount = 0;
		int changedComputationCount = 0;
		Thread thread = new Thread(() =>
		{
			try
			{
				PluginConfig config = ConfigStore.Deserialize("{}");
				ConfigStore.Normalize(config);
				SceneMenuConfig scene = config.Menus[SceneKeys.Part];
				scene.MenuSizePx = 550;
				scene.CenterRadiusPx = 24;
				scene.Rings = new List<RingConfig>
				{
					Ring(0, 8, 215, "属性处理", "设定固定面", "设置为正视图", "压铆拉铆牙孔", "快速钣金", "门板切口", "QuickSplit", "快速工程图"),
					Ring(1, 12, 383, "质量分析", "快速上色", "零件转子装配", "删除面", "移动面", "转换到钣金", "关闭角", "折叠", "展开", "斜接法兰", "切口", "闭合角"),
					Ring(2, 16, 550, "从选择生成草图", "草图图片", "等距实体", "镜向实体", "修复草图", "草图阵列", "转折", "放样折弯", "边线法兰", "折叠", "展开", "关闭角", "切口", "斜接法兰", "质量分析", "自动爆炸")
				};
				using Form form = new Form { Width = 620, Height = 620 };
				using RadialMenuControl wheel = new RadialMenuControl(Color.White) { Dock = DockStyle.Fill, ScaleMenuForPreview = false };
				form.Controls.Add(wheel);
				form.CreateControl();
				form.PerformLayout();
				wheel.CreateControl();
				wheel.SetMenu(config, scene);
				using Bitmap bitmap = new Bitmap(wheel.Width, wheel.Height);

				Stopwatch first = Stopwatch.StartNew();
				wheel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
				first.Stop();
				firstPaintMilliseconds = first.ElapsedMilliseconds;
				firstComputationCount = wheel.AutoTextLayoutComputationCount;
				Assert.Equal(0, firstComputationCount);
                Assert.Contains(Enumerable.Range(0, bitmap.Width), x => bitmap.GetPixel(x, bitmap.Height / 2).ToArgb() != Color.White.ToArgb());

				Stopwatch repeated = Stopwatch.StartNew();
				for (int index = 0; index < 20; index++)
				{
					wheel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
				}
				repeated.Stop();
				repeatedPaintMilliseconds = repeated.ElapsedMilliseconds;
				Assert.Equal(firstComputationCount, wheel.AutoTextLayoutComputationCount);

				scene.Rings[0].Items[0].Label = "属性批量处理";
				wheel.SetMenu(config, scene);
				wheel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
				changedComputationCount = wheel.AutoTextLayoutComputationCount;
				Assert.Equal(0, changedComputationCount);
                string? hover = null; wheel.HoverTextChanged += text => hover = text;
                wheel.UpdateCursor(new Point(wheel.Width / 2, wheel.Height / 2 - 90));
                Assert.Equal(scene.Rings[0].Items[0].Label, hover);
			}
			catch (Exception ex)
			{
				failure = ex;
			}
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();
		if (failure != null)
		{
			throw failure;
		}
		_output.WriteLine("First paint: {0} ms; 20 cached paints: {1} ms; computations: {2} -> {3} after one label changed.", firstPaintMilliseconds, repeatedPaintMilliseconds, firstComputationCount, changedComputationCount);
	}

	[Fact]
	public void HoverPaintReusesStaticBackgroundLayer()
	{
		Exception? failure = null;
		long firstPaintMilliseconds = 0;
		long hoverPaintMilliseconds = 0;
		Thread thread = new Thread(() =>
		{
			try
			{
				PluginConfig config = ConfigStore.Deserialize("{}");
				ConfigStore.Normalize(config);
				SceneMenuConfig scene = config.Menus[SceneKeys.Part];
				scene.MenuSizePx = 550;
				scene.CenterRadiusPx = 24;
				scene.Rings = new List<RingConfig>
				{
					Ring(0, 8, 215, "属性处理", "设定固定面", "设置为正视图", "压铆拉铆牙孔", "快速钣金", "门板切口", "QuickSplit", "快速工程图"),
					Ring(1, 12, 383, "质量分析", "快速上色", "零件转子装配", "删除面", "移动面", "转换到钣金", "关闭角", "折叠", "展开", "斜接法兰", "切口", "闭合角")
				};
				using Form form = new Form { Width = 620, Height = 620 };
				using RadialMenuControl wheel = new RadialMenuControl(Color.White)
				{
					Dock = DockStyle.Fill,
					DrawLabels = false,
					ScaleMenuForPreview = false
				};
				form.Controls.Add(wheel);
				form.CreateControl();
				form.PerformLayout();
				wheel.CreateControl();
				wheel.SetMenu(config, scene);
				using Bitmap bitmap = new Bitmap(wheel.Width, wheel.Height);

				Stopwatch first = Stopwatch.StartNew();
				wheel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
				first.Stop();
				firstPaintMilliseconds = first.ElapsedMilliseconds;
				Assert.Equal(1, wheel.StaticLayerRenderCount);

				Stopwatch hover = Stopwatch.StartNew();
				for (int index = 0; index < 20; index++)
				{
					int x = 310 + (int)(180 * Math.Cos(index * Math.PI / 10));
					int y = 310 + (int)(180 * Math.Sin(index * Math.PI / 10));
					wheel.UpdateCursor(new Point(x, y));
					wheel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
				}
				hover.Stop();
				hoverPaintMilliseconds = hover.ElapsedMilliseconds;
				Assert.Equal(1, wheel.StaticLayerRenderCount);
			}
			catch (Exception ex)
			{
				failure = ex;
			}
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();
		if (failure != null)
		{
			throw failure;
		}
		_output.WriteLine("Static first paint: {0} ms; 20 hover paints: {1} ms; static layer renders: 1.", firstPaintMilliseconds, hoverPaintMilliseconds);
	}

	[Fact]
	public void ForegroundWarmupPrimesFirstOpenWithoutShowingWindow()
	{
		Exception? failure = null;
		long firstWarmupMilliseconds = 0;
		long repeatedWarmupMilliseconds = 0;
		int computationCount = 0;
		Thread thread = new Thread(() =>
		{
			try
			{
				PluginConfig config = ConfigStore.Deserialize("{}");
				ConfigStore.Normalize(config);
				SceneMenuConfig scene = config.Menus[SceneKeys.Part];
				scene.MenuSizePx = 550;
				scene.CenterRadiusPx = 24;
				scene.Rings = new List<RingConfig>
				{
					Ring(0, 8, 215, "属性处理", "设定固定面", "设置为正视图", "压铆拉铆牙孔", "快速钣金", "门板切口", "QuickSplit", "快速工程图"),
					Ring(1, 12, 383, "质量分析", "快速上色", "零件转子装配", "删除面", "移动面", "转换到钣金", "关闭角", "折叠", "展开", "斜接法兰", "切口", "闭合角")
				};
				using RadialForegroundWindow window = new RadialForegroundWindow();
				Stopwatch first = Stopwatch.StartNew();
				window.Warmup(config, SceneKeys.Part);
				first.Stop();
				firstWarmupMilliseconds = first.ElapsedMilliseconds;
				computationCount = window.AutoTextLayoutComputationCount;
				Assert.Equal(0, computationCount);
                Assert.True(window.Controls[0].IsHandleCreated);
				Assert.False(window.Visible);

				Stopwatch repeated = Stopwatch.StartNew();
				window.Warmup(config, SceneKeys.Part);
				repeated.Stop();
				repeatedWarmupMilliseconds = repeated.ElapsedMilliseconds;
				Assert.Equal(computationCount, window.AutoTextLayoutComputationCount);
				Assert.False(window.Visible);
			}
			catch (Exception ex)
			{
				failure = ex;
			}
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();
		if (failure != null)
		{
			throw failure;
		}
		_output.WriteLine("First background warmup: {0} ms; repeated warmup: {1} ms; cached layouts: {2}.", firstWarmupMilliseconds, repeatedWarmupMilliseconds, computationCount);
	}

	private static RingConfig Ring(int index, int slots, int diameter, params string[] labels)
	{
		return new RingConfig
		{
			Index = index,
			Slots = slots,
			DiameterPx = diameter,
			Enabled = true,
			Items = labels.Select((label, slot) => new MenuItemConfig
			{
				Slot = slot,
				Label = label,
				Kind = ActionKinds.NativeCommand,
				Value = NativeCommandReference.Encode(1000 + index * 100 + slot)
			}).ToList()
		};
	}
}
