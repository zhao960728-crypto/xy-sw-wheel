using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace XY.SolidWorksPlugin;

internal sealed class UserMacroCommandRegistry : IDisposable
{
    internal const int GroupId = 1637552380;
    private readonly ICommandManager _manager;
    private bool _created;
    private string _iconDirectory;
    internal int[] RuntimeIds { get; private set; } = Array.Empty<int>();
    internal UserMacroCommand[] RegisteredCommands { get; private set; } = Array.Empty<UserMacroCommand>();

    internal UserMacroCommandRegistry(ICommandManager manager) => _manager = manager ?? throw new ArgumentNullException(nameof(manager));

    internal void Rebuild(IEnumerable<UserMacroCommand> source)
    {
        var commands = (source ?? Enumerable.Empty<UserMacroCommand>()).ToArray();
        ValidateDefinitions(commands);
        commands = commands.Select(Clone).ToArray();
        DisposeGroup();
        if (commands.Length == 0) return;
        object savedIds;
        bool hadGroup = _manager.GetGroupDataFromRegistry(GroupId, out savedIds);
        bool changed = !hadGroup || !(savedIds as int[] ?? Array.Empty<int>()).OrderBy(id => id)
            .SequenceEqual(commands.Select(command => command.CommandUserId).OrderBy(id => id));
        int error = 0;
        var group = _manager.CreateCommandGroup2(GroupId, "XY轮盘 用户宏", "用户导入的宏", "用户导入的宏", -1, changed, ref error) as ICommandGroup;
        if (group == null || !SwAddin.IsCommandGroupCreateSuccess(error))
            throw new InvalidOperationException("用户宏命令组创建失败，宿主错误=" + error);
        _created = true;
        try
        {
            // HasToolbar controls whether toolbar commands exist. Visibility
            // is separate: retain their native identity for CommandTab buttons.
            group.HasMenu = false; group.HasToolbar = true;
            group.ShowInDocumentType = SwAddin.ToolsMenuDocumentTypes;
            _iconDirectory = WheelCommandTabs.CreateIcons(group, Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName));
            int[] indexes = AddItems(group, commands);
            if (indexes.Any(index => index < 0) || !group.Activate())
                throw new InvalidOperationException("用户宏按钮注册失败，未执行任何宏。");
            group.SetToolbarVisibility(false, SwAddin.ToolsMenuDocumentTypes);
            int[] runtime = indexes.Select(index => group.CommandID[index]).ToArray();
            if (runtime.Any(id => id <= 0) || runtime.Distinct().Count() != runtime.Length)
                throw new InvalidOperationException("用户宏按钮身份回读不一致，未执行任何宏。");
            RuntimeIds = runtime;
            RegisteredCommands = commands;
        }
        catch { DisposeGroup(); throw; }
    }

    internal static int[] AddItems(ICommandGroup group, IEnumerable<UserMacroCommand> source)
        => source.Select(command => group.AddCommandItem2(command.Name, -1, "运行用户宏：" + command.Name, command.Name, 6,
            Callback(command.CommandUserId, false), Callback(command.CommandUserId, true), command.CommandUserId,
            (int)swCommandItemType_e.swToolbarItem)).ToArray();

    internal static string Callback(int id, bool enable)
    {
        if (id < UserMacroStore.FirstCommandUserId || id == int.MaxValue) throw new InvalidOperationException("宏按钮 ID 无效。");
        // SOLIDWORKS single-argument callbacks require a string parameter even
        // when the expression contains digits. User labels never enter it.
        return (enable ? "RunUserMacroCommandEnable(" : "RunUserMacroCommand(") + id.ToString(CultureInfo.InvariantCulture) + ")";
    }

    internal static void ValidateDefinitions(IReadOnlyCollection<UserMacroCommand> commands)
    {
        if (commands.Any(command => command == null)) throw new InvalidOperationException("宏配置含空项。");
        if (commands.GroupBy(command => command.CommandUserId).Any(group => group.Count() != 1)
            || commands.GroupBy(command => command.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1))
            throw new InvalidOperationException("宏按钮 ID 或 GUID 重复，未注册。");
        foreach (var command in commands)
        {
            if (!Guid.TryParseExact(command.Id, "N", out _)) throw new InvalidOperationException("宏 GUID 无效，请重新导入。");
            Callback(command.CommandUserId, false);
            UserMacroStore.ValidateName(command.Name, commands, command.Id);
        }
    }

    internal static string Fingerprint(IEnumerable<UserMacroCommand> commands)
        => string.Join("\u001e", (commands ?? Enumerable.Empty<UserMacroCommand>()).Select(command => command == null ? "<null>"
            : string.Join("\u001f", command.Id, command.CommandUserId.ToString(CultureInfo.InvariantCulture), command.Name,
                command.FilePath, command.ModuleName, command.ProcedureName)));

    internal static UserMacroCommand Resolve(IEnumerable<UserMacroCommand> registered, IEnumerable<UserMacroCommand> configured, string data)
    {
        if (!int.TryParse(data, NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id < UserMacroStore.FirstCommandUserId)
            throw new InvalidOperationException("宏按钮身份无效，未执行。");
        var known = (registered ?? Enumerable.Empty<UserMacroCommand>()).Where(command => command?.CommandUserId == id).ToArray();
        var current = (configured ?? Enumerable.Empty<UserMacroCommand>()).Where(command => command?.CommandUserId == id).ToArray();
        if (known.Length != 1 || current.Length != 1 || Fingerprint(known) != Fingerprint(current))
            throw new InvalidOperationException("宏按钮已移除、存在重复或其配置已变化，请保存设置后重试；未改投其他宏。");
        return Clone(known[0]);
    }

    private static UserMacroCommand Clone(UserMacroCommand command) => new UserMacroCommand
    {
        Id = command.Id, CommandUserId = command.CommandUserId, Name = command.Name, FilePath = command.FilePath,
        ModuleName = command.ModuleName, ProcedureName = command.ProcedureName
    };

    private void DisposeGroup()
    {
        if (_created)
        {
            int result = _manager.RemoveCommandGroup2(GroupId, true);
            if (result != (int)swRemoveCommandGroupErrors.swRemoveCommandGroup_Success)
                throw new InvalidOperationException("用户宏命令组卸载失败，未重复创建。");
            _created = false;
        }
        RuntimeIds = Array.Empty<int>();
        RegisteredCommands = Array.Empty<UserMacroCommand>();
        WheelCommandTabs.DeleteIcons(_iconDirectory); _iconDirectory = null;
    }

    public void Dispose() => DisposeGroup();
}
