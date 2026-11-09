using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

[Collection("Feature cleanup UI")]
public sealed class UserMacroSettingsUiTests
{
    [Fact]
    public void CommandTabDefaultsAndExplicitHiddenStateRoundTripIndependently() => Sta(() =>
    {
        PluginConfig current = NewConfig();
        using var settings = Window(() => current, value => current = value);
        Assert.True(Field<CheckBox>(settings, "_commandTabEnabled").Checked);
        Assert.Equal("XY轮盘", Field<TextBox>(settings, "_commandTabName").Text);
        Field<TextBox>(settings, "_commandTabName").Text = "我的制造工具";
        Field<CheckBox>(settings, "_commandTabEnabled").Checked = false;
        Assert.False(Field<TextBox>(settings, "_commandTabName").Enabled);
        Save(settings);
        Assert.False(current.WheelCommandTabEnabled);
        Assert.Equal("我的制造工具", current.WheelCommandTabName);
        Assert.True(current.RadialMenuEnabled);
        Assert.True(current.ExplodeEnabled);
        using var reopened = Window(() => current, value => current = value);
        Assert.False(Field<CheckBox>(reopened, "_commandTabEnabled").Checked);
        Assert.Equal("我的制造工具", Field<TextBox>(reopened, "_commandTabName").Text);
    });

    [Fact]
    public void UneditedTabSettingsKeepLatestValuesFromAnotherSettingsWindow() => Sta(() =>
    {
        PluginConfig current = NewConfig();
        using var settings = Window(() => current, value => current = value);
        current.WheelCommandTabEnabled = false; current.WheelCommandTabName = "另一窗口保存的名称";
        Save(settings);
        Assert.False(current.WheelCommandTabEnabled);
        Assert.Equal("另一窗口保存的名称", current.WheelCommandTabName);
        Assert.False(Field<CheckBox>(settings, "_commandTabEnabled").Checked);
    });

    [Fact]
    public void ConsecutiveSavesRebaseTabChoicesWithoutMutatingTheCommittedRuntimeInstance() => Sta(() =>
    {
        PluginConfig runtime = NewConfig();
        var visibilityTransitions = new List<string>(); var nameTransitions = new List<string>();
        // This deliberately retains the exact object received, as SwAddin's
        // successful save does. A clone in this test would hide the regression.
        using var settings = new SettingsWindow(() => ConfigStore.Clone(runtime), value =>
        {
            if (runtime.WheelCommandTabEnabled != value.WheelCommandTabEnabled)
                visibilityTransitions.Add(runtime.WheelCommandTabEnabled + "->" + value.WheelCommandTabEnabled);
            if (runtime.WheelCommandTabName != value.WheelCommandTabName)
                nameTransitions.Add(runtime.WheelCommandTabName + "->" + value.WheelCommandTabName);
            runtime = value;
        }, mode: SettingsWindowMode.FeatureTools);
        var visible = Field<CheckBox>(settings, "_commandTabEnabled"); var name = Field<TextBox>(settings, "_commandTabName");
        visible.Checked = false; Save(settings);
        Assert.False(Field<bool>(settings, "_tabBaselineEnabled"));
        visible.Checked = true; Call(settings, "ApplyEditorToConfig");
        Assert.False(runtime.WheelCommandTabEnabled);
        Save(settings);
        Assert.True(runtime.WheelCommandTabEnabled); Assert.True(Field<bool>(settings, "_tabBaselineEnabled"));
        Assert.Equal(new[] { "True->False", "False->True" }, visibilityTransitions);
        name.Text = "第一名称"; Save(settings);
        Assert.Equal("第一名称", Field<string>(settings, "_tabBaselineName"));
        name.Text = "XY轮盘"; Call(settings, "ApplyEditorToConfig");
        Assert.Equal("第一名称", runtime.WheelCommandTabName);
        Save(settings);
        Assert.Equal(new[] { "XY轮盘->第一名称", "第一名称->XY轮盘" }, nameTransitions);
        Assert.NotSame(runtime, Field<PluginConfig>(settings, "_config"));
    });

    [Fact]
    public void MacroNameIsRequiredAndReadingEntryPointsDoesNotImportOrRunAnything() => Sta(() =>
    {
        int reads = 0;
        using var dialog = new UserMacroImportDialog(_ => { reads++; return Entries(("Module1", "main")); },
            name => UserMacroStore.ValidateName(name, null));
        dialog.LoadSource(Path.Combine(Path.GetTempPath(), "read-only.swp"));
        Assert.Equal(1, reads);
        Assert.Equal("", dialog.FunctionName);
        Assert.Equal("Module1", dialog.ModuleName);
        Assert.Equal("main", dialog.ProcedureName);
        Assert.False(Field<Button>(dialog, "_confirm").Enabled);
        Field<TextBox>(dialog, "_name").Text = "我的自定功能";
        Assert.True(Field<Button>(dialog, "_confirm").Enabled);
        Assert.Equal(DialogResult.None, dialog.DialogResult);
        Assert.Contains("*.swp;*.swb", UserMacroImportDialog.MacroFileFilter);
    });

    [Fact]
    public void MultipleModulesAndProceduresRequireAnExplicitSelection() => Sta(() =>
    {
        using var dialog = new UserMacroImportDialog(_ => Entries(("First", "main"), ("First", "other"), ("Second", "main")),
            name => UserMacroStore.ValidateName(name, null));
        dialog.LoadSource(Path.Combine(Path.GetTempPath(), "multiple.swp"));
        Field<TextBox>(dialog, "_name").Text = "多入口";
        var modules = Field<ComboBox>(dialog, "_module");
        var procedures = Field<ComboBox>(dialog, "_procedure");
        Assert.Equal(-1, modules.SelectedIndex); Assert.Equal(-1, procedures.SelectedIndex);
        Assert.False(Field<Button>(dialog, "_confirm").Enabled);
        modules.SelectedIndex = 0;
        Assert.Equal(-1, procedures.SelectedIndex);
        Assert.False(Field<Button>(dialog, "_confirm").Enabled);
        procedures.SelectedIndex = 1;
        Assert.Equal("First", dialog.ModuleName); Assert.Equal("other", dialog.ProcedureName);
        Assert.True(Field<Button>(dialog, "_confirm").Enabled);
    });

    [Fact]
    public void ActualSwbEntryWithoutModuleRemainsSelectable() => Sta(() =>
    {
        using var dialog = new UserMacroImportDialog(_ => Entries(("", "main")), name => UserMacroStore.ValidateName(name, null));
        dialog.LoadSource(Path.Combine(Path.GetTempPath(), "legacy.swb"));
        Field<TextBox>(dialog, "_name").Text = "文本宏";
        Assert.Equal("", dialog.ModuleName); Assert.Equal("main", dialog.ProcedureName);
        Assert.Contains("SWB", Field<ComboBox>(dialog, "_module").GetItemText(Field<ComboBox>(dialog, "_module").SelectedItem));
        Assert.True(Field<Button>(dialog, "_confirm").Enabled);
    });

    [Theory]
    [InlineData("Module1", ".swp")]
    [InlineData("", ".swb")]
    public void AcceptedModelessImportRetainsAllFieldsAfterCloseDisposeAndAwait(string module, string extension) => Sta(() =>
    {
        int reads = 0;
        using var owner = new Form(); owner.Show();
        using var dialog = new UserMacroImportDialog(_ => { reads++; return Entries((module, "main")); }, name => UserMacroStore.ValidateName(name, null));
        async Task<(DialogResult result, string path, string name, string module, string procedure)> ReadAcceptedAfterClose()
        {
            DialogResult result = await dialog.ShowModelessAsync(owner);
            // Reproduce an owner continuation queued after Close disposes the
            // real controls, instead of reading while FormClosed is on stack.
            await Task.Yield();
            Assert.True(dialog.IsDisposed);
            Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
            return (result, dialog.SourcePath, dialog.FunctionName, dialog.ModuleName, dialog.ProcedureName);
        }
        var accepted = ReadAcceptedAfterClose();
        string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "accepted-macro" + extension));
        dialog.LoadSource(path);
        var confirm = Field<Button>(dialog, "_confirm");
        Assert.False(confirm.Enabled);
        Field<TextBox>(dialog, "_name").Text = "  保留自定义功能名  ";
        Assert.True(confirm.Enabled); confirm.PerformClick(); dialog.Dispose();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!accepted.IsCompleted && timer.ElapsedMilliseconds < 3000) { Application.DoEvents(); Thread.Sleep(1); }
        Assert.True(accepted.IsCompleted);
        var snapshot = accepted.GetAwaiter().GetResult();
        Assert.Equal(DialogResult.OK, snapshot.result); Assert.Equal(path, snapshot.path);
        Assert.Equal("保留自定义功能名", snapshot.name); Assert.Equal(module, snapshot.module); Assert.Equal("main", snapshot.procedure);
        Assert.Equal(1, reads);
    });

    [Fact]
    public void AutoSaveDoesNotCommitMacroOrTabDraftAndCloseDeletesOnlyImportedCopy() => Sta(() =>
    {
        using var files = new TempMacro(); PluginConfig current = NewConfig();
        string copied;
        using (var settings = Window(() => current, value => current = value))
        {
            var imported = settings.ImportUserMacroDraft(files.Source, "待保存宏", "", "main", files.ManagedRoot);
            copied = imported.FilePath;
            Assert.True(File.Exists(copied)); Assert.Empty(current.UserMacros);
            Field<CheckBox>(settings, "_commandTabEnabled").Checked = false;
            Field<TextBox>(settings, "_commandTabName").Text = "尚未保存";
            Call(settings, "PersistEditorConfig");
            Assert.Empty(current.UserMacros); Assert.True(current.WheelCommandTabEnabled); Assert.Equal("XY轮盘", current.WheelCommandTabName);
            Assert.Single(Field<PluginConfig>(settings, "_config").UserMacros);
        }
        Assert.False(File.Exists(copied)); Assert.Equal(files.Contents, File.ReadAllText(files.Source));
    });

    [Fact]
    public void ApplyingMacroKeepsManagedCopyAndStableMetadataAfterClosing() => Sta(() =>
    {
        using var files = new TempMacro(); PluginConfig current = NewConfig(); UserMacroCommand imported;
        using (var settings = Window(() => current, value => current = value))
        {
            imported = settings.ImportUserMacroDraft(files.Source, "已保存宏", "", "main", files.ManagedRoot);
            Save(settings);
            UserMacroCommand saved = Assert.Single(current.UserMacros);
            Assert.Equal(imported.Id, saved.Id); Assert.Equal(imported.CommandUserId, saved.CommandUserId);
            Assert.Equal(imported.FilePath, saved.FilePath); Assert.Equal("", saved.ModuleName); Assert.Equal("main", saved.ProcedureName);
        }
        Assert.True(File.Exists(imported.FilePath)); Assert.Equal(files.Contents, File.ReadAllText(files.Source));
        using var reopened = Window(() => current, value => current = value);
        Assert.Equal("已保存宏", Assert.Single(Field<PluginConfig>(reopened, "_config").UserMacros).Name);
    });

    [Fact]
    public void FailedSaveKeepsDraftReviewableAndClosingCleansItsCopy() => Sta(() =>
    {
        using var files = new TempMacro(); PluginConfig current = NewConfig(); string copied;
        using (var settings = Window(() => current, _ => throw new InvalidOperationException("native tab conflict")))
        {
            copied = settings.ImportUserMacroDraft(files.Source, "冲突测试", "", "main", files.ManagedRoot).FilePath;
            var error = Assert.Throws<TargetInvocationException>(() => Call(settings, "SaveConfig"));
            Assert.IsType<InvalidOperationException>(error.InnerException);
            Assert.Single(Field<PluginConfig>(settings, "_config").UserMacros);
            Assert.Empty(current.UserMacros); Assert.True(File.Exists(copied));
        }
        Assert.False(File.Exists(copied)); Assert.Equal(files.Contents, File.ReadAllText(files.Source));
    });

    [Fact]
    public void ExpectedUiSaveConflictIsShownWithoutEscapingTheClickHandler() => Sta(() =>
    {
        PluginConfig current = NewConfig();
        using var settings = Window(() => current, _ => throw new InvalidOperationException("选项卡名称已使用"));
        Field<TextBox>(settings, "_commandTabName").Text = "有冲突的名称";
        try
        {
            Call(settings, "SaveConfigFromUi");
            Assert.Equal("有冲突的名称", Field<TextBox>(settings, "_commandTabName").Text);
            Assert.Equal("XY轮盘", current.WheelCommandTabName);
            Assert.NotEmpty(settings.OwnedForms);
        }
        finally { foreach (Form notice in settings.OwnedForms) notice.Close(); }
    });

    [Fact]
    public void DuplicateNameFailsBeforeAddingASecondCopyAndDraftRemovalDeletesOnlyItsCopy() => Sta(() =>
    {
        using var files = new TempMacro(); PluginConfig current = NewConfig();
        using var settings = Window(() => current, value => current = value);
        var imported = settings.ImportUserMacroDraft(files.Source, "唯一名称", "", "main", files.ManagedRoot);
        Assert.Throws<InvalidOperationException>(() => settings.ImportUserMacroDraft(files.Source, "唯一名称", "", "main", files.ManagedRoot));
        Assert.Single(Directory.GetDirectories(files.ManagedRoot));
        var grid = Field<DataGridView>(settings, "_userMacroGrid"); grid.CurrentCell = grid.Rows[0].Cells[0];
        Call(settings, "RemoveSelectedUserMacro");
        Assert.Empty(Field<PluginConfig>(settings, "_config").UserMacros); Assert.Empty(current.UserMacros);
        Assert.False(File.Exists(imported.FilePath)); Assert.Equal(files.Contents, File.ReadAllText(files.Source));
    });

    [Fact]
    public void DeletedSavedMacroIsOnlyRemovedOnExplicitApplyAndLatestOtherMacrosSurvive() => Sta(() =>
    {
        PluginConfig current = NewConfig();
        var existing = new UserMacroCommand { Id = Guid.NewGuid().ToString("N"), CommandUserId = 10000, Name = "已有宏", FilePath = "missing.swp", ModuleName = "Module1", ProcedureName = "main" };
        current.UserMacros.Add(existing);
        using var settings = Window(() => current, value => current = value);
        var grid = Field<DataGridView>(settings, "_userMacroGrid"); grid.CurrentCell = grid.Rows[0].Cells[0];
        Call(settings, "RemoveSelectedUserMacro");
        Call(settings, "PersistEditorConfig"); Assert.Single(current.UserMacros);
        current.UserMacros.Add(new UserMacroCommand { Id = Guid.NewGuid().ToString("N"), CommandUserId = 10001, Name = "另一窗口的新宏", FilePath = "other.swp", ModuleName = "Module1", ProcedureName = "main" });
        Save(settings);
        Assert.Equal("另一窗口的新宏", Assert.Single(current.UserMacros).Name);
    });

    private static List<UserMacroEntryPoint> Entries(params (string module, string procedure)[] entries) => entries.Select(entry => new UserMacroEntryPoint { ModuleName = entry.module, ProcedureName = entry.procedure }).ToList();
    private static PluginConfig NewConfig() { var config = ConfigStore.Deserialize("{}"); ConfigStore.Normalize(config); return config; }
    private static SettingsWindow Window(Func<PluginConfig> load, Action<PluginConfig> save) => new SettingsWindow(() => ConfigStore.Clone(load()), value => save(ConfigStore.Clone(value)), mode: SettingsWindowMode.FeatureTools);
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static object? Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(value, args);
    private static void Save(SettingsWindow window) { try { Call(window, "SaveConfig"); } finally { foreach (Form notice in window.OwnedForms) notice.Close(); } }
    private static void Sta(Action action) { Exception? error = null; var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } }); thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (error != null) throw error; }
    private sealed class TempMacro : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "xy-macro-ui-" + Guid.NewGuid().ToString("N"));
        internal string Source => Path.Combine(_directory, "sample.swb");
        internal string ManagedRoot => Path.Combine(_directory, "managed");
        internal string Contents => "Sub main()\r\n' UI tests only: never executed\r\nEnd Sub\r\n";
        internal TempMacro() { Directory.CreateDirectory(_directory); File.WriteAllText(Source, Contents); }
        public void Dispose() { if (Directory.Exists(_directory) && Path.GetFullPath(_directory).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) Directory.Delete(_directory, recursive: true); }
    }
}
