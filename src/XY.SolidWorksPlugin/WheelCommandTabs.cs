using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swcommands;

namespace XY.SolidWorksPlugin;

internal static partial class WheelCommandTabs
{
    internal const string Title = "XY轮盘";
    internal const string LegacyOwnedTitle = "小阳轮盘";
    internal static readonly int[] DocumentTypes = { 1, 2, 3 };
    // Positions in SwAddin's command list; new commands are appended to preserve saved command IDs.
	internal static readonly int[][] Groups = { new[] { 0, 4, 3 }, new[] { 1, 2, 5 }, new[] { 6 } };
	private static readonly string[] Icons = { "settings", "watermark-settings", "process-recognition", "process-recognition", "settings", "explode-all", "macro-run" };

    internal static string CreateIcons(ICommandGroup group, string hostDirectory = null)
    {
        string directory = Path.Combine(Path.GetTempPath(), "XY.SolidWorksPlugin", "command-icons-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        int explodeId = -1;
        int macroId = (int)swCommands_e.swCommands_RunMacro;
        var native = new Dictionary<int, Bitmap>();
        try
        {
            if (!string.IsNullOrEmpty(hostDirectory) && NativeWheelIconMappings.TryGetBasicCommandId("explode-all", out explodeId))
                native = SolidWorksIconResources.Read(hostDirectory, new[] { explodeId, macroId });
        }
        catch (Exception ex) { Logger.Trace("Native explode toolbar artwork unavailable: " + ex.Message); }
        try
        {
            var strips = new List<string>();
            var mains = new List<string>();
            foreach (int size in new[] { 20, 32, 40, 64, 96, 128 })
            {
                string stripPath = Path.Combine(directory, "commands-" + size + ".png");
                string mainPath = Path.Combine(directory, "wheel-" + size + ".png");
                using (var strip = new Bitmap(size * Icons.Length, size, PixelFormat.Format32bppArgb))
                {
                    using (var graphics = Graphics.FromImage(strip))
                    for (int i = 0; i < Icons.Length; i++)
                    {
                        if (Icons[i] == "explode-all" && native.TryGetValue(explodeId, out var original))
                            WheelIconDrawing.Draw(graphics, original, new Rectangle(i * size, 0, size, size));
                        else if (Icons[i] == "macro-run" && native.TryGetValue(macroId, out var macro))
                            WheelIconDrawing.Draw(graphics, macro, new Rectangle(i * size, 0, size, size));
                        else using (var icon = UnifiedWheelArtwork.Render(Icons[i], size))
                            graphics.DrawImageUnscaled(icon, i * size, 0);
                    }
                    strip.Save(stripPath, ImageFormat.Png);
                }
                using (var main = UnifiedWheelArtwork.Render("wheel", size)) main.Save(mainPath, ImageFormat.Png);
                strips.Add(stripPath);
                mains.Add(mainPath);
            }
            group.IconList = strips.ToArray();
            group.MainIconList = mains.ToArray();
            return directory;
        }
        catch { DeleteIcons(directory); throw; }
        finally { foreach (var image in native.Values) image.Dispose(); }
    }

    internal static void DeleteIcons(string directory)
    {
        if (string.IsNullOrEmpty(directory)) return;
        try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        catch (Exception ex) { Logger.Error("Remove owned CommandManager icon cache failed", ex); }
    }

    internal static bool RequiresLegacyMigration(int[] userIds)
    {
        if (userIds == null || userIds.Length == 0) return false;
		int[] retained = { 1, 3, 5, 2, SwAddin.FeatureSettingsUserId, SwAddin.ExplodeAllUserId, SwAddin.UserMacroImportUserId };
        return !userIds.OrderBy(id => id).SequenceEqual(retained.OrderBy(id => id));
    }

    internal static bool ShouldMigrateTabs(bool hasCurrentGroup, bool completed, int[] userIds) =>
        !hasCurrentGroup || !completed || RequiresLegacyMigration(userIds);

    internal static string MigrationRegistryPath(string revision)
    {
        string major = (revision ?? "").Split('.')[0];
        if (!int.TryParse(major, out int number) || number <= 0)
            throw new InvalidOperationException("无法确认 SOLIDWORKS 版本，未修改轮盘选项卡迁移状态。");
        return @"Software\XY\XYRadialMenu.SW\CommandManager\" + number;
    }

    internal static bool IsMigrationComplete(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return Equals(key?.GetValue("CompletedGroup"), SwAddin.CommandGroupId);
    }

    internal static void MarkMigrationComplete(string path)
        => WriteMigrationGroup(path, SwAddin.CommandGroupId);

    internal static void MarkMigrationPending(string path) => WriteMigrationGroup(path, 0);

    private static void WriteMigrationGroup(string path, int groupId)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path);
        if (key == null) throw new InvalidOperationException("无法保存轮盘选项卡迁移完成标记。");
        key.SetValue("CompletedGroup", groupId, RegistryValueKind.DWord);
        if (!Equals(key.GetValue("CompletedGroup"), groupId))
            throw new InvalidOperationException("轮盘选项卡迁移完成标记回读失败。");
    }

    internal static void RemoveLegacyOwnedGroup(ICommandManager manager)
    {
        object ids;
        bool registered = manager.GetGroupDataFromRegistry(SwAddin.LegacyCommandGroupId, out ids);
        bool loaded = manager.GetCommandGroup(SwAddin.LegacyCommandGroupId) != null;
        Logger.Info("Legacy owned group before removal. Id=" + SwAddin.LegacyCommandGroupId
            + "; RegisteredIds=" + registered + "; RuntimeLoaded=" + loaded);
        if (!registered && !loaded) return;
        // The old schema belongs to this product. False retires its saved toolbar
        // too; unload of the new group continues to use RuntimeOnly=true.
        int result = manager.RemoveCommandGroup2(SwAddin.LegacyCommandGroupId, false);
        bool remainsRegistered = manager.GetGroupDataFromRegistry(SwAddin.LegacyCommandGroupId, out ids);
        bool remainsLoaded = manager.GetCommandGroup(SwAddin.LegacyCommandGroupId) != null;
        Logger.Info("Legacy owned group after removal. Id=" + SwAddin.LegacyCommandGroupId
            + "; Result=" + result + "; RegisteredIds=" + remainsRegistered + "; RuntimeLoaded=" + remainsLoaded);
        // GetGroupDataFromRegistry exposes saved UserID mappings for use before
        // CreateCommandGroup2. A nonempty cache after removal does not establish
        // that a toolbar still exists. Never equate its bool with live UI state.
        // Failed(0) is only an idempotent absence when no runtime group existed
        // before OR after the request; an actual loaded-group failure still fails.
        if (!IsLegacyRemovalComplete(result, loaded, remainsLoaded))
            throw new InvalidOperationException("无法移除本产品旧版命令组。Id=" + SwAddin.LegacyCommandGroupId
                + "; Result=" + result + "; BeforeLoaded=" + loaded + "; AfterLoaded=" + remainsLoaded
                + "; SavedIdsReported=" + remainsRegistered);
        Logger.Info("Retired owned legacy command group. Id=" + SwAddin.LegacyCommandGroupId + "; Result=" + result
            + "; RuntimeAbsent=True; SavedIdsReported=" + remainsRegistered
            + "; AlreadyAbsent=" + (result != (int)swRemoveCommandGroupErrors.swRemoveCommandGroup_Success));
    }

    internal static bool IsLegacyRemovalComplete(int result, bool loadedBefore, bool loadedAfter) =>
        !loadedAfter && (result == (int)swRemoveCommandGroupErrors.swRemoveCommandGroup_Success
            || (result == (int)swRemoveCommandGroupErrors.swRemoveCommandGroup_Failed && !loadedBefore));

    private sealed class TabSnapshot
    {
        internal int DocumentType;
        internal string Title;
        internal bool Exists;
        internal bool Visible;
        internal readonly List<Tuple<int[], int[]>> Boxes = new List<Tuple<int[], int[]>>();
    }

    private static TabSnapshot CaptureTab(ICommandManager manager, int documentType, string title = Title)
    {
        var tab = manager.GetCommandTab(documentType, title) as ICommandTab;
        var snapshot = new TabSnapshot { DocumentType = documentType, Title = title, Exists = tab != null };
        if (tab == null) return snapshot;
        snapshot.Visible = tab.Visible;
        object[] boxes = tab.CommandTabBoxes() as object[] ?? new object[0];
        if (tab.GetCommandTabBoxCount() != boxes.Length)
            throw new InvalidOperationException("本产品选项卡分组枚举不完整，取消迁移。");
        foreach (ICommandTabBox box in boxes)
        {
            object ids, styles;
            int count = box.GetCommands(out ids, out styles);
            int[] commands = ids as int[] ?? new int[0], display = styles as int[] ?? new int[0];
            if (count != commands.Length || commands.Length != display.Length)
                throw new InvalidOperationException("无法完整备份本产品选项卡，取消迁移。");
            snapshot.Boxes.Add(Tuple.Create(commands, display));
        }
        return snapshot;
    }

    private static void RestoreTab(ICommandManager manager, TabSnapshot snapshot)
    {
        if (SameTabSnapshot(snapshot, CaptureTab(manager, snapshot.DocumentType, snapshot.Title))) return;
        var current = manager.GetCommandTab(snapshot.DocumentType, snapshot.Title) as ICommandTab;
        if (current != null)
        {
            manager.RemoveCommandTab((CommandTab)current);
            if (manager.GetCommandTab(snapshot.DocumentType, snapshot.Title) != null)
                throw new InvalidOperationException("无法移除失败的本产品选项卡，未追加重复按钮。");
        }
        if (!snapshot.Exists) return;
        var restored = manager.AddCommandTab(snapshot.DocumentType, snapshot.Title) as ICommandTab;
        if (restored == null) throw new InvalidOperationException("无法恢复本产品选项卡。");
        foreach (var saved in snapshot.Boxes)
        {
            var box = restored.AddCommandTabBox() as ICommandTabBox;
            if (box == null || (saved.Item1.Length > 0 && !box.AddCommands(saved.Item1, saved.Item2)))
                throw new InvalidOperationException("无法恢复本产品选项卡按钮。");
        }
        restored.Visible = snapshot.Visible;
        if (!SameTabSnapshot(snapshot, CaptureTab(manager, snapshot.DocumentType, snapshot.Title)))
            throw new InvalidOperationException("本产品选项卡恢复回读不一致。");
    }

    private static bool SameTabSnapshot(TabSnapshot first, TabSnapshot second) => first.Exists == second.Exists
        && (!first.Exists || first.Visible == second.Visible && first.Boxes.Count == second.Boxes.Count
            && first.Boxes.Zip(second.Boxes, (left, right) => left.Item1.SequenceEqual(right.Item1) && left.Item2.SequenceEqual(right.Item2)).All(equal => equal));

    internal static int Register(ICommandManager manager, int[] commandIds, bool migrateLegacyGroup = false, ISet<int> deferredDocumentTypes = null)
    {
        if (commandIds == null || commandIds.Length != SwAddin.ExpectedCommandGroupItemCount || commandIds.Any(id => id <= 0) || commandIds.Distinct().Count() != commandIds.Length)
            throw new InvalidOperationException("轮盘选项卡命令 ID 不完整，未开始迁移。");
        // Exact product titles only. The historical tab can outlive its group
        // and Completed marker, so retire it after all three new tabs succeed.
        // Read every box first; failed registration/cleanup restores each owned
        // tab actually touched, including visibility and user-added buttons.
        var snapshots = DocumentTypes.Select(type => CaptureTab(manager, type)).ToArray();
        var legacy = DocumentTypes.Select(type => CaptureTab(manager, type, LegacyOwnedTitle)).Where(tab => tab.Exists).ToArray();
        var changedTypes = new HashSet<int>();
        var legacyTouched = new List<TabSnapshot>();
        deferredDocumentTypes?.Clear();
        try
        {
            int registered = RegisterCore(manager, commandIds, migrateLegacyGroup, changedTypes, deferredDocumentTypes);
            if (registered != DocumentTypes.Length) throw new InvalidOperationException("轮盘三个新选项卡尚未全部注册，保留旧版选项卡。");
            foreach (var snapshot in legacy)
            {
                if (deferredDocumentTypes?.Contains(snapshot.DocumentType) == true) continue;
                // SW can return false for a tab belonging to an inactive document
                // type. Keep registration usable and let the real context-change
                // maintenance retry that one exact title in its active document.
                if (TryRemoveLegacyOwnedTab(manager, snapshot.DocumentType)) legacyTouched.Add(snapshot);
                else deferredDocumentTypes?.Add(snapshot.DocumentType);
            }
            return registered;
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            foreach (var snapshot in legacyTouched.AsEnumerable().Reverse().Concat(snapshots.Where(tab => changedTypes.Contains(tab.DocumentType))))
                try { RestoreTab(manager, snapshot); }
                catch (Exception restoreFailure) { errors.Add(restoreFailure); Logger.Error("Restore owned wheel tab failed", restoreFailure); }
            if (errors.Count > 1) throw new AggregateException("轮盘选项卡迁移及恢复失败，禁止交付。", errors);
            throw;
        }
    }

    internal static bool TryMaintainCurrentDocumentTab(ICommandManager manager, int documentType, int[] commandIds)
    {
        if (manager == null || !DocumentTypes.Contains(documentType)) return false;
        TabSnapshot snapshot = null;
        var changed = new HashSet<int>();
        try
        {
            snapshot = CaptureTab(manager, documentType);
            int registered = RegisterCore(manager, commandIds, true, changed, null, new[] { documentType });
            return registered == 1 && TryRemoveLegacyOwnedTab(manager, documentType);
        }
        catch (Exception failure)
        {
            if (snapshot != null && changed.Contains(documentType))
                try { RestoreTab(manager, snapshot); }
                catch (Exception restoreFailure) { Logger.Error("Restore current owned tab after deferred repair failed", restoreFailure); }
            Logger.Trace("Owned tab repair deferred until active document settles. DocType=" + documentType + "; " + failure.Message);
            return false;
        }
    }

    internal static bool TryRemoveLegacyOwnedTab(ICommandManager manager, int documentType)
    {
        if (manager == null || !DocumentTypes.Contains(documentType)) return false;
        try
        {
            var old = manager.GetCommandTab(documentType, LegacyOwnedTitle) as ICommandTab;
            if (old == null) return true;
            bool reported = manager.RemoveCommandTab((CommandTab)old);
            bool remains = manager.GetCommandTab(documentType, LegacyOwnedTitle) != null;
            if (remains)
            {
                Logger.Info("Owned legacy tab cleanup pending active document context. Title=" + LegacyOwnedTitle
                    + "; DocType=" + documentType + "; RemoveReported=" + reported);
                return false;
            }
            Logger.Info("Retired exact owned legacy tab. Title=" + LegacyOwnedTitle + "; DocType=" + documentType + "; ReadBackAbsent=True");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Trace("Owned legacy tab cleanup deferred. DocType=" + documentType + "; " + ex.Message);
            return false;
        }
    }

    private static int RegisterCore(ICommandManager manager, int[] commandIds, bool migrateLegacyGroup, ISet<int> changedTypes,
        ISet<int> deferredDocumentTypes = null, IEnumerable<int> documentTypes = null)
    {
        if (commandIds == null || commandIds.Length != SwAddin.ExpectedCommandGroupItemCount || commandIds.Any(id => id <= 0) || commandIds.Distinct().Count() != commandIds.Length)
            throw new InvalidOperationException("轮盘选项卡命令 ID 不完整：" + (commandIds == null ? "null" : string.Join(",", commandIds)));
        int count = 0;
        foreach (int documentType in documentTypes ?? DocumentTypes)
        {
            Logger.Info("Wheel tab begin doctype=" + documentType + "; ids=" + string.Join(",", commandIds));
            var tab = manager.GetCommandTab(documentType, Title) as ICommandTab;
            bool? previousVisibility = null;
            // Only a pending product schema migration replaces these owned tabs.
            if (tab != null && migrateLegacyGroup)
            {
                int[] existingIds = CaptureTab(manager, documentType).Boxes.SelectMany(box => box.Item1).ToArray();
                bool sameRuntimeCommands = existingIds.OrderBy(id => id).SequenceEqual(commandIds.OrderBy(id => id));
                Logger.Info("Wheel existing tab ids=" + string.Join(",", existingIds) + "; ExactRuntimeMatch=" + sameRuntimeCommands);
                if (sameRuntimeCommands) { count++; continue; }
                changedTypes.Add(documentType);
                previousVisibility = tab.Visible;
                bool removed = manager.RemoveCommandTab((CommandTab)tab);
                if (manager.GetCommandTab(documentType, Title) != null)
                {
                    changedTypes.Remove(documentType);
                    if (deferredDocumentTypes == null) throw new InvalidOperationException("无法移除本产品旧版功能选项卡，未追加重复按钮。");
                    deferredDocumentTypes.Add(documentType);
                    Logger.Info("Owned XY tab repair pending active document context. DocType=" + documentType + "; RemoveReported=" + removed);
                    count++;
                    continue;
                }
                tab = null;
            }
            bool created = tab == null;
            if (!created)
            {
                // A completed schema belongs to the user now, including buttons
                // deliberately removed or repositioned through SW customization.
                count++;
                continue;
            }
            Logger.Info("Wheel tab lookup created=" + created);
            if (created) { changedTypes.Add(documentType); tab = manager.AddCommandTab(documentType, Title) as ICommandTab; }
            Logger.Info("Wheel tab add returned=" + (tab != null));
            if (tab == null) throw new InvalidOperationException("无法创建轮盘选项卡：" + documentType);
            try
            {
                var existing = new HashSet<int>();
                foreach (ICommandTabBox box in (tab.CommandTabBoxes() as object[]) ?? new object[0])
                {
                    object ids, styles;
                    box.GetCommands(out ids, out styles);
                    foreach (int id in (ids as int[]) ?? new int[0]) existing.Add(id);
                }
                foreach (int[] group in Groups)
                {
                    int[] missing = group.Select(index => commandIds[index]).Where(id => !existing.Contains(id)).ToArray();
                    if (missing.Length == 0) continue;
                    Logger.Info("Wheel tab adding box count=" + missing.Length);
                    var box = tab.AddCommandTabBox() as ICommandTabBox;
                    int style = (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextHorizontal;
                    if (box == null || !box.AddCommands(missing, Enumerable.Repeat(style, missing.Length).ToArray()))
                        throw new InvalidOperationException("轮盘选项卡按钮注册失败：" + documentType);
                }
                // Respect visibility and placement chosen by the user on subsequent loads.
                if (created) tab.Visible = previousVisibility ?? true;
                var actual = CaptureTab(manager, documentType);
                if (actual.Boxes.Count != Groups.Length ||
                    !actual.Boxes.SelectMany(box => box.Item1).OrderBy(id => id).SequenceEqual(commandIds.OrderBy(id => id)))
                    throw new InvalidOperationException("轮盘新选项卡回读不一致：" + documentType);
                count++;
            }
            catch
            {
                if (created) manager.RemoveCommandTab((CommandTab)tab);
                throw;
            }
        }
        return count;
    }
}
