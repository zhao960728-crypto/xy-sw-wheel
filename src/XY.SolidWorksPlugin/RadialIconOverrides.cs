using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;

namespace XY.SolidWorksPlugin;

internal sealed class RadialIconOverrides : IDisposable
{
	internal sealed class Entry
	{
		public string Scene { get; set; }
		public int Ring { get; set; }
		public int Slot { get; set; }
		public string Kind { get; set; }
		public string Value { get; set; }
		public string Image { get; set; }
	}
	internal sealed class Document
	{
		public int Version { get; set; } = 1;
		public List<Entry> Icons { get; set; } = new List<Entry>();
	}
	private readonly string _path;
	private DateTime _stamp = DateTime.MinValue;
	private long _length = -1;
	private Dictionary<IconKey, Bitmap> _images = new Dictionary<IconKey, Bitmap>();
	internal RadialIconOverrides(string path = null)
	{
		_path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XY", "XY轮盘", "radial-icons.json");
	}
	// The persisted JSON stays unchanged; painting only needs an exact lookup.
	private readonly struct IconKey : IEquatable<IconKey>
	{
		private readonly string _scene, _kind, _value;
		private readonly int _ring, _slot;
		internal IconKey(string scene, int ring, int slot, string kind, string value)
		{ _scene = scene; _ring = ring; _slot = slot; _kind = kind; _value = value; }
		public bool Equals(IconKey other) => _ring == other._ring && _slot == other._slot
			&& string.Equals(_scene, other._scene, StringComparison.Ordinal)
			&& string.Equals(_kind, other._kind, StringComparison.Ordinal)
			&& string.Equals(_value, other._value, StringComparison.Ordinal);
		public override bool Equals(object value) => value is IconKey other && Equals(other);
		public override int GetHashCode()
		{
			unchecked
			{
				int hash = _scene?.GetHashCode() ?? 0;
				hash = hash * 397 ^ _ring; hash = hash * 397 ^ _slot;
				hash = hash * 397 ^ (_kind?.GetHashCode() ?? 0);
				return hash * 397 ^ (_value?.GetHashCode() ?? 0);
			}
		}
	}
	internal void Reload()
	{
		try
		{
			var info = new FileInfo(_path);
			DateTime stamp = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
			long length = info.Exists ? info.Length : 0;
			if (_stamp == stamp && _length == length) return;
			if (length > 1024 * 1024) throw new InvalidDataException("Icon configuration exceeds 1 MB.");
			var next = new Dictionary<IconKey, Bitmap>();
			try
			{
				var document = info.Exists ? new JavaScriptSerializer().Deserialize<Document>(File.ReadAllText(_path)) : new Document();
				if (document == null || document.Version != 1) throw new InvalidDataException("Unsupported icon configuration.");
				foreach (var entry in document.Icons ?? new List<Entry>())
				{
					if (entry == null || next.Count >= 512 || string.IsNullOrWhiteSpace(entry.Image)) continue;
					try
					{
						string directory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_path), "RadialIcons")) + Path.DirectorySeparatorChar;
						string imagePath = Path.GetFullPath(Path.Combine(directory, entry.Image));
						if (!imagePath.StartsWith(directory, StringComparison.OrdinalIgnoreCase) || !File.Exists(imagePath) || new FileInfo(imagePath).Length > 8 * 1024 * 1024) continue;
						using var stream = File.OpenRead(imagePath);
						using var image = Image.FromStream(stream);
						if (image.Width > 2048 || image.Height > 2048) continue;
						var bitmap = WheelIconDrawing.CopySource(image);
						var key = new IconKey(entry.Scene, entry.Ring, entry.Slot, entry.Kind, entry.Value);
						if (next.TryGetValue(key, out var old)) old.Dispose();
						next[key] = bitmap;
					}
					catch (Exception ex) { Logger.Trace("Custom wheel icon skipped: " + ex.Message); }
				}
				Dispose(); _images = next; _stamp = stamp; _length = length;
			}
			catch { foreach (var image in next.Values) image.Dispose(); throw; }
		}
		catch (Exception ex) { Logger.Trace("Custom wheel icon configuration: " + ex.Message); }
	}
	internal bool Draw(Graphics graphics, string scene, int ring, MenuItemConfig item, RectangleF bounds)
	{
		if (_images.Count == 0 || !_images.TryGetValue(new IconKey(scene, ring, item.Slot, item.Kind, item.Value), out var image)) return false;
		float scale = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
		WheelIconDrawing.Draw(graphics, image, new RectangleF(bounds.X + (bounds.Width - image.Width * scale) / 2,
			bounds.Y + (bounds.Height - image.Height * scale) / 2, image.Width * scale, image.Height * scale));
		return true;
	}
	public void Dispose() { foreach (var image in _images.Values) image.Dispose(); _images.Clear(); }
}
