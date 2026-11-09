using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class SettingsUiRenderTests
{
	[Fact]
	public void MainAndComboEditorsExposeComboAdvanceControls()
	{
		Exception? failure = null;
		Thread thread = new Thread(() =>
		{
			try
			{
				PluginConfig config = ConfigStore.Deserialize("{}");
				ConfigStore.Normalize(config);
				config.FontSizePx = 8;
				config.FontName = "SimSun";
				config.SketchWheelEnabled = true;
				SceneMenuConfig sketchScene = config.Menus[SceneKeys.Sketch];
				sketchScene.MenuSizePx = 550;
				sketchScene.CenterRadiusPx = 24;
					sketchScene.Rings = new List<RingConfig>
				{
					new RingConfig { Index = 0, Slots = 8, DiameterPx = 210, Enabled = true, Items = Items(40, "属性处理", "删除圆角", "材质数量", "快速上色", "门板切口", "设置固定面", "统计数量", "自动爆炸") },
					new RingConfig { Index = 1, Slots = 12, DiameterPx = 360, Enabled = true, Items = Items(80, "直接编辑", "线性阵列", "移动面", "删除面", "关闭角", "转换到钣金", "快速工程图", "设置为正视图", "复制实体", "分割", "组合", "圆角") },
					new RingConfig { Index = 2, Slots = 16, DiameterPx = 550, Enabled = true, Items = Items(120, "质量分析", "Quick Split", "斜接法兰", "切口", "闭合角", "展开", "折叠", "边线法兰", "放样折弯", "转折", "镜向实体", "草图阵列", "修复草图", "等距实体", "草图图片", "从选择生成草图") }
				};
				using SettingsWindow settings = new SettingsWindow(() => config, _ => { });
				settings.Location = new Point(-32000, -32000);
				settings.Show();
				Application.DoEvents();
				Assert.Contains(Descendants(settings).OfType<Button>(), control => control.Text == "功能开关");
				Assert.DoesNotContain(Descendants(settings).OfType<Button>(), control => control.Text == "工序识别" || control.Text == "门板/封板");
				CheckBox sketchWheel = Descendants(settings).OfType<CheckBox>().Single(control => control.Name == "scene-enabled-sw.sketch");
				Assert.True(sketchWheel.Checked);
				sketchWheel.Checked = false;
				Application.DoEvents();
				Descendants(settings).OfType<Button>().Single(control => control.Text == "基础").PerformClick();
				Application.DoEvents();
				Assert.Contains(Descendants(settings).OfType<ComboBox>(), control => Convert.ToString(control.SelectedItem) == "SW 空白页");
				Button sketchPage = Descendants(settings).OfType<Button>().Single(control => control.Text == "SW 草图页");
				sketchPage.PerformClick();
				Application.DoEvents();
				Assert.Contains(Descendants(settings).OfType<ComboBox>(), control => Convert.ToString(control.SelectedItem) == "SW 草图页");
				Capture(settings, "settings-sketch-wheel.png");
				Button shortcutPage = Descendants(settings).OfType<Button>().Single(control => control.Text == "命令库");
				shortcutPage.PerformClick();
				Application.DoEvents();
				Assert.Contains(Descendants(settings).OfType<Button>(), control => control.Text == "读取SW命令");
				TreeView commandTree = Descendants(settings).OfType<TreeView>().Single();
				Assert.Equal("全部命令", commandTree.Nodes[0].Text);
				Capture(settings, "settings-command-tree.png");
				Button commandPage = Descendants(settings).OfType<Button>().Single(control => control.Text == "格子命令");
				commandPage.PerformClick();
				Application.DoEvents();
				settings.PerformLayout();
				Assert.Contains(Descendants(settings).OfType<Button>(), control => control.Text == "修改显示名");
				Capture(settings, "settings-combo-advance.png");

				Button appearancePage = Descendants(settings).OfType<Button>().Single(control => control.Text == "外观");
				appearancePage.PerformClick();
				Application.DoEvents();
				settings.PerformLayout();
				Assert.Contains(Descendants(settings).OfType<Label>(), control => control.Text == "轮盘直径 px");
				Assert.Contains(Descendants(settings).OfType<Label>(), control => control.Text == "直径");
				Assert.DoesNotContain(Descendants(settings).OfType<Label>(), control => control.Text == "当前格字号 px");
				Assert.DoesNotContain(Descendants(settings).OfType<Button>(), control => control.Text == "全部自适应");
				AssertPreviewCellToolsVisible(settings);
				Capture(settings, "settings-appearance-cad-layout.png");
				CapturePreviewPanePrintWindow(settings, "settings-preview-pane.png");
				CaptureSingleRingAutoFit(config);

				Button fontPage = Descendants(settings).OfType<Button>().Single(control => control.Text == "字体与适配");
				fontPage.PerformClick();
				Application.DoEvents();
				settings.PerformLayout();
				Assert.Contains(Descendants(settings).OfType<Label>(), control => control.Text == "当前格字号 px");
				Assert.Contains(Descendants(settings).OfType<Label>(), control => control.Text == "当前格字体");
				Assert.Contains(Descendants(settings).OfType<Label>(), control => control.Text == "当前格文字颜色");
				Assert.Contains(Descendants(settings).OfType<CheckBox>(), control => control.Text == "自动适配格子");
				Assert.Contains(Descendants(settings).OfType<Button>(), control => control.Text == "全部自适应");
				Assert.Contains(Descendants(settings).OfType<Button>(), control => control.Text == "应用字体到全部格");
				Assert.Contains(Descendants(settings).OfType<Button>(), control => control.Text == "应用颜色到全部格");
				CheckBox autoFit = Descendants(settings).OfType<CheckBox>().Single(control => control.Text == "自动适配格子");
				autoFit.Checked = false;
				NumericUpDown cellFontSize = Descendants(settings).OfType<NumericUpDown>().Single(control => control.Maximum == 72m);
				cellFontSize.Value = 22m;
				Application.DoEvents();
				Assert.Equal(22, config.Menus[SceneKeys.Sketch].Rings.SelectMany(ring => ring.Items).First().FontSizePx);
				Capture(settings, "settings-font-and-autofit.png");
				Descendants(settings).OfType<Button>().Single(control => control.Text == "全部自适应").PerformClick();
				Application.DoEvents();
				Descendants(settings).OfType<Button>().Single(control => control.Text == "格子命令").PerformClick();
				Application.DoEvents();
				DataGridView wheelGrid = Descendants(settings).OfType<DataGridView>().Single(grid => grid.Columns.Contains("FontSize"));
				Assert.All(wheelGrid.Rows.Cast<DataGridViewRow>().Where(row => !string.IsNullOrWhiteSpace(Convert.ToString(row.Cells["Value"].Value))),
					row => Assert.True(string.IsNullOrWhiteSpace(Convert.ToString(row.Cells["FontSize"].Value))));
				using SettingsWindow featureSettings = new SettingsWindow(() => config, _ => { }, mode: SettingsWindowMode.FeatureTools);
				featureSettings.Location = new Point(-32000, -32000);
				featureSettings.Show();
				Application.DoEvents();
				Assert.DoesNotContain(Descendants(featureSettings).OfType<Button>(), control => control.Text == "外观" || control.Text == "格子命令" || control.Text == "查看轮盘");
				Button processPage = Descendants(featureSettings).OfType<Button>().Single(control => control.Text == "工序识别");
				processPage.PerformClick();
				Application.DoEvents();
				featureSettings.PerformLayout();
				Assert.Contains(Descendants(featureSettings).OfType<Label>(), control => control.Text == "表处识别方式");
				Assert.Contains(Descendants(featureSettings).OfType<Label>(), control => control.Text == "材质表处映射");
				Assert.Contains(Descendants(featureSettings).OfType<Label>(), control => control.Text.Contains("全部确定写入"));
				Assert.Contains(Descendants(featureSettings).OfType<TextBox>(), control => control.Text.Contains("AISI=拉丝"));
				Assert.Contains(Descendants(featureSettings).OfType<Label>(), control => control.Text == "补充固定工序");
				Assert.Contains(Descendants(featureSettings).OfType<Button>(), control => control.Text == "工序组、类型默认组与名称设置…");
				Capture(featureSettings, "settings-process-recognition.png");

				Button featurePage = Descendants(featureSettings).OfType<Button>().Single(control => control.Text == "功能开关");
				featurePage.PerformClick();
				Application.DoEvents();
				featureSettings.PerformLayout();
				Assert.Contains(Descendants(featureSettings).OfType<CheckBox>(), control => control.Text == "启用工序识别" && control.Checked);
				Assert.Contains(Descendants(featureSettings).OfType<CheckBox>(), control => control.Text == "启用属性水印");
				Assert.Contains(Descendants(featureSettings).OfType<CheckBox>(), control => control.Text == "启用轮盘与鼠标触发");
				Assert.Contains(Descendants(featureSettings).OfType<CheckBox>(), control => control.Text == "启用快捷键候选" && control.Checked);
				Assert.DoesNotContain(Descendants(featureSettings).OfType<CheckBox>(), control => control.Text == "启用属性快速写入");
				Assert.DoesNotContain(Descendants(featureSettings).OfType<CheckBox>(), control => control.Text == "启用门板/封板间隙调整");
				Assert.DoesNotContain(Descendants(featureSettings).OfType<CheckBox>(), control => control.Text == "启用报价");
				Assert.DoesNotContain(Descendants(featureSettings).OfType<CheckBox>(), control => control.Text == "启用模型与孔上色");
				Assert.DoesNotContain(Descendants(featureSettings).OfType<CheckBox>(), control => control.Text == "启用批量底孔／工艺孔");
				Assert.DoesNotContain(Descendants(featureSettings).OfType<CheckBox>(), control => control.Text == "启用爆炸图工具");
				Assert.Contains(Descendants(featureSettings).OfType<CheckBox>(), control => control.Text == "启用一键爆炸" && control.Checked);
				Assert.DoesNotContain(Descendants(featureSettings).OfType<CheckBox>(), control => control.Text == "启用门缝显示标注");
				Assert.Contains(Descendants(featureSettings).OfType<CheckBox>(), control => control.Text == "开启工序识别后自动后台识别" && !control.Enabled);
				Assert.DoesNotContain(Descendants(featureSettings).OfType<CheckBox>(), control => control.Text == "写入后刷新属性水印");
				Assert.Equal(4, Descendants(featureSettings).OfType<GroupBox>().Count());
				Assert.DoesNotContain(Descendants(featureSettings), control => control.Text.Contains("切割清单"));
				Capture(featureSettings, "settings-feature-switches.png");

				Button aboutPage = Descendants(featureSettings).OfType<Button>().Single(control => control.Text == "关于");
				aboutPage.PerformClick();
				Application.DoEvents();
				featureSettings.PerformLayout();
				TableLayoutPanel aboutDetails = Descendants(featureSettings).OfType<TableLayoutPanel>()
					.Single(control => control.Name == "AboutDetails");
				Assert.Contains(Descendants(aboutDetails).OfType<Label>(), label => label.Text == "作者");
				Assert.Contains(Descendants(aboutDetails).OfType<Label>(), label => label.Text == "XY");
				Assert.Contains(Descendants(aboutDetails).OfType<Label>(), label => label.Text == "联系");
				Assert.Equal(2, aboutDetails.RowCount);
				var contactCodes = Descendants(featureSettings).OfType<PictureBox>().ToArray();
				Assert.Equal(2, contactCodes.Length);
				Assert.All(contactCodes, code => Assert.NotNull(code.Image));
				Assert.Equal(new[] { "XY.Contact.Douyin.jpg", "XY.Contact.WeChat.jpg" }, contactCodes.Select(code => code.Name).OrderBy(name => name).ToArray());
				Capture(featureSettings, "settings-about.png");

				Button helpPage = Descendants(featureSettings).OfType<Button>().Single(control => control.Text == "功能说明");
				helpPage.PerformClick();
				Application.DoEvents();
				featureSettings.PerformLayout();
				Assert.Contains(Descendants(featureSettings).OfType<Label>(), label => label.Text == "可采用衔接键，手动进行下一步。");
				Assert.Contains(Descendants(featureSettings).OfType<Label>(), label => label.Text == "字号适配");
				Assert.Contains(Descendants(featureSettings).OfType<Label>(), label => label.Text == "显示名与搜索");
				Assert.Contains(Descendants(featureSettings).OfType<Label>(), label => label.Text == "错误日志");
				Capture(featureSettings, "settings-help.png");

				KeyboardShortcutRow[] catalogRows =
				{
					new KeyboardShortcutRow { Hotkey = "直接执行", Command = "直线", Category = "SOLIDWORKS > 草图 > 草图绘制实体", ActionKind = ActionKinds.NativeCommand, Value = NativeCommandReference.Encode(43) },
					new KeyboardShortcutRow { Hotkey = "直接执行", Command = "剪裁实体", Category = "SOLIDWORKS > 草图 > 草图工具", ActionKind = ActionKinds.NativeCommand, Value = NativeCommandReference.Encode(59) }
				};
				using ComboCommandEditor editor = new ComboCommandEditor("测试组合", Array.Empty<ComboCommandConfig>(), catalogRows, getDisplayName: row => row.Command == "直线" ? "画线别名" : row.Command);
				editor.Location = new Point(-32000, -32000);
				editor.Show();
				Application.DoEvents();
				editor.PerformLayout();
				Assert.Contains(Descendants(editor).OfType<CheckBox>(), control => control.Text == "衔接键");
				TreeView comboTree = Descendants(editor).OfType<TreeView>().Single();
				Assert.Contains(comboTree.Nodes[0].Nodes.Cast<TreeNode>(), node => node.Text == "SOLIDWORKS");
				DataGridView comboCatalog = Descendants(editor).OfType<DataGridView>().Single();
				Assert.Contains(comboCatalog.Rows.Cast<DataGridViewRow>(), row => Convert.ToString(row.Cells["Command"].Value) == "画线别名");
				CheckBox autoConfirm = Descendants(editor).OfType<CheckBox>().Single(control => control.Text == "自动确定");
				CheckBox delayCheck = Descendants(editor).OfType<CheckBox>().Single(control => control.Text == "按键衔接");
				autoConfirm.Checked = true;
				delayCheck.Checked = true;
				Assert.False(autoConfirm.Checked);
				autoConfirm.Checked = true;
				Assert.False(delayCheck.Checked);
				Assert.DoesNotContain(Descendants(editor).OfType<Button>(), control => control.Text == "选");
				Capture(editor, "combo-editor-advance.png");

				using BackupContentDialog exportDialog = new BackupContentDialog(exporting: true);
				exportDialog.Location = new Point(-32000, -32000);
				exportDialog.Show();
				Application.DoEvents();
				CheckBox[] exportOptions = Descendants(exportDialog).OfType<CheckBox>().ToArray();
				Assert.Contains(exportOptions, control => control.Text == "轮盘配置" && control.Checked);
				Assert.Contains(exportOptions, control => control.Text == "SOLIDWORKS 自定义快捷键" && control.Checked);
				Assert.Contains(Descendants(exportDialog).OfType<Button>(), control => control.Text == "确定");
				Capture(exportDialog, "backup-content-export.png");

				using BackupContentDialog importDialog = new BackupContentDialog(exporting: false, configAvailable: false, shortcutsAvailable: true);
				importDialog.Location = new Point(-32000, -32000);
				importDialog.Show();
				Application.DoEvents();
				CheckBox unavailableConfig = Descendants(importDialog).OfType<CheckBox>().Single(control => control.Text == "轮盘配置");
				Assert.False(unavailableConfig.Enabled);
				Assert.True(Descendants(importDialog).OfType<CheckBox>().Single(control => control.Text == "SOLIDWORKS 自定义快捷键").Checked);
				Capture(importDialog, "backup-content-import.png");
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
			ExceptionDispatchInfo.Capture(failure).Throw();
		}
	}

	private static IEnumerable<Control> Descendants(Control root)
	{
		foreach (Control child in root.Controls)
		{
			yield return child;
			foreach (Control descendant in Descendants(child))
			{
				yield return descendant;
			}
		}
	}

	// Regression test: the preview pane must always expose a cell-editing toolbar
	// (drag-to-add, drag-to-remove, shape picker, drop-to-place, resize,
// nudge, batch, practice) with non-zero bounds once the preview pane is
	// laid out. The earlier FlowLayoutPanel + Dock=Top + AutoSize layout
	// collapsed the toolbar to zero height and the user saw no buttons; this
	// guard fails the test if a future refactor brings that bug back.
	private static void AssertPreviewCellToolsVisible(Form settings)
	{
		Button add = Descendants(settings).OfType<Button>().Single(control => control.Text == "＋ 加格");
		Button remove = Descendants(settings).OfType<Button>().Single(control => control.Text == "－ 删格");
		Button drop = Descendants(settings).OfType<Button>().Single(control => control.Text == "拖入新格");
		Button resize = Descendants(settings).OfType<Button>().Single(control => control.Text == "调大小");
		Button nudge = Descendants(settings).OfType<Button>().Single(control => control.Text == "微调");
		Button batch = Descendants(settings).OfType<Button>().Single(control => control.Text == "批量");
		CheckBox practice = Descendants(settings).OfType<CheckBox>().Single(control => control.Text == "练习");
		Assert.True(add.Visible && add.Width > 0 && add.Height > 0, "加格按钮不可见或尺寸为零: " + add.Bounds);
		Assert.True(remove.Visible && remove.Width > 0 && remove.Height > 0, "删格按钮不可见或尺寸为零: " + remove.Bounds);
		Assert.True(drop.Visible && drop.Width > 0 && drop.Height > 0, "拖入新格按钮不可见或尺寸为零: " + drop.Bounds);
		Assert.True(resize.Visible && resize.Width > 0 && resize.Height > 0, "调大小按钮不可见或尺寸为零: " + resize.Bounds);
		Assert.True(nudge.Visible && nudge.Width > 0 && nudge.Height > 0, "微调按钮不可见或尺寸为零: " + nudge.Bounds);
		Assert.True(batch.Visible && batch.Width > 0 && batch.Height > 0, "批量按钮不可见或尺寸为零: " + batch.Bounds);
		Assert.True(practice.Visible && practice.Width > 0 && practice.Height > 0, "练习复选框不可见或尺寸为零: " + practice.Bounds);
	}

	private static List<MenuItemConfig> Items(int firstCommandId, params string[] labels)
	{
		return labels.Select((label, index) => new MenuItemConfig
		{
			Slot = index,
			Label = label,
			Kind = ActionKinds.NativeCommand,
			Value = NativeCommandReference.Encode(firstCommandId + index)
		}).ToList();
	}

	private static void CaptureSingleRingAutoFit(PluginConfig config)
	{
		List<MenuItemConfig> items = Items(300, "打开", "格散", "随机着色", "新建", "STEP格式互换", "散件组装配", "自动工程图", "材质数量");
		items[1].FontSizePx = 30;
		items[1].TextColor = "#FF0000";
		items[4].FontSizePx = 19;
		items[5].FontSizePx = 21;
		items[7].FontSizePx = 29;
		items[7].TextColor = "#00FF80";
		SceneMenuConfig scene = new SceneMenuConfig
		{
			Key = SceneKeys.Blank,
			MenuSizePx = 550,
			CenterRadiusPx = 24,
			Rings = new List<RingConfig>
			{
				new RingConfig
				{
					Index = 0,
					Slots = 8,
					DiameterPx = 550,
					Enabled = true,
					Items = items
				}
			}
		};
		using Form form = new Form
		{
			Width = 620,
			Height = 620,
			Location = new Point(-32000, -32000),
			BackColor = Color.White,
			FormBorderStyle = FormBorderStyle.FixedSingle
		};
		using RadialMenuControl wheel = new RadialMenuControl(Color.White)
		{
			Dock = DockStyle.Fill,
			ScaleMenuForPreview = false
		};
		form.Controls.Add(wheel);
		wheel.SetMenu(config, scene);
		form.Show();
		Application.DoEvents();
		Capture(form, "radial-single-ring-auto.png");
		form.Close();
		CaptureConfiguredScenes();
	}

	private static void CaptureConfiguredScenes()
	{
		string? configPath = Environment.GetEnvironmentVariable("XY_UI_REAL_CONFIG_PATH");
		if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
		{
			return;
		}
		PluginConfig config = ConfigStore.Deserialize(File.ReadAllText(configPath));
		ConfigStore.Normalize(config);
		foreach (KeyValuePair<string, SceneMenuConfig> pair in config.Menus)
		{
			int menuSize = Math.Max(260, Math.Min(900, pair.Value.MenuSizePx > 0 ? pair.Value.MenuSizePx : config.MenuSizePx));
			using Form form = new Form
			{
				Width = menuSize + 70,
				Height = menuSize + 70,
				Location = new Point(-32000, -32000),
				BackColor = Color.White,
				FormBorderStyle = FormBorderStyle.FixedSingle
			};
			using RadialMenuControl wheel = new RadialMenuControl(Color.White)
			{
				Dock = DockStyle.Fill,
				ScaleMenuForPreview = false
			};
			form.Controls.Add(wheel);
			wheel.SetMenu(config, pair.Value);
			form.Show();
			Application.DoEvents();
			Capture(form, "radial-real-" + pair.Key.Replace('.', '-') + ".png");
			form.Close();
		}
	}

	private static void Capture(Form form, string fileName)
	{
		string? directory = Environment.GetEnvironmentVariable("XY_UI_CAPTURE_DIR");
		if (string.IsNullOrWhiteSpace(directory))
		{
			return;
		}
		Directory.CreateDirectory(directory);
		form.CreateControl();
		using Bitmap bitmap = new Bitmap(form.Width, form.Height);
		form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
		bitmap.Save(Path.Combine(directory, fileName));
	}

	// Capture the preview pane using PrintWindow (PW_RENDERFULLCONTENT = 0x02).
	// Form.DrawToBitmap skips Button/Label content inside nested Dock=Top
	// panels, so the only reliable way to render the cell toolbar in a
	// headless test is to ask Windows to print the window to a bitmap.
	private static void CapturePreviewPanePrintWindow(Form form, string fileName)
	{
		string? directory = Environment.GetEnvironmentVariable("XY_UI_CAPTURE_DIR");
		if (string.IsNullOrWhiteSpace(directory))
		{
			return;
		}
		Directory.CreateDirectory(directory);
		Panel? previewPane = Descendants(form).OfType<Panel>().FirstOrDefault(panel =>
			panel.Dock == DockStyle.Fill && panel.BackColor == Color.FromArgb(232, 241, 236) && panel.Padding == new Padding(24));
		if (previewPane == null) return;
		form.Visible = true;
		form.Location = new Point(0, 0);
		form.WindowState = FormWindowState.Normal;
		form.BringToFront();
		form.Refresh();
		Application.DoEvents();
		System.Threading.Thread.Sleep(120);
		using Bitmap bitmap = new Bitmap(previewPane.Width, previewPane.Height);
		using Graphics g = Graphics.FromImage(bitmap);
		IntPtr hdc = g.GetHdc();
		try
		{
			PrintWindow(previewPane.Handle, hdc, 0x02);
		}
		finally
		{
			g.ReleaseHdc(hdc);
		}
		bitmap.Save(Path.Combine(directory, fileName));
		form.Location = new Point(-32000, -32000);
	}

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
}
