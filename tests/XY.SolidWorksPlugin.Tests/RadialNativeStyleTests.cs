using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public class RadialNativeStyleTests
{
	[Fact]
	public void FactoryHotkeyIconsRespectOverridesRemovalsAndReadFailures()
	{
		var icons=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
		var entries=new List<ShortcutRegistryEntry>();
		RadialCommandIcons.AddDefaultHotkeys(icons,entries,false);Assert.Empty(icons);
		entries.Add(new ShortcutRegistryEntry{Collection="SubArray",Flags=8,Key=79,CommandId=0});
		entries.Add(new ShortcutRegistryEntry{Collection="AddArray",Flags=8,Key=78,CommandId=99});
		entries.Add(new ShortcutRegistryEntry{Collection="AddArray",Flags=8,Key=78,CommandId=100});
		RadialCommandIcons.AddDefaultHotkeys(icons,entries,true);
		Assert.False(icons.ContainsKey("Ctrl+O"));Assert.False(icons.ContainsKey("Ctrl+N"));Assert.Equal(2,icons["Ctrl+S"]);
		icons["Ctrl+O"]=101;entries.Clear();RadialCommandIcons.AddDefaultHotkeys(icons,entries,true);
		Assert.Equal(101,icons["Ctrl+O"]);Assert.Equal(1,icons["Ctrl+N"]);
	}

	[Fact]
	public void ThirdPartyIconUsesBoundCommandDespiteRenamedCaption()
	{
		var flags=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
		var field=typeof(RadialCommandIcons).GetField("_namedAssets",flags)!;
		var sync=typeof(RadialCommandIcons).GetField("Sync",flags)!.GetValue(null)!;
		lock(sync)
		{
			var previous=field.GetValue(null);
			using var correct=new Bitmap(24,24);using(var g=Graphics.FromImage(correct))g.Clear(Color.Green);
			using var wrong=new Bitmap(24,24);using(var g=Graphics.FromImage(wrong))g.Clear(Color.Red);
			try
			{
				field.SetValue(null,new Dictionary<string,Bitmap>{{"删除特征",correct},{"快速上色",wrong}});
				using var output=new Bitmap(24,24);using var graphics=Graphics.FromImage(output);
				var item=new MenuItemConfig{Kind=ActionKinds.NativeCommand,Value=ExternalToolbarCommandReference.Encode(1,41663,"删特征"),Label="快速上色"};
				RadialCommandIcons.Draw(graphics,item,new RectangleF(0,0,24,24),SceneKeys.Part);
				Assert.Equal(Color.Green.ToArgb(),output.GetPixel(12,12).ToArgb());
				item.Kind=ActionKinds.ExternalPlugin;
				item.Value=ExternalNamedCommandReference.Encode(1,"第三方插件 > 示例插件","删除特征");
				graphics.Clear(Color.Transparent);
				RadialCommandIcons.Draw(graphics,item,new RectangleF(0,0,24,24),SceneKeys.Part);
				Assert.Equal(Color.Green.ToArgb(),output.GetPixel(12,12).ToArgb());
				graphics.Clear(Color.Transparent);
				RadialCommandIcons.Draw(graphics,item,new RectangleF(0,0,24,24),SceneKeys.Drawing);
				Assert.NotEqual(Color.Green.ToArgb(),output.GetPixel(12,12).ToArgb());
				graphics.Clear(Color.Transparent);item.Kind=ActionKinds.Hotkey;item.Value="unmapped-"+Guid.NewGuid();
				RadialCommandIcons.Draw(graphics,item,new RectangleF(0,0,24,24),SceneKeys.Part);
				Assert.NotEqual(Color.Red.ToArgb(),output.GetPixel(12,12).ToArgb());
			}
			finally {field.SetValue(null,previous);}
		}
	}

	[Fact]
	public void ExternalToolbarIdentityUsesResourceAndRemovedBottomHoleFallsBack()
	{
		var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
		var field = typeof(RadialCommandIcons).GetField("_namedAssets", flags)!;
		var sync = typeof(RadialCommandIcons).GetField("Sync", flags)!.GetValue(null)!;
		lock (sync)
		{
			var previous = field.GetValue(null);
			using var tooth = new Bitmap(24, 24);
			using (var g = Graphics.FromImage(tooth)) g.Clear(Color.Green);
			using var removed = new Bitmap(24, 24);
			using (var g = Graphics.FromImage(removed)) g.Clear(Color.Red);
			try
			{
				field.SetValue(null, new Dictionary<string, Bitmap>
				{
					["删除特征"] = tooth,
					["压铆拉铆底孔"] = removed
				});
				using var output = new Bitmap(24, 24);
				using var graphics = Graphics.FromImage(output);
				var toolbar = new MenuItemConfig
				{
					Kind = ActionKinds.ExternalPlugin,
					Value = ExternalToolbarCommandReference.Encode(1, 41663, "删特征"),
					Label = "用户改名"
				};
				RadialCommandIcons.Draw(graphics, toolbar, new RectangleF(0, 0, 24, 24), SceneKeys.Part);
				Assert.Equal(Color.Green.ToArgb(), output.GetPixel(12, 12).ToArgb());

				graphics.Clear(Color.Transparent);
				var bottomHole = new MenuItemConfig
				{
					Kind = ActionKinds.ExternalPlugin,
					Value = ExternalNamedCommandReference.Encode(1, "第三方插件 > 博士钣金", "压铆拉铆底孔"),
					Label = "旧配置"
				};
				RadialCommandIcons.Draw(graphics, bottomHole, new RectangleF(0, 0, 24, 24), SceneKeys.Part);
				Assert.NotEqual(Color.Red.ToArgb(), output.GetPixel(12, 12).ToArgb());
			}
			finally
			{
				field.SetValue(null, previous);
			}
		}
	}

	[Fact]
	public void QinJianIdentityUsesCommandOrTabIconResource()
	{
		var field = typeof(RadialCommandIcons).GetField("_namedAssets", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
		var sync = typeof(RadialCommandIcons).GetField("Sync", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
		lock (sync)
		{
			var previous = field.GetValue(null);
			using var icon = new Bitmap(24, 24);
			using (var g = Graphics.FromImage(icon)) g.Clear(Color.Green);
			try
			{
				field.SetValue(null, new Dictionary<string, Bitmap> { ["门板切口"] = icon });
				using var output = new Bitmap(24, 24);
				using var graphics = Graphics.FromImage(output);
				RadialCommandIcons.Draw(graphics, new MenuItemConfig
				{
					Kind = ActionKinds.QinJian,
					Value = QinJianActionValue.Format("琴剑钣金", "门板切口"),
					Label = "用户改名"
				}, new RectangleF(0, 0, 24, 24), SceneKeys.Part);
				Assert.Equal(Color.Green.ToArgb(), output.GetPixel(12, 12).ToArgb());
			}
			finally { field.SetValue(null, previous); }
		}
	}
	[Fact]
	public void UnknownHotkeyDoesNotAcquireNativeOpenIcon()
	{
		var field=typeof(RadialCommandIcons).GetField("_resources",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
		var sync=typeof(RadialCommandIcons).GetField("Sync",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
		lock(sync)
		{
			var previous=field.GetValue(null);
			using var open=new Bitmap(24,24); using(var g=Graphics.FromImage(open)) g.Clear(Color.Red);
			try
			{
				field.SetValue(null,new Dictionary<int,Bitmap>{{0,open}});
				using var output=new Bitmap(24,24);using var graphics=Graphics.FromImage(output);
				RadialCommandIcons.Draw(graphics,new MenuItemConfig{Kind=ActionKinds.Hotkey,Value="unmapped-"+Guid.NewGuid(),Label="Unmapped"},new RectangleF(0,0,24,24),SceneKeys.Part);
				Assert.NotEqual(Color.Red.ToArgb(),output.GetPixel(12,12).ToArgb());
			}
			finally { field.SetValue(null,previous); }
		}
	}

	[Fact]
	public void BasicActionIconRendersInsideTinyCell()
	{
		// Dense preview rings can produce cells below the icon catalogue's
		// 16 px source floor.  The renderer must downscale a valid source rather
		// than throw from OnPaint (which turns the WinForms preview into a red X).
		using var output = new Bitmap(8, 8);
		using var graphics = Graphics.FromImage(output);
		var item = new MenuItemConfig { Kind = ActionKinds.Basic, Value = "settings", Label = "设置" };
		RadialCommandIcons.Draw(graphics, item, new RectangleF(0, 0, 8, 8), SceneKeys.Part);
		Assert.True(Enumerable.Range(0, output.Width).SelectMany(x => Enumerable.Range(0, output.Height).Select(y => output.GetPixel(x, y).A)).Any(alpha => alpha > 0));
	}
	[Fact]
	public void CardinalIconsCommitTheirOwnBindingsAndOpenCenterCancels()
	{
		Exception? failure = null;
		var thread = new Thread(() =>
		{
			try
			{
				var config = new PluginConfig();
				var ring = new RingConfig { Index = 0, Slots = 8, DiameterPx = 520, Enabled = true };
				for (int slot = 0; slot < 8; slot++) ring.Items.Add(new MenuItemConfig { Slot = slot, Label = "Command " + slot, Kind = ActionKinds.Hotkey, Value = "Ctrl+" + slot });
				var scene = new SceneMenuConfig { Key = SceneKeys.Part, MenuSizePx = 520, CenterRadiusPx = 24, Rings = new List<RingConfig> { ring } };
				using var wheel = new RadialMenuControl(Color.Fuchsia) { Size = new Size(520, 520) };
				wheel.SetMenu(config, scene);
				MenuItemConfig? selected = null;
				int cancelled = 0;
				wheel.ItemSelected += (_, item) => selected = item;
				wheel.CancelRequested += (_, __) => cancelled++;
				for (int slot = 0; slot < 8; slot++)
				{
					double angle = slot * Math.PI / 4;
					wheel.CommitAt(new Point(260 + (int)(210 * Math.Sin(angle)), 260 - (int)(210 * Math.Cos(angle))));
					Assert.Same(ring.Items[slot], selected);
				}
wheel.CommitAt(new Point(260, 260));
			wheel.CommitAt(new Point(260, 150)); // Inside the widened single-ring hub: the open middle cancels too.
			wheel.CommitAt(new Point(-10, -10));
			Assert.Equal(3, cancelled);
				Assert.Equal(24, scene.CenterRadiusPx); // Appearance never writes the stored setting.
			}
			catch (Exception ex) { failure = ex; }
		});
		thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
		if (failure != null) throw failure;
	}

	[Fact]
	public void CustomIconRequiresMatchingCommandAndReloadsAfterReset()
	{
		string directory = Path.Combine(Path.GetTempPath(), "xy-icon-test-" + Guid.NewGuid());
		Directory.CreateDirectory(Path.Combine(directory, "RadialIcons"));
		try
		{
			using (var icon = new Bitmap(24,24)) { using (var g = Graphics.FromImage(icon)) g.Clear(Color.Red); icon.Save(Path.Combine(directory,"RadialIcons","red.png")); }
			string path = Path.Combine(directory, "radial-icons.json");
			File.WriteAllText(path,new JavaScriptSerializer().Serialize(new RadialIconOverrides.Document { Icons = new List<RadialIconOverrides.Entry> { new RadialIconOverrides.Entry { Scene=SceneKeys.Part,Ring=0,Slot=0,Kind=ActionKinds.Hotkey,Value="Ctrl+1",Image="red.png" } } }));
			using var overrides = new RadialIconOverrides(path);
			overrides.Reload();
			using var output = new Bitmap(24,24); using var graphics = Graphics.FromImage(output);
			var item = new MenuItemConfig {Slot=0,Kind=ActionKinds.Hotkey,Value="Ctrl+1"};
			Assert.True(overrides.Draw(graphics,SceneKeys.Part,0,item,new RectangleF(0,0,24,24)));
			Assert.Equal(Color.Red.ToArgb(),output.GetPixel(12,12).ToArgb());
			item.Value="Ctrl+2";
			Assert.False(overrides.Draw(graphics,SceneKeys.Part,0,item,new RectangleF(0,0,24,24)));
			item.Value="Ctrl+1";
			Assert.False(overrides.Draw(graphics,SceneKeys.Assembly,0,item,new RectangleF(0,0,24,24)));
			File.WriteAllText(path,"{\"Version\":1,\"Icons\":[]}"); overrides.Reload();
			Assert.False(overrides.Draw(graphics,SceneKeys.Part,0,item,new RectangleF(0,0,24,24)));
		}
		finally { Directory.Delete(directory,true); }
	}
}
