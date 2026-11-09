using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using SolidWorks.Interop.swcommands;
using SolidWorks.Interop.sldworks;

namespace XY.SolidWorksPlugin;

internal static class ExternalMenuCommandCatalog
{
	internal sealed class Entry
	{
		public int Id;
		public string Name;
		public string Path;
		public bool Enabled;
		public string Shortcut;
	}
	private static readonly HashSet<int> NativeIds = new HashSet<int>(Enum.GetValues(typeof(swCommands_e)).Cast<swCommands_e>().Select(x => (int)x));
	public static List<Entry> Read(ISldWorks host)
	{
		var result = new List<Entry>();
		if (host == null) return result;
		try
		{
			IFrame frame = host.IFrameObject();
			ReadMenu(ResolveMenuHandle(frame, GetMenu), "", true, new HashSet<IntPtr>(), result);
		}
		catch (Exception ex) { Logger.Trace("Read SOLIDWORKS native frame menu failed: " + ex.Message); }
		return result;
	}

	internal static IntPtr ResolveMenuHandle(IFrame frame, Func<IntPtr, IntPtr> windowMenu)
	{
		if (frame == null) return IntPtr.Zero;
		try
		{
			// SOLIDWORKS can expose its backing menu without attaching it to
			// the frame HWND. Preserve the native 64-bit handle from IFrame.
			long menu = frame.GetMenux64();
			if (menu != 0) return new IntPtr(menu);
		}
		catch (Exception ex) { Logger.Trace("Native frame menu handle unavailable: " + ex.Message); }
		IntPtr window = new IntPtr(frame.GetHWndx64());
		return window == IntPtr.Zero ? IntPtr.Zero : windowMenu(window);
	}

	public static List<Entry> Read(IntPtr window)
	{
		var result = new List<Entry>();
		ReadMenu(GetMenu(window), "", true, new HashSet<IntPtr>(), result);
		return result;
	}
	private static void ReadMenu(IntPtr menu, string path, bool parentEnabled, HashSet<IntPtr> visited, List<Entry> output)
	{
		if (menu == IntPtr.Zero || !visited.Add(menu) || visited.Count > 512) return;
		int count = Math.Min(2048, GetMenuItemCount(menu));
		for (int i = 0; i < count; i++)
		{
			var text = new StringBuilder(512);
			GetMenuString(menu, (uint)i, text, text.Capacity, 0x400);
			string name = CleanName(text.ToString());
			bool enabled = parentEnabled && (GetMenuState(menu, (uint)i, 0x400) & 3) == 0;
			IntPtr child = GetSubMenu(menu, i);
			if (child != IntPtr.Zero) { ReadMenu(child, path.Length == 0 ? name : path + " > " + name, enabled, visited, output); continue; }
			int id = unchecked((int)GetMenuItemID(menu, i));
			// Third-party add-ins own user command IDs.  They are not guaranteed
			// to be above 10,000; the only reliable exclusion is a SOLIDWORKS
			// swCommands_e ID (plus the invalid menu sentinel).
			if (id <= 0 || id == unchecked((int)0xFFFFFFFF) || NativeIds.Contains(id) || name.Length == 0 || path.Contains("XY")) continue;
			var fields = text.ToString().Split('\t');
			output.Add(new Entry { Id = id, Name = name, Path = path, Enabled = enabled, Shortcut = fields.Length > 1 ? fields[1].Trim() : "" });
		}
	}
	internal static string CleanName(string text) => (text ?? "").Split('\t')[0].Replace("&", "").Replace("...", "").TrimEnd('…', '.', ' ').Trim();
	[DllImport("user32.dll")] private static extern IntPtr GetMenu(IntPtr window);
	[DllImport("user32.dll")] private static extern IntPtr GetSubMenu(IntPtr menu, int position);
	[DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr menu);
	[DllImport("user32.dll")] private static extern uint GetMenuItemID(IntPtr menu, int position);
	[DllImport("user32.dll")] private static extern uint GetMenuState(IntPtr menu, uint item, uint flags);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMenuString(IntPtr menu, uint item, StringBuilder text, int size, uint flags);
}
