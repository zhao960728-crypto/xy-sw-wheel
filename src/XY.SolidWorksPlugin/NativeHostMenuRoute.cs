using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace XY.SolidWorksPlugin;

// Adapted from the reference HostMenuRoute: fresh bounded text/state snapshots,
// with no initialization messages, icon discovery or persisted native handles.
internal static class NativeHostMenuRoute
{
    internal sealed class Item
    {
        internal string Caption;
        internal uint Id, State, Type, Style;
        internal IntPtr SubMenu;
    }
    internal sealed class Leaf
    {
        internal string[] Path;
        internal int Id;
        internal bool SafeNativePath, Enabled;
    }
    internal sealed class Snapshot
    {
        internal readonly List<Leaf> Leaves = new List<Leaf>();
        internal bool Complete = true;
        internal Leaf ResolveVerifiedNative(int uiId, string resourceCaption, string nativeSymbol = null)
        {
            if (!Complete || uiId <= 0 || uiId > ushort.MaxValue || string.IsNullOrWhiteSpace(resourceCaption)) return null;
            var matches = Leaves.Where(leaf => leaf.Id == uiId).Take(2).ToArray();
            if (matches.Length != 1) return null;
            var target = matches[0];
            return target.SafeNativePath && target.Path.Length > 0
                && (Caption(target.Path.Last()) == Caption(resourceCaption)
                    || NativeMenuRouteCaptionAliases.Matches(nativeSymbol, uiId, resourceCaption, target.Path)) ? target : null;
        }
    }

    internal static string Caption(string text)
    {
        string caption = Regex.Replace((text ?? "").Split('\t')[0], @"\(&.\)", "");
        return caption.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&").Trim().TrimEnd('.', '…').Trim();
    }

    internal static Snapshot Capture(IntPtr root) => Capture(root, GetMenuItemCount, Read);

    internal static Snapshot Capture(IntPtr root, Func<IntPtr, int> count, Func<IntPtr, int, Item> read,
        int maxItems = 4096, int maxDepth = 16)
    {
        var result = new Snapshot();
        var visited = new HashSet<IntPtr>();
        var watch = Stopwatch.StartNew();
        int remaining = maxItems;
        Action<IntPtr, string[], bool, bool> walk = null;
        walk = (menu, path, safe, enabled) =>
        {
            if (!result.Complete) return;
            if (menu == IntPtr.Zero || path.Length > maxDepth || !visited.Add(menu)) { result.Complete = false; return; }
            int size = count(menu);
            if (size < 0 || size > remaining) { result.Complete = false; return; }
            var items = new List<Item>();
            for (int index = 0; index < size; index++)
            {
                if (--remaining < 0 || watch.ElapsedMilliseconds > 150) { result.Complete = false; return; }
                Item item = read(menu, index);
                if (item == null) { result.Complete = false; return; }
                items.Add(item);
            }
            var duplicateNames = new HashSet<string>(items.GroupBy(item => Caption(item.Caption), StringComparer.Ordinal)
                .Where(group => group.Count() > 1).Select(group => group.Key), StringComparer.Ordinal);
            foreach (Item item in items)
            {
                if (!result.Complete) break;
                string caption = Caption(item.Caption);
                string[] next = path.Concat(new[] { caption }).ToArray();
                if (next.Length > maxDepth) { result.Complete = false; break; }
                bool valid = safe && caption.Length > 0 && !duplicateNames.Contains(caption)
                    && (item.Type & 0x800) == 0 && (item.Style & 0x08000000) == 0;
                bool active = enabled && (item.State & 3) == 0;
                if (item.SubMenu != IntPtr.Zero)
                    walk(item.SubMenu, next, valid && (item.Type & 0x100) == 0, active);
                else result.Leaves.Add(new Leaf
                {
                    Path = next, Id = unchecked((int)item.Id), Enabled = active,
                    // Only the resource-qualified native leaf may be owner drawn.
                    // Owner-drawn ancestors and notify-by-position menus stay unsafe.
                    SafeNativePath = valid && item.Id > 0 && item.Id <= ushort.MaxValue
                });
            }
            visited.Remove(menu);
        };
        walk(root, Array.Empty<string>(), true, true);
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MenuInfo
    {
        internal uint Size, Mask, Style, MaxHeight;
        internal IntPtr Background;
        internal uint ContextHelp;
        internal UIntPtr Data;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MenuItemInfo
    {
        internal uint Size, Mask, Type, State, Id;
        internal IntPtr SubMenu, Checked, Unchecked;
        internal UIntPtr Data;
        internal IntPtr Text;
        internal uint Length;
        internal IntPtr Bitmap;
    }
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool GetMenuInfo(IntPtr menu, ref MenuInfo info);
    [DllImport("user32.dll", EntryPoint = "GetMenuItemInfoW")] private static extern bool GetMenuItemInfo(IntPtr menu, uint item, bool position, ref MenuItemInfo info);
    private static Item Read(IntPtr menu, int position)
    {
        const int capacity = 2048;
        IntPtr buffer = Marshal.AllocHGlobal(capacity * 2);
        try
        {
            var info = new MenuItemInfo { Size = (uint)Marshal.SizeOf(typeof(MenuItemInfo)), Mask = 0x147, Text = buffer, Length = capacity };
            var style = new MenuInfo { Size = (uint)Marshal.SizeOf(typeof(MenuInfo)), Mask = 0x10 };
            if (!GetMenuItemInfo(menu, (uint)position, true, ref info) || !GetMenuInfo(menu, ref style) || info.Length >= capacity - 1) return null;
            return new Item { Caption = Marshal.PtrToStringUni(buffer, (int)info.Length), Id = info.Id, State = info.State,
                Type = info.Type, Style = style.Style, SubMenu = info.SubMenu };
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
