using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using SolidWorks.Interop.swcommands;

namespace XY.SolidWorksPlugin;

// Read the installed host's named PNG resources. No host binaries are copied or
// executed; each loaded resource module is released after the small images are copied.
internal static class SolidWorksIconResources
{
	private static readonly Dictionary<string, byte[]> WpfImages = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
	private static string _wpfHost = "";
	private static readonly Dictionary<string, string> WpfKeys = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["swCommands_IntersectionCurve"] = "resources/pmicons/i_sketchintersectioncurve_20.png",
		["swCommands_ConstructionGeometry"] = "resources/pmicons/i_sketchconstructiongeometry_20.png",
		["swCommands_SplitEntities"] = "resources/pmicons/i_sketchsplitentities_20.png",
		["swCommands_ConvertEntities"] = "resources/pmicons/i_sketchconstructiongeometry_16.png",
		["swCommands_JogLine"] = "resources/pmicons/I_RouteJogLine_16.png"
	};
	[DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
	[DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr FindResource(IntPtr module, string name, string type);
	[DllImport("kernel32.dll")] private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
	[DllImport("kernel32.dll")] private static extern IntPtr LockResource(IntPtr resource);
	[DllImport("kernel32.dll")] private static extern uint SizeofResource(IntPtr module, IntPtr resource);
	[DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
	internal static Dictionary<int, Bitmap> Read(string hostDirectory, IEnumerable<int> requested = null, bool classic = false)
	{
		var result = new Dictionary<int, Bitmap>();
		var wanted = new HashSet<int>(requested ?? NativeWheelIconMappings.Resources.Keys);
		IntPtr blue = Open(Path.Combine(hostDirectory, "swStyleBlueu.dll"));
		IntPtr color = classic ? Open(Path.Combine(hostDirectory, "swStyleColoru.dll")) : IntPtr.Zero;
		try
		{
			foreach (int id in wanted)
			{
				Bitmap image = null;
				if (NativeWheelIconMappings.Resources.TryGetValue(id, out string name))
				{
					image = ReadBest(color, name) ?? ReadBest(blue, name);
					if (image == null) image = ReadWpf(hostDirectory, id);
				}
				if (image != null) result[id] = image;
			}
			if (wanted.Contains(501) && !result.ContainsKey(501))
			{
				string material = Path.Combine(hostDirectory, "data", "resources", "sldcostcommon", "sldtaskpane", "i_standardeditmaterial_20.png");
				if (File.Exists(material)) { using var image = Image.FromFile(material); result[501] = Copy(image); }
			}
		}
		catch { foreach (var image in result.Values) image.Dispose(); throw; }
		finally { if (blue != IntPtr.Zero) FreeLibrary(blue); if (color != IntPtr.Zero) FreeLibrary(color); }
		return result;
	}

	private static IntPtr Open(string path) => File.Exists(path) ? LoadLibraryEx(path, IntPtr.Zero, 2) : IntPtr.Zero;

	internal static IEnumerable<string> PngCandidates(string name)
	{
		if (string.IsNullOrWhiteSpace(name)) yield break;
		var match = Regex.Match(name, @"_(20|24|32|40|48|64|96|100|128|256)$", RegexOptions.CultureInvariant);
		if (match.Success)
		{
			string prefix = name.Substring(0, match.Index);
			foreach (int size in new[] { 256, 128, 100, 96, 64, 48, 40, 32, 24, 20 }) yield return prefix + "_" + size;
			yield return prefix;
		}
		else yield return name;
	}

	private static Bitmap ReadBest(IntPtr module, string name)
	{
		if (module == IntPtr.Zero) return null;
		foreach (string candidate in PngCandidates(name))
		{
			IntPtr resource = FindResource(module, candidate, "PNG");
			uint size = resource == IntPtr.Zero ? 0 : SizeofResource(module, resource);
			if (size == 0 || size > 1024 * 1024) continue;
			IntPtr data = LockResource(LoadResource(module, resource));
			if (data == IntPtr.Zero) continue;
			var bytes = new byte[size]; Marshal.Copy(data, bytes, 0, bytes.Length);
			try { using var stream = new MemoryStream(bytes); using var image = Image.FromStream(stream); return Copy(image); }
			catch (Exception ex) when (ex is ArgumentException || ex is ExternalException || ex is OutOfMemoryException) { }
		}
		return null;
	}

	private static Bitmap Copy(Image image)
	{
		return WheelIconDrawing.CopySource(image);
	}

	private static Bitmap ReadWpf(string directory, int id)
	{
		string api = Enum.GetName(typeof(swCommands_e), id);
		if (api == null || !WpfKeys.TryGetValue(api, out string key)) return null;
		if (!string.Equals(_wpfHost, directory, StringComparison.OrdinalIgnoreCase))
		{
			WpfImages.Clear(); _wpfHost = directory;
			string path = Path.Combine(directory, "WPFRes.dll");
			try
			{
				if (!File.Exists(path)) return null;
				using var stream = Assembly.LoadFrom(path).GetManifestResourceStream("WPFRes.g.resources");
				if (stream == null) return null;
				using var reader = new ResourceReader(stream);
				IDictionaryEnumerator entries = reader.GetEnumerator();
				var wanted = new HashSet<string>(WpfKeys.Values, StringComparer.OrdinalIgnoreCase);
				while (entries.MoveNext())
				{
					if (!(entries.Key is string name) || !wanted.Contains(name) || !(entries.Value is Stream content) || content.Length <= 0 || content.Length > 1024 * 1024) continue;
					using var buffer = new MemoryStream(); content.Position = 0; content.CopyTo(buffer); WpfImages[name] = buffer.ToArray();
				}
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is BadImageFormatException || ex is ArgumentException)
			{ Logger.Trace("Host WPF wheel artwork unavailable: " + ex.Message); }
		}
		if (!WpfImages.TryGetValue(key, out var bytes)) return null;
		try { using var stream = new MemoryStream(bytes); using var image = Image.FromStream(stream); return Copy(image); }
		catch (Exception ex) when (ex is ArgumentException || ex is ExternalException || ex is OutOfMemoryException) { return null; }
	}
}
