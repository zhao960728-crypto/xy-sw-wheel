using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace XY.SolidWorksPlugin;

internal static class ConfigStore
{
	private static readonly object Sync = new object();

	private static PluginConfig? _cached;

	private static DateTime _cachedWriteUtc;

	public static string BaseDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XY", "XY轮盘");

	public static string ConfigPath => Path.Combine(BaseDirectory, "solidworks-self-plugin.json");

	public static string AssetDirectory => Path.Combine(BaseDirectory, "assets");

	public static PluginConfig Load()
	{
		lock (Sync) return LoadCore();
	}

	private static PluginConfig LoadCore()
	{
		Directory.CreateDirectory(BaseDirectory);
		DateTime dateTime = (File.Exists(ConfigPath) ? File.GetLastWriteTimeUtc(ConfigPath) : DateTime.MinValue);
		lock (Sync)
		{
			if (_cached != null && dateTime == _cachedWriteUtc)
			{
				return Clone(_cached);
			}
		}
		PluginConfig config;
		if (File.Exists(ConfigPath))
		{
			try
			{
				config = ReadConfigFile(ConfigPath);
			}
			catch (Exception ex)
			{
				Logger.Error("Config load failed", ex);
				File.Copy(ConfigPath, ConfigPath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), overwrite: false);
				config = DefaultConfig();
			}
		}
		else
		{
			config = DefaultConfig();
			Save(config);
		}
		Normalize(config);
		lock (Sync)
		{
			_cached = Clone(config);
			_cachedWriteUtc = (File.Exists(ConfigPath) ? File.GetLastWriteTimeUtc(ConfigPath) : DateTime.MinValue);
		}
		return Clone(config);
	}

	public static void Save(PluginConfig config)
	{
		lock (Sync)
		{
			Normalize(config);
			Directory.CreateDirectory(BaseDirectory);
			PersistSceneAssets(config);
			string contents = Serialize(config);
			PluginConfig snapshot = Deserialize(contents);
			WriteConfigFile(ConfigPath, contents);
			_cached = snapshot;
			_cachedWriteUtc = File.GetLastWriteTimeUtc(ConfigPath);
		}
	}

	internal static string SaveWithBackup(PluginConfig config, string reason)
	{
		lock (Sync) return SaveWithBackupCore(config, reason);
	}

	private static string SaveWithBackupCore(PluginConfig config, string reason)
	{
		Directory.CreateDirectory(BaseDirectory);
		if (!File.Exists(ConfigPath))
		{
			Save(config);
			return "";
		}
		string safeReason = new string((reason ?? "migration").Where(char.IsLetterOrDigit).Take(32).ToArray());
		if (safeReason.Length == 0) safeReason = "migration";
		string backupPath = ConfigPath + ".backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + "-" + safeReason;
		File.Copy(ConfigPath, backupPath, overwrite: false);
		try
		{
			Save(config);
			return backupPath;
		}
		catch (Exception saveException)
		{
			try
			{
				WriteConfigFile(ConfigPath, File.ReadAllText(backupPath, Encoding.UTF8));
				lock (Sync)
				{
					_cached = null;
					_cachedWriteUtc = DateTime.MinValue;
				}
			}
			catch (Exception restoreException)
			{
				throw new AggregateException("配置迁移保存失败，自动恢复也失败；请从备份恢复：" + backupPath, saveException, restoreException);
			}
			throw;
		}
	}

	internal static void WriteConfigFile(string path, string contents)
	{
		lock (Sync)
		{
			string fullPath = Path.GetFullPath(path);
			Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
			// Serialize local readers/writers and replace from the same directory.
			// Never truncate the user's working configuration before writing succeeds.
			string temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
			try
			{
				using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				using (var writer = new StreamWriter(stream, Encoding.UTF8))
				{
					writer.Write(contents);
					writer.Flush();
					stream.Flush(flushToDisk: true);
				}
				if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null);
				else File.Move(temporary, fullPath);
			}
			finally
			{
				try { if (File.Exists(temporary)) File.Delete(temporary); }
				catch (Exception ex) { Logger.Trace("Temporary config cleanup failed: " + ex.Message); }
			}
		}
	}

	internal static PluginConfig ReadConfigFile(string path)
	{
		lock (Sync) return Deserialize(File.ReadAllText(path, Encoding.UTF8));
	}

	internal static void Normalize(PluginConfig config)
	{
		UserMacroStore.NormalizeConfig(config);
		config.TriggerMode = TriggerModes.Normalize(config.TriggerMode);
		config.HoldDelayMs = Math.Max(150, Math.Min(2000, config.HoldDelayMs));
		config.MenuSizePx = Math.Max(260, Math.Min(900, config.MenuSizePx));
		config.CenterRadiusPx = Math.Max(20, Math.Min(config.MenuSizePx / 3, config.CenterRadiusPx));
		config.FontSizePx = Math.Max(8, Math.Min(28, config.FontSizePx));
		config.FontName = NormalizeFontName(config.FontName, "SimSun");
		config.OpacityPercent = Math.Max(20, Math.Min(100, config.OpacityPercent));
		config.ComboAdvanceKey = ComboAdvanceKeys.Normalize(config.ComboAdvanceKey);
		PropertyWatermarkConfig.Normalize(config.PropertyWatermark);
		config.ProcessRecognition ??= new ProcessRecognitionConfig();
		ProcessRecognitionConfig.Normalize(config.ProcessRecognition);
		if (config.Menus == null)
		{
			Dictionary<string, SceneMenuConfig> dictionary = (config.Menus = new Dictionary<string, SceneMenuConfig>(StringComparer.OrdinalIgnoreCase));
		}
		string[] all = SceneKeys.All;
		foreach (string key in all)
		{
			if (!config.Menus.TryGetValue(key, out SceneMenuConfig value) || value == null)
			{
				config.Menus[key] = DefaultScene(key);
				continue;
			}
			value.Key = key;
			value.StyleId = WheelLayoutStyles.NormalizeStyleId(value.StyleId);
			value.BackgroundImagePath = (value.BackgroundImagePath ?? "").Trim();
			value.BackgroundOpacityPercent = Math.Max(0, Math.Min(100, value.BackgroundOpacityPercent));
			value.SurfaceOpacityPercent = Math.Max(5, Math.Min(100, value.SurfaceOpacityPercent > 0 ? value.SurfaceOpacityPercent : 65));
			// Legacy scenes saved an explicit "IconAndText"; keep it. New scenes
			// start from "Auto" so the drawing picks the best fit per cell.
			value.ContentMode = NormalizeCellContentMode(value.ContentMode, "Auto", allowInherit: false);
			// A scene saved before the surface colours went live still holds the
			// old placeholder values. It adopts the current default look instead
			// of staying on a stale disc the user cannot tell apart from before.
			// The default is the CAD palette: this wheel is expected to read the
			// same as its CAD counterpart, and anything else is a choice the
			// user makes in the palette dialog.
			if (value.Appearance == null || value.Appearance.IsLegacyDefault()
				|| value.Appearance.IsUntouchedDarkGlass())
			{
				value.Appearance = WheelAppearance.Classic();
			}
			value.Appearance.Normalize();
			value.MenuSizePx = Math.Max(260, Math.Min(900, value.MenuSizePx > 0 ? value.MenuSizePx : config.MenuSizePx));
			value.CenterRadiusPx = Math.Max(20, Math.Min(value.MenuSizePx / 3, value.CenterRadiusPx > 0 ? value.CenterRadiusPx : config.CenterRadiusPx));
			SceneMenuConfig sceneMenuConfig = value;
			if (sceneMenuConfig.Rings == null)
			{
				List<RingConfig> list = (sceneMenuConfig.Rings = new List<RingConfig>());
			}
			if (sceneMenuConfig.UnassignedItems == null)
			{
				List<MenuItemConfig> list2 = (sceneMenuConfig.UnassignedItems = new List<MenuItemConfig>());
			}
			if (value.Rings.Count == 0)
			{
				value.Rings.Add(new RingConfig
				{
					Index = 0,
					Slots = 8
				});
			}
			value.UnassignedItems = value.UnassignedItems
				.Where((MenuItemConfig item) => item != null && (IsValidMenuItem(item) || ProductFeatureScope.IsRetiredBinding(item)))
				.ToList();
			foreach (MenuItemConfig unassigned in value.UnassignedItems)
			{
				NormalizeMenuItem(unassigned);
			}
			value.Rings = value.Rings.OrderBy((RingConfig r) => r.Index).Take(RingConfig.MaximumRingCount).Select(delegate(RingConfig ringConfig2, int index)
			{
				ringConfig2.Index = index;
				return ringConfig2;
			})
				.ToList();
			foreach (RingConfig ring in value.Rings)
			{
				ring.Slots = Math.Max(1, Math.Min(32, ring.Slots));
				ring.SizeWeight = Math.Max(1, Math.Min(12, ring.SizeWeight));
				ring.Name = Limit(ring.Name, 32);
				ring.NodeInnerSizePercent = Math.Max(40, Math.Min(160, ring.NodeInnerSizePercent > 0 ? ring.NodeInnerSizePercent : 100));
				ring.NodeOuterSizePercent = Math.Max(40, Math.Min(160, ring.NodeOuterSizePercent > 0 ? ring.NodeOuterSizePercent : 100));
				RingConfig ringConfig = ring;
				if (ringConfig.Items == null)
				{
					List<MenuItemConfig> list3 = (ringConfig.Items = new List<MenuItemConfig>());
				}
				ring.Items = (from item in ring.Items
					where item != null && item.Slot >= 0 && item.Slot < ring.Slots
					where IsValidMenuItem(item) || ProductFeatureScope.IsRetiredBinding(item)
					group item by item.Slot into @group
					select @group.Last() into item
					orderby item.Slot
					select item).ToList();
				foreach (MenuItemConfig item in ring.Items)
				{
					NormalizeMenuItem(item);
				}
			}
			RingDiameterLayout.Normalize(value);
		}
		foreach (string item in config.Menus.Keys.Where((string value2) => !SceneKeys.All.Contains<string>(value2, StringComparer.OrdinalIgnoreCase)).ToList())
		{
			config.Menus.Remove(item);
		}
		if (config.GlobalComboAdvanceEnabled)
		{
			foreach (MenuItemConfig combo in config.Menus.Values
				.SelectMany(scene => scene.Rings)
				.SelectMany(ring => ring.Items)
				.Where(item => string.Equals(item.Kind, ActionKinds.Combo, StringComparison.OrdinalIgnoreCase)))
			{
				combo.ComboAdvanceEnabled = true;
				combo.ComboAdvanceKey = config.ComboAdvanceKey;
			}
		}
		ProductFeatureScope.NormalizeConfig(config);
	}

	private static PluginConfig DefaultConfig()
	{
		PluginConfig pluginConfig = new PluginConfig();
		string[] all = SceneKeys.All;
		foreach (string key in all)
		{
			pluginConfig.Menus[key] = DefaultScene(key);
		}
		return pluginConfig;
	}

	private static SceneMenuConfig DefaultScene(string key)
	{
		SceneMenuConfig obj = new SceneMenuConfig
		{
			Key = key,
			MenuSizePx = 520,
			CenterRadiusPx = 44
		};
		RingConfig ringConfig = new RingConfig
		{
			Index = 0,
			Slots = 8,
			Enabled = true
		};
		obj.Rings.Add(ringConfig);
		return obj;
	}

	internal static PluginConfig Clone(PluginConfig config)
	{
		return Deserialize(Serialize(config));
	}

	internal static string Serialize(PluginConfig config)
	{
		var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
		var root = (IDictionary<string, object>)serializer.DeserializeObject(serializer.Serialize(config));
		foreach (var pair in config.UnknownSettings)
			if (!root.Keys.Any(key => string.Equals(key, pair.Key, StringComparison.OrdinalIgnoreCase))) root[pair.Key] = pair.Value;
		return serializer.Serialize(root);
	}

	internal static PluginConfig Deserialize(string json)
	{
		var serializer = new JavaScriptSerializer
		{
			MaxJsonLength = int.MaxValue
		};
		PluginConfig config = serializer.Deserialize<PluginConfig>(json) ?? DefaultConfig();
		// Old configurations may own custom groups but predate type bindings.
		// Constructor presets belong to a new section, not to omitted fields in
		// an existing section; otherwise they can reference absent group names.
		var root = serializer.DeserializeObject(json) as IDictionary<string, object>;
		var known = new HashSet<string>(typeof(PluginConfig).GetProperties().Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
		if (root != null)
			foreach (var pair in root.Where(pair => !known.Contains(pair.Key))) config.UnknownSettings[pair.Key] = pair.Value;
		var processNode = root?.FirstOrDefault(pair => string.Equals(pair.Key, nameof(PluginConfig.ProcessRecognition), StringComparison.OrdinalIgnoreCase)).Value as IDictionary<string, object>;
		if (processNode != null && config.ProcessRecognition != null && !processNode.Keys.Any(key => string.Equals(key, nameof(ProcessRecognitionConfig.ProcessTypeGroups), StringComparison.OrdinalIgnoreCase)))
			config.ProcessRecognition.ProcessTypeGroups = "";
		return config;
	}

	private static string UniqueAssetName(string path)
	{
		string extension = Path.GetExtension(path);
		if (string.IsNullOrWhiteSpace(extension))
		{
			extension = ".dat";
		}
		using SHA256 sha = SHA256.Create();
		byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant()));
		string hash = BitConverter.ToString(bytes, 0, 8).Replace("-", "").ToLowerInvariant();
		string name = SanitizeFileName(Path.GetFileNameWithoutExtension(path));
		if (name.Length > 32)
		{
			name = name.Substring(0, 32);
		}
		return name + "-" + hash + extension.ToLowerInvariant();
	}

	private static string SanitizeFileName(string value)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		string text = new string((value ?? "asset").Select((char c) => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
		return string.IsNullOrWhiteSpace(text) ? "asset" : text;
	}

	private static string NormalizeColor(string value)
	{
		string text = (value ?? "").Trim();
		if (text.Length == 0)
		{
			return "";
		}
		try
		{
			System.Drawing.Color color = System.Drawing.ColorTranslator.FromHtml(text);
			return System.Drawing.ColorTranslator.ToHtml(System.Drawing.Color.FromArgb(color.R, color.G, color.B));
		}
		catch
		{
			return "";
		}
	}

	private static string NormalizeFontName(string value, string fallback)
	{
		string fontName = (value ?? "").Trim();
		return fontName.Length == 0 ? fallback : fontName;
	}

	private static void PersistSceneAssets(PluginConfig config)
	{
		try
		{
			Directory.CreateDirectory(AssetDirectory);
			string assetRoot = Path.GetFullPath(AssetDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			foreach (SceneMenuConfig scene in config.Menus.Values)
			{
				string path = (scene.BackgroundImagePath ?? "").Trim();
				if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
				{
					continue;
				}
				string fullPath = Path.GetFullPath(path);
				if (fullPath.StartsWith(assetRoot, StringComparison.OrdinalIgnoreCase))
				{
					scene.BackgroundImagePath = fullPath;
					continue;
				}
				string target = Path.Combine(AssetDirectory, UniqueAssetName(fullPath));
				File.Copy(fullPath, target, overwrite: true);
				scene.BackgroundImagePath = target;
			}
		}
		catch (Exception ex)
		{
			Logger.Error("Persist scene assets failed", ex);
		}
	}

	private static bool IsValidMenuItem(MenuItemConfig item)
	{
		string kind = (item.Kind ?? ActionKinds.Hotkey).Trim();
		if (kind.Equals(ActionKinds.Combo, StringComparison.OrdinalIgnoreCase))
		{
			return item.ComboCommands != null && item.ComboCommands.Any(IsValidComboCommand);
		}
		if (kind.Equals(ActionKinds.Basic, StringComparison.OrdinalIgnoreCase))
		{
			return BasicActions.Contains(item.Value);
		}
		return !string.IsNullOrWhiteSpace(item.Value);
	}

	// Steps are shared by every kind: a plain cell can now hold the sequence it
	// recorded, so the same editor and executor serve single functions and combos.
	private static void NormalizeComboCommands(MenuItemConfig item)
	{
		if (item.ComboCommands == null)
		{
			item.ComboCommands = new List<ComboCommandConfig>();
		}
		item.ComboCommands = item.ComboCommands
			.Where(IsValidComboCommand)
			.Select(delegate(ComboCommandConfig command)
			{
				command.Label = (command.Label ?? "").Trim();
				command.Kind = NormalizeCommandKind(command.Kind);
				command.Value = (command.Value ?? "").Trim();
				command.WaitMode = ComboWaitModes.Normalize(command.WaitMode);
				command.AdvanceKey = string.IsNullOrWhiteSpace(command.AdvanceKey) ? "" : ComboAdvanceKeys.Normalize(command.AdvanceKey);
				command.OnError = ComboErrorPolicies.Normalize(command.OnError);
				command.DelayMs = Math.Max(0, Math.Min(600000, command.DelayMs));
				command.TimeoutMs = Math.Max(0, Math.Min(600000, command.TimeoutMs));
				command.RetryCount = Math.Max(0, Math.Min(5, command.RetryCount));
				return command;
			})
			.ToList();
		if (string.IsNullOrWhiteSpace(item.Value)
			&& string.Equals(item.Kind, ActionKinds.Combo, StringComparison.OrdinalIgnoreCase))
		{
			item.Value = string.Join(" -> ", item.ComboCommands.Select((ComboCommandConfig command) => string.IsNullOrWhiteSpace(command.Label) ? command.Value : command.Label));
		}
	}

	private static void NormalizeMenuItem(MenuItemConfig item)
	{
		item.Label = (item.Label ?? "").Trim();
		item.Kind = NormalizeCommandKind(item.Kind);
		item.Value = (item.Value ?? "").Trim();
		item.FontSizePx = Math.Max(0, Math.Min(72, item.FontSizePx));
		item.FontName = NormalizeFontName(item.FontName, "");
		item.FontFamily = NormalizeFontName(item.FontFamily, "");
		item.TextColor = NormalizeColor(item.TextColor);
		item.ComboAdvanceKey = ComboAdvanceKeys.Normalize(item.ComboAdvanceKey);
		item.IconData = Limit(item.IconData, 256);
		item.ImageMode = string.Equals(item.ImageMode, "Image", StringComparison.OrdinalIgnoreCase) ? "Image" : "Icon";
		item.CellSizePercent = Math.Max(40, Math.Min(160, item.CellSizePercent > 0 ? item.CellSizePercent : 100));
		item.CellShape = NormalizeCellShape(item.CellShape);
		item.IconRatioPercent = Math.Max(20, Math.Min(90, item.IconRatioPercent > 0 ? item.IconRatioPercent : 55));
		// Free layout works in normalised wheel coordinates so it survives a size change.
		item.FreeX = NormalizeCoordinate(item.FreeX);
		item.FreeY = NormalizeCoordinate(item.FreeY);
		item.ContentMode = NormalizeCellContentMode(item.ContentMode, "", allowInherit: true);
		NormalizeComboCommands(item);
	}

	private static double? NormalizeCoordinate(double? value)
	{
		if (!value.HasValue) return null;
		double number = value.Value;
		if (double.IsNaN(number) || double.IsInfinity(number)) return null;
		return Math.Max(-1.5, Math.Min(1.5, number));
	}

	internal static string NormalizeCellContentMode(string value, string fallback, bool allowInherit)
	{
		string mode = (value ?? "").Trim();
		if (allowInherit && mode.Equals("Inherit", StringComparison.OrdinalIgnoreCase)) return "";
		if (mode.Equals("Auto", StringComparison.OrdinalIgnoreCase)) return "Auto";
		if (mode.Equals("IconOnly", StringComparison.OrdinalIgnoreCase)) return "IconOnly";
		if (mode.Equals("TextOnly", StringComparison.OrdinalIgnoreCase)) return "TextOnly";
		if (mode.Equals("IconAndText", StringComparison.OrdinalIgnoreCase)) return "IconAndText";
		if (mode.Equals("ImageOnly", StringComparison.OrdinalIgnoreCase)) return "ImageOnly";
		if (mode.Equals("ImageAndText", StringComparison.OrdinalIgnoreCase)) return "ImageAndText";
		return fallback;
	}

	private static string NormalizeCellShape(string value)
	{
		string shape = (value ?? "").Trim();
		if (shape.Equals("Square", StringComparison.OrdinalIgnoreCase)) return "Square";
		if (shape.Equals("Rounded", StringComparison.OrdinalIgnoreCase)) return "Rounded";
		return "Circle";
	}

	private static string Limit(string value, int maxLength)
	{
		string text = (value ?? "").Trim();
		return text.Length <= maxLength ? text : text.Substring(0, maxLength);
	}

	private static bool IsValidComboCommand(ComboCommandConfig command)
	{
		if (command == null)
		{
			return false;
		}
		string kind = NormalizeCommandKind(command.Kind);
		string value = (command.Value ?? "").Trim();
		if (kind.Equals(ActionKinds.Basic, StringComparison.OrdinalIgnoreCase))
		{
			// Keep retired steps so a saved combo stops explicitly instead of skipping work.
			return !string.IsNullOrWhiteSpace(value);
		}
		if (kind.Equals(ActionKinds.UiClick, StringComparison.OrdinalIgnoreCase)
			|| kind.Equals(ActionKinds.NativeCommand, StringComparison.OrdinalIgnoreCase)
			|| kind.Equals(ActionKinds.QinJian, StringComparison.OrdinalIgnoreCase)
			|| kind.Equals(ActionKinds.ExternalPlugin, StringComparison.OrdinalIgnoreCase)
			|| kind.Equals(ActionKinds.JiaLiChuang, StringComparison.OrdinalIgnoreCase))
		{
			return !string.IsNullOrWhiteSpace(value);
		}
		if (kind.Equals(ActionKinds.Delay, StringComparison.OrdinalIgnoreCase))
		{
			return int.TryParse(value, out int delayMs) && delayMs >= 0;
		}
		return !string.IsNullOrWhiteSpace(value);
	}

	private static string NormalizeCommandKind(string value)
	{
		string kind = (value ?? "").Trim();
		if (kind.Equals(ActionKinds.Basic, StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.Basic;
		}
		if (kind.Equals(ActionKinds.Combo, StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.Combo;
		}
		if (kind.Equals(ActionKinds.UiClick, StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.UiClick;
		}
		if (kind.Equals(ActionKinds.NativeCommand, StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.NativeCommand;
		}
		if (kind.Equals(ActionKinds.QinJian, StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.QinJian;
		}
		if (kind.Equals(ActionKinds.ExternalPlugin, StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.ExternalPlugin;
		}
		if (kind.Equals(ActionKinds.JiaLiChuang, StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.JiaLiChuang;
		}
		if (kind.Equals(ActionKinds.Delay, StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.Delay;
		}
		if (kind.Equals(ActionKinds.ShortcutPalette, StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.ShortcutPalette;
		}
		return ActionKinds.Hotkey;
	}
}
