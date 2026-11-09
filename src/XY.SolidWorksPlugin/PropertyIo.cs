using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using SolidWorks.Interop.sldworks;

namespace XY.SolidWorksPlugin;

internal static class PropertyIo
{
	public static string Read(IModelDoc2 doc, string name)
	{
		return Read(doc, "自定义属性", name);
	}

	public static string Read(IModelDoc2 doc, string source, string name)
	{
		try
		{
			if (doc == null || string.IsNullOrWhiteSpace(name)) return "";
			if (string.Equals(source, "原生属性", StringComparison.OrdinalIgnoreCase)) return ReadModelCalculatedValue(doc, name);
			string[] candidates = Candidates(name).ToArray();
			object configurationManager = ConfigurationManager(doc);
			object documentManager = DocumentManager(doc);
			foreach (string candidate in candidates)
			{
				string value = ReadIfPresent(configurationManager, candidate);
				if (!string.IsNullOrWhiteSpace(value)) return value;
			}
			foreach (string candidate in candidates)
			{
				string value = ReadIfPresent(documentManager, candidate);
				if (!string.IsNullOrWhiteSpace(value)) return value;
			}
			return "";
		}
		catch { return ""; }
	}

	public static List<string> Names(IModelDoc2 doc)
	{
		IEnumerable<string> configurationNames = Names(ConfigurationManager(doc));
		IEnumerable<string> documentNames = Names(DocumentManager(doc));
		return configurationNames.Concat(documentNames)
			.Where(x => !string.IsNullOrWhiteSpace(x))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	public static void Write(IModelDoc2 doc, string name, string value)
	{
		object manager = DocumentManager(doc); if (manager == null) throw new InvalidOperationException("无法获取当前模型文档自定义属性管理器。");
		if (manager is ICustomPropertyManager customProperties)
		{
			if (Names(manager).Contains(name, StringComparer.OrdinalIgnoreCase))
				customProperties.Set2(name, value);
			else
				customProperties.Add3(name, 30, value, 1); // 30=文本；1=替换同名属性
			return;
		}
		if (Names(manager).Contains(name, StringComparer.OrdinalIgnoreCase))
		{
			MethodInfo set2 = manager.GetType().GetMethod("Set2");
			if (set2 == null) throw new InvalidOperationException("当前 SOLIDWORKS 版本不支持更新自定义属性。");
			set2.Invoke(manager, new object[] { name, value });
			return;
		}
		MethodInfo add3 = manager.GetType().GetMethod("Add3");
		if (add3 == null) throw new InvalidOperationException("当前 SOLIDWORKS 版本不支持新增自定义属性。");
		add3.Invoke(manager, new object[] { name, 30, value, 1 }); // 30=文本，1=替换同名属性
	}

	public static int WriteChanged(IModelDoc2 doc, IEnumerable<KeyValuePair<string, string>> values)
	{
		object manager = DocumentManager(doc);
		if (manager == null) throw new InvalidOperationException("无法获取当前模型文档自定义属性管理器。");
		HashSet<string> names = new HashSet<string>(Names(manager), StringComparer.OrdinalIgnoreCase);
		int changed = 0;
		foreach (KeyValuePair<string, string> pair in (values ?? Enumerable.Empty<KeyValuePair<string, string>>())
			.Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
			.GroupBy(pair => pair.Key.Trim(), StringComparer.OrdinalIgnoreCase)
			.Select(group => group.Last()))
		{
			string name = pair.Key.Trim();
			string value = pair.Value ?? "";
			if (names.Contains(name) && string.Equals(ReadValue(manager, name), value, StringComparison.Ordinal)) continue;
			if (manager is ICustomPropertyManager customProperties)
			{
				if (names.Contains(name)) customProperties.Set2(name, value);
				else customProperties.Add3(name, 30, value, 1);
			}
			else if (names.Contains(name))
			{
				MethodInfo set2 = manager.GetType().GetMethod("Set2");
				if (set2 == null) throw new InvalidOperationException("当前 SOLIDWORKS 版本不支持更新自定义属性。");
				set2.Invoke(manager, new object[] { name, value });
			}
			else
			{
				MethodInfo add3 = manager.GetType().GetMethod("Add3");
				if (add3 == null) throw new InvalidOperationException("当前 SOLIDWORKS 版本不支持新增自定义属性。");
				add3.Invoke(manager, new object[] { name, 30, value, 1 });
			}
			names.Add(name);
			changed++;
		}
		return changed;
	}

	private static string ReadValue(object manager, string name)
	{
		if (manager is ICustomPropertyManager customProperties)
		{
			customProperties.Get4(name, false, out string value, out string resolved);
			return resolved ?? value ?? "";
		}
		MethodInfo get4 = manager?.GetType().GetMethod("Get4");
		if (get4 == null) return "";
		object[] args = { name, false, "", "" };
		get4.Invoke(manager, args);
		return (args[3] as string) ?? (args[2] as string) ?? "";
	}

	private static string ReadIfPresent(object manager, string name)
	{
		return Names(manager).Contains(name, StringComparer.OrdinalIgnoreCase) ? ReadValue(manager, name) : "";
	}

	private static string ReadModelCalculatedValue(IModelDoc2 doc, string name)
	{
		if (string.Equals(name, "文件名", StringComparison.OrdinalIgnoreCase))
		{
			string path = doc.GetPathName();
			return string.IsNullOrWhiteSpace(path) ? doc.GetTitle() : System.IO.Path.GetFileNameWithoutExtension(path);
		}
		if (ModelThicknessResolver.IsThicknessProperty(name)) return ModelThicknessResolver.Read(doc);
		if (IsMassProperty(name)) return ReadMass(doc);
		return "";
	}

	private static bool IsMassProperty(string name)
	{
		return string.Equals(name, "单重", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "质量", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "重量", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Weight", StringComparison.OrdinalIgnoreCase);
	}

	private static string ReadMass(IModelDoc2 doc)
	{
		try
		{
			IMassProperty massProperty = doc.Extension?.CreateMassProperty();
			return massProperty == null ? "" : massProperty.Mass.ToString("0.###", CultureInfo.InvariantCulture);
		}
		catch { return ""; }
	}

	private static string ExistingName(object manager, IEnumerable<string> candidates)
	{
		HashSet<string> names = new HashSet<string>(Names(manager), StringComparer.OrdinalIgnoreCase);
		return candidates.FirstOrDefault(names.Contains);
	}

	private static IEnumerable<string> Candidates(string name)
	{
		yield return name;
		if (string.Equals(name, "材料", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "材质", StringComparison.OrdinalIgnoreCase)) { yield return "材料"; yield return "材质"; }
		else if (ModelThicknessResolver.IsThicknessProperty(name)) { yield return "板厚"; yield return "厚度"; yield return "料厚"; yield return "SW-Sheet Metal Thickness"; }
		else if (string.Equals(name, "单重", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "质量", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "重量", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Weight", StringComparison.OrdinalIgnoreCase)) { yield return "单重"; yield return "质量"; yield return "重量"; yield return "Weight"; }
	}

	private static IEnumerable<string> Names(object manager)
	{
		if (manager == null) return Enumerable.Empty<string>();
		object value = manager is ICustomPropertyManager customProperties
			? customProperties.GetNames()
			: manager.GetType().GetMethod("GetNames")?.Invoke(manager, null);
		return value is System.Collections.IEnumerable list ? list.Cast<object>().Select(x => x?.ToString() ?? "") : Enumerable.Empty<string>();
	}

	private static object ConfigurationManager(IModelDoc2 doc)
	{
		if (doc == null) return null;
		try
		{
			// Configuration is a COM interface inheriting IConfiguration. Its
			// runtime type does not expose inherited Name through GetProperty.
			// Read through the typed interface, and never treat an unknown
			// configuration name as the document-level manager (empty name).
			IConfiguration configuration = doc.ConfigurationManager?.ActiveConfiguration;
			string configName = configuration?.Name;
			if (string.IsNullOrWhiteSpace(configName)) return null;
			return doc.Extension?.get_CustomPropertyManager(configName);
		}
		catch (Exception ex)
		{
			Logger.Trace("Cannot read active-configuration property manager: " + ex.Message);
			return null;
		}
	}

	private static object DocumentManager(IModelDoc2 doc)
	{
		if (doc == null) return null;
		object extension = doc.Extension;
		try { return doc.Extension?.get_CustomPropertyManager(""); }
		catch { }
		return extension.GetType().GetMethod("get_CustomPropertyManager")?.Invoke(extension, new object[] { "" });
	}
}
