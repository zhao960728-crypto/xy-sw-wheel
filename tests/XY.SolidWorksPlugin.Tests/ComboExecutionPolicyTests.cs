using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ComboExecutionPolicyTests
{
	[Fact]
	public void QuickSheetMetalUsesSingleTriggerBehavior()
	{
		ComboCommandConfig command = new ComboCommandConfig
		{
			Label = "快速钣金..",
			Kind = ActionKinds.Hotkey,
			Value = "Alt+Shift+5",
			AutoConfirm = true
		};

		Assert.True(SwAddin.IsPassThroughComboCommand(command));
		Assert.True(SwAddin.UsesDoctorUiThreadSingleTrigger(command));
	}

	[Fact]
	public void QinJianCommandNeverUsesDoctorQuickSheetMetalDispatch()
	{
		ComboCommandConfig command = new ComboCommandConfig
		{
			Label = "快速钣金",
			Kind = ActionKinds.QinJian,
			Value = "琴剑钣金 > 快速钣金",
			AutoConfirm = true
		};

		Assert.False(SwAddin.UsesDoctorUiThreadSingleTrigger(command));
	}

	[Fact]
	public void OtherAutomaticCommandsStillUseAutomaticConfirmation()
	{
		ComboCommandConfig command = new ComboCommandConfig
		{
			Label = "删特征..",
			Kind = ActionKinds.Hotkey,
			Value = "Alt+Shift+1",
			AutoConfirm = true
		};

		Assert.False(SwAddin.IsPassThroughComboCommand(command));
	}

	[Fact]
	public void QinJianCommandCanCompleteWithoutObservableUi()
	{
		ComboCommandConfig command = new ComboCommandConfig
		{
			Label = "去特征留实体",
			Kind = ActionKinds.QinJian,
			Value = "琴剑SW工具 > 去特征留实体",
			AutoConfirm = true
		};

		Assert.True(SwAddin.AllowsUnobservedComboCompletion(command));
	}

	[Fact]
	public void HotkeyCommandStillRequiresObservableCompletion()
	{
		ComboCommandConfig command = new ComboCommandConfig
		{
			Label = "普通命令",
			Kind = ActionKinds.Hotkey,
			Value = "Ctrl+Q",
			AutoConfirm = true
		};

		Assert.False(SwAddin.AllowsUnobservedComboCompletion(command));
	}

	[Fact]
	public void QinJianCommandUsesDedicatedQinJianFlow()
	{
		ComboCommandConfig command = new ComboCommandConfig
		{
			Label = "去除圆角",
			Kind = ActionKinds.QinJian,
			Value = "琴剑钣金 > 去除圆角"
		};

		Assert.True(SwAddin.IsQinJianComboCommand(command));
	}

	[Fact]
	public void DoctorHotkeyStaysOutOfQinJianFlow()
	{
		ComboCommandConfig command = new ComboCommandConfig
		{
			Label = "删特征..",
			Kind = ActionKinds.Hotkey,
			Value = "Alt+Shift+1",
			AutoConfirm = true
		};

		Assert.False(SwAddin.IsQinJianComboCommand(command));
	}

	[Fact]
	public void JiaLiChuangCommandUsesDedicatedDirectPluginFlow()
	{
		ComboCommandConfig command = new ComboCommandConfig
		{
			Label = "快速倒角",
			Kind = ActionKinds.JiaLiChuang,
			Value = "快速倒角Show"
		};

		Assert.True(SwAddin.IsJiaLiChuangComboCommand(command));
		Assert.True(SwAddin.AllowsUnobservedComboCompletion(command));
		Assert.False(SwAddin.UsesDoctorUiThreadSingleTrigger(command));
	}

	[Fact]
	public void JiaLiChuangLabelCannotAccidentallyEnterDoctorQuickSheetMetalFlow()
	{
		ComboCommandConfig command = new ComboCommandConfig
		{
			Label = "快速钣金",
			Kind = ActionKinds.JiaLiChuang,
			Value = "钣金功能Callback",
			AutoConfirm = true
		};

		Assert.False(SwAddin.UsesDoctorUiThreadSingleTrigger(command));
	}

	[Theory]
	[InlineData("STEP格式互换", "Alt+Shift+0", true)]
	[InlineData("STEP格式转换", "Alt+Shift+0", true)]
	[InlineData("散件组装配", "Alt+Shift+8", false)]
	public void DoctorBatchFormatConversionHasDedicatedCompletionFlow(string label, string value, bool expected)
	{
		ComboCommandConfig command = new ComboCommandConfig
		{
			Label = label,
			Kind = ActionKinds.Hotkey,
			Value = value
		};

		Assert.Equal(expected, SwAddin.IsDoctorBatchFormatConversion(command));
	}

	[Theory]
	[InlineData("自动出图..", ActionKinds.Hotkey, "Alt+Shift+E", true)]
	[InlineData("自动工程图", ActionKinds.Hotkey, "Alt+Shift+E", true)]
	[InlineData("属性处理", ActionKinds.Hotkey, "Alt+Shift+T", false)]
	[InlineData("自动出图", ActionKinds.QinJian, "琴剑钣金 > 自动出图", false)]
	[InlineData("自动出图", ActionKinds.JiaLiChuang, "AutoDrawing", false)]
	public void DoctorAutomaticDrawingKeepsItsTaskPaneOpen(string label, string kind, string value, bool expected)
	{
		ComboCommandConfig command = new ComboCommandConfig
		{
			Label = label,
			Kind = kind,
			Value = value
		};

		Assert.Equal(expected, SwAddin.IsDoctorPersistentTaskPaneCommand(command));
	}

	[Theory]
	[InlineData("属性处理", ActionKinds.Hotkey, "Alt+Shift+T", true)]
	[InlineData("属性处理", ActionKinds.QinJian, "琴剑钣金 > 属性处理", false)]
	[InlineData("属性处理", ActionKinds.JiaLiChuang, "属性处理Callback", false)]
	[InlineData("自动出图", ActionKinds.Hotkey, "Alt+Shift+E", false)]
	public void DoctorPropertyProcessingUsesItsOwnTaskPaneFlow(string label, string kind, string value, bool expected)
	{
		ComboCommandConfig command = new ComboCommandConfig
		{
			Label = label,
			Kind = kind,
			Value = value
		};

		Assert.Equal(expected, SwAddin.IsDoctorPropertyTaskPaneCommand(command));
	}

	[Theory]
	[InlineData(true, 0, 3, true)]
	[InlineData(true, 1, 3, true)]
	[InlineData(true, 2, 3, false)]
	[InlineData(false, 0, 3, false)]
	public void MandatoryAdvanceCheckOnlyAppliesToManualStepsBeforeAnotherStep(bool required, int index, int count, bool expected)
	{
		ComboCommandConfig command = new ComboCommandConfig { RequireAdvanceKey = required };

		Assert.Equal(expected, SwAddin.RequiresStepAdvanceKey(command, index, count));
	}

	[Fact]
	public void AutomaticConfirmationDisablesAdvanceKeyEvenWhenDelayCheckWasSaved()
	{
		ComboCommandConfig command = new ComboCommandConfig { AutoConfirm = true, RequireAdvanceKey = true };

		Assert.False(SwAddin.RequiresStepAdvanceKey(command, 0, 2));
	}

	[Theory]
	[InlineData("属性处理...", ActionKinds.Hotkey, "Alt+Shift+T", 0, 2, true)]
	[InlineData("STEP格式互换...", ActionKinds.Hotkey, "Alt+Shift+0", 0, 2, true)]
	[InlineData("STEP格式转换...", ActionKinds.Hotkey, "Alt+Shift+0", 1, 2, false)]
	[InlineData("散件组装配...", ActionKinds.Hotkey, "Alt+Shift+8", 0, 2, false)]
	[InlineData("属性处理", ActionKinds.QinJian, "琴剑钣金 > 属性处理", 0, 2, false)]
	[InlineData("属性处理", ActionKinds.JiaLiChuang, "属性处理Callback", 0, 2, false)]
	public void DoctorUnobservableCommandsOfferAdvanceKeyFallback(string label, string kind, string value, int index, int count, bool expected)
	{
		ComboCommandConfig command = new ComboCommandConfig
		{
			Label = label,
			Kind = kind,
			Value = value
		};

		Assert.Equal(expected, SwAddin.UsesDoctorAdvanceKeyFallback(command, index, count));
		Assert.Equal(expected, SwAddin.ShouldEnableStepAdvanceKey(command, comboAdvanceEnabled: false, index, count));
		Assert.False(SwAddin.RequiresStepAdvanceKey(command, index, count));
	}

	[Theory]
	[InlineData("属性处理...", "Alt+Shift+T")]
	[InlineData("STEP格式转换...", "Alt+Shift+0")]
	public void DoctorAutomaticConfirmationDoesNotUseManualAdvance(string label, string value)
	{
		ComboCommandConfig command = new ComboCommandConfig
		{
			Label = label,
			Kind = ActionKinds.Hotkey,
			Value = value,
			AutoConfirm = true
		};

		Assert.False(SwAddin.UsesDoctorAdvanceKeyFallback(command, 0, 2));
		Assert.False(SwAddin.RequiresStepAdvanceKey(command, 0, 2));
		Assert.False(SwAddin.ShouldEnableStepAdvanceKey(command, comboAdvanceEnabled: true, 0, 2));
	}

	[Fact]
	public void AdvanceStatusTextNamesTheFunctionAndConfiguredKey()
	{
		ComboCommandConfig command = new ComboCommandConfig { Label = "属性处理..." };

		string text = SwAddin.BuildComboAdvanceStatusText(command, "Ctrl");

		Assert.Contains("属性处理", text);
		Assert.Contains("功能完成后", text);
		Assert.Contains("Ctrl", text);
		Assert.Contains("进入下一步", text);
	}

	[Fact]
	public void DoctorFallbackStatusTextExplainsNormalDetectionAndManualAdvance()
	{
		ComboCommandConfig command = new ComboCommandConfig { Label = "STEP格式转换..." };

		string text = SwAddin.BuildDoctorAdvanceFallbackStatusText(command, "Ctrl");

		Assert.Contains("正在判断", text);
		Assert.Contains("如已完成", text);
		Assert.Contains("Ctrl", text);
		Assert.Contains("手动进入下一步", text);
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(true, true)]
	public void BatchWindowCloseOnlyFailsAutomaticSteps(bool autoConfirm, bool expectedFailure)
	{
		ComboCommandConfig command = new ComboCommandConfig { AutoConfirm = autoConfirm };

		Assert.Equal(expectedFailure, SwAddin.ShouldFailDoctorBatchCloseWithoutStart(command));
	}

}
