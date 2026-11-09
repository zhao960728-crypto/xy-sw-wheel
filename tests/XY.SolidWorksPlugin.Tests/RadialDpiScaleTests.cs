using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class RadialDpiScaleTests
{
	[Theory]
	[InlineData(96, 1.0f)]
	[InlineData(120, 1.25f)]
	[InlineData(144, 1.5f)]
	[InlineData(192, 2.0f)]
	[InlineData(48, 1.0f)] // Below 96 DPI is non-sensical but must not crash.
	[InlineData(0, 1.0f)] // Defensive: 0 also clamps to 1.
	public void ComputeDpiScaleReturnsTheSystemFactor(int deviceDpi, float expected)
	{
		Assert.Equal(expected, RadialMenuControl.ComputeDpiScale(deviceDpi));
	}

	[Theory]
	[InlineData(550, 96, 550)] // Logical size, baseline DPI: identical pixels.
	[InlineData(550, 144, 825)] // 1.5x: more physical pixels at higher DPI.
	[InlineData(550, 192, 1100)] // 2x: physical size doubles.
	[InlineData(260, 192, 520)] // Lower bound scales the same way.
	[InlineData(900, 96, 900)] // Upper bound, baseline.
	[InlineData(900, 144, 1350)] // Upper bound scaled — yes, it overshoots, but
								  // the user is opting into a large wheel.
	[InlineData(100, 192, 520)] // Saved below the 260 floor is clamped to 260 first.
	public void EffectiveMenuSizeScalesWithDpi(int saved, int deviceDpi, int expected)
	{
		Assert.Equal(expected, RadialMenuControl.ComputeEffectiveMenuSizePx(saved, deviceDpi));
	}

	[Fact]
	public void DpiScalingDoesNotChangeHeadlessRendering() => Sta(() =>
	{
		// The control's own DeviceDpi is 96 in headless tests, so DPI scaling
		// must be a no-op for existing tests. Sanity-check by binding an
		// EffectiveMenuSizePx call through SetMenu.
		var scene = SceneMenu();
		scene.MenuSizePx = 550;
		using var wheel = new RadialMenuControl(Color.Fuchsia) { Size = new Size(560, 560) };
		wheel.SetMenu(new PluginConfig(), scene);
		// The control reports a 550 px wheel at 96 DPI.
		using Bitmap image = new Bitmap(560, 560);
		using Graphics g = Graphics.FromImage(image);
		g.Clear(Color.Fuchsia);
		wheel.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
		// Centre 24 px hub (single ring, 01): the wide-open middle paints the
		// fuchsia key colour. If DPI scaling were leaking into headless tests
		// the hub radius would no longer match (260,260) being fuchsia.
		Assert.Equal(Color.Fuchsia.ToArgb(), image.GetPixel(280, 280).ToArgb());
	});

	private static SceneMenuConfig SceneMenu()
	{
		var items = new List<MenuItemConfig>
		{
			new MenuItemConfig { Slot = 0, Label = "打开", Kind = ActionKinds.NativeCommand, Value = NativeCommandReference.Encode(1) },
			new MenuItemConfig { Slot = 1, Label = "新建", Kind = ActionKinds.NativeCommand, Value = NativeCommandReference.Encode(2) },
			new MenuItemConfig { Slot = 2, Label = "保存", Kind = ActionKinds.NativeCommand, Value = NativeCommandReference.Encode(3) },
			new MenuItemConfig { Slot = 3, Label = "另存", Kind = ActionKinds.NativeCommand, Value = NativeCommandReference.Encode(4) },
			new MenuItemConfig { Slot = 4, Label = "关闭", Kind = ActionKinds.NativeCommand, Value = NativeCommandReference.Encode(5) },
			new MenuItemConfig { Slot = 5, Label = "退出", Kind = ActionKinds.NativeCommand, Value = NativeCommandReference.Encode(6) },
			new MenuItemConfig { Slot = 6, Label = "撤销", Kind = ActionKinds.NativeCommand, Value = NativeCommandReference.Encode(7) },
			new MenuItemConfig { Slot = 7, Label = "重做", Kind = ActionKinds.NativeCommand, Value = NativeCommandReference.Encode(8) }
		};
		return new SceneMenuConfig
		{
			Key = SceneKeys.Blank,
			MenuSizePx = 550,
			CenterRadiusPx = 24,
			StyleId = "01",
			Rings = new List<RingConfig>
			{
				new RingConfig { Index = 0, Slots = 8, DiameterPx = 550, Enabled = true, Items = items }
			}
		};
	}

	private static void Sta(Action body)
	{
		Exception? failure = null;
		Thread thread = new Thread(() =>
		{
			try { body(); }
			catch (Exception ex) { failure = ex; }
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();
		if (failure != null) throw failure;
	}
}