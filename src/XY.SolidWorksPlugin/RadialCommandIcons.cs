using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace XY.SolidWorksPlugin;

// Copies only public toolbar images on the host thread. Painting never calls COM
// or holds a borrowed SOLIDWORKS image-list handle across threads.
internal static class RadialCommandIcons
{
	private static readonly object Sync = new object();
	private static readonly Dictionary<int,Bitmap> EmptyImages = new Dictionary<int,Bitmap>();
	private static readonly Dictionary<string,int> EmptyNames = new Dictionary<string,int>();
	private static readonly Dictionary<int,int> EmptyIds = new Dictionary<int,int>();
	private static Dictionary<int, Bitmap> _images = new Dictionary<int, Bitmap>();
	private static Dictionary<string, int> _hotkeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
	private static Dictionary<string, int> _names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
	private static Dictionary<int, int> _nativeIds = new Dictionary<int, int>();
	private static Dictionary<int, Bitmap> _resources = new Dictionary<int, Bitmap>();
	private static string _resourceHost = "";
	private static bool _resourceClassic;
	private static int _generation;
	private static Dictionary<string, Bitmap> _namedAssets = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
	private static int _assemblyCount = -1;
	private static DateTime _nextAssetRefresh;
	private sealed class Snapshot
	{
		internal Dictionary<int, Bitmap> Images;
		internal Dictionary<string, int> Names;
		internal Dictionary<string, int> Hotkeys;
		internal Dictionary<int, int> NativeIds;
		internal DateTime ReadUtc;
		internal DateTime ConfigWriteUtc;
		internal int AssemblyCount;
	}
	private static readonly Dictionary<string, Snapshot> Scenes = new Dictionary<string, Snapshot>();
	private static string _scene = "";
	internal static int ImageCount { get { lock (Sync) return _images.Count; } }
	internal static int Generation { get { lock (Sync) return _generation; } }

	internal static void Refresh(ISldWorks host, string scene)
	{
		if (host == null) return;
		var configWriteUtc = System.IO.File.GetLastWriteTimeUtc(ConfigStore.ConfigPath);
		int assemblyCount = AppDomain.CurrentDomain.GetAssemblies().Length;
		lock (Sync)
		{
			if (Scenes.TryGetValue(scene,out var cached) && cached.ConfigWriteUtc == configWriteUtc && cached.AssemblyCount == assemblyCount && DateTime.UtcNow-cached.ReadUtc < TimeSpan.FromSeconds(10))
			{
				_images=cached.Images;_names=cached.Names;_hotkeys=cached.Hotkeys;_nativeIds=cached.NativeIds;_scene=scene;
				return;
			}
		}
		_scene = scene;
		Dictionary<int, Bitmap> images = null;
		try
		{
			if (assemblyCount != _assemblyCount || DateTime.UtcNow >= _nextAssetRefresh)
			{
				var assets = ThirdPartyIconResources.ReadLoaded(host);
				lock (Sync) { foreach (var image in _namedAssets.Values) image.Dispose(); _namedAssets=assets; _assemblyCount=assemblyCount; _nextAssetRefresh=DateTime.UtcNow.AddMinutes(1); }
			}
			string hostPath = host.GetExecutablePath();
			if (System.IO.File.Exists(hostPath)) hostPath = System.IO.Path.GetDirectoryName(hostPath);
			bool classic = false;
			try { classic = host.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSystemColorsIconColor) == 1; }
			catch (Exception ex) { Logger.Trace("Host wheel icon color preference unavailable: " + ex.Message); }
			if (_resourceHost != hostPath || _resourceClassic != classic)
			{
				lock (Sync) { foreach (var image in _resources.Values) image.Dispose(); _resources.Clear(); _resourceHost = hostPath; _resourceClassic = classic; }
			}
			var entries = new List<ShortcutRegistryEntry>();
			bool shortcutsRead = false;
			try { entries = SolidWorksShortcutBackupService.ReadCurrent(host).Entries; shortcutsRead = true; }
			catch (Exception ex) { Logger.Trace("Wheel shortcut icon mapping: " + ex.Message); }
			var hotkeys = entries.Where(e => e.Collection.Equals("AddArray", StringComparison.OrdinalIgnoreCase))
				.GroupBy(e => HotkeyKey(SolidWorksShortcutBackupService.DecodeHotkey(e.Flags, e.Key)), StringComparer.OrdinalIgnoreCase)
				.Where(g => g.Select(e => e.CommandId).Distinct().Count() == 1)
				.ToDictionary(g => g.Key, g => g.First().CommandId, StringComparer.OrdinalIgnoreCase);
			AddDefaultHotkeys(hotkeys, entries, shortcutsRead);
			// Default accelerators need not appear in the custom AddArray registry.
			// Read the current native menu label, so removed/rebound shortcuts are
			// not silently replaced by hard-coded defaults.
			var nativeMenu = ExternalMenuCommandCatalog.Read(host);
			int documentType = (host.ActiveDoc as IModelDoc2)?.GetType() ?? 1;
			foreach (int command in new[]{0,1,2})
			{
				string parent;
				string name = NameKey(host.GetMenuStrings(command,documentType,out parent));
				foreach (var entry in nativeMenu.Where(e => NameKey(e.Name) == name && !string.IsNullOrWhiteSpace(e.Shortcut)))
					if (!hotkeys.ContainsKey(HotkeyKey(entry.Shortcut))) hotkeys[HotkeyKey(entry.Shortcut)] = command;
			}
			var requested = new HashSet<int>();
			var externalRequested = new HashSet<int>();
			var namedRequested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			void AddRequested(string kind,string value)
			{
				if (ExternalToolbarCommandReference.TryDecode(value,out int externalType,out int external) && externalType == documentType)
				{
					externalRequested.Add(external);
					string externalName = ExternalToolbarCommandReference.Name(value);
					if (externalName.Length > 0) namedRequested.Add(NameKey(externalName));
				}
				else if (kind == ActionKinds.NativeCommand && NativeCommandReference.TryDecodeId(value,out int command)) requested.Add(command);
				else if (kind == ActionKinds.Basic && NativeWheelIconMappings.TryGetBasicCommandId(value, out int basicCommand)) requested.Add(basicCommand);
				else if (kind == ActionKinds.ExternalPlugin && ExternalNamedCommandReference.TryDecode(value,out int namedType,out _,out string name) && namedType == documentType) namedRequested.Add(NameKey(name));
				else if (kind == ActionKinds.Hotkey && hotkeys.TryGetValue(HotkeyKey(value),out int shortcut) && shortcut >= 0) { if (shortcut < 0x8000) requested.Add(shortcut); else externalRequested.Add(shortcut); }
			}
			var config = ConfigStore.Load();
			var visibleMenus = config.Menus.TryGetValue(scene, out var visibleScene)
				? new[] { visibleScene } : config.Menus.Values.AsEnumerable();
			foreach (var menu in visibleMenus)
				foreach (var ring in menu.Rings)
					foreach (var item in ring.Items)
					{
						AddRequested(item.Kind,item.Value);
						// Imported/legacy scenes can deserialize a missing ComboCommands
						// field as null before ConfigStore has had a chance to normalize
						// the scene.  Icon refresh must remain read-only and tolerate that
						// state, otherwise one malformed cell prevents every icon from
						// refreshing.
						foreach (var step in item.ComboCommands ?? Enumerable.Empty<ComboCommandConfig>())
							if (step != null) AddRequested(step.Kind, step.Value);
					}
			// The supplied reference resolves original artwork before the wheel is
			// painted, and only for bindings that can actually appear in this scene.
			var missingResources = requested.Where(id => !_resources.ContainsKey(id) && (NativeWheelIconMappings.Resources.ContainsKey(id) || id == 501)).ToArray();
			if (missingResources.Length > 0)
			{
				var resources = SolidWorksIconResources.Read(hostPath, missingResources, classic);
				lock (Sync)
				{
					foreach (var entry in resources) _resources[entry.Key] = entry.Value;
				}
			}
			requested.ExceptWith(_resources.Keys);
			images = ToolbarExternalCommandCatalog.ReadIcons(host, requested, externalRequested, namedRequested, out var names, out var nativeIds);
			lock (Sync)
			{
				if (Scenes.TryGetValue(scene, out var old)) foreach (var image in old.Images.Values) image.Dispose();
				_images = images;
				_hotkeys = hotkeys;
				_names = names;
				_nativeIds = nativeIds;
				Scenes[scene] = new Snapshot { Images = images, Names = names, Hotkeys = hotkeys, NativeIds = nativeIds, ReadUtc=DateTime.UtcNow,ConfigWriteUtc=configWriteUtc,AssemblyCount=AppDomain.CurrentDomain.GetAssemblies().Length };
				_generation++;
				images = null;
			}
		}
		catch (Exception ex) { Logger.Trace("Native wheel icon scan: " + ex.Message); }
		finally { if (images != null) foreach (var image in images.Values) image.Dispose(); }
	}

	internal static void Clear()
	{
		lock (Sync)
		{
			foreach (var snapshot in Scenes.Values) foreach (var image in snapshot.Images.Values) image.Dispose();
			Scenes.Clear();
			_images.Clear();
			_hotkeys.Clear();
			_names.Clear();
			_nativeIds.Clear();
			foreach (var image in _resources.Values) image.Dispose();
			_resources.Clear(); _resourceHost = "";
			_generation++;
			UnifiedWheelArtwork.Clear();
			foreach (var image in _namedAssets.Values) image.Dispose();
			_namedAssets.Clear(); _assemblyCount = -1;
			_nextAssetRefresh = DateTime.MinValue;
			_scene = "";
		}
	}

	internal static void Draw(Graphics graphics, MenuItemConfig item, RectangleF bounds, string scene)
	{
		if (TryDrawExactNative(graphics, item, bounds, scene)) return;
		if (item.Kind == ActionKinds.Basic && UnifiedWheelArtwork.TryDraw(graphics, item.Value, bounds)) return;
		if (item.Kind == ActionKinds.ShortcutPalette)
		{
			using var pen = new Pen(Color.FromArgb(68,112,155), 1.2f);
			graphics.DrawRectangle(pen, bounds.X+1, bounds.Y+4, bounds.Width-2, bounds.Height-8);
			for (int row=0; row<2; row++) for (int col=0; col<5; col++) graphics.DrawRectangle(pen,bounds.X+4+col*3.5f,bounds.Y+8+row*3,1,1);
			graphics.DrawLine(pen,bounds.X+7,bounds.Bottom-6,bounds.Right-7,bounds.Bottom-6);
			return;
		}
		if (item.Kind == ActionKinds.Combo)
		{
			var step = item.ComboCommands?.FirstOrDefault(s => s.Kind != ActionKinds.Delay && s.Kind != ActionKinds.Combo && !string.IsNullOrWhiteSpace(s.Value));
			if (step != null) Draw(graphics, new MenuItemConfig { Label = step.Label, Kind = step.Kind, Value = step.Value }, bounds, scene);
			else DrawFallback(graphics, item, bounds);
			using var badge = new SolidBrush(Color.FromArgb(68,112,155));
			graphics.FillRectangle(badge, bounds.Right - 9, bounds.Bottom - 7, 9, 7);
			using var line = new Pen(Color.White, 1);
			graphics.DrawLine(line, bounds.Right-7,bounds.Bottom-5,bounds.Right-4,bounds.Bottom-5);
			graphics.DrawLine(line, bounds.Right-5,bounds.Bottom-3,bounds.Right-2,bounds.Bottom-3);
			return;
		}
		lock (Sync)
		{
			Scenes.TryGetValue(scene, out var snapshot);
			var images = snapshot?.Images ?? EmptyImages;
			var names = snapshot?.Names ?? EmptyNames;
			var hotkeys = snapshot?.Hotkeys ?? EmptyNames;
			var nativeIds = snapshot?.NativeIds ?? EmptyIds;
			int id = -1;
			bool native = false;
			string boundName = "";
			string boundGroup = "";
			int current = scene == SceneKeys.Drawing ? 3 : scene == SceneKeys.Assembly ? 2 : 1;
			if (item.Kind == ActionKinds.Basic && NativeWheelIconMappings.TryGetBasicCommandId(item.Value, out int basicId)) { id = basicId; native = true; }
			else if (ExternalToolbarCommandReference.TryDecode(item.Value, out int externalDocumentType, out int externalId))
			{
				// Toolbar references can be captured as an ExternalPlugin step or
				// remain in legacy NativeCommand cells. Keep the stored command ID
				// authoritative for both forms and verify the document environment.
				if (externalDocumentType == current)
				{
					id = externalId;
					boundName = ExternalToolbarCommandReference.Name(item.Value);
				}
			}
			else if (item.Kind == ActionKinds.NativeCommand)
			{
				native = NativeCommandReference.TryDecodeId(item.Value, out id);
			}
			else if (item.Kind == ActionKinds.Hotkey)
			{
				bool found = hotkeys.TryGetValue(HotkeyKey(item.Value), out id);
				if (!found) id = -1;
				native = found && id >= 0 && id < 0x8000;
			}
			else if (item.Kind == ActionKinds.QinJian && QinJianActionValue.TryParse(item.Value, out string qinJianTab, out string qinJianName))
			{
				boundGroup = qinJianTab;
				boundName = qinJianName;
			}
			else if (item.Kind == ActionKinds.ExternalPlugin && ExternalPluginActionValue.TryParse(item.Value, out string externalTab, out string externalName))
			{
				boundGroup = externalTab;
				boundName = externalName;
			}
			else if (item.Kind == ActionKinds.ExternalPlugin && ExternalNamedCommandReference.TryDecode(item.Value,out int namedType,out string category,out string name) && namedType == current)
			{
				if (ToolbarExternalCommandCatalog.IsRemovedCommand(-1, name)) { DrawFallback(graphics, item, bounds); return; }
				boundGroup = category;
				boundName=name;
			}
			if (native && _resources.TryGetValue(id, out var resource)) { WheelIconDrawing.Draw(graphics, resource, bounds); return; }
			if (native) id = nativeIds.TryGetValue(id, out int toolbarId) ? toolbarId : -1;
			if (ToolbarExternalCommandCatalog.IsRemovedCommand(id, boundName)) { DrawFallback(graphics, item, bounds); return; }
			// Captions are user-editable. Resolve images from the stored command
			// identity or the live toolbar metadata, never from the display label.
			if (boundName.Length == 0 && id > 0) boundName = names.FirstOrDefault(pair => pair.Value == id).Key ?? "";
			if (!native && id > 0 && boundName.Length == 0) ToolbarExternalCommandCatalog.TryResolveMappedName(current,id,out boundName);
			if (boundName.Length > 0 && names.TryGetValue(NameKey(boundName),out int namedId) && namedId <= 0) {DrawFallback(graphics,item,bounds);return;}
			if (!native && boundName.Length > 0 && _namedAssets.TryGetValue(ThirdPartyIconResources.AssetName(boundName), out var named)) { WheelIconDrawing.Draw(graphics, named, bounds); return; }
			if (!native && boundGroup.Length > 0 && _namedAssets.TryGetValue(ThirdPartyIconResources.AssetName(boundGroup), out var groupIcon)) { WheelIconDrawing.Draw(graphics, groupIcon, bounds); return; }
			if (!native && boundName.Length > 0)
			{
				if (names.TryGetValue(NameKey(boundName), out int verifiedId)) id = verifiedId;
				else if (names.Any(pair => pair.Value == id)) id = -1; // A live identity disagrees with the saved mapping.
			}
			if (id > 0 && images.TryGetValue(id, out Bitmap image))
			{
				WheelIconDrawing.Draw(graphics, image, bounds);
				return;
			}
		}
		if (item.Kind == ActionKinds.Basic
			&& WheelIconCatalog.TryGetBasicActionIconId(item.Value, out string basicIconId))
		{
			// Keep the source icon within the catalogue's supported range.  Small
			// cells are common on dense rings and the previous 1..15 px request
			// threw during painting, leaving WinForms' red error-X in the preview.
			int sourceSize = Math.Max(16, Math.Min(1024, (int)Math.Ceiling(Math.Min(bounds.Width, bounds.Height))));
			using var icon = WheelIconCatalog.Render(basicIconId, sourceSize);
			if (icon != null) { WheelIconDrawing.Draw(graphics, icon, bounds); return; }
		}
		DrawFallback(graphics, item, bounds);
	}

	// An old automatically assigned catalogue icon must not hide the actual
	// command artwork. Only stored command identities participate; labels and
	// catalogue ids are deliberately absent from this lookup.
	internal static bool TryDrawExactNative(Graphics graphics, MenuItemConfig item, RectangleF bounds, string scene)
	{
		int command = -1;
		bool found = item.Kind == ActionKinds.NativeCommand
			? NativeCommandReference.TryDecodeId(item.Value, out command)
			: item.Kind == ActionKinds.Basic && NativeWheelIconMappings.TryGetBasicCommandId(item.Value, out command);
		if (!found) return false;
		lock (Sync)
		{
			if (_resources.TryGetValue(command, out var resource))
			{
				WheelIconDrawing.Draw(graphics, resource, bounds);
				return true;
			}
			if (Scenes.TryGetValue(scene, out var snapshot)
				&& snapshot.NativeIds.TryGetValue(command, out int toolbar)
				&& snapshot.Images.TryGetValue(toolbar, out var image))
			{
				WheelIconDrawing.Draw(graphics, image, bounds);
				return true;
			}
		}
		return false;
	}

	internal static void AddDefaultHotkeys(Dictionary<string,int> hotkeys, List<ShortcutRegistryEntry> entries, bool readSucceeded)
	{
		if (!readSucceeded) return;
		// SOLIDWORKS factory accelerators are absent from AddArray. SubArray
		// removes defaults; any explicit binding (including conflicts) takes priority.
		foreach (var pair in new[]{new KeyValuePair<string,int>("Ctrl+O",0),new KeyValuePair<string,int>("Ctrl+N",1),new KeyValuePair<string,int>("Ctrl+S",2)})
			if (!hotkeys.ContainsKey(pair.Key) && !entries.Any(e => HotkeyKey(SolidWorksShortcutBackupService.DecodeHotkey(e.Flags,e.Key)).Equals(pair.Key,StringComparison.OrdinalIgnoreCase))) hotkeys[pair.Key]=pair.Value;
	}

	internal static string NameKey(string name) => System.Text.RegularExpressions.Regex.Replace((name ?? ""), @"[（(]&?[A-Za-z][）)]", "").Replace("&", "").Replace(".", "").Replace("…", "").Replace("[", "").Replace("]", "").Replace(" ", "").Trim();

	internal static string HotkeyKey(string value)
	{
		string key = System.Text.RegularExpressions.Regex.Replace((value ?? "").Replace(" ", ""), "Control", "Ctrl", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
		return System.Text.RegularExpressions.Regex.Replace(key, @"(?i)(?<=\+)Plus$", "+");
	}

	// A neutral action-type symbol means no verified command icon was available.
	// Never infer a different native feature from a user-editable display label.
    internal static void DrawFallback(Graphics g, MenuItemConfig item, RectangleF bounds)
    {
        // Missing is explicit; a keyboard/document/info symbol is not a feature icon.
        using var pen = new Pen(Color.FromArgb(158,166,173), 1f) { DashStyle = DashStyle.Dot };
        g.DrawRectangle(pen, bounds.X+2, bounds.Y+2, bounds.Width-4, bounds.Height-4);
        using var font = new Font("Segoe UI", Math.Max(8, bounds.Height * 0.62f), FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.FromArgb(126,139,150));
        using var format = new StringFormat { Alignment=StringAlignment.Center, LineAlignment=StringAlignment.Center };
        g.DrawString("?",font,brush,bounds,format);
    }
}
