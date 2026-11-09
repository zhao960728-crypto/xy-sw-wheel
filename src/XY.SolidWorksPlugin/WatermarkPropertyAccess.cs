using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace XY.SolidWorksPlugin;

internal static class WatermarkPropertyAccess
{
	internal static string[] Aliases(string name) => name switch
	{
		"材质" or "材料" => new[] { name, name == "材质" ? "材料" : "材质", "Material" },
		"工序" or "工艺流程" => new[] { name, name == "工序" ? "工艺流程" : "工序", "工艺" },
		"厚度" or "板厚" => new[] { name, name == "厚度" ? "板厚" : "厚度" },
		"质量" or "重量" or "单重" => new[] { name, "质量", "重量", "单重", "Weight", "Mass" }.Distinct().ToArray(),
		_ => new[] { name }
	};

	internal sealed class Value
	{
		public string Text { get; set; } = "";
		public string Configuration { get; set; } = "";
		public string Name { get; set; } = "";
		public string Raw { get; set; } = "";
		public bool Exists { get; set; }
		public int Type { get; set; } = 30;
		internal string RequestedConfiguration { get; set; } = "";
		internal string RequestedName { get; set; } = "";
		internal string Source { get; set; } = "自定义属性";
		public string Origin => (Exists ? "" : "尚未填写 / ") + (Configuration.Length > 0 ? "配置：" + Configuration : "文档属性") + " / " + Name;
	}

	public static Value Read(IModelDoc2 doc, string configuration, string source, string name)
	{
		Value fallback = new Value { Name = name, Configuration = configuration ?? "", RequestedConfiguration = configuration ?? "", RequestedName = name ?? "", Source = source };
		if (doc == null || string.IsNullOrWhiteSpace(name)) return fallback;
		bool mayReadNative = source == "原生属性" && (configuration == (doc.ConfigurationManager?.ActiveConfiguration?.Name ?? "") || name == "文件名");
		if (mayReadNative)
		{
			fallback.Text = PropertyIo.Read(doc, source, name);
			if (!string.IsNullOrWhiteSpace(fallback.Text)) return fallback;
		}
		foreach (string scope in new[] { configuration ?? "", "" }.Distinct())
		{
			ICustomPropertyManager manager = doc.Extension.get_CustomPropertyManager(scope);
			HashSet<string> names = new HashSet<string>((manager.GetNames() as object[] ?? Array.Empty<object>()).Select(Convert.ToString), StringComparer.OrdinalIgnoreCase);
			foreach (string candidate in Aliases(name))
			{
				if (!names.Contains(candidate)) continue;
				manager.Get4(candidate, false, out string raw, out string resolved);
				var value = new Value { Text = string.IsNullOrEmpty(resolved) ? raw ?? "" : resolved, Raw = raw ?? "", Exists = true, Configuration = scope, Name = candidate,
					Type = manager.GetType2(candidate), RequestedConfiguration = configuration ?? "", RequestedName = name, Source = source };
				// An explicit empty value in the effective scope is authoritative.
				// Falling through would redisplay an old document-level process after clearing it.
				return value;
			}
		}
		if (mayReadNative)
			fallback.Text = PropertyIo.Read(doc, source, name);
		return fallback;
	}

	public static void Write(IModelDoc2 doc, Value target, string text)
	{
		if (target == null) throw new InvalidOperationException("属性编辑目标已失效，请重新读取。");
		if (target.Source == "原生属性") throw new InvalidOperationException("模型计算属性为只读，请在模型中修改。");
		// A newly added configuration property can shadow the document property
		// while an editor is open. Validate the effective identity as well as the
		// old property's value before changing anything.
		if (target.RequestedName.Length > 0)
		{
			Value effective = Read(doc, target.RequestedConfiguration, target.Source, target.RequestedName);
			if (effective.Configuration != target.Configuration || effective.Name != target.Name || effective.Exists != target.Exists)
				throw new InvalidOperationException("属性的有效来源已被其他窗口修改，请重新读取后编辑，未写入旧作用域。");
		}
		var store = new PropertyWriteBatch.ModelStore(doc, target.Configuration);
		var current = store.Read(target.Name);
		if (current.Exists != target.Exists || (current.Exists && (current.Raw != target.Raw || current.Type != target.Type)))
			throw new InvalidOperationException("属性已被其他窗口修改，请重新读取后编辑，未覆盖最新值。");
		PropertyWriteBatch.Apply(store, new[] { new KeyValuePair<string, string>(target.Name, text) });
	}

	internal static IModelDoc2 ResolveDocument(ISldWorks host, bool selectedPart, out string configuration)
	{
		IModelDoc2 active = host?.ActiveDoc as IModelDoc2;
		configuration = active?.ConfigurationManager?.ActiveConfiguration?.Name ?? "";
		if (active == null || !selectedPart || active.GetType() != 2) return active;
		ISelectionMgr selection = active.ISelectionManager;
		// Appended multi-selection should follow the most recently clicked part,
		// not remain stuck on the first selected component.
		for (int index = selection?.GetSelectedObjectCount2(-1) ?? 0; index >= 1; index--)
		{
			object selected = selection.GetSelectedObject6(index, -1);
			IComponent2 component = selection.GetSelectedObjectsComponent4(index, -1) as IComponent2
				?? selected as IComponent2
				?? (selected as IEntity)?.GetComponent() as IComponent2;
			IModelDoc2 document = component?.GetModelDoc2() as IModelDoc2;
			if (document == null || document.GetType() != 1) continue;
			configuration = component.ReferencedConfiguration ?? "";
			return document;
		}
		return active;
	}
}
