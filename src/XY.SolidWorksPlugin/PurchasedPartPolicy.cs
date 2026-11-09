using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace XY.SolidWorksPlugin;

internal static class PurchasedPartPolicy
{
    internal const string PropertyName = "是否外购件";
    internal static bool? ExplicitFlag(string value)
    {
        string text = (value ?? "").Trim();
        if (new[] { "是", "true", "yes", "1", "√", "开启", "外购", "外购件", "采购", "采购件" }.Contains(text, StringComparer.OrdinalIgnoreCase)) return true;
        if (new[] { "否", "false", "no", "0", "×", "关闭", "无", "非外购", "非外购件", "不外购", "否外购", "非采购", "非采购件", "不采购", "自制件", "自制", "自产", "自加工", "内制", "内加工", "厂内加工" }.Contains(text, StringComparer.OrdinalIgnoreCase)) return false;
        return null;
    }
    internal static bool IsSupplyType(string type) => type == PartTypes.Purchased || type == PartTypes.FootPad || type == PartTypes.Standard;
    internal static bool IsPurchased(ProcessRecognitionInput input)
    {
        if (input == null) return false;
        bool? flag = ExplicitFlag(input.Purchase);
        if (flag.HasValue) return flag.Value;
        return IsSupplyType(PartTypeCatalog.FromText(input.PartType, input.DocumentTitle, input.ProfileDescription).Type)
            || IsSupplyType(PurchasedHardwareCatalog.Match(input).Type);
    }

    // Read the referenced configuration without activating it or changing the
    // parent's current document. An explicit non-purchased flag wins over names.
    internal static ProcessRecognitionInput ReadReferencedIdentity(IModelDoc2 document, string configuration, string title, ProcessRecognitionConfig config)
    {
        string Read(params string[] names)
        {
            if (document == null) return "";
            foreach (string scope in new[] { configuration ?? "", "" }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var manager = document.Extension.get_CustomPropertyManager(scope);
                foreach (string name in names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    manager.Get4(name, false, out string raw, out string resolved);
                    string value = string.IsNullOrWhiteSpace(resolved) ? raw : resolved;
                    if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                }
            }
            return "";
        }
        return new ProcessRecognitionInput { DocumentTitle = title ?? "",
            IsAssembly = document?.GetType() == 2, IsPart = document?.GetType() == 1,
            Purchase = Read(PropertyName, "是否外购", "是否采购", "外购标记", "采购标记", "外购件", "采购件", "采购状态"),
            PartType = Read(config?.PartTypePropertyName, "零件类型", "物料类型", "部件类型", "类型"),
            ItemDescription = Read("物料名称", "品名", "零件名称", "描述", "Description", "Part Description"),
            StandardCode = Read("标准号", "标准编号", "标准", "型号", "规格型号", "Standard", "Part Number") };
    }

    internal static bool HasPurchasedAncestor(IComponent2 component, ProcessRecognitionConfig config)
    {
        // The root assembly is included by AddRootAssemblyPart as well.
        var parent = component?.GetParent() as IComponent2;
        int depth = 0;
        while (parent != null && depth++ < 128)
        {
            var document = parent.GetModelDoc2() as IModelDoc2;
            string path = parent.GetPathName() ?? "";
            string title = path.Length > 0 ? System.IO.Path.GetFileName(path) : document?.GetTitle() ?? parent.Name2 ?? "";
            if (document != null && IsPurchased(ReadReferencedIdentity(document, parent.ReferencedConfiguration, title, config))) return true;
            if (parent.IsRoot()) break;
            parent = parent.GetParent() as IComponent2;
        }
        return false;
    }
    internal static string Display(ProcessRecognitionInput input)
    {
        if (input == null) return "待识别外购身份";
        bool? flag = ExplicitFlag(input.Purchase);
        if (flag == true) return "外购件（明确标记：是）";
        if (flag == false) return "非外购（明确标记：否）";
        if (!string.IsNullOrWhiteSpace(input.Purchase)) return "外购标记未识别：" + input.Purchase + (IsPurchased(input) ? "（类型/名称疑似外购）" : "");
        return IsPurchased(input) ? "疑似外购（类型/名称识别，尚未标记）" : "未标记外购";
    }
    internal static string ReadFlag(IModelDoc2 document)
    {
        string current = document.ConfigurationManager?.ActiveConfiguration?.Name ?? "";
        string[] names = { PropertyName, "是否外购", "是否采购", "外购标记", "采购标记", "外购件", "采购件", "采购状态" };
        foreach (string scope in new[] { current, "" }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var manager = document.Extension.get_CustomPropertyManager(scope);
            var existing = (manager.GetNames() as object[] ?? Array.Empty<object>()).Select(Convert.ToString).ToArray();
            foreach (string name in names)
            {
                if (!existing.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                manager.Get4(name, false, out string raw, out string resolved);
                string value = string.IsNullOrWhiteSpace(resolved) ? raw : resolved;
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
        }
        return "";
    }

    internal static PropertyWriteBatch.Snapshot ReadSnapshot(IModelDoc2 document, string configuration)
    {
        var manager = document.Extension.get_CustomPropertyManager(configuration ?? "");
        bool exists = (manager.GetNames() as object[] ?? Array.Empty<object>()).Select(Convert.ToString).Contains(PropertyName, StringComparer.OrdinalIgnoreCase);
        if (!exists) return new PropertyWriteBatch.Snapshot();
        manager.Get4(PropertyName, false, out string raw, out string resolved);
        return new PropertyWriteBatch.Snapshot { Exists = true, Type = manager.GetType2(PropertyName), Raw = raw ?? "", Resolved = resolved ?? raw ?? "" };
    }
    internal static int Apply(PropertyWriteBatch.Store store, PropertyWriteBatch.Snapshot expected, bool purchased)
    {
        if (expected == null) throw new InvalidOperationException("没有外购标记预览，请重新读取。");
        var current = store.Read(PropertyName);
        if (current.Exists != expected.Exists || (current.Exists && (current.Raw != expected.Raw || current.Resolved != expected.Resolved || current.Type != expected.Type)))
            throw new InvalidOperationException("外购标记在预览后已变化，未覆盖；请重新读取。");
        return PropertyWriteBatch.Apply(store, new[] { new KeyValuePair<string,string>(PropertyName, purchased ? "是" : "否") });
    }
}
