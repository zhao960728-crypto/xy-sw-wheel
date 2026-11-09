using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace XY.SolidWorksPlugin;

internal static class UserMacroStore
{
    internal const int FirstCommandUserId = 10000;
    internal static string StorageRoot => Path.Combine(ConfigStore.BaseDirectory, "macros");
    private static readonly string[] ReservedNames = { "轮盘设置", "属性水印设置", "工序识别", "快捷键候选", "功能设置", "一键爆炸", "加入宏" };

    internal static void NormalizeConfig(PluginConfig config)
    {
        if (config == null) return;
        string title = (config.WheelCommandTabName ?? "").Trim();
        config.WheelCommandTabName = title.Length == 0 || title.Length > 64 || title.Any(char.IsControl) ? "XY轮盘" : title;
        config.UserMacros ??= new List<UserMacroCommand>();
        foreach (var command in config.UserMacros.Where(command => command != null))
        {
            command.Name = (command.Name ?? "").Trim();
            command.FilePath = (command.FilePath ?? "").Trim();
            command.ModuleName = (command.ModuleName ?? "").Trim();
            command.ProcedureName = (command.ProcedureName ?? "").Trim();
            if (Guid.TryParse(command.Id, out Guid id)) command.Id = id.ToString("N");
        }
    }

    internal static string ValidateTabName(string name)
    {
        string title = (name ?? "").Trim();
        if (title.Length == 0 || title.Length > 64 || title.Any(char.IsControl))
            throw new InvalidOperationException("选项卡名称须为 1–64 个可见字符。");
        return title;
    }

    internal static string ValidateName(string name, IEnumerable<UserMacroCommand> existing, string exceptId = null)
    {
        string value = (name ?? "").Trim();
        if (value.Length == 0 || value.Length > 64 || value.Any(char.IsControl))
            throw new InvalidOperationException("宏功能名须为 1–64 个可见字符。");
        if (ReservedNames.Contains(value, StringComparer.OrdinalIgnoreCase)
            || (existing ?? Enumerable.Empty<UserMacroCommand>()).Any(command => command != null
                && (exceptId == null || !string.Equals(command.Id, exceptId, StringComparison.OrdinalIgnoreCase))
                && string.Equals((command.Name ?? "").Trim(), value, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("功能名已存在，请为宏输入不同的名称。");
        return value;
    }

    internal static UserMacroCommand Import(string sourcePath, string name, string moduleName, string procedureName,
        IEnumerable<UserMacroCommand> existing, string storageRoot = null)
    {
        var current = (existing ?? Enumerable.Empty<UserMacroCommand>()).Where(command => command != null).ToArray();
        string functionName = ValidateName(name, current);
        string source = ValidateSource(sourcePath);
        ValidateEntryPoint(moduleName, procedureName, IsTextMacro(source));
        string root = Path.GetFullPath(storageRoot ?? StorageRoot);
        Directory.CreateDirectory(root);
        EnsureOrdinaryDirectory(root);
        string id = Guid.NewGuid().ToString("N");
        string directory = Path.Combine(root, id);
        string destination = Path.Combine(directory, Path.GetFileName(source));
        var command = new UserMacroCommand
        {
            Id = id, CommandUserId = AllocateUserId(root, current), Name = functionName, FilePath = destination,
            ModuleName = (moduleName ?? "").Trim(), ProcedureName = procedureName.Trim()
        };
        try
        {
            Directory.CreateDirectory(directory);
            File.Copy(source, destination, overwrite: false);
            Validate(command, root);
            return command;
        }
        catch
        {
            try { DeleteOwnedImportedFile(command, current, root); }
            catch (Exception error) { Logger.Error("Clean failed macro import copy deferred", error); }
            throw;
        }
    }

    // Only this imported GUID/file pair may be removed. No recursive directory
    // deletion, no source-file deletion, and never a file still in saved config.
    internal static void DeleteOwnedImportedFile(UserMacroCommand command, IEnumerable<UserMacroCommand> committed, string storageRoot = null)
    {
        if (command == null || (committed ?? Enumerable.Empty<UserMacroCommand>()).Any(saved => saved != null
            && (string.Equals(saved.Id, command.Id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(saved.FilePath, command.FilePath, StringComparison.OrdinalIgnoreCase)))) return;
        string path;
        try { path = OwnedPath(command, storageRoot ?? StorageRoot); }
        catch (Exception error) when (error is ArgumentException || error is InvalidOperationException || error is IOException) { return; }
        if (File.Exists(path)) File.Delete(path);
        string directory = Path.GetDirectoryName(path);
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, recursive: false);
    }

    internal static void Validate(UserMacroCommand command, string storageRoot = null)
    {
        if (command == null) throw new InvalidOperationException("宏配置为空。");
        if (command.CommandUserId < FirstCommandUserId || command.CommandUserId == int.MaxValue)
            throw new InvalidOperationException("宏按钮身份无效，请重新导入。");
        ValidateName(command.Name, null);
        string path = OwnedPath(command, storageRoot ?? StorageRoot);
        ValidateSource(path);
        ValidateEntryPoint(command.ModuleName, command.ProcedureName, IsTextMacro(path));
    }

    internal static void ValidateConfiguration(PluginConfig config, string storageRoot = null)
    {
        ValidateTabName(config?.WheelCommandTabName);
        var commands = config?.UserMacros ?? new List<UserMacroCommand>();
        if (commands.Any(command => command == null)
            || commands.GroupBy(command => command.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1)
            || commands.GroupBy(command => command.CommandUserId).Any(group => group.Count() != 1))
            throw new InvalidOperationException("宏 GUID 或按钮 ID 重复，未注册或执行；请移除重复项后重新导入。");
        foreach (var command in commands)
        {
            ValidateName(command.Name, commands, command.Id);
            Validate(command, storageRoot);
        }
    }

    internal static List<UserMacroEntryPoint> ReadEntryPoints(ISldWorks host, string sourcePath)
    {
        if (host == null) throw new InvalidOperationException("SOLIDWORKS 尚未连接，不能读取宏入口。");
        string source = ValidateSource(sourcePath);
        bool textMacro = IsTextMacro(source);
        object methods = null;
        try { methods = host.GetMacroMethods(source, (int)swMacroMethods_e.swMethodsWithoutArguments); }
        catch when (textMacro) { /* Legacy SWB can run without VBA module metadata. */ }
        var entries = ParseEntryPoints(methods, textMacro);
        if (entries.Count > 0 || !textMacro) return entries;
        return ReadTextEntryPoints(File.ReadAllText(source));
    }

    internal static List<UserMacroEntryPoint> ParseEntryPoints(object methods, bool allowEmptyModule = false)
    {
        var result = new List<UserMacroEntryPoint>();
        foreach (object value in methods is string one ? new object[] { one } : methods as Array ?? Array.Empty<object>())
        {
            string method = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            int separator = method.LastIndexOf('.');
            if (separator >= method.Length - 1 || (separator <= 0 && !allowEmptyModule)) continue;
            string module = separator <= 0 ? "" : method.Substring(0, separator).Trim();
            string procedure = separator <= 0 ? method.Trim() : method.Substring(separator + 1).Trim();
            try { ValidateEntryPoint(module, procedure, allowEmptyModule); } catch (InvalidOperationException) { continue; }
            if (result.Any(entry => string.Equals(entry.ModuleName, module, StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.ProcedureName, procedure, StringComparison.OrdinalIgnoreCase))) continue;
            result.Add(new UserMacroEntryPoint { ModuleName = module, ProcedureName = procedure });
        }
        return result.OrderByDescending(entry => string.Equals(entry.ProcedureName, "main", StringComparison.OrdinalIgnoreCase))
            .ThenBy(entry => entry.ToString(), StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static List<UserMacroEntryPoint> ReadTextEntryPoints(string source)
    {
        var names = Regex.Matches(source ?? "", @"^\s*(?:(?:Public|Private|Friend|Static)\s+)?Sub\s+([\p{L}_][\p{L}\p{N}_]*)\s*(?:\(\s*\))?\s*(?:'.*)?$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase).Cast<Match>().Select(match => match.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return ParseEntryPoints(names, allowEmptyModule: true);
    }

    internal delegate bool Runner(string path, string module, string procedure, int options, out int error);

    internal static void Execute(ISldWorks host, UserMacroCommand command)
    {
        if (host == null) throw new InvalidOperationException("SOLIDWORKS 尚未连接。");
        Execute(command, host.RunMacro2);
    }

    internal static void Execute(UserMacroCommand command, Runner run, string storageRoot = null)
    {
        Validate(command, storageRoot);
        if (run == null) throw new ArgumentNullException(nameof(run));
        int error;
        bool success = run(command.FilePath, command.ModuleName, command.ProcedureName,
            (int)swRunMacroOption_e.swRunMacroUnloadAfterRun, out error);
        if (!success || error != 0)
            throw new InvalidOperationException("宏执行失败，未重试。功能=" + command.Name + "；入口=" + command.ModuleName + "."
                + command.ProcedureName + "；宿主错误=" + error + "；文件=" + command.FilePath);
    }

    private static string ValidateSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("请选择宏文件。");
        string fullPath = Path.GetFullPath(path);
        string extension = Path.GetExtension(fullPath);
        if (!extension.Equals(".swp", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".swb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("当前支持 SOLIDWORKS VBA 宏 .swp / .swb；请导入完整的宏文件。");
        if (!File.Exists(fullPath)) throw new InvalidOperationException("宏文件不存在：" + fullPath);
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("宏文件路径是链接，请重新导入普通文件。");
        return fullPath;
    }

    private static bool IsTextMacro(string path) => Path.GetExtension(path).Equals(".swb", StringComparison.OrdinalIgnoreCase);

    private static void ValidateEntryPoint(string module, string procedure, bool allowEmptyModule = false)
    {
        module ??= "";
        if ((!allowEmptyModule && string.IsNullOrWhiteSpace(module)) || string.IsNullOrWhiteSpace(procedure)
            || module.Any(char.IsControl) || procedure.Any(char.IsControl) || module.Contains(".") || procedure.Contains("."))
            throw new InvalidOperationException("请填写宏的模块名和无参数过程名，或从读取到的入口中选择。");
    }

    private static string OwnedPath(UserMacroCommand command, string storageRoot)
    {
        if (command == null || !Guid.TryParseExact(command.Id, "N", out _))
            throw new InvalidOperationException("宏身份无效，请重新导入。");
        string root = Path.GetFullPath(storageRoot), directory = Path.Combine(root, command.Id);
        string path = Path.GetFullPath(command.FilePath ?? "");
        if (!string.Equals(Path.GetDirectoryName(path), directory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("宏文件不在该宏的受控存储目录，请重新导入。");
        EnsureOrdinaryDirectory(root); EnsureOrdinaryDirectory(directory);
        return path;
    }

    private static void EnsureOrdinaryDirectory(string directory)
    {
        if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("宏存储目录是链接，未读取或清理其内容。");
    }

    private static int AllocateUserId(string root, IEnumerable<UserMacroCommand> existing)
    {
        // The independent high-water mark survives removing every macro. An old
        // saved toolbar callback can never be rebound to a newly imported macro.
        string allocator = Path.Combine(root, ".next-command-user-id");
        using var stream = new FileStream(allocator, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var bytes = new byte[stream.Length > 32 ? 0 : (int)stream.Length];
        if (stream.Length > 32) throw new InvalidOperationException("宏按钮 ID 记录无效，未重新使用旧 ID。");
        stream.Read(bytes, 0, bytes.Length);
        string text = Encoding.ASCII.GetString(bytes);
        int stored = FirstCommandUserId;
        if (text.Length > 0 && (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out stored) || stored < FirstCommandUserId))
            throw new InvalidOperationException("宏按钮 ID 记录无效，未重新使用旧 ID。");
        long next = Math.Max((long)stored, (long)existing.Select(command => command.CommandUserId).DefaultIfEmpty(FirstCommandUserId - 1).Max() + 1);
        if (next >= int.MaxValue) throw new InvalidOperationException("宏按钮 ID 已用尽。");
        byte[] updated = Encoding.ASCII.GetBytes((next + 1).ToString(CultureInfo.InvariantCulture));
        stream.Position = 0; stream.SetLength(0); stream.Write(updated, 0, updated.Length); stream.Flush(flushToDisk: true);
        return (int)next;
    }
}
