using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public class IntelligentWorkflowTests
{
	private sealed class MemoryStore : PropertyWriteBatch.Store
	{
		public readonly Dictionary<string, PropertyWriteBatch.Snapshot> Values = new();
		public string? FailAt;
		public int Writes;
		public PropertyWriteBatch.Snapshot Read(string name) => Values.TryGetValue(name, out var value) ? value : new();
		public void Set(string name, string text, PropertyWriteBatch.Snapshot previous)
		{
			Writes++; Values[name] = new() { Exists = true, Raw = text, Type = previous.Type };
			if (name == FailAt) throw new InvalidOperationException("simulated host rejection");
		}
		public void Restore(string name, PropertyWriteBatch.Snapshot previous) { if (previous.Exists) Values[name] = previous; else Values.Remove(name); }
	}
	[Fact]
	public void BatchRestoresNewAndExistingPropertiesAfterPartialFailure()
	{
		var store = new MemoryStore { FailAt = "new" };
		store.Values["formula"] = new() { Exists = true, Type = 3, Raw = "\"SW-Mass\"", Resolved = "12" };
		var error = Assert.Throws<InvalidOperationException>(() => PropertyWriteBatch.Apply(store, new[] { Pair("formula", "changed"), Pair("new", "new value") }));
		Assert.Contains("已恢复", error.Message);
		Assert.Equal("\"SW-Mass\"", store.Read("formula").Raw);
		Assert.Equal(3, store.Read("formula").Type);
		Assert.False(store.Read("new").Exists);
	}
	[Fact]
	public void ConflictingDuplicateTargetsFailBeforeAnyWrite()
	{
		var store = new MemoryStore();
		Assert.Throws<InvalidOperationException>(() => PropertyWriteBatch.Apply(store, new[] { Pair("Process", "a"), Pair("process", "b") }));
		Assert.Equal(0, store.Writes);
	}
	[Fact]
	public void IdenticalValuesDoNotDirtyTheDocument()
	{
		var store = new MemoryStore(); store.Values["工序"] = new() { Exists = true, Raw = "激光-折弯" };
		Assert.Equal(0, PropertyWriteBatch.Apply(store, new[] { Pair("工序", "激光-折弯") }));
		Assert.Equal(0, store.Writes);
	}
	[Theory]
	[InlineData("Brass", "SS", false)]
	[InlineData("Glass Fiber", "SS", false)]
	[InlineData("SS304", "SS", true)]
	[InlineData("SUS 304", "SUS", true)]
	[InlineData("Q235B", "Q235", true)]
	public void MaterialAbbreviationsDoNotMatchInsideUnrelatedWords(string material, string keyword, bool expected)
		=> Assert.Equal(expected, ProcessRecognitionService.MatchesMaterialRule(material, keyword));
	[Fact]
	public void SpecificMaterialRuleWinsOverGeneralRule()
	{
		var input = new ProcessRecognitionInput { IsPart = true, Material = "SUS304", Features = new[] { new ProcessFeatureFact { TypeName = "SMBaseFlange" } } };
		var result = ProcessRecognitionService.Recognize(input, true, new() { SurfaceMaterialRules = "SUS=拉丝|SUS304=抛光" });
		Assert.Contains(result, row => row.Name == "抛光"); Assert.DoesNotContain(result, row => row.Name == "拉丝");
	}
	[Fact]
	public void CollidingProcessPropertyNamesCannotSilentlyOverwriteSummary()
	{
		Assert.Throws<InvalidOperationException>(() => ProcessRecognitionService.BuildPropertyValues(new[] { new RecognizedProcess { Name = "折弯" } }, null, new() { SummaryPropertyName = "工序", CraftDescriptionPropertyName = "工序" }));
	}
	[Fact]
	public void WatermarkRefreshAndCancelNeverSaveOrMutateLiveSettings() => Sta(() =>
	{
		var config = new PluginConfig(); int saves = 0, refreshes = 0;
		string original = config.PropertyWatermark.Fields[0].DisplayName;
		using var window = new PropertyWatermarkSettingsWindow(() => config, _ => saves++, () => null, () => refreshes++);
		var grid = Descendants(window).OfType<DataGridView>().Single();
		var fields = (List<PropertyWatermarkField>)((BindingSource)grid.DataSource).DataSource;
		fields[0].DisplayName = "草稿名称";
		Call(window, "RefreshProperties");
		Assert.Equal(0, saves); Assert.Equal(0, refreshes); Assert.Equal(original, config.PropertyWatermark.Fields[0].DisplayName);
	});
	[Fact]
	public void WatermarkSaveKeepsPositionMovedWhileSettingsWereOpen() => Sta(() =>
	{
		var config = new PluginConfig(); PluginConfig? saved = null;
		using var window = new PropertyWatermarkSettingsWindow(() => config, value => saved = value, () => null, () => { });
		config.PropertyWatermark.PositionMode = "custom"; config.PropertyWatermark.OffsetX = 331; config.PropertyWatermark.OffsetY = 222;
		Call(window, "ApplyConfig");
		Assert.NotNull(saved); Assert.Equal(331, saved!.PropertyWatermark.OffsetX); Assert.Equal(222, saved.PropertyWatermark.OffsetY);
		Assert.Equal("custom", saved.PropertyWatermark.PositionMode);
	});
	[Fact]
	public void ManualProcessEditReplacesMachineConfidenceAndEvidence() => Sta(() =>
	{
		using var window = new ProcessRecognitionWindow(() => null);
		var grid = Descendants(window).OfType<DataGridView>().Single();
		grid.Rows.Add(1, "手动焊接", "94%", "原来识别为折弯");
		typeof(DataGridView).GetMethod("OnCellEndEdit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(grid, new object[] { new DataGridViewCellEventArgs(1, 0) });
		Assert.Equal("手动", grid.Rows[0].Cells["Confidence"].Value);
		Assert.DoesNotContain("折弯", grid.Rows[0].Cells["Evidence"].Value.ToString());
	});
	[Fact]
	public void LongWatermarkLineFitsAvailableWidth() => Sta(() =>
	{
		using var font = new Font("SimSun", 12f);
		string text = PropertyWatermarkWindow.FitLine(new string('工', 500), font, 320);
		Assert.EndsWith("…", text); Assert.True(TextRenderer.MeasureText(text, font).Width <= 320);
	});
	[Fact]
	public void WeightAliasesPreserveLegacyCustomPropertyLookup()
		=> Assert.Contains("单重", WatermarkPropertyAccess.Aliases("质量"));
	private static KeyValuePair<string, string> Pair(string key, string value) => new(key, value);
	[Fact]
	public void DigitShortcutAndExactMatchHavePriority()
	{
		var rows = new[] { new KeyboardShortcutRow { Hotkey = "Ctrl+Alt+1", Command = "其他" }, new KeyboardShortcutRow { Hotkey = "Ctrl+1", Command = "目标" } };
		Assert.Equal("目标", ShortcutPaletteMatcher.Match(rows, ShortcutPaletteMatcher.TokensFromKeyData(Keys.Control | Keys.D1)).First().Command);
	}
	[Fact]
	public void ToolWindowFitsNegativeCoordinateMonitor()
	{
		var area = new Rectangle(-1920, 0, 1920, 1040);
		var bounds = PluginToolWindow.FitWindow(new Rectangle(-2500, 900, 1000, 600), area);
		Assert.True(area.Contains(bounds)); Assert.Equal(new Size(1000, 600), bounds.Size);
	}
	[Fact]
	public void ReplacingAssemblyWindowDoesNotDisposePartDraft() => Sta(() =>
	{
		var addin = new SwAddin(); using var part = new ProcessRecognitionWindow(() => null);
		typeof(SwAddin).GetField("_partProcessWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(addin, part);
		Call(addin, "CloseAssemblyProcessWindow"); Assert.False(part.IsDisposed);
		Call(addin, "ClosePartProcessWindow"); Assert.True(part.IsDisposed);
	});
	private static void Call(object target, string method) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, null);
	private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(control => new[] { control }.Concat(Descendants(control)));
	private static void Sta(Action action)
	{
		Exception? failure = null; var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
		thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(15000), "UI test timeout"); if (failure != null) throw failure;
	}
}
