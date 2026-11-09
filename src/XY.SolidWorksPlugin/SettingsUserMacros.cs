using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

internal sealed partial class SettingsWindow
{
    private readonly CheckBox _commandTabEnabled = new CheckBox { Text = "显示选项卡", AutoSize = true, Checked = true };
    private readonly TextBox _commandTabName = new TextBox { Text = "XY轮盘", Dock = DockStyle.Fill, MaxLength = 64 };
    private readonly DataGridView _userMacroGrid = new DataGridView
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle
    };
    private readonly Label _userMacroStatus = new Label { Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.FromArgb(78, 94, 108) };
    private bool _macroControlsReady, _userMacroImportOpen, _tabBaselineEnabled, _commitUserMacroDrafts;
    private string _tabBaselineName = "XY轮盘";
    private readonly Dictionary<string, UserMacroCommand> _removedUserMacros = new Dictionary<string, UserMacroCommand>(StringComparer.OrdinalIgnoreCase);
    private sealed class PendingMacroImport
    {
        internal UserMacroCommand Command;
        internal string StorageRoot;
    }
    private readonly List<PendingMacroImport> _pendingUserMacroImports = new List<PendingMacroImport>();

    private void LoadUserMacroSettings()
    {
        if (!_macroControlsReady)
        {
            _macroControlsReady = true;
            _commandTabEnabled.CheckedChanged += (_, __) => _commandTabName.Enabled = _commandTabEnabled.Checked;
            _userMacroGrid.Columns.Add("FunctionName", "功能名");
            _userMacroGrid.Columns.Add("EntryPoint", "模块 / 过程");
            _userMacroGrid.Columns.Add("MacroFile", "宏文件");
        }
        _tabBaselineEnabled = _config.WheelCommandTabEnabled;
        _tabBaselineName = _config.WheelCommandTabName ?? "XY轮盘";
        _commandTabEnabled.Checked = _tabBaselineEnabled; _commandTabName.Text = _tabBaselineName;
        _commandTabName.Enabled = _commandTabEnabled.Checked;
        RefreshUserMacroRows();
    }

    private Control BuildUserMacroSection()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Top, Height = 260, ColumnCount = 1, RowCount = 4, Padding = new Padding(0, 6, 12, 8) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = "选项卡中的宏", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Anchor = AnchorStyles.Left }, 0, 0);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        var add = new Button { Text = "加入宏…", AutoSize = true }; add.Click += (_, __) => BeginImportUserMacro();
        var remove = new Button { Text = "移除所选", AutoSize = true }; remove.Click += (_, __) => RemoveSelectedUserMacro();
        buttons.Controls.Add(add); buttons.Controls.Add(remove);
        buttons.Controls.Add(new Label { Text = "只接收 .swp / .swb，功能名由你命名。", AutoSize = true, Padding = new Padding(8, 8, 0, 0) });
        panel.Controls.Add(buttons, 0, 1); panel.Controls.Add(_userMacroGrid, 0, 2); panel.Controls.Add(_userMacroStatus, 0, 3);
        RefreshUserMacroRows(); return panel;
    }

    internal async void BeginImportUserMacro()
    {
        if (_userMacroImportOpen || IsDisposed) return;
        RenderSection("features");
        _userMacroGrid.Focus(); _userMacroImportOpen = true;
        try
        {
            using var dialog = new UserMacroImportDialog(path => UserMacroStore.ReadEntryPoints(_getSolidWorks(), path),
                name => UserMacroStore.ValidateName(name, _config.UserMacros));
            if (await dialog.ShowModelessAsync(this) != DialogResult.OK || IsDisposed) return;
            ImportUserMacroDraft(dialog.SourcePath, dialog.FunctionName, dialog.ModuleName, dialog.ProcedureName);
        }
        catch (Exception error)
        {
            Logger.Error("Import user macro failed", error);
            if (!IsDisposed) ToolNotice.Show(this, "加入宏失败：" + error.Message, "加入宏", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally { _userMacroImportOpen = false; }
    }

    internal UserMacroCommand ImportUserMacroDraft(string path, string name, string module, string procedure, string storageRoot = null)
    {
        _config.UserMacros ??= new List<UserMacroCommand>();
        UserMacroCommand imported = UserMacroStore.Import(path, name, module, procedure, _config.UserMacros, storageRoot);
        try
        {
            _pendingUserMacroImports.Add(new PendingMacroImport { Command = imported, StorageRoot = storageRoot });
            _config.UserMacros.Add(imported); RefreshUserMacroRows(imported.Id);
            _userMacroStatus.Text = "已加入待保存列表；点击“应用并保存”后显示在选项卡，尚未运行宏。";
            return imported;
        }
        catch
        {
            _config.UserMacros.Remove(imported);
            UserMacroStore.DeleteOwnedImportedFile(imported, (_load().UserMacros ?? new List<UserMacroCommand>()), storageRoot);
            _pendingUserMacroImports.RemoveAll(item => item.Command.Id == imported.Id); throw;
        }
    }

    private void RefreshUserMacroRows(string selectId = null)
    {
        if (_userMacroGrid.Columns.Count == 0) return;
        string selectedId = selectId ?? (_userMacroGrid.CurrentRow?.Tag as UserMacroCommand)?.Id;
        _userMacroGrid.Rows.Clear();
        foreach (UserMacroCommand command in (_config.UserMacros ?? new List<UserMacroCommand>()).Where(command => command != null).OrderBy(command => command.CommandUserId))
        {
            string entryPoint = string.IsNullOrEmpty(command.ModuleName) ? command.ProcedureName : command.ModuleName + "." + command.ProcedureName;
            int index = _userMacroGrid.Rows.Add(command.Name, entryPoint, Path.GetFileName(command.FilePath));
            DataGridViewRow row = _userMacroGrid.Rows[index]; row.Tag = command;
            row.Cells["MacroFile"].ToolTipText = command.FilePath;
            if (command.Id == selectedId) _userMacroGrid.CurrentCell = row.Cells[0];
        }
    }

    private void RemoveSelectedUserMacro()
    {
        UserMacroCommand command = _userMacroGrid.CurrentRow?.Tag as UserMacroCommand;
        if (command == null) { _userMacroStatus.Text = "请先选择要移除的宏。"; return; }
        try
        {
            PendingMacroImport pending = _pendingUserMacroImports.FirstOrDefault(item => item.Command.Id == command.Id);
            if (pending != null)
            {
                UserMacroStore.DeleteOwnedImportedFile(command, _load().UserMacros, pending.StorageRoot);
                _pendingUserMacroImports.Remove(pending);
            }
            if (pending == null) _removedUserMacros[command.Id] = command;
            _config.UserMacros.Remove(command); RefreshUserMacroRows();
            _userMacroStatus.Text = "已从待保存列表移除；保存后更新选项卡。源宏文件不受影响。";
        }
        catch (Exception error) { Logger.Error("Remove user macro draft failed", error); ToolNotice.Show(this, "移除失败：" + error.Message, "加入宏"); }
    }

    private void ApplyCommandTabSettings()
    {
        _config.WheelCommandTabEnabled = _commandTabEnabled.Checked;
        _config.WheelCommandTabName = UserMacroStore.ValidateTabName(_commandTabName.Text);
    }

    private void MergeUserMacroSettings(PluginConfig latest)
    {
        bool visible = _commandTabEnabled.Checked;
        string title = UserMacroStore.ValidateTabName(_commandTabName.Text);
        _config.WheelCommandTabEnabled = visible == _tabBaselineEnabled ? latest.WheelCommandTabEnabled : visible;
        _config.WheelCommandTabName = string.Equals(title, _tabBaselineName, StringComparison.Ordinal) ? latest.WheelCommandTabName : title;
        var merged = (latest.UserMacros ?? new List<UserMacroCommand>()).Where(command => command != null && !_removedUserMacros.ContainsKey(command.Id)).ToList();
        foreach (PendingMacroImport pending in _pendingUserMacroImports.Where(item => (_config.UserMacros ?? new List<UserMacroCommand>()).Any(command => command.Id == item.Command.Id)))
        {
            UserMacroCommand command = pending.Command;
            if (merged.Any(saved => saved.Id == command.Id)) continue;
            UserMacroStore.ValidateName(command.Name, merged);
            UserMacroStore.Validate(command, pending.StorageRoot);
            if (merged.Any(saved => saved.CommandUserId == command.CommandUserId)) throw new InvalidOperationException("宏按钮 ID 已被其他窗口使用，请重新打开设置后加入；没有覆盖已有宏。");
            merged.Add(command);
        }
        _config.UserMacros = merged;
    }

    private void CompleteUserMacroSave()
    {
        // Load committed state before deleting any managed copy. A save callback
        // may update runtime UI after writing the file; never delete its input.
        PluginConfig committed = _load();
        foreach (UserMacroCommand removed in _removedUserMacros.Values)
        {
            try { UserMacroStore.DeleteOwnedImportedFile(removed, committed.UserMacros); }
            catch (Exception error) { Logger.Error("Remove saved macro copy deferred", error); _userMacroStatus.Text = "设置已保存，旧宏副本清理未完成；源宏文件不受影响。"; }
        }
        _removedUserMacros.Clear();
        _pendingUserMacroImports.RemoveAll(item => (committed.UserMacros ?? new List<UserMacroCommand>()).Any(command => command.Id == item.Command.Id));
        _config.UserMacros = committed.UserMacros ?? new List<UserMacroCommand>();
        _config.WheelCommandTabEnabled = committed.WheelCommandTabEnabled; _config.WheelCommandTabName = committed.WheelCommandTabName;
        LoadUserMacroSettings();
    }

    private void CleanupPendingUserMacroImports()
    {
        if (_pendingUserMacroImports.Count == 0) return;
        try
        {
            PluginConfig committed = _load();
            foreach (PendingMacroImport pending in _pendingUserMacroImports)
            {
                try { UserMacroStore.DeleteOwnedImportedFile(pending.Command, committed.UserMacros, pending.StorageRoot); }
                catch (Exception error) { Logger.Error("Cancel one user macro draft cleanup deferred", error); }
            }
            _pendingUserMacroImports.Clear();
        }
        catch (Exception error) { Logger.Error("Cancel user macro draft cleanup deferred", error); }
    }
}
