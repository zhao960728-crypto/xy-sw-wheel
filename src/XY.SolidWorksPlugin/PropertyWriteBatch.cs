using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace XY.SolidWorksPlugin;

// A confirmed edit is one batch: validate every target first, preserve property
// types and expressions, and restore earlier writes if any later write fails.
internal static class PropertyWriteBatch
{
	internal sealed class Snapshot
	{
		public bool Exists;
		public int Type = 30;
		public string Raw = "";
		public string Resolved = "";
	}
	internal interface Store
	{
		Snapshot Read(string name);
		void Set(string name, string value, Snapshot previous);
		void Restore(string name, Snapshot previous);
	}
	internal sealed class ModelStore : Store
	{
		private readonly ICustomPropertyManager _manager;
		public ModelStore(IModelDoc2 document, string configuration = "")
		{
			if (document == null) throw new InvalidOperationException("当前没有模型。");
			if (document.IsOpenedReadOnly()) throw new InvalidOperationException("模型为只读，未写入属性。");
			_manager = document.Extension.get_CustomPropertyManager(configuration ?? "");
		}
		public Snapshot Read(string name)
		{
			bool exists = (_manager.GetNames() as object[] ?? Array.Empty<object>()).Select(Convert.ToString).Contains(name, StringComparer.OrdinalIgnoreCase);
			if (!exists) return new Snapshot();
			_manager.Get4(name, false, out string raw, out string resolved);
			return new Snapshot { Exists = true, Type = _manager.GetType2(name), Raw = raw ?? "", Resolved = resolved ?? raw ?? "" };
		}
		public void Set(string name, string value, Snapshot previous)
		{
			int result = previous.Exists ? _manager.Set2(name, value) : _manager.Add3(name, 30, value, 1);
			if (result != 0 || !string.Equals(Read(name).Raw, value, StringComparison.Ordinal))
				throw new InvalidOperationException("属性“" + name + "”写入未通过校验（" + result + "）。");
		}
		public void Restore(string name, Snapshot previous)
		{
			if (previous.Exists)
			{
				if (_manager.Add3(name, previous.Type, previous.Raw, 1) != 0) throw new InvalidOperationException(name);
			}
			else if (Read(name).Exists && _manager.Delete2(name) != 0) throw new InvalidOperationException(name);
			Snapshot restored = Read(name);
			if (restored.Exists != previous.Exists || (previous.Exists && (restored.Raw != previous.Raw || restored.Type != previous.Type)))
				throw new InvalidOperationException(name);
		}
		public void Delete(string name)
		{
			if (!(_manager.GetNames() as object[] ?? Array.Empty<object>()).Select(Convert.ToString).Contains(name, StringComparer.OrdinalIgnoreCase)) return;
			int result = _manager.Delete2(name);
			if (result != 0 || (_manager.GetNames() as object[] ?? Array.Empty<object>()).Select(Convert.ToString).Contains(name, StringComparer.OrdinalIgnoreCase))
				throw new InvalidOperationException("属性“" + name + "”删除未通过校验（" + result + "）。");
		}
	}
	internal sealed class TargetStore : Store
	{
		private readonly IModelDoc2 _document;
		private readonly IReadOnlyDictionary<string, WatermarkPropertyAccess.Value> _targets;
		public TargetStore(IModelDoc2 document, IReadOnlyDictionary<string, WatermarkPropertyAccess.Value> targets) { _document = document; _targets = targets; }
		private ModelStore Scope(string key) => new ModelStore(_document, _targets[key].Configuration);
		public Snapshot Read(string key) => Scope(key).Read(_targets[key].Name);
		public void Set(string key, string value, Snapshot previous) => Scope(key).Set(_targets[key].Name, value, previous);
		public void Restore(string key, Snapshot previous) => Scope(key).Restore(_targets[key].Name, previous);
	}
	public static int Apply(Store store, IEnumerable<KeyValuePair<string, string>> values)
	{
		var changes = (values ?? Enumerable.Empty<KeyValuePair<string, string>>()).ToList();
		if (changes.Any(pair => string.IsNullOrWhiteSpace(pair.Key))) throw new InvalidOperationException("属性名称不能为空。");
		if (changes.GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Any(group => group.Select(pair => pair.Value).Distinct().Count() > 1))
			throw new InvalidOperationException("多个字段写入了同名属性，请先修改重复的属性名称。");
		changes = changes.GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
		var before = changes.ToDictionary(pair => pair.Key, pair => store.Read(pair.Key), StringComparer.OrdinalIgnoreCase);
		var attempted = new List<string>();
		try
		{
			foreach (var pair in changes)
			{
				if (before[pair.Key].Exists && before[pair.Key].Raw == (pair.Value ?? "")) continue;
				attempted.Add(pair.Key);
				store.Set(pair.Key, pair.Value ?? "", before[pair.Key]);
			}
			return attempted.Count;
		}
		catch (Exception ex)
		{
			var failed = new List<string>();
			foreach (string name in attempted.AsEnumerable().Reverse())
			{
				try { store.Restore(name, before[name]); }
				catch { failed.Add(name); }
			}
			throw new InvalidOperationException(ex.Message + (failed.Count == 0 ? " 已恢复本次写入前的属性，草稿仍保留。" : " 以下属性恢复失败，请检查：" + string.Join("、", failed)), ex);
		}
	}
}
