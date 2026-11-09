using System.Collections.Generic;
using System.Linq;
using XY.SolidWorksPlugin;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

[Collection("Command capture interaction")]
public class ComboStepRecordingTests
{
	private static ComboCommandConfig Step(string value = "cmd", string kind = ActionKinds.NativeCommand) =>
		new ComboCommandConfig { Label = value, Kind = kind, Value = value };

	[Fact]
	public void PlannerCarriesSelectionSaveAndRestore()
	{
		ComboCommandConfig command = Step();
		command.SaveSelectionBefore = true;
		command.RestoreSelectionAfter = true;

		ComboExecutionStep step = ComboExecutionPlanner.Build(new[] { command }).Single();

		Assert.True(step.SaveSelectionBefore);
		Assert.True(step.RestoreSelectionAfter);
	}

	[Fact]
	public void RestoreWithoutCaptureIsIgnored()
	{
		ComboCommandConfig command = Step();
		command.SaveSelectionBefore = false;
		command.RestoreSelectionAfter = true;

		ComboExecutionStep step = ComboExecutionPlanner.Build(new[] { command }).Single();

		Assert.False(step.SaveSelectionBefore);
		Assert.False(step.RestoreSelectionAfter);
	}

	[Fact]
	public void PlannerCarriesPerStepAdvanceKeyAndWaitSettings()
	{
		ComboCommandConfig command = Step();
		command.AdvanceKey = "Shift";
		command.WaitMode = ComboWaitModes.Delay;
		command.DelayMs = 900;
		command.TimeoutMs = 4000;
		command.OnError = ComboErrorPolicies.Skip;

		ComboExecutionStep step = ComboExecutionPlanner.Build(new[] { command }).Single();

		Assert.Equal("Shift", step.AdvanceKey);
		Assert.Equal(ComboWaitModes.Delay, step.WaitMode);
		Assert.Equal(900, step.DelayMs);
		Assert.Equal(4000, step.TimeoutMs);
		Assert.True(step.SkipsOnError);
		Assert.False(step.RetriesOnError);
	}

	[Fact]
	public void RetryPolicyNeedsACount()
	{
		ComboCommandConfig command = Step();
		command.OnError = ComboErrorPolicies.Retry;

		Assert.False(ComboExecutionPlanner.Build(new[] { command }).Single().RetriesOnError);

		command.RetryCount = 2;
		ComboExecutionStep step = ComboExecutionPlanner.Build(new[] { command }).Single();
		Assert.True(step.RetriesOnError);
		Assert.Equal(2, step.RetryCount);
	}

	[Fact]
	public void ExplicitWaitModeReplacesGenericManualWait()
	{
		ComboCommandConfig command = Step();
		Assert.True(ComboExecutionPlanner.Build(new[] { command }).Single().WaitForManualCompletion);

		command.WaitMode = ComboWaitModes.Delay;
		Assert.False(ComboExecutionPlanner.Build(new[] { command }).Single().WaitForManualCompletion);
	}

	[Fact]
	public void WaitAndErrorModesNormalizeUnknownValues()
	{
		Assert.Equal(ComboWaitModes.None, ComboWaitModes.Normalize("bogus"));
		Assert.Equal(ComboWaitModes.WindowClosed, ComboWaitModes.Normalize("windowclosed"));
		Assert.Equal(ComboErrorPolicies.Stop, ComboErrorPolicies.Normalize(null));
		Assert.Equal(ComboErrorPolicies.Skip, ComboErrorPolicies.Normalize("SKIP"));
	}

	[Fact]
	public void StepsSurviveOnANonComboCell()
	{
		PluginConfig config = ConfigStore.Deserialize(ConfigStore.Serialize(new PluginConfig()));
		ConfigStore.Normalize(config);
		SceneMenuConfig scene = config.Menus.Values.First();
		RingConfig ring = scene.Rings[0];
		ring.Items.Add(new MenuItemConfig
		{
			Slot = 0,
			Kind = ActionKinds.Hotkey,
			Value = "ctrl+1",
			ComboCommands = new List<ComboCommandConfig> { Step("sw-native:1") }
		});

		ConfigStore.Normalize(config);

		MenuItemConfig item = config.Menus.Values.First().Rings[0].Items.Single();
		Assert.Equal(ActionKinds.Hotkey, item.Kind);
		Assert.Single(item.ComboCommands);
	}

	[Fact]
	public void CellGeometryIsClampedDuringNormalize()
	{
		PluginConfig config = new PluginConfig();
		ConfigStore.Normalize(config);
		SceneMenuConfig scene = config.Menus.Values.First();
		scene.Rings[0].NodeInnerSizePercent = 500;
		scene.Rings[0].NodeOuterSizePercent = 0;
		scene.Rings[0].Items.Add(new MenuItemConfig
		{
			Slot = 0,
			Kind = ActionKinds.Hotkey,
			Value = "ctrl+1",
			CellSizePercent = 900,
			IconRatioPercent = 5,
			FreeX = 9.9,
			CellShape = "triangle"
		});

		ConfigStore.Normalize(config);

		RingConfig ring = config.Menus.Values.First().Rings[0];
		Assert.Equal(160, ring.NodeInnerSizePercent);
		Assert.Equal(100, ring.NodeOuterSizePercent);
		MenuItemConfig item = ring.Items.Single();
		Assert.Equal(160, item.CellSizePercent);
		Assert.Equal(20, item.IconRatioPercent);
		Assert.Equal(1.5, item.FreeX);
		Assert.Equal("Circle", item.CellShape);
	}

	[Fact]
	public void SceneCarriesUnassignedPoolAndAppearance()
	{
		PluginConfig config = new PluginConfig();
		ConfigStore.Normalize(config);
		SceneMenuConfig scene = config.Menus.Values.First();
		scene.UnassignedItems = null;
		scene.Appearance = null;
		scene.SurfaceOpacityPercent = 0;

		ConfigStore.Normalize(config);

		SceneMenuConfig result = config.Menus.Values.First();
		Assert.NotNull(result.UnassignedItems);
		Assert.NotNull(result.Appearance);
		Assert.Equal(65, result.SurfaceOpacityPercent);
	}

	[Fact]
	public void AppearanceColoursRoundTrip()
	{
		WheelAppearance appearance = new WheelAppearance { FillMode = "Gradient", StartColor = "bad", GradientAngle = -30 };
		appearance.Normalize();
		Assert.Equal("Gradient", appearance.FillMode);
		// An unreadable colour falls back to the current default surface.
		Assert.Equal(WheelAppearance.Classic().StartColor, appearance.StartColor);
		Assert.Equal(330, appearance.GradientAngle);
	}

	[Fact]
	public void LegacyAppearanceIsUpgradedToTheCurrentDefaultLook()
	{
		PluginConfig config = new PluginConfig();
		ConfigStore.Normalize(config);
		SceneMenuConfig scene = config.Menus.Values.First();
		// Exactly what an older build wrote: opaque white disc, teal accents,
		// blank caption colour (the field did not exist yet).
		scene.Appearance = new WheelAppearance { FillMode = "Solid", StartColor = "#FFFFFF",
			EndColor = "#DDF4F5", BorderColor = "#D7DFE6", HoverColor = "#008CA2", SelectedColor = "#008CA2",
			TextColor = "" };

		ConfigStore.Normalize(config);

WheelAppearance result = config.Menus.Values.First().Appearance;
		Assert.Equal(WheelAppearance.PresetClassic, result.PresetId);
		Assert.Equal(WheelAppearance.Classic().StartColor, result.StartColor);
		// The CAD palette has no frosted glass and no drop shadow.
		Assert.False(result.Glass);
	}

	[Fact]
	public void PresetChoicesAreNotMistakenForLegacyDefault()
	{
		// Classic and Native share the old hex palette but set an explicit
		// caption colour; a user picking them must not be silently rerouted
		// to the dark-glass default on the next Normalize.
		PluginConfig config = new PluginConfig();
		ConfigStore.Normalize(config);
		SceneMenuConfig scene = config.Menus.Values.First();
		scene.Appearance = WheelAppearance.Classic();

		ConfigStore.Normalize(config);

		Assert.Equal(WheelAppearance.PresetClassic, config.Menus.Values.First().Appearance.PresetId);
		Assert.Equal(WheelAppearance.Classic().StartColor, config.Menus.Values.First().Appearance.StartColor);
	}

[Fact]
	public void CustomisedAppearanceSurvivesNormalization()
	{
		PluginConfig config = new PluginConfig();
		ConfigStore.Normalize(config);
		SceneMenuConfig scene = config.Menus.Values.First();
		scene.Appearance = WheelAppearance.Midnight();

		ConfigStore.Normalize(config);

		WheelAppearance result = config.Menus.Values.First().Appearance;
		Assert.Equal(WheelAppearance.PresetMidnight, result.PresetId);
		Assert.Equal(WheelAppearance.Midnight().StartColor, result.StartColor);
	}

	[Fact]
	public void PresetsRoundTripByIdentity()
	{
		foreach (string id in new[] { WheelAppearance.PresetDarkGlass, WheelAppearance.PresetClassic,
			WheelAppearance.PresetNative, WheelAppearance.PresetMidnight })
		{
			WheelAppearance preset = WheelAppearance.FromPreset(id);
			Assert.Equal(id, preset.PresetId);
			Assert.Equal(id, preset.Clone().PresetId);
			Assert.Equal(preset.StartColor, preset.Clone().StartColor);
		}
		// An unknown id is not an error; it resolves to the default look.
		Assert.Equal(WheelAppearance.PresetDarkGlass, WheelAppearance.FromPreset("nonsense").PresetId);
	}

	[Fact]
	public void ResolvedFontFamilyPrefersTheSharedField()
	{
		MenuItemConfig item = new MenuItemConfig { FontName = "宋体", FontFamily = "Microsoft YaHei UI" };
		Assert.Equal("Microsoft YaHei UI", item.ResolvedFontFamily());
		item.FontFamily = "";
		Assert.Equal("宋体", item.ResolvedFontFamily());
	}

	[Fact]
	public void RecordingKeepsGenuineRepeatedCommands()
	{
		using CommandRecordingSession session = new CommandRecordingSession();
		session.Start();
		session.Append(Step("same"));
		session.Append(Step("same"));
		session.Append(Step("other"));

		Assert.Equal(3, session.Commands.Count);
		Assert.Equal(0, session.Merged);
	}

	[Fact]
	public void RecordingOnlyMergesShortDuplicateHostNotifications()
	{
		long now = 0;
		using var session = new CommandRecordingSession(() => now);
		session.Start();
		session.Append(Step("same"), true);
		now = 40;
		session.Append(Step("same"), true);
		now = 160;
		session.Append(Step("same"), true);
		CommandRecordingSession.ObserveClosed(1);
		now = 170;
		session.Append(Step("same"), true);
		Assert.Equal(3, session.Commands.Count);
		Assert.Equal(1, session.Merged);
	}

	[Fact]
	public void PausedTimeIsExcludedFromRecordedIntervals()
	{
		long now = 0;
		using var session = new CommandRecordingSession(() => now) { RecordDelays = true, DelayThresholdMs = 1500 };
		session.Start();
		session.Append(Step("a"));
		now = 500;
		session.Paused = true;
		now = 12500;
		session.Paused = false;
		now = 13000;
		session.Append(Step("b"));
		Assert.Equal(new[] { "a", "b" }, session.Commands.Select(c => c.Value));
		now = 15000;
		session.Append(Step("c"));
		Assert.Equal("2000", session.Commands[2].Value);
		Assert.Equal(ActionKinds.Delay, session.Commands[2].Kind);
	}

	[Fact]
	public void PureRecordedDelayIsNotWaitedTwiceByThePlanner()
	{
		var delay = new ComboCommandConfig { Kind = ActionKinds.Delay, Value = "2200", WaitMode = ComboWaitModes.Delay, DelayMs = 2200 };
		ComboExecutionStep step = ComboExecutionPlanner.Build(new[] { delay }).Single();
		Assert.Equal("2200", step.Command.Value);
		Assert.Equal(0, step.DelayMs);
	}

	[Fact]
	public void ReRecordingReplacesSynchronouslyAndPreservesStepOptions()
	{
		using var session = new CommandRecordingSession { RecordDelays = true };
		session.Start();
		ComboCommandConfig original = Step("a");
		original.AdvanceKey = "Shift";
		original.WaitMode = ComboWaitModes.ManualConfirm;
		original.OnError = ComboErrorPolicies.Retry;
		original.RetryCount = 2;
		session.Append(original);
		session.Append(Step("b"));
		Assert.True(session.BeginReplace(0));
		session.Append(Step("new"));
		session.Append(Step("unwanted"));
		Assert.True(session.Paused);
		Assert.False(session.IsReplacing);
		Assert.Equal(new[] { "new", "b" }, session.Commands.Select(c => c.Value));
		Assert.Equal("Shift", session.Commands[0].AdvanceKey);
		Assert.Equal(ComboWaitModes.ManualConfirm, session.Commands[0].WaitMode);
		Assert.Equal(2, session.Commands[0].RetryCount);
		Assert.Equal("a", original.Value);
	}

	[Fact]
	public void CancelReRecordingKeepsTheOriginalAndBlocksPendingEdits()
	{
		using var session = new CommandRecordingSession();
		session.Start();
		session.Append(Step("a"));
		session.BeginReplace(0);
		session.RemoveAt(0);
		session.Insert(0, Step("insert"));
		session.Replace(0, Step("replace"));
		session.CancelReplace();
		session.Append(Step("late"));
		Assert.Equal("a", Assert.Single(session.Commands).Value);
		Assert.True(session.Paused);
	}

	[Fact]
	public void InsertedStepsAreClonedAndEditingDoesNotRecordPhantomIntervals()
	{
		long now = 0;
		using var session = new CommandRecordingSession(() => now) { RecordDelays = true };
		session.Start();
		session.Append(Step("a"));
		var inserted = Step("inserted");
		session.Insert(0, inserted);
		inserted.Value = "changed";
		now = 40000;
		session.Append(Step("b"));
		Assert.Equal(new[] { "inserted", "a", "b" }, session.Commands.Select(c => c.Value));
	}

	[Fact]
	public void RecordingClonesStepsSoLaterEditsDoNotLeak()
	{
		using CommandRecordingSession session = new CommandRecordingSession();
		session.Start();
		ComboCommandConfig source = Step("cmd");
		session.Append(source);
		source.Label = "changed";

		Assert.Equal("cmd", session.Commands[0].Label);
	}

	[Fact]
	public void RecordingSupportsInsertMoveReplace()
	{
		using CommandRecordingSession session = new CommandRecordingSession();
		session.Start();
		session.Append(Step("a"));
		session.Append(Step("b"));

		session.Insert(1, Step("mid"));
		Assert.Equal(new[] { "a", "mid", "b" }, session.Commands.Select(c => c.Value).ToArray());

		session.Move(2, -1);
		Assert.Equal(new[] { "a", "b", "mid" }, session.Commands.Select(c => c.Value).ToArray());

		session.Replace(0, Step("z"));
		Assert.Equal("z", session.Commands[0].Value);
	}

	[Fact]
	public void PausedRecordingIgnoresCommands()
	{
		using CommandRecordingSession session = new CommandRecordingSession();
		session.Start();
		session.Paused = true;
		session.Append(Step());

		Assert.Empty(session.Commands);
	}
}
