using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace XY.SolidWorksPlugin;

internal static partial class WheelCommandTabs
{
    private static readonly object OwnershipSync = new object();
    private static readonly HashSet<string> OwnedTitles = new HashSet<string>(StringComparer.Ordinal) { Title, LegacyOwnedTitle };
    private static readonly HashSet<int> OwnedMacroIds = new HashSet<int>();

    internal static string ReadOwnedTitle(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        string title = key?.GetValue("OwnedTabTitle") as string ?? Title;
        lock (OwnershipSync)
        {
            foreach (var name in key?.GetValue("OwnedTabTitles") as string[] ?? Array.Empty<string>())
                if (ValidOwnedTitle(name)) OwnedTitles.Add(name);
            if (ValidOwnedTitle(title)) OwnedTitles.Add(title);
        }
        return ValidOwnedTitle(title) ? title : Title;
    }

    private static bool ValidOwnedTitle(string title) => !string.IsNullOrWhiteSpace(title) && title.Length <= 64 && !title.Any(char.IsControl);

    internal static void RecordOwnedTitle(string path, string title)
    {
        title = UserMacroStore.ValidateTabName(title);
        lock (OwnershipSync)
        {
            OwnedTitles.Add(title);
            using var key = Registry.CurrentUser.CreateSubKey(path);
            if (key == null) throw new InvalidOperationException("无法保存本产品选项卡身份。");
            key.SetValue("OwnedTabTitles", OwnedTitles.OrderBy(name => name, StringComparer.Ordinal).ToArray(), RegistryValueKind.MultiString);
            key.SetValue("OwnedTabTitle", title, RegistryValueKind.String);
        }
    }

    internal static int[] ReadOwnedMacroIds(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        int[] ids = (key?.GetValue("OwnedMacroCommandIds") as string[] ?? Array.Empty<string>())
            .Select(value => int.TryParse(value, out int id) ? id : 0).Where(id => id > 0).Distinct().ToArray();
        lock (OwnershipSync) OwnedMacroIds.UnionWith(ids);
        return ids;
    }

    internal static void RecordOwnedMacroIds(string path, int[] ids)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path);
        if (key == null) throw new InvalidOperationException("无法保存用户宏按钮身份。");
        lock (OwnershipSync)
        {
            OwnedMacroIds.UnionWith((ids ?? Array.Empty<int>()).Where(id => id > 0));
            key.SetValue("OwnedMacroCommandIds", OwnedMacroIds.OrderBy(id => id)
                .Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(), RegistryValueKind.MultiString);
        }
    }

    internal static void ValidateConfiguredTitle(ICommandManager manager, string title, string previousOwnedTitle = null, IEnumerable<int> ownedCommandIds = null)
    {
        title = UserMacroStore.ValidateTabName(title);
        bool known;
        lock (OwnershipSync) known = OwnedTitles.Contains(title);
        if (known || string.Equals(title, previousOwnedTitle, StringComparison.Ordinal))
        {
            if (ownedCommandIds != null && title != Title && title != LegacyOwnedTitle)
            {
                var ids = new HashSet<int>(ownedCommandIds);
                foreach (var type in DocumentTypes)
                {
                    var snapshot = CaptureTab(manager, type, title);
                    if (snapshot.Exists && !snapshot.Boxes.SelectMany(box => box.Item1).Any(ids.Contains))
                        throw new InvalidOperationException("同名选项卡不含可确认的本产品命令，未修改：" + title);
                }
            }
            return;
        }
        if (DocumentTypes.Any(type => manager.GetCommandTab(type, title) != null))
            throw new InvalidOperationException("该选项卡名称已被其他选项卡使用，请输入不同名称：" + title);
    }

    internal static int RegisterConfigured(ICommandManager manager, int[] commandIds, string title, bool visible,
        int[] macroIds, string previousOwnedTitle = null, ISet<int> deferredDocumentTypes = null, bool migrate = false, int[] previousMacroIds = null)
        => ApplyConfigured(manager, commandIds, title, visible, macroIds, previousOwnedTitle, deferredDocumentTypes, migrate, previousMacroIds, DocumentTypes);

    internal static bool TryMaintainConfiguredCurrentDocumentTab(ICommandManager manager, int documentType, int[] commandIds,
        string title, bool visible, int[] macroIds, string previousOwnedTitle = null, int[] previousMacroIds = null, bool migrate = false)
    {
        if (manager == null || !DocumentTypes.Contains(documentType)) return false;
        try
        {
            var deferred = new HashSet<int>();
            return ApplyConfigured(manager, commandIds, title, visible, macroIds, previousOwnedTitle, deferred, migrate,
                previousMacroIds, new[] { documentType }) == 1 && deferred.Count == 0;
        }
        catch (Exception error) { Logger.Trace("Configured wheel tab repair deferred: " + error.Message); return false; }
    }

    private static int ApplyConfigured(ICommandManager manager, int[] commandIds, string title, bool visible,
        int[] macroIds, string previousOwnedTitle, ISet<int> deferred, bool migrate, int[] previousMacroIds, IEnumerable<int> documentTypes)
    {
        title = UserMacroStore.ValidateTabName(title);
        if (manager == null || commandIds == null || commandIds.Length != SwAddin.ExpectedCommandGroupItemCount
            || commandIds.Any(id => id <= 0) || commandIds.Distinct().Count() != commandIds.Length)
            throw new InvalidOperationException("轮盘选项卡命令身份不完整。");
        macroIds ??= Array.Empty<int>();
        if (macroIds.Any(id => id <= 0) || macroIds.Distinct().Count() != macroIds.Length || macroIds.Intersect(commandIds).Any())
            throw new InvalidOperationException("用户宏按钮身份重复或无效。");
        int[] historicalMacroIds;
        lock (OwnershipSync)
        {
            OwnedMacroIds.UnionWith(previousMacroIds ?? Array.Empty<int>());
            historicalMacroIds = OwnedMacroIds.ToArray();
        }
        var ownIds = new HashSet<int>(commandIds.Concat(macroIds).Concat(historicalMacroIds));
        ValidateConfiguredTitle(manager, title, previousOwnedTitle, ownIds);
        var types = documentTypes.ToArray();
        string[] owned;
        lock (OwnershipSync) owned = OwnedTitles.ToArray();
        var titles = owned.Concat(new[] { title, previousOwnedTitle }).Where(ValidOwnedTitle).Distinct(StringComparer.Ordinal).ToArray();
        var snapshots = types.SelectMany(type => titles.Select(name => CaptureTab(manager, type, name))).ToArray();
        if (previousOwnedTitle != null && previousOwnedTitle != Title && previousOwnedTitle != LegacyOwnedTitle)
            foreach (var previous in snapshots.Where(snapshot => snapshot.Exists && snapshot.Title == previousOwnedTitle))
                if (!previous.Boxes.SelectMany(box => box.Item1).Any(ownIds.Contains))
                    throw new InvalidOperationException("无法确认原选项卡的实际命令归属，未迁移：" + previousOwnedTitle);
        var changed = new HashSet<Tuple<int, string>>();
        var removeMacroIds = new HashSet<int>(historicalMacroIds.Except(macroIds).Except(commandIds));
        deferred?.Clear();
        try
        {
            foreach (int type in types)
            {
                var current = snapshots.Single(snapshot => snapshot.DocumentType == type && snapshot.Title == title);
                var previous = snapshots.FirstOrDefault(snapshot => snapshot.DocumentType == type && snapshot.Title == previousOwnedTitle);
                var source = previousOwnedTitle != title && previous?.Exists == true ? previous : current;
                var desired = new TabSnapshot { DocumentType = type, Title = title, Exists = true, Visible = visible };
                var baseIds = new HashSet<int>(commandIds);
                bool replaceSchema = migrate && !source.Boxes.SelectMany(box => box.Item1).Any(baseIds.Contains);
                var seenBase = new HashSet<int>();
                if (!source.Exists || replaceSchema)
                {
                    foreach (var group in Groups) desired.Boxes.Add(Tuple.Create(group.Select(index => commandIds[index]).ToArray(),
                        Enumerable.Repeat((int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextHorizontal, group.Length).ToArray()));
                }
                else foreach (var box in source.Boxes)
                {
                    var kept = Enumerable.Range(0, box.Item1.Length).Where(index => !removeMacroIds.Contains(box.Item1[index])
                        && (!migrate || !baseIds.Contains(box.Item1[index]) || seenBase.Add(box.Item1[index]))).ToArray();
                    if (kept.Length > 0) desired.Boxes.Add(Tuple.Create(kept.Select(index => box.Item1[index]).ToArray(), kept.Select(index => box.Item2[index]).ToArray()));
                }
                if (migrate && source.Exists && !replaceSchema)
                {
                    var existingBase = new HashSet<int>(desired.Boxes.SelectMany(box => box.Item1));
                    foreach (var group in Groups)
                    {
                        int[] missing = group.Select(index => commandIds[index]).Where(id => !existingBase.Contains(id)).ToArray();
                        if (missing.Length > 0) desired.Boxes.Add(Tuple.Create(missing,
                            Enumerable.Repeat((int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextHorizontal, missing.Length).ToArray()));
                    }
                }
                int[] missingMacros = macroIds.Except(desired.Boxes.SelectMany(box => box.Item1)).ToArray();
                if (missingMacros.Length > 0) desired.Boxes.Add(Tuple.Create(missingMacros,
                    Enumerable.Repeat((int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextHorizontal, missingMacros.Length).ToArray()));
                if (!ApplyConfiguredSnapshot(manager, current, desired, changed, deferred)) continue;
                foreach (var old in snapshots.Where(snapshot => snapshot.DocumentType == type && snapshot.Title != title && snapshot.Exists))
                {
                    if (old.Title != Title && old.Title != LegacyOwnedTitle && !old.Boxes.SelectMany(box => box.Item1).Any(ownIds.Contains)) continue;
                    var tab = manager.GetCommandTab(type, old.Title) as ICommandTab;
                    if (tab == null) continue;
                    changed.Add(Tuple.Create(type, old.Title));
                    manager.RemoveCommandTab((CommandTab)tab);
                    if (manager.GetCommandTab(type, old.Title) is ICommandTab remaining)
                    {
                        remaining.Visible = false;
                        if (deferred == null) throw new InvalidOperationException("旧选项卡尚不能移除：" + old.Title);
                        deferred.Add(type);
                    }
                }
            }
            lock (OwnershipSync)
            {
                OwnedTitles.Add(title);
                OwnedMacroIds.UnionWith(macroIds);
                foreach (var retired in owned.Where(name => name != title && name != Title && name != LegacyOwnedTitle))
                    if (DocumentTypes.All(type => manager.GetCommandTab(type, retired) == null)) OwnedTitles.Remove(retired);
                var referenced = new HashSet<int>(DocumentTypes.SelectMany(type => OwnedTitles.Select(name => CaptureTab(manager, type, name)))
                    .SelectMany(snapshot => snapshot.Boxes).SelectMany(box => box.Item1));
                OwnedMacroIds.RemoveWhere(id => !macroIds.Contains(id) && !referenced.Contains(id));
            }
            return types.Length;
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            foreach (var original in snapshots.Reverse().Where(snapshot => changed.Contains(Tuple.Create(snapshot.DocumentType, snapshot.Title))))
                try { RestoreTab(manager, original); }
                catch (Exception error) { errors.Add(error); }
            if (errors.Count > 1) throw new AggregateException("选项卡设置失败且恢复未完成。", errors);
            throw;
        }
    }

    private static bool ApplyConfiguredSnapshot(ICommandManager manager, TabSnapshot current, TabSnapshot desired,
        ISet<Tuple<int, string>> changed, ISet<int> deferred)
    {
        var tab = manager.GetCommandTab(desired.DocumentType, desired.Title) as ICommandTab;
        bool sameLayout = current.Exists && current.Boxes.Count == desired.Boxes.Count && current.Boxes.Zip(desired.Boxes,
            (left, right) => left.Item1.SequenceEqual(right.Item1) && left.Item2.SequenceEqual(right.Item2)).All(equal => equal);
        if (sameLayout)
        {
            if (current.Visible != desired.Visible) { changed.Add(Tuple.Create(desired.DocumentType, desired.Title)); tab.Visible = desired.Visible; }
        }
        else
        {
            if (tab != null)
            {
                changed.Add(Tuple.Create(desired.DocumentType, desired.Title));
                manager.RemoveCommandTab((CommandTab)tab);
                if (manager.GetCommandTab(desired.DocumentType, desired.Title) != null)
                {
                    if (deferred == null) throw new InvalidOperationException("选项卡当前不可更新，未追加重复按钮。");
                    deferred.Add(desired.DocumentType); return false;
                }
            }
            changed.Add(Tuple.Create(desired.DocumentType, desired.Title));
            tab = manager.AddCommandTab(desired.DocumentType, desired.Title) as ICommandTab;
            if (tab == null) throw new InvalidOperationException("无法创建选项卡：" + desired.Title);
            foreach (var saved in desired.Boxes)
            {
                var box = tab.AddCommandTabBox() as ICommandTabBox;
                if (box == null || !box.AddCommands(saved.Item1, saved.Item2)) throw new InvalidOperationException("选项卡按钮更新未完成。");
            }
            tab.Visible = desired.Visible;
        }
        var actual = CaptureTab(manager, desired.DocumentType, desired.Title);
        if (!SameTabSnapshot(desired, actual))
            throw new InvalidOperationException("选项卡设置回读不一致。文档类型=" + desired.DocumentType
                + "；名称=" + desired.Title + "；预期=" + DescribeConfiguredTab(desired) + "；实际=" + DescribeConfiguredTab(actual));
        return true;
    }

    private static string DescribeConfiguredTab(TabSnapshot snapshot) => "存在=" + snapshot.Exists + ",显示=" + snapshot.Visible
        + ",分组=" + string.Join("|", snapshot.Boxes.Select(box => "[" + string.Join(",", box.Item1.Zip(box.Item2,
            (id, style) => id + "/" + style)) + "]"));
}
