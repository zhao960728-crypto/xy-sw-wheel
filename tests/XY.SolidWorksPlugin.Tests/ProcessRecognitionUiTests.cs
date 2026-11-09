using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ProcessRecognitionUiTests
{
	[Fact]
	public void SinglePartWindowProvidesExplicitConfirmWriteButton()
	{
		Exception? failure = null;
		Thread thread = new Thread(() =>
		{
			try
			{
				using ProcessRecognitionWindow window = new ProcessRecognitionWindow(() => null);
				Button confirm = Descendants(window).OfType<Button>().Single(button => button.Text == "确定写入");
				Assert.Same(confirm, window.AcceptButton);
			}
			catch (Exception ex)
			{
				failure = ex;
			}
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();
		if (failure != null) throw failure;
	}

	[Fact]
	public void AdditionalAttributeButtonStagesValueWithoutWritingDocument()
	{
		Exception? failure = null;
		Thread thread = new Thread(() =>
		{
			try
			{
				using ProcessRecognitionWindow window = new ProcessRecognitionWindow(() => null);
				window.StageAdditionalPropertyForTest("工序类型", "焊接");
				Assert.Equal(1, window.PendingAdditionalPropertyCount);
			}
			catch (Exception ex)
			{
				failure = ex;
			}
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();
		if (failure != null) throw failure;
	}

	[Fact]
	public void ProcessGroupRuleCanBeEditedAndSaved()
	{
		Exception? failure = null;
		Thread thread = new Thread(() =>
		{
			try
			{
				var config = new ProcessRecognitionConfig { ProcessCombinations = "钣金件=激光下料>折弯", ProcessTypeGroups = "" };
				using ProcessRulesWindow window = new ProcessRulesWindow(config);
				var field = typeof(ProcessRulesWindow).GetField("_combinations", BindingFlags.Instance | BindingFlags.NonPublic);
				Assert.NotNull(field);
				DataGridView grid = (DataGridView)field!.GetValue(window)!;
				Assert.False(grid.ReadOnly);
				grid.Rows[0].Cells[0].Value = "钣金制造";
				grid.Rows[0].Cells[1].Value = "激光下料>折弯>喷粉";
				typeof(ProcessRulesWindow).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
				Assert.Equal(DialogResult.OK, window.DialogResult);
				Assert.Equal("钣金制造=激光下料>折弯>喷粉", window.Result.ProcessCombinations);
			}
			catch (Exception ex) { failure = ex; }
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();
		if (failure != null) throw failure;
	}

	private static IEnumerable<Control> Descendants(Control root)
	{
		foreach (Control child in root.Controls)
		{
			yield return child;
			foreach (Control descendant in Descendants(child)) yield return descendant;
		}
	}
}
