using System;
using System.Drawing;
using System.Linq;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class IntegrationRegressionTests
{
	[Theory]
	[InlineData(31)] [InlineData(33)] [InlineData(37)] [InlineData(46)] [InlineData(52)]
	public void RenamedTappedWizardHoleStillRequiresTapping(int type)
	{
		var input = new ProcessRecognitionInput { IsPart = true, Features = new[] { new ProcessFeatureFact { Name = "安装位置", TypeName = "HoleWzd", HoleWizardType = type } } };
		Assert.Contains(ProcessRecognitionService.Recognize(input, false), process => process.Name == "攻丝");
	}
	[Theory]
	[InlineData(2)] [InlineData(10)] [InlineData(24)] [InlineData(29)]
	public void RenamedCounterboreOrCountersinkStillRequiresRecessOperation(int type)
	{
		var input = new ProcessRecognitionInput { IsPart = true, Features = new[] { new ProcessFeatureFact { Name = "位置A", TypeName = "HoleWzd", HoleWizardType = type } } };
		Assert.Contains(ProcessRecognitionService.Recognize(input, true), process => process.Name == "沉孔");
	}
	[Theory]
	[InlineData(22)] [InlineData(25)] [InlineData(69)] [InlineData(72)]
	public void PlainHolesDoNotAcquireTappingOrRecessProcesses(int type)
	{
		var input = new ProcessRecognitionInput { IsPart = true, Features = new[] { new ProcessFeatureFact { Name = "位置A", TypeName = "HoleWzd", HoleWizardType = type } } };
		Assert.DoesNotContain(ProcessRecognitionService.Recognize(input, true), process => process.Name == "攻丝" || process.Name == "沉孔");
	}
	[Fact]
	public void SuppressedTappedHoleDoesNotContribute()
	{
		var input = new ProcessRecognitionInput { IsPart = true, Features = new[] { new ProcessFeatureFact { TypeName = "HoleWzd", HoleWizardType = 31, IsSuppressed = true } } };
		Assert.DoesNotContain(ProcessRecognitionService.Recognize(input, true), process => process.Name == "攻丝");
	}
	[Fact]
	public void ExternalReferenceRetainsReadableIdentityAndLegacyDocumentGuard()
	{
		string value = ExternalToolbarCommandReference.Encode(2, 40000, "博士/属性处理 + A");
		Assert.True(ExternalToolbarCommandReference.TryDecode(value, out int doc, out int id));
		Assert.Equal(2, doc); Assert.Equal(40000, id); Assert.Equal("博士/属性处理 + A", ExternalToolbarCommandReference.Name(value));
		Assert.True(ExternalToolbarCommandReference.TryDecode("sw-external-toolbar:1/40000", out _, out _));
		Assert.False(ExternalToolbarCommandReference.TryDecode("sw-external-toolbar:9/40000", out _, out _));
	}
	[Fact]
	public void RecordingPauseDeleteAndRepeatedCommandsPreserveUserSequence()
	{
		using var session = new CommandRecordingSession();
		var zoom = new ComboCommandConfig { Label = "适合窗口", Kind = ActionKinds.Basic, Value = "zoom-fit", RequireAdvanceKey = true };
		var clear = new ComboCommandConfig { Label = "清除选择", Kind = ActionKinds.Basic, Value = "clear-selection", RequireAdvanceKey = true };
		// A command clicked while paused is dropped, and consecutive identical
		// commands collapse into one step instead of repeating the same work.
		session.Append(zoom); session.Paused = true; session.Append(clear); session.Paused = false; session.Append(clear);
		Assert.Equal(2, session.Commands.Count);
		Assert.Equal("zoom-fit", session.Commands[0].Value);
		Assert.Equal("clear-selection", session.Commands[1].Value);
		clear.Value = "changed-after-append";
		Assert.Equal("clear-selection", session.Commands[1].Value);
		session.RemoveAt(0); Assert.Single(session.Commands); Assert.True(session.Commands[0].RequireAdvanceKey);
	}
	[Fact]
	public void ImmediateBuiltInsDoNotWaitForAWindowAndInteractiveToolsDo()
	{
		Assert.True(SwAddin.IsImmediateComboCommand(new ComboCommandConfig { Kind = ActionKinds.Basic, Value = "zoom-fit" }));
		Assert.True(SwAddin.IsImmediateComboCommand(new ComboCommandConfig { Kind = ActionKinds.Delay, Value = "100" }));
		Assert.False(SwAddin.IsImmediateComboCommand(new ComboCommandConfig { Kind = ActionKinds.Basic, Value = "explode-view" }));
		Assert.False(SwAddin.IsImmediateComboCommand(new ComboCommandConfig { Kind = ActionKinds.Hotkey, Value = "F" }));
	}
	[Fact]
	public void WheelFitsSmallWorkAreaWithoutChangingNormalDesktopSize()
	{
		Assert.Equal(568, RadialMenuWindow.FitMenuSize(900, new Size(800, 580)));
		Assert.Equal(600, RadialMenuWindow.FitMenuSize(600, new Size(1920, 1040)));
	}
	[Fact]
	public void OneClickExplosionHasASelectableWheelEntry()
	{
		Assert.True(BasicActions.Contains("explode-all"));
	}

	[Fact]
	public void ExternalPluginImportRetainsKindAndReferenceInComboEditor()
	{
		Exception? failure = null;
		var thread = new System.Threading.Thread(() => {
			try {
				var slotType = typeof(ComboCommandEditor).GetNestedType("ComboSlotControl", System.Reflection.BindingFlags.NonPublic);
				using var slot = (System.Windows.Forms.Control)Activator.CreateInstance(slotType, 1);
				var reference = ExternalNamedCommandReference.Encode(1, "第三方插件 > 博士钣金", "自动出图");
				using var editor = new ComboCommandEditor("测试", Array.Empty<ComboCommandConfig>(), Array.Empty<KeyboardShortcutRow>());
				var row = new KeyboardShortcutRow { ActionKind = ActionKinds.ExternalPlugin, Category = "第三方插件 > 博士钣金", Command = "自动出图", Hotkey = "已安装 · 执行前匹配", Value = reference };
				Assert.True((bool)typeof(ComboCommandEditor).GetMethod("ApplyShortcutToSlot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(editor, new object[] { slot, row }));
				var actual = (ComboCommandConfig)slotType.GetMethod("ReadCommand").Invoke(slot, null);
				Assert.Equal(ActionKinds.ExternalPlugin, actual.Kind);
				Assert.Equal(reference, actual.Value);
				Assert.True(actual.RequireAdvanceKey);
			} catch (Exception ex) { failure = ex; }
		});
		thread.SetApartmentState(System.Threading.ApartmentState.STA); thread.Start(); thread.Join();
		if (failure != null) throw failure;
	}

	[Fact]
	public void ComboEditorRoundTripRetainsRecordedStepHandshakeOptions()
	{
		Exception? failure = null;
		var thread = new System.Threading.Thread(() =>
		{
			try
			{
				var slotType = typeof(ComboCommandEditor).GetNestedType("ComboSlotControl", System.Reflection.BindingFlags.NonPublic);
				using var slot = (System.Windows.Forms.Control)Activator.CreateInstance(slotType, 1);
				var input = new ComboCommandConfig
				{
					Label = "博士自动出图",
					Kind = ActionKinds.ExternalPlugin,
					Value = ExternalNamedCommandReference.Encode(1, "第三方插件 > 博士钣金", "自动出图"),
					AutoConfirm = true,
					RequireAdvanceKey = true,
					AdvanceKey = "Shift",
					WaitMode = ComboWaitModes.PropertyPane,
					DelayMs = 321,
					TimeoutMs = 654,
					OnError = ComboErrorPolicies.Retry,
					RetryCount = 2,
					SaveSelectionBefore = true,
					RestoreSelectionAfter = true
				};
				slotType.GetMethod("SetCommand")!.Invoke(slot, new object[] { input });
				var output = (ComboCommandConfig)slotType.GetMethod("ReadCommand")!.Invoke(slot, null)!;
				Assert.Equal(input.Kind, output.Kind);
				Assert.Equal(input.Value, output.Value);
				Assert.Equal(input.AdvanceKey, output.AdvanceKey);
				Assert.Equal(input.WaitMode, output.WaitMode);
				Assert.Equal(input.DelayMs, output.DelayMs);
				Assert.Equal(input.TimeoutMs, output.TimeoutMs);
				Assert.Equal(input.OnError, output.OnError);
				Assert.Equal(input.RetryCount, output.RetryCount);
				Assert.True(output.SaveSelectionBefore);
				Assert.True(output.RestoreSelectionAfter);
			}
			catch (Exception ex) { failure = ex; }
		});
		thread.SetApartmentState(System.Threading.ApartmentState.STA);
		thread.Start();
		thread.Join();
		if (failure != null) throw failure;
	}

	[Fact]
	public void PersistentDoctorPaneRequiresAdvanceEvenWhenAutomaticIsSelected()
	{
		var command = new ComboCommandConfig { Kind = ActionKinds.ExternalPlugin, Label = "我的出图", Value = ExternalNamedCommandReference.Encode(1, "第三方插件 > 博士钣金", "自动出图"), AutoConfirm = true };
		Assert.True(SwAddin.IsDoctorPersistentTaskPaneCommand(command));
		Assert.True(SwAddin.ShouldEnableStepAdvanceKey(command, false, 0, 2));
		command.Value = ExternalNamedCommandReference.Encode(1, "第三方插件 > 盛兴钣金", "自动出图");
		Assert.False(SwAddin.IsDoctorPersistentTaskPaneCommand(command));
	}

	[Fact]
	public void VendorFeatureManifestDoesNotConfuseVideoVisibilityWithFunctionAvailability()
	{
		var names = ThirdPartyPluginFunctionCatalog.ReadFeatureManifest("{\"videos\":[{\"featureId\":\"process\",\"name\":\"工序识别\",\"enabled\":false},{\"name\":\"说明\"}]}");
		Assert.Equal(new[] { "工序识别" }, names);
	}
}
