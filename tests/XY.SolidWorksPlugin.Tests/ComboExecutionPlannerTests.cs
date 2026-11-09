using System.Linq;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ComboExecutionPlannerTests
{
	[Fact]
	public void BuildWaitsForManualDeleteFilletWhenAutoConfirmIsOff()
	{
		ComboCommandConfig[] commands =
		{
			new ComboCommandConfig { Label = "删除圆角..", Kind = ActionKinds.Hotkey, Value = "Alt+Shift+T", AutoConfirm = false },
			new ComboCommandConfig { Label = "门板切口..", Kind = ActionKinds.Hotkey, Value = "Alt+Shift+Period", AutoConfirm = true }
		};

		ComboExecutionStep[] steps = ComboExecutionPlanner.Build(commands).ToArray();

		Assert.Equal(2, steps.Length);
		Assert.Equal("Alt+Shift+T", steps[0].Command.Value);
		Assert.True(steps[0].WaitForManualCompletion);
		Assert.False(steps[0].SaveSelectionBefore);
		Assert.False(steps[0].RestoreSelectionAfter);
		Assert.Equal("Alt+Shift+Period", steps[1].Command.Value);
	}

	[Fact]
	public void BuildKeepsDeleteFilletAsOneConfiguredStepWhenAutoConfirmIsOn()
	{
		ComboCommandConfig[] commands =
		{
			new ComboCommandConfig { Label = "删除圆角..", Kind = ActionKinds.Hotkey, Value = "Alt+Shift+T", AutoConfirm = true }
		};

		ComboExecutionStep[] steps = ComboExecutionPlanner.Build(commands).ToArray();

		Assert.Single(steps);
		Assert.Equal("Alt+Shift+T", steps[0].Command.Value);
		Assert.True(steps[0].Command.AutoConfirm);
		Assert.False(steps[0].WaitForManualCompletion);
		Assert.False(steps[0].SaveSelectionBefore);
	}

	[Fact]
	public void BuildPreservesMandatoryAdvanceKeyCheck()
	{
		ComboCommandConfig[] commands =
		{
			new ComboCommandConfig
			{
				Label = "格式转换",
				Kind = ActionKinds.Hotkey,
				Value = "Alt+Shift+0",
				AutoConfirm = true,
				RequireAdvanceKey = true
			}
		};

		ComboExecutionStep step = Assert.Single(ComboExecutionPlanner.Build(commands));
		Assert.True(step.Command.RequireAdvanceKey);
	}

	[Fact]
	public void BuildWaitsForManualCompletionWhenAutoConfirmIsOff()
	{
		ComboCommandConfig[] commands =
		{
			new ComboCommandConfig { Label = "门板切口..", Kind = ActionKinds.Hotkey, Value = "Alt+Shift+Period", AutoConfirm = false }
		};

		ComboExecutionStep step = ComboExecutionPlanner.Build(commands).Single();

		Assert.True(step.WaitForManualCompletion);
	}

	[Fact]
	public void BuildDoesNotWaitForManualCompletionWhenAutoConfirmIsOn()
	{
		ComboCommandConfig[] commands =
		{
			new ComboCommandConfig { Label = "门板切口..", Kind = ActionKinds.Hotkey, Value = "Alt+Shift+Period", AutoConfirm = true }
		};

		ComboExecutionStep step = ComboExecutionPlanner.Build(commands).Single();

		Assert.False(step.WaitForManualCompletion);
	}

	[Fact]
	public void BuildPreservesFiveConfiguredCommandsInOrder()
	{
		ComboCommandConfig[] commands = Enumerable.Range(1, 5)
			.Select(index => new ComboCommandConfig
			{
				Label = "命令" + index,
				Kind = ActionKinds.Hotkey,
				Value = "Alt+" + index,
				AutoConfirm = index == 5
			})
			.ToArray();

		ComboExecutionStep[] steps = ComboExecutionPlanner.Build(commands).ToArray();

		Assert.Equal(5, steps.Length);
		Assert.Equal(new[] { "Alt+1", "Alt+2", "Alt+3", "Alt+4", "Alt+5" }, steps.Select(step => step.Command.Value));
	}

	[Fact]
	public void BuildKeepsEachQinJianCommandAsOneStep()
	{
		ComboCommandConfig[] commands =
		{
			new ComboCommandConfig { Label = "去特征留实体", Kind = ActionKinds.QinJian, Value = "琴剑SW工具 > 去特征留实体", AutoConfirm = true },
			new ComboCommandConfig { Label = "去除圆角", Kind = ActionKinds.QinJian, Value = "琴剑钣金 > 去除圆角" },
			new ComboCommandConfig { Label = "门板切口", Kind = ActionKinds.QinJian, Value = "琴剑钣金 > 门板切口" },
			new ComboCommandConfig { Label = "快速插入折弯", Kind = ActionKinds.QinJian, Value = "琴剑钣金 > 快速插入折弯", AutoConfirm = true }
		};

		ComboExecutionStep[] steps = ComboExecutionPlanner.Build(commands).ToArray();

		Assert.Equal(4, steps.Length);
		Assert.Equal(commands.Select(command => command.Value), steps.Select(step => step.Command.Value));
	}

	[Fact]
	public void BuildDoesNotExpandQinJianDeleteFillet()
	{
		ComboCommandConfig command = new ComboCommandConfig
		{
			Label = "删除圆角",
			Kind = ActionKinds.QinJian,
			Value = "琴剑钣金 > 删除圆角",
			AutoConfirm = true
		};

		ComboExecutionStep step = ComboExecutionPlanner.Build(new[] { command }).Single();

		Assert.Equal(ActionKinds.QinJian, step.Command.Kind);
		Assert.Equal(command.Value, step.Command.Value);
	}
}
