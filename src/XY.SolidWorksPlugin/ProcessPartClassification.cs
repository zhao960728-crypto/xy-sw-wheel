using System;
using System.Collections.Generic;
using System.Linq;

namespace XY.SolidWorksPlugin;

// The same read-only inference drives the analysis, list label and optional
// confirmed property suggestion; process text alone does not prove geometry.
internal static class ProcessPartClassification
{
    internal static PartTypeMatch Resolve(ProcessRecognitionInput input, IEnumerable<RecognizedProcess> processes = null)
    {
        if (input == null) return new PartTypeMatch();
        var facts = (input.Features ?? Array.Empty<ProcessFeatureFact>()).Where(fact => fact != null && !fact.IsSuppressed).ToArray();
        var names = (processes ?? Array.Empty<RecognizedProcess>()).Where(process => process != null).Select(process => process.Name).ToArray();
        bool Feature(params string[] types) => facts.Any(fact =>
        {
            string type = (fact.TypeName ?? "").Trim();
            return type.Length > 0 && types.Any(token => type.IndexOf(token ?? "", StringComparison.OrdinalIgnoreCase) >= 0);
        });
        var geometry = input.Geometry;
        bool complete = geometry?.GeometryComplete == true;
        var profileEvidence = ProcessStructuralProfilePolicy.Inspect(input);
        bool genericProfileType = string.IsNullOrWhiteSpace(input.PartType)
            || string.Equals(input.PartType, PartTypes.Other, StringComparison.OrdinalIgnoreCase)
            || string.Equals(input.PartType, PartTypes.Profile, StringComparison.OrdinalIgnoreCase)
            || string.Equals(input.PartType, "型材", StringComparison.OrdinalIgnoreCase)
            || string.Equals(input.PartType, "型材件", StringComparison.OrdinalIgnoreCase);
        string declaredProfileKind = ProcessTypeGroupPolicy.CanonicalType(input.PartType);
        bool declaredStructuralType = genericProfileType || ProcessStructuralProfilePolicy.IsKnownKind(declaredProfileKind);
        bool declaredGeometryConflict = ProcessStructuralProfilePolicy.IsKnownKind(declaredProfileKind) && profileEvidence.HasGeometryConflictWith(declaredProfileKind);
        bool sheet = geometry?.IsNativeSheetMetal == true || (complete && geometry.IsSheetLike)
            || Feature("sheetmetal", "smbaseflange", "baseflange", "flatpattern", "flattenbends", "processbends", "onebend", "sketchbend", "edgeflange", "miterflange", "loftedbend", "hem", "jog");
        bool tube = complete && geometry.StraightTubeBodyCount > 0;
        bool weld = ProcessManufacturingPlan.HasWeldingEvidence(input)
            || names.Any(name => new[] { "焊接", "组焊", "焊接封角", "点焊", "氩弧焊", "二保焊", "激光焊" }.Contains(name));
        bool structure = Feature("weldment", "structuralmember", "weldmentprofile");
        // A single structural tube is a stock profile, not proof that it has been welded.
        if (tube && geometry.StraightTubeBodyCount == geometry.SolidBodyCount && !weld) structure = false;
        bool revolve = Feature("revolution", "revolve", "revolvethin", "revcut") || names.Contains("车削");
        bool milling = ProcessRecognitionService.HasMillingType(facts, new[] { "cut", "cutthin", "extrusioncut", "ice" }) || names.Contains("铣削");
        if (!PurchasedPartPolicy.IsPurchased(input) && !input.IsAssembly && (profileEvidence.HasConflict || declaredGeometryConflict) && declaredStructuralType)
        {
            string reason = profileEvidence.ConflictReason;
            if (declaredGeometryConflict && reason.Length == 0) reason = "类型属性“" + input.PartType + "”与实体截面“" + string.Join("、", profileEvidence.GeometryKinds) + "”不一致";
            return new PartTypeMatch { Type = "型材混合待确认", Confidence = 0, Evidence = reason + "；需按实体分别确认截面、材质和长度" };
        }
        PartTypeMatch type = ProcessRecognitionService.ResolvePartType(input, sheet, structure, weld, tube, revolve, milling, input.IsAssembly);
        if (input.IsAssembly && !PurchasedPartPolicy.IsPurchased(input))
        {
            var textType = PartTypeCatalog.FromText(input.PartType, input.DocumentTitle, input.ProfileDescription);
            if (textType.IsEmpty || textType.Type == PartTypes.Other)
                return new PartTypeMatch { Type = weld ? PartTypes.WeldAssembly : "装配总成", Confidence = 62,
                    Evidence = weld ? "装配体含明确焊接工序/特征证据，按焊接总成推定" : "普通装配体尚无明确焊接证据，按装配总成推定" };
        }
        bool nativeSheet = geometry?.IsNativeSheetMetal == true || Feature("sheetmetal", "smbaseflange", "baseflange", "flatpattern", "flattenbends", "processbends");
        if (!PurchasedPartPolicy.IsPurchased(input) && complete && geometry.SolidBodyCount > 1 && geometry.SheetBodyCount > 0 && geometry.StraightTubeBodyCount > 0 && !weld && (string.IsNullOrWhiteSpace(input.PartType) || input.PartType == PartTypes.Other))
            return new PartTypeMatch { Type = "多类型实体（汇合待确认）", Confidence = 0, Evidence = "同时包含钣金和管材实体；没有明确焊接证据，不能仅凭文档型材名称把全部实体归入型材或焊接件" };
        if (nativeSheet && geometry?.SolidBodyCount <= 1 && type.Type == PartTypes.Profile && (string.IsNullOrWhiteSpace(input.PartType) || input.PartType == PartTypes.Other))
            return new PartTypeMatch { Type = weld ? PartTypes.Mixed : PartTypes.SheetMetal, Confidence = 92, Evidence = "名称/规格有型材线索，但本单体存在原生钣金制造证据；按钣金实体分类，" + (weld ? "同时有焊接证据" : "L形本身不证明现货角钢") };
        if (!type.IsEmpty) return type;
        return new PartTypeMatch { Type = "待确认机加工", Confidence = 0,
            Evidence = geometry?.SolidBodyCount > 0 ? "仅检测到普通实心实体，缺少可确认的机加工/板材/型材依据；通用机加工工序只是待复核建议" : "尚缺明确类型、有效制造特征或完整实体几何；需人工确认" };
    }

    internal static string Display(ProcessRecognitionInput input, IEnumerable<RecognizedProcess> processes)
    {
        var names = (processes ?? Array.Empty<RecognizedProcess>()).Where(process => process != null).Select(process => process.Name).ToArray();
        if (PurchasedPartPolicy.IsPurchased(input) || names.Contains("外购")) return "标准/外购件";
        if (input?.IsAssembly == true) return ProcessManufacturingPlan.HasWeldingEvidence(input) || names.Any(name => new[] { "焊接", "组焊", "点焊", "氩弧焊", "二保焊", "激光焊", "焊接封角" }.Contains(name)) ? "焊接总成" : "装配总成";
        if (names.Contains("外加工")) return PartTypes.Outsourced;
        var type = Resolve(input, processes);
        return type.Type == PartTypes.WeldAssembly ? "焊接总成" : type.Type;
    }

    internal static string Describe(ProcessRecognitionInput input, IEnumerable<RecognizedProcess> processes = null)
    {
        var type = Resolve(input, processes);
        string confidence = type.Confidence > 0 ? "（推定置信 " + type.Confidence + "%）" : "（尚未确认）";
        string supplied = PurchasedPartPolicy.IsPurchased(input) ? "；供货分类：" + (input?.IsAssembly == true ? "外购装配总成" : "外购/标准零件") : "";
        return "分类推定：" + type.Type + confidence + supplied + "。\r\n分类依据：" + type.Evidence
            + "。\r\n当前类型属性：" + (string.IsNullOrWhiteSpace(input?.PartType) ? "未填写" : input.PartType)
            + "；分析不修改属性，确认写入时只补空白或通用‘其他’类型，已有人工类型保留。";
    }

    internal static IReadOnlyList<KeyValuePair<string, string>> PropertySuggestion(ProcessRecognitionInput input, ProcessRecognitionConfig config, IEnumerable<RecognizedProcess> processes = null)
    {
        if (input == null || config == null || (!string.IsNullOrWhiteSpace(input.PartType) && !string.Equals(input.PartType.Trim(), PartTypes.Other, StringComparison.OrdinalIgnoreCase))) return Array.Empty<KeyValuePair<string, string>>();
        var type = Resolve(input, processes);
        if (type.IsEmpty || type.Confidence < 64 || type.Type == "待确认机加工") return Array.Empty<KeyValuePair<string, string>>();
        // Incomplete geometry is allowed only when text, marker or metadata independently declares the type.
        if (input.Geometry?.GeometryComplete != true)
        {
            var text = PartTypeCatalog.FromText(input.PartType, input.DocumentTitle, input.ProfileDescription);
            if (text.IsEmpty && PurchasedHardwareCatalog.Match(input).IsEmpty && PurchasedPartPolicy.ExplicitFlag(input.Purchase) != true) return Array.Empty<KeyValuePair<string, string>>();
        }
        return new[] { new KeyValuePair<string, string>(config.PartTypePropertyName, type.Type) };
    }
}
