using System;
using System.Collections.Generic;
using System.Linq;

namespace XY.SolidWorksPlugin;

/// <summary>The product owns the wheel, watermark, process workflow and one-click explosion.</summary>
internal static class ProductFeatureScope
{
    // View/document shortcuts remain normal SOLIDWORKS commands available to the
    // wheel. External/native command kinds are deliberately outside this list.
    private static readonly HashSet<string> AllowedBasicActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "settings", "watermark-settings", "process-recognition", "explode-all",
        "new-part", "new-assembly", "new-drawing", "rebuild", "force-rebuild",
        "normal-to", "zoom-fit", "clear-selection", "view-front", "view-back",
        "view-left", "view-right", "view-top", "view-bottom", "view-isometric"
    };

    internal static bool IsAllowedBasicAction(string value) => AllowedBasicActions.Contains((value ?? "").Trim());

    internal static bool IsRetiredAction(string kind, string value) =>
        string.Equals(kind?.Trim(), ActionKinds.Basic, StringComparison.OrdinalIgnoreCase)
        && !IsAllowedBasicAction(value);

    internal static bool IsRetiredBinding(MenuItemConfig item) => item != null
        && (IsRetiredAction(item.Kind, item.Value)
            || item.ComboCommands?.Any(command => command != null && IsRetiredAction(command.Kind, command.Value)) == true);

    /// <summary>
    /// Keep old settings and complete sequences recoverable, but remove retired
    /// bindings from active rings. Never silently drop just one step of a combo.
    /// </summary>
    internal static int NormalizeConfig(PluginConfig config)
    {
        if (config == null) return 0;
        int removed = 0;
        if (config.Menus == null) return removed;
        foreach (SceneMenuConfig scene in config.Menus.Values.Where(scene => scene != null))
        {
            if (scene.Rings == null) continue;
            scene.UnassignedItems ??= new List<MenuItemConfig>();
            foreach (RingConfig ring in scene.Rings.Where(ring => ring?.Items != null))
            {
                foreach (MenuItemConfig item in ring.Items.Where(IsRetiredBinding).ToArray())
                {
                    if (!scene.UnassignedItems.Contains(item)) scene.UnassignedItems.Add(item);
                    ring.Items.Remove(item);
                    removed++;
                }
            }
        }
        return removed;
    }

    /// <summary>Reject the whole sequence before any action or selection change.</summary>
    internal static void EnsureExecutable(MenuItemConfig item)
    {
        if (IsRetiredBinding(item))
            throw new InvalidOperationException("该轮盘配置包含已移除的扩展功能；整组未执行。原配置已保留，请重新选择轮盘、水印、工序、一键爆炸或 SOLIDWORKS 命令。");
    }
}
