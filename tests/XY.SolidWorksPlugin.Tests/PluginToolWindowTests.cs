using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class PluginToolWindowTests
{
	[Fact]
	public void ModelessEditorLeavesOwnerEnabledAndCompletesOnValidatedClick() => Sta(() =>
	{
		using var owner = new Form(); owner.Show();
		using var window = new PluginToolWindow();
		var ok = new Button { Text = "保存", DialogResult = DialogResult.OK };
		bool valid = false; ok.Click += (_, __) => { if (!valid) window.DialogResult = DialogResult.None; };
		window.Controls.Add(ok);
		var result = window.ShowModelessAsync(owner);
		Assert.False(window.Modal); Assert.True(owner.Enabled); Assert.False(result.IsCompleted);
		ok.PerformClick(); Application.DoEvents();
		Assert.True(window.Visible); Assert.False(result.IsCompleted);
		valid = true; ok.PerformClick(); Application.DoEvents();
		Assert.Equal(DialogResult.OK, result.GetAwaiter().GetResult()); Assert.True(window.IsDisposed); Assert.True(owner.Enabled);
	});
	[Fact]
	public void DisposingOwnerCancelsPendingModelessEditor() => Sta(() =>
	{
		using var owner = new Form(); owner.Show();
		using var window = new PluginToolWindow();
		var result = window.ShowModelessAsync(owner);
		owner.Dispose(); Application.DoEvents();
		Assert.True(window.IsDisposed); Assert.True(result.IsCompleted);
		Assert.Equal(DialogResult.Cancel, result.GetAwaiter().GetResult());
	});
	[Fact]
	public void StylingPreservesColorSamplesWarningsAndUserInput() => Sta(() =>
	{
		using var window = new PluginToolWindow();
		var swatch = new Button { BackColor = Color.Magenta, ForeColor = Color.White, Text = "文字颜色" };
		PluginToolWindow.PreserveAppearance(swatch);
		var warning = new Label { ForeColor = Color.DarkOrange, Text = "请确认" };
		var input = new NumericUpDown { Value = 6.05m, DecimalPlaces = 2 };
		var grid = new DataGridView { AllowUserToAddRows = false }; grid.Columns.Add("Color", "颜色"); grid.Rows.Add("自定义"); grid.Rows[0].Cells[0].Style.BackColor = Color.LimeGreen;
		window.Controls.AddRange(new Control[] { swatch, warning, input, grid }); window.ApplyVisualStyle(window);
		Assert.Equal(Color.Magenta, swatch.BackColor); Assert.Equal(Color.DarkOrange, warning.ForeColor); Assert.Equal(6.05m, input.Value);
		Assert.Equal(Color.LimeGreen, grid.Rows[0].Cells[0].Style.BackColor);
	});
	[Fact]
	public void DynamicPagesAreStyledWithoutExecutingActionsOrChangingSelection() => Sta(() =>
	{
		using var window = new PluginToolWindow(); window.ApplyVisualStyle(window);
		int clicked = 0; var panel = new Panel(); var button = new Button { Text = "保存设置", AutoSize = true }; button.Click += (_, __) => clicked++;
		var combo = new ComboBox(); combo.Items.AddRange(new object[] { "A", "B" }); combo.SelectedIndex = 1;
		panel.Controls.AddRange(new Control[] { button, combo }); window.Controls.Add(panel); window.ApplyVisualStyle(window);
		Assert.Equal(PluginToolWindow.Accent, button.BackColor); Assert.Equal("B", combo.SelectedItem); Assert.Equal(0, clicked);
		panel.Dispose(); window.ApplyVisualStyle(window);
	});
	[Fact]
	public void RemovedPagesReleaseThemeSubscriptionsAndCanBeReadded() => Sta(() =>
	{
		using var window = new PluginToolWindow(); var tabs = new TabControl(); tabs.TabPages.Add("配置"); window.Controls.Add(tabs); window.ApplyVisualStyle(window);
		Assert.Equal(TabDrawMode.OwnerDrawFixed, tabs.DrawMode); window.Controls.Remove(tabs); Assert.Equal(TabDrawMode.Normal, tabs.DrawMode);
		window.Controls.Add(tabs); Assert.Equal(TabDrawMode.OwnerDrawFixed, tabs.DrawMode);
	});
	[Fact]
	public void DisabledPrimaryButtonRegainsItsActionStyleWhenEnabled() => Sta(() =>
	{
		using var window = new PluginToolWindow(); var button = new Button { Text = "保存设置", Enabled = false }; window.Controls.Add(button); window.ApplyVisualStyle(window);
		Assert.NotEqual(PluginToolWindow.Accent, button.BackColor); button.Enabled = true; Assert.Equal(PluginToolWindow.Accent, button.BackColor);
	});
	[Fact]
	public void PrimaryActionAndBodyTextHaveReadableContrast()
	{
		Assert.True(Contrast(Color.White, PluginToolWindow.Accent) >= 4.5);
		Assert.True(Contrast(PluginToolWindow.Ink, PluginToolWindow.Canvas) >= 4.5);
	}
	[Fact]
	public void TransparentOverlaysKeepTheirOwnRendering()
	{
		Assert.Equal(typeof(Form), typeof(RadialMenuWindow).BaseType);
		Assert.Equal(typeof(Form), typeof(RadialForegroundWindow).BaseType);
		Assert.Equal(typeof(Form), typeof(PropertyWatermarkWindow).BaseType);
	}
	private static double Contrast(Color a, Color b) { double x = Luminance(a), y = Luminance(b); return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05); }
	private static double Luminance(Color c) => 0.2126 * Linear(c.R / 255d) + 0.7152 * Linear(c.G / 255d) + 0.0722 * Linear(c.B / 255d);
	private static double Linear(double n) => n <= 0.04045 ? n / 12.92 : Math.Pow((n + 0.055) / 1.055, 2.4);
	private static void Sta(Action action)
	{
		Exception? failure = null; var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
		thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (failure != null) throw failure;
	}
}
