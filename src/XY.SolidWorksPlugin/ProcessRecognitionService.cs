using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace XY.SolidWorksPlugin;

internal sealed class ProcessFeatureFact
{
	public string Name { get; set; } = "";

	public string TypeName { get; set; } = "";

	public bool IsSubFeature { get; set; }
	public bool IsSuppressed { get; set; }
	public int? HoleWizardType { get; set; }
	public int? HemType { get; set; }
}

internal sealed class ProcessGeometryFact
{
	public string BodyName { get; set; } = "";
	public string ProfileKind { get; set; } = "";
	public bool GeometryComplete { get; set; } = true;
	public bool IsNativeSheetMetal { get; set; }
	public IReadOnlyList<ProcessGeometryFact> BodyFacts { get; set; } = Array.Empty<ProcessGeometryFact>();

	public int SolidBodyCount { get; set; }

	public int SheetBodyCount { get; set; }

	public int StraightTubeBodyCount { get; set; }

	public int FaceCount { get; set; }

	public int PlanarFaceCount { get; set; }

	public int CylindricalFaceCount { get; set; }

	public int BendCount { get; set; }

	public int CutoutCount { get; set; }
	public bool CutoutCountReliable { get; set; } = true;
	public string CutoutCountEvidence { get; set; } = "";

	// Geometry-only fastener evidence. These counts describe evidence, not a
	// confirmed catalogue item: a stepped bore can be a counterbore, a PEM
	// insert, or a weld nut until the connection method is confirmed.
	public int PressFitGeometryCount { get; set; }

	public int PressPinGeometryCount { get; set; }

	public int WeldRivetGeometryCount { get; set; }

	public int CounterboreGeometryCount { get; set; }

	public string FastenerGeometryEvidence { get; set; } = "";

	public double ThicknessMm { get; set; }

	public bool IsSheetLike => SheetBodyCount > 0 && ThicknessMm > 0;
}

internal sealed class ProcessPlaneGeometryFact
{
	public double AreaMm2 { get; set; }

	public double[] Normal { get; set; } = Array.Empty<double>();

	public double[] PointMm { get; set; } = Array.Empty<double>();

	public int InnerLoopCount { get; set; }

	public IReadOnlyList<ProcessCircleGeometryFact> InnerCircles { get; set; } = Array.Empty<ProcessCircleGeometryFact>();
}

internal sealed class ProcessCircleGeometryFact
{
	public double RadiusMm { get; set; }

	public double[] CenterMm { get; set; } = Array.Empty<double>();

	public double[] Axis { get; set; } = Array.Empty<double>();
}

internal sealed class ProcessCylinderGeometryFact
{
	public double AreaMm2 { get; set; }

	public double RadiusMm { get; set; }

	public double[] AxisPointMm { get; set; } = Array.Empty<double>();

	public double[] Axis { get; set; } = Array.Empty<double>();

	public double AxisLengthMm { get; set; }

	public double AxisCenterMm { get; set; }

	public bool IsInward { get; set; }

	// A failed surface evaluation must not be treated as an outward wall.
	// Synthetic geometry defaults to known so existing pure geometry tests stay
	// concise; the SolidWorks reader sets this false until it proves the sense.
	public bool IsInwardKnown { get; set; } = true;

	public double ArcAngleRadians => RadiusMm > 0.0001 && AxisLengthMm > 0.0001
		? AreaMm2 / (RadiusMm * AxisLengthMm)
		: 0;
}

internal sealed class ProcessRecognitionInput
{
	public string DocumentTitle { get; set; } = "";

	public string Material { get; set; } = "";

	public string SurfaceTreatment { get; set; } = "";

	public string PartType { get; set; } = "";

	public string ProcessType { get; set; } = "";
	public string ExistingProcess { get; set; } = "";

	public string Outsource { get; set; } = "";

	public string Purchase { get; set; } = "";
	public PropertyWriteBatch.Snapshot PurchaseMarkerSnapshot { get; set; }

	public string ProfileDescription { get; set; } = "";
	public string ItemDescription { get; set; } = "";
	public string StandardCode { get; set; } = "";

	public bool IsPart { get; set; }

	public bool IsAssembly { get; set; }

	public IReadOnlyList<ProcessFeatureFact> Features { get; set; } = Array.Empty<ProcessFeatureFact>();
	public IReadOnlyList<ProcessHoleFact> HoleFacts { get; set; } = Array.Empty<ProcessHoleFact>();
	public bool FeatureReadComplete { get; set; } = true;

	public ProcessGeometryFact Geometry { get; set; } = new ProcessGeometryFact();
}

internal sealed class RecognizedProcess
{
	public string Name { get; set; } = "";

	public int Confidence { get; set; }

	public string Evidence { get; set; } = "";
}

internal static class ProcessRecognitionService
{
	private static readonly string[] SheetMetalTypes = { "sheetmetal", "smbaseflange", "baseflange", "flatpattern", "flattenbends", "processbends" };
	private static readonly string[] BendTypes = { "onebend", "sketchbend", "edgeflange", "miterflange", "loftedbend", "hem", "jog" };
	private static readonly string[] WeldmentTypes = { "weldment", "structuralmember", "weldmemberfeat" };
	private static readonly string[] WeldTypes = { "weldbead", "cosmeticweld", "weldcorner" };
	private static readonly string[] HoleTypes = { "holewzd", "wizardhole", "simplehole", "counterbore", "countersink" };
	private static readonly string[] CounterSinkTypes = { "counterbore", "countersink" };
	private static readonly string[] ThreadTypes = { "cosmeticthread", "thread" };
	private static readonly string[] RevolveTypes = { "revolution", "revolvecut", "revcut" };
	private static readonly string[] MillingTypes = { "boss", "bossthin", "extrusion", "extrudethin", "extrude", "cut", "cutthin", "extrusioncut", "ice", "sweep", "loft", "boundary" };
	private static readonly string[] StockExtrusionTypes = { "boss", "bossthin", "extrusion", "extrudethin", "extrude" };
	private static readonly string[] FormingTypes = { "formtoolinstance", "formtool", "dome", "rib", "louver", "louvers" };

	public static IReadOnlyList<RecognizedProcess> Recognize(ProcessRecognitionInput input, bool deep, ProcessRecognitionConfig config = null)
	{
		if (input == null) throw new ArgumentNullException(nameof(input));
		config ??= new ProcessRecognitionConfig();
		if (!input.IsPart && !input.IsAssembly) throw new InvalidOperationException("工序识别当前只支持零件和装配体文档。");
		// A saved route is an explicit user choice, even without a matched type
		// preset. Preserve order and repeated inspections before any inference.
		if (!string.IsNullOrWhiteSpace(input.ExistingProcess))
			return input.ExistingProcess.Split(new[] { string.IsNullOrEmpty(config.Separator) ? "-" : config.Separator, ">", "→" }, StringSplitOptions.RemoveEmptyEntries)
				.Select(name => name.Trim()).Where(name => name.Length > 0)
				.Select(name => new RecognizedProcess { Name = name, Confidence = 100, Evidence = "模型已有人工工序，自动识别未覆盖或重排" }).ToArray();
		var processes = RecognizeCore(input, deep, config);
		// Supply identity is authoritative; a generic machining rule must never
		// turn a purchased hinge assembly or screw into a manufactured part.
		if (PurchasedPartPolicy.IsPurchased(input) || !string.IsNullOrWhiteSpace(input.ExistingProcess)
			|| ProcessRecognitionConfig.ResolveCombination(config, input.ProcessType).Count > 0) return processes;
		processes = MergeHoleProcesses(processes, input.HoleFacts, config);
		var evaluated = ProcessRuleEngine.Apply(config, input, processes);
		if (evaluated.Errors.Count > 0) throw new InvalidOperationException(string.Join("\n", evaluated.Errors));
		return evaluated.Processes;
	}

	internal static IReadOnlyList<RecognizedProcess> MergeHoleProcesses(IReadOnlyList<RecognizedProcess> processes, IEnumerable<ProcessHoleFact> facts, ProcessRecognitionConfig config = null)
	{
		var result = (processes ?? Array.Empty<RecognizedProcess>()).ToList();
		var mappings = ProcessRecognitionConfig.ParseProcessNameMappings(config?.ProcessNameMappings).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
		string Canonical(string name) => name == "攻牙" ? "攻丝" : name.StartsWith("压铆", StringComparison.Ordinal) ? "压铆" : name.StartsWith("焊铆", StringComparison.Ordinal) ? "焊铆" : name;
		string Display(string name) => mappings.TryGetValue(name, out var direct) ? direct : mappings.TryGetValue(Canonical(name), out var alias) ? alias : name;
		string Identity(string name) => Canonical(Display(Canonical(name)));
		foreach (var fact in facts ?? Array.Empty<ProcessHoleFact>())
		{
			if (fact == null || !fact.IsConfirmed || string.IsNullOrWhiteSpace(fact.ProcessName)) continue;
			string name = fact.ProcessName.Trim();
			if (name.StartsWith("压铆", StringComparison.Ordinal)) name = "压铆";
			else if (name.StartsWith("焊铆", StringComparison.Ordinal)) name = "焊铆";
			name = Display(name);
			var known = result.FirstOrDefault(p => Identity(p.Name) == Identity(name));
			string evidence = "孔工艺联动（" + fact.Source + "）：" + fact.Evidence;
			if (known != null)
			{
				int at = result.IndexOf(known);
				result[at] = new RecognizedProcess { Name = known.Name, Confidence = Math.Max(known.Confidence, 96), Evidence = known.Evidence + "；" + evidence };
				continue;
			}
			int insert = result.FindIndex(p => new[] { "折弯", "焊接", "组焊", "装配", "打磨", "抛光", "表面处理", "烤漆", "丝印", "检验", "入库", "包装" }.Any(stop => p.Name.Contains(stop)));
			var row = new RecognizedProcess { Name = name, Confidence = 96, Evidence = evidence };
			if (insert < 0) result.Add(row); else result.Insert(insert, row);
		}
		return result;
	}

	private static string ThreadProcessName(IEnumerable<ProcessFeatureFact> facts)
		=> facts.Any(f => WizardHoleIsTapped(f.HoleWizardType) || ContainsAny(f.Name ?? "", "攻丝", "攻牙", "丝锥")) ? "攻丝" : "螺纹加工";

	private static IReadOnlyList<RecognizedProcess> RecognizeCore(ProcessRecognitionInput input, bool deep, ProcessRecognitionConfig config)
	{
		if (input == null) throw new ArgumentNullException(nameof(input));
		config ??= new ProcessRecognitionConfig();
		ProcessRecognitionConfig.Normalize(config);
		if (input.IsAssembly) return RecognizeAssembly(input, config);
		if (!input.IsPart) throw new InvalidOperationException("工序识别当前只支持零件和装配体文档。");
		List<ProcessFeatureFact> facts = (input.Features ?? Array.Empty<ProcessFeatureFact>())
			.Where(fact => fact != null && !fact.IsSuppressed && (deep || !fact.IsSubFeature))
			.ToList();
		ProcessGeometryFact geometry = input.Geometry ?? new ProcessGeometryFact();
		List<ProcessFeatureFact> processFacts = facts.Where(fact => !IsBendContainer(fact)).ToList();
		IReadOnlyList<RecognizedProcess> declared = RecognizeDeclaredSupplyType(input);
		if (declared.Count > 0) return ResolvePurchasedDefaultGroup(input, config) ?? FinalizeProcesses(AppendExplicitSurface(declared, input, config), config);
		var combination = ProcessRecognitionConfig.ResolveCombination(config, input.ProcessType);
		if (combination.Count > 0) return combination;
		ProcessTypeGroupMatch defaultGroup = ProcessTypeGroupPolicy.Resolve(config, input);
		if (defaultGroup.Matched)
		{
			if (!string.IsNullOrWhiteSpace(input.ExistingProcess))
				return input.ExistingProcess.Split(new[] { config.Separator, ">", "→" }, StringSplitOptions.RemoveEmptyEntries).Select(name => new RecognizedProcess { Name = name, Confidence = 100, Evidence = "模型已有工序，类型默认组未覆盖" }).ToList();
			return AugmentDefaultGroup(ProcessRecognitionConfig.ResolveCombination(config, defaultGroup.Group), input, config);
		}
		Dictionary<string, Candidate> candidates = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);

		string declaredProcessType = (input.ProcessType ?? "").Trim();
		PartTypeMatch textType = ResolvePartType(input, false, false, false, false, false, false, false);
		var profileEvidence = ProcessStructuralProfilePolicy.Inspect(input);
		bool declaredProfile = !profileEvidence.HasConflict && (IsStructuralProfileType(declaredProcessType)
			|| textType.Type == PartTypes.Profile);
		bool stockFormed = textType.Type == PartTypes.Casting || textType.Type == PartTypes.Forging
			|| textType.Type == PartTypes.Plastic || textType.Type == PartTypes.Additive;
		bool featureSheetMetal = HasTypeToken(facts, SheetMetalTypes) || HasText(facts, "钣金", "平板型式", "基体法兰", "基体-法兰");
		// A document-level profile label does not erase a separate sheet body.
		bool sheetMetal = !declaredProfile && (featureSheetMetal || geometry.IsSheetLike)
			|| (geometry.IsSheetLike && geometry.SolidBodyCount > 1 && geometry.SheetBodyCount < geometry.SolidBodyCount);
		bool featureBend = HasTypeToken(processFacts, BendTypes) || HasText(processFacts, "折弯", "边线法兰", "斜接法兰", "褶边", "卷边", "转折");
		bool weldment = HasTypeToken(facts, WeldmentTypes) || HasText(facts, "结构构件", "焊件");
		bool weld = HasTypeToken(facts, WeldTypes) || HasText(facts, "焊缝", "焊接");
		bool hole = HasTypeToken(facts, HoleTypes) || HasText(facts, "孔向导", "异型孔", "钻孔");
		bool counterSink = HasTypeToken(facts, CounterSinkTypes) || HasText(facts, "沉孔", "沉头孔", "锪孔") || facts.Any(fact => WizardHoleHasRecess(fact.HoleWizardType));
		bool thread = HasTypeToken(facts, ThreadTypes) || HasText(facts, "螺纹", "攻丝", "攻牙") || facts.Any(fact => WizardHoleIsTapped(fact.HoleWizardType));
		bool revolve = HasTypeToken(facts, RevolveTypes) || HasText(facts, "旋转凸台", "旋转切除", "车削");
		bool onlyStraightTubes = geometry.SolidBodyCount > 0 && geometry.StraightTubeBodyCount == geometry.SolidBodyCount;
		// A stock tube extrusion describes its section, not a milling operation.
		// Keep cuts and explicit machining evidence even when the final body is straight.
		bool milling = HasMillingType(facts, onlyStraightTubes || stockFormed || declaredProfile ? MillingTypes.Except(StockExtrusionTypes).ToArray() : MillingTypes)
			|| HasText(facts, "拉伸切除", "铣削") || (!onlyStraightTubes && !stockFormed && !declaredProfile && HasText(facts, "拉伸凸台"));
		bool louver = HasTypeToken(facts, "louver", "louvers") || HasText(facts, "百叶");
		bool dome = HasTypeToken(facts, "dome") || HasText(facts, "凸包", "冲压凸台");
		bool bridge = HasText(facts, "冲桥", "桥形");
		bool forming = HasTypeToken(facts, FormingTypes) || HasText(facts, "百叶", "凸包", "冲桥", "桥形", "加强筋", "成形工具", "成型工具");
		bool namedForming = louver || dome || bridge;

		if (sheetMetal)
		{
			if (forming) Add(candidates, "数冲", 10, 93, "检测到百叶、凸包、冲桥或成形工具，需数冲成形");
			else if (featureSheetMetal) Add(candidates, "激光下料", 10, 94, "检测到钣金基体或平板型式");
			else Add(candidates, "激光下料", 10, 88, GeometrySheetEvidence(geometry));
			if (geometry.CutoutCount > 0) Add(candidates, "激光下料", 10, 88, "相邻壁面拓扑检测到 " + geometry.CutoutCount + " 个独立闭合平面内轮廓；" + (geometry.CutoutCountReliable ? "总数已核对" : "总数仍需展开复核"));
			if (config.IncludeDeburr) Add(candidates, config.DeburrProcessName, 15, 88, "钣金下料后的可配置去毛刺工序");
		}
		if (weldment && (!sheetMetal || geometry.StraightTubeBodyCount > 0 || geometry.SolidBodyCount > geometry.SheetBodyCount))
		{
			Add(candidates, "锯切下料", 10, 91, "检测到焊件或结构构件");
		}
		else if (geometry.StraightTubeBodyCount > 0 || declaredProfile)
		{
			string evidence = IsStructuralProfileType(declaredProcessType)
				? "工序类型属性已指定型材类型“" + declaredProcessType + "”"
				: string.IsNullOrWhiteSpace(textType.Evidence) ? "实体几何识别为闭合等截面直管" : textType.Evidence;
			Add(candidates, "锯切下料", 10, declaredProfile ? 94 : 89,
				declaredProfile ? evidence : "实体几何检测到闭合等截面直管，截面圆角不计为折弯");
			if (config.IncludeDeburr) Add(candidates, config.DeburrProcessName, 15, 88, "型材下料后的可配置去毛刺工序");
		}

		if (revolve) Add(candidates, "车削", 20, 88, "检测到旋转类特征");
		if (!sheetMetal && milling) Add(candidates, "铣削", 22, 78, "检测到实体拉伸、切除或轮廓加工特征");
		if (hole) Add(candidates, "钻孔", 30, 91, "检测到孔向导或孔特征");
		if (counterSink) Add(candidates, "沉孔", 35, 94, "检测到沉孔、沉头孔或锪孔特征");
		if (thread) Add(candidates, ThreadProcessName(facts), 40, 90, "检测到螺纹特征；只有明确攻牙/攻丝或螺纹孔向导定义才判为攻丝");
		if (louver) Add(candidates, "冲百叶窗", 45, 97, "原生 Louver 成形特征或百叶语义证据");
		if (dome) Add(candidates, "打凸包", 45, 97, "原生 Dome 成形特征或凸包语义证据");
		if (bridge) Add(candidates, "冲桥", 45, 97, "冲桥/桥形语义证据");
		if (forming && !namedForming) Add(candidates, "冲压成形", 45, 92, "检测到成形工具或加强筋特征");
		if (!sheetMetal && HasType(processFacts, "chamfer")) Add(candidates, "倒角", 32, 94, "检测到原生 Chamfer 倒角特征");
		if (processFacts.Any(fact => string.Equals(fact.TypeName, "Hem", StringComparison.OrdinalIgnoreCase) && fact.HemType == (int)swHemTypes_e.swHemTypeClosed)) Add(candidates, "压死边", 52, 96, "原生 Hem 褶边类型明确为闭合死边");
		if (HasType(processFacts, "jog")) Add(candidates, "段差折弯", 53, 94, "检测到原生 Jog 转折特征");
		if (featureBend) Add(candidates, "折弯", 50, 94, "检测到钣金折弯或法兰特征");
		else if (geometry.BendCount > 0) Add(candidates, "折弯", 50, 89, "实体几何检测到 " + geometry.BendCount + " 组同轴内外圆柱折弯面");
		bool declaredWeld = ProcessManufacturingPlan.HasWeldingEvidence(input);
		if (weld || declaredWeld || (weldment && geometry.SolidBodyCount > 1))
			Add(candidates, "焊接", 60, weld ? 93 : declaredWeld ? 97 : 60,
				weld ? "检测到焊缝特征；所需折弯完成后再焊接" : declaredWeld ? "类型/工序属性或特征明确要求焊接；先完成所需折弯" : "多个结构构件的组焊建议，实际连接方式仍须确认");
		// Press-fit, pressed pins and weld rivets sit between tapping and forming.
		FastenerProcesses.Add(candidates, processFacts, geometry);
		AddDeclaredProcessType(candidates, declaredProcessType, config);
		AddOutsourceCandidate(candidates, input);
		if (declaredProfile || onlyStraightTubes)
		{
			if (config.IncludeInspection) Add(candidates, config.InspectionProcessName, 75, 86, "型材下料后的标准检验工序");
			if (config.IncludePackaging) Add(candidates, config.PackagingProcessName, 100, 82, "型材成品可配置收尾工序");
		}

		foreach (ProcessFeatureFact fact in processFacts)
		{
			string text = ((fact.Name ?? "") + " " + (fact.TypeName ?? "")).ToLowerInvariant();
			AddNamedProcessHints(candidates, text, fact.Name, !string.Equals(fact.TypeName, "Hem", StringComparison.OrdinalIgnoreCase) || fact.HemType == (int)swHemTypes_e.swHemTypeClosed);
		}

		string explicitSurfaceProperty = (input.SurfaceTreatment ?? "").Trim();
		if (!sheetMetal && explicitSurfaceProperty.Length > 0)
		{
			RemoveInferredSurfaceCandidates(candidates);
			if (candidates.Count == 0 && geometry.SolidBodyCount > 0)
			{
				Add(candidates, "机加工", 20, 55, "检测到非等厚薄板实体，按通用机加工待复核");
			}
			ResolvedSurface explicitSurface = ResolveSurface(input, config);
			int explicitIndex = 0;
			foreach (string name in SplitExplicitProcessNames(explicitSurface.Name))
				Add(candidates, name, 80 + explicitIndex++, explicitSurface.Confidence, explicitSurface.Evidence);
		}

		if (sheetMetal)
		{
			int surfaceOrder = 80;
			if (config.IncludeInspection && !config.InspectionAfterSurface) Add(candidates, config.InspectionProcessName, 75, 84, "钣金标准检验工序；当前设置为表面处理前检验");
			if (explicitSurfaceProperty.Length > 0)
			{
				RemoveInferredSurfaceCandidates(candidates);
				ResolvedSurface surface = ResolveSurface(input, config);
				int explicitIndex = 0;
				foreach (string name in SplitExplicitProcessNames(surface.Name))
					Add(candidates, name, surfaceOrder + explicitIndex++, surface.Confidence, surface.Evidence);
			}
			else if (!HasExplicitSurfaceCandidate(candidates))
			{
				ResolvedSurface surface = ResolveSurface(input, config);
				int fallbackIndex = 0;
				foreach (string name in SplitExplicitProcessNames(surface.Name))
					Add(candidates, name, surfaceOrder + fallbackIndex++, surface.Confidence, surface.Evidence);
			}
			if (config.IncludeInspection && config.InspectionAfterSurface) Add(candidates, config.InspectionProcessName, 90, 84, "钣金标准检验工序；当前设置为表面处理后检验");
			if (config.IncludePackaging) Add(candidates, config.PackagingProcessName, 100, 82, "钣金成品可配置收尾工序");
			if (config.IncludeShipping) Add(candidates, config.ShippingProcessName, 110, 82, "钣金成品可配置交付工序");
		}

		if (candidates.Count == 0)
		{
			// No machining evidence at all: fall back to the part type chain so a
			// declared or named type still produces an editable starting point.
			PartTypeMatch partType = ResolvePartType(input, sheetMetal, weldment, weld, geometry.StraightTubeBodyCount > 0, revolve, milling, false);
			IReadOnlyList<string> chain = ProcessRecognitionConfig.ResolveTypeChain(config, partType.Type);
			if (chain.Count > 0)
			{
				return FinalizeProcesses(chain.Select((string step, int index) => new RecognizedProcess
				{
					Name = step,
					Confidence = Math.Max(50, partType.Confidence - index),
					Evidence = partType.Evidence + "，按该类型默认工序链生成"
				}).ToList(), config);
			}
			if (geometry.SolidBodyCount > 0)
			{
				return FinalizeProcesses(new[]
				{
					new RecognizedProcess { Name = "机加工", Confidence = 55, Evidence = "检测到非等厚薄板实体，未命中标准件或专用工艺规则，按通用机加工待复核" }
				}, config);
			}
			return FinalizeProcesses(new[]
			{
				new RecognizedProcess { Name = "未识别", Confidence = 0, Evidence = "特征树与实体几何均未命中现有规则，请手动选择或填写工序" }
			}, config);
		}

		return FinalizeProcesses(candidates.Values
			.OrderBy(candidate => candidate.Order)
			.ThenBy(candidate => candidate.Name, StringComparer.CurrentCulture)
			.Select(candidate => new RecognizedProcess
			{
				Name = candidate.Name,
				Confidence = candidate.Confidence,
				Evidence = string.Join("；", candidate.Evidence.Distinct(StringComparer.CurrentCultureIgnoreCase))
			})
			.ToList(), config);
	}

	private static IReadOnlyList<RecognizedProcess> RecognizeAssembly(ProcessRecognitionInput input, ProcessRecognitionConfig config)
	{
		IReadOnlyList<RecognizedProcess> declared = RecognizeDeclaredSupplyType(input);
		if (declared.Count > 0) return ResolvePurchasedDefaultGroup(input, config) ?? declared;
		var combination = ProcessRecognitionConfig.ResolveCombination(config, input.ProcessType);
		if (combination.Count > 0) return combination;
		ProcessTypeGroupMatch defaultGroup = ProcessTypeGroupPolicy.Resolve(config, input);
		if (defaultGroup.Matched)
		{
			if (!string.IsNullOrWhiteSpace(input.ExistingProcess))
				return input.ExistingProcess.Split(new[] { config.Separator, ">", "→" }, StringSplitOptions.RemoveEmptyEntries).Select(name => new RecognizedProcess { Name = name, Confidence = 100, Evidence = "模型已有工序，类型默认组未覆盖" }).ToList();
			return ProcessRecognitionConfig.ResolveCombination(config, defaultGroup.Group);
		}
		string type = (input.PartType ?? "").Trim();
		string title = (input.DocumentTitle ?? "").Trim();
		string assemblyText = type + " " + (input.ProcessType ?? "") + " " + title;
		bool groupWeld = ContainsAny(input.ProcessType ?? "", "焊接")
			|| ContainsAny(assemblyText, "组焊", "焊接件", "焊接总成", "焊接组件", "焊件", "点焊", "碰焊", "氩弧焊", "二保焊", "气保焊", "激光焊");
		List<RecognizedProcess> result = new List<RecognizedProcess>();
		if (groupWeld)
		{
			string weldProcess = ContainsAny(assemblyText, "点焊", "碰焊") ? "点焊"
				: ContainsAny(assemblyText, "氩弧焊") ? "氩弧焊"
				: ContainsAny(assemblyText, "二保焊", "气保焊") ? "二保焊"
				: ContainsAny(assemblyText, "激光焊") ? "激光焊"
				: "组焊";
			result.Add(new RecognizedProcess { Name = weldProcess, Confidence = type.Length > 0 ? 99 : 91, Evidence = type.Length > 0 ? "装配体类型属性已明确标记为焊接总成" : "装配体名称命中组焊/焊接总成规则" });
			result.Add(new RecognizedProcess { Name = "打磨", Confidence = 86, Evidence = "组焊件焊后标准清理和打磨工序" });
		}
		else
		{
			result.Add(new RecognizedProcess { Name = "装配", Confidence = 82, Evidence = "装配体按通用装配工序处理；可用类型属性标记组焊或外购" });
		}
		string outsource = PropertyFlag(input.Outsource, "外加工");
		if (outsource.Length > 0) result.Add(new RecognizedProcess { Name = outsource, Confidence = 99, Evidence = "装配体外加工属性已明确指定“" + input.Outsource + "”" });
		result = AppendExplicitSurface(result, input, config).ToList();
		if (config?.IncludeInspection != false) result.Add(new RecognizedProcess { Name = config.InspectionProcessName, Confidence = 86, Evidence = groupWeld ? "组焊总成尺寸和焊缝检验" : "装配完成检验" });
		return FinalizeProcesses(result, config);
	}

	private static IReadOnlyList<RecognizedProcess> AugmentDefaultGroup(IReadOnlyList<RecognizedProcess> group, ProcessRecognitionInput input, ProcessRecognitionConfig config)
	{
		var result = group.Select(p => new RecognizedProcess { Name = p.Name, Confidence = p.Confidence, Evidence = p.Evidence }).ToList();
		var geometry = input.Geometry ?? new ProcessGeometryFact();
		var facts = (input.Features ?? Array.Empty<ProcessFeatureFact>()).Where(f => f != null && !f.IsSuppressed && !IsBendContainer(f)).ToList();
		void Insert(string name, string evidence, int order, int confidence = 94)
		{
			if (result.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) return;
			int Rank(string existing) => existing == "折弯" ? 50 : existing.IndexOf("焊", StringComparison.Ordinal) >= 0 ? 60
				: existing == "打磨" ? 70 : existing.Contains("检验") || existing.Contains("入库") || existing.Contains("包装") || existing.Contains("出货") || existing == "表面处理" ? 80 : 20;
			int at = result.FindIndex(p => Rank(p.Name) > order);
			var process = new RecognizedProcess { Name = name, Confidence = Math.Max(50, Math.Min(99, confidence)), Evidence = "类型默认组补充模型证据：" + evidence };
			if (at < 0) result.Add(process); else result.Insert(at, process);
		}
		if (geometry.IsSheetLike) Insert("激光下料", "另有板件实体需要单独下料", 10);
		if (geometry.StraightTubeBodyCount > 0) Insert("锯切下料", "另有管材实体需要单独下料", 10);
		if (HasType(facts, MillingTypes.Except(StockExtrusionTypes).ToArray()) || HasText(facts, "拉伸切除", "铣削")) Insert("铣削", "型材切除或明确加工特征", 22);
		if (HasType(facts, HoleTypes)) Insert("钻孔", "孔向导/孔特征", 30);
		if (HasType(facts, ThreadTypes) || facts.Any(f => WizardHoleIsTapped(f.HoleWizardType))) Insert(ThreadProcessName(facts), "螺纹特征/孔向导定义", 40);
		if (HasType(facts, BendTypes) || (geometry.GeometryComplete && geometry.BendCount > 0)) Insert("折弯", "真实折弯特征或几何", 50);
		if (ProcessManufacturingPlan.HasWeldingEvidence(input)) Insert("焊接", "明确焊接声明/特征，折弯后焊接", 60);
		var fasteners = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
		FastenerProcesses.Add(fasteners, facts, geometry);
		foreach (var candidate in fasteners.Values) Insert(candidate.Name, string.Join("；", candidate.Evidence), candidate.Order, candidate.Confidence);
		return AppendExplicitSurface(result, input, config);
	}

	private static IReadOnlyList<RecognizedProcess> FinalizeProcesses(IEnumerable<RecognizedProcess> processes, ProcessRecognitionConfig config)
	{
		Dictionary<string, string> mappings = ProcessRecognitionConfig.ParseProcessNameMappings(config?.ProcessNameMappings)
			.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.CurrentCultureIgnoreCase);
		List<RecognizedProcess> result = (processes ?? Array.Empty<RecognizedProcess>()).Where(process => process != null).ToList();
		foreach (string process in ProcessRecognitionConfig.ParseAdditionalProcesses(config?.AdditionalProcesses))
		{
			result.Add(new RecognizedProcess { Name = process, Confidence = 80, Evidence = "设置中启用的自定义补充工序" });
		}
		return result
			.Select(process =>
			{
				string name = (process.Name ?? "").Trim();
				if (mappings.TryGetValue(name, out string replacement)) name = replacement;
				return new RecognizedProcess { Name = name, Confidence = process.Confidence, Evidence = process.Evidence };
			})
			.Where(process => process.Name.Length > 0)
			.GroupBy(process => process.Name, StringComparer.CurrentCultureIgnoreCase)
			.Select(group => new RecognizedProcess { Name = group.Key, Confidence = group.Max(process => process.Confidence),
				Evidence = string.Join("；", group.Select(process => process.Evidence).Where(evidence => !string.IsNullOrWhiteSpace(evidence)).Distinct()) })
			.ToList();
	}

	private static void AddDeclaredProcessType(Dictionary<string, Candidate> candidates, string processType, ProcessRecognitionConfig config)
	{
		string value = (processType ?? "").Trim();
		if (value.Length == 0) return;
		string evidence = "自定义属性“" + config.ProcessTypePropertyName + "”已明确指定“" + value + "”";
		if (ContainsAny(value, "焊接封角")) Add(candidates, "焊接封角", 60, 99, evidence);
		else if (ContainsAny(value, "焊接")) Add(candidates, "焊接", 60, 99, evidence);
		if (ContainsAny(value, "打磨")) Add(candidates, "打磨", 70, 99, evidence);
		if (ContainsAny(value, "装配")) Add(candidates, "装配", 65, 99, evidence);
		bool structuralProfile = IsStructuralProfileType(value);
		if (structuralProfile)
		{
			Add(candidates, "锯切下料", 10, 99, evidence);
			if (config.IncludeDeburr) Add(candidates, config.DeburrProcessName, 15, 94, "型材下料后的可配置去毛刺工序");
		}
		if (config.IncludeInspection && (ContainsAny(value, "焊接", "装配") || structuralProfile))
			Add(candidates, config.InspectionProcessName, 75, 90, "工序类型属性对应的标准检验工序");
	}

	private static bool IsStructuralProfileType(string value)
		=> PartTypeCatalog.FromText(value, value).Type == PartTypes.Profile;

    private static IReadOnlyList<RecognizedProcess> ResolvePurchasedDefaultGroup(ProcessRecognitionInput input, ProcessRecognitionConfig config)
    {
        if (!PurchasedPartPolicy.IsPurchased(input)) return null;
        var match = ProcessTypeGroupPolicy.Resolve(config, input);
        if (!match.Matched) return null;
        if (!string.IsNullOrWhiteSpace(input.ExistingProcess))
            return input.ExistingProcess.Split(new[] { config.Separator, ">", "→" }, StringSplitOptions.RemoveEmptyEntries).Select(name => new RecognizedProcess { Name = name, Confidence = 100, Evidence = "已有工序保留，外购默认组未覆盖" }).ToList();
        return FinalizeProcesses(AppendExplicitSurface(ProcessRecognitionConfig.ResolveCombination(config, match.Group), input, config), config);
    }

	private static IReadOnlyList<RecognizedProcess> RecognizeDeclaredSupplyType(ProcessRecognitionInput input)
	{
		string type = (input.PartType ?? "").Trim();
		string title = (input.DocumentTitle ?? "").Trim();
		PartTypeMatch classification = PartTypeCatalog.FromText(type, title, input.ProfileDescription);
		PartTypeMatch hardware = PurchasedHardwareCatalog.Match(input);
		if (!hardware.IsEmpty) classification = hardware;
		bool? purchaseFlag = PurchasedPartPolicy.ExplicitFlag(input.Purchase);
		bool purchased = purchaseFlag == true || (purchaseFlag != false && (classification.Type == PartTypes.Purchased || classification.Type == PartTypes.FootPad));
		bool standard = purchaseFlag != false && (classification.Type == PartTypes.Standard || ContainsAny(type, "标准件", "紧固件") || LooksLikeStandardHardware(title));
		bool outsourced = purchaseFlag != true && classification.Type == PartTypes.Outsourced;
		if (!purchased && !standard && !outsourced) return Array.Empty<RecognizedProcess>();
		if (outsourced)
		{
			return new[]
			{
				new RecognizedProcess { Name = "外加工", Confidence = 97, Evidence = "零件类型或文件名已明确标记为外加工件" },
				new RecognizedProcess { Name = "回厂检验", Confidence = 84, Evidence = "外加工件回厂后需核对尺寸和加工要求" },
				new RecognizedProcess { Name = "入库", Confidence = 82, Evidence = "外加工件检验后入库" }
			};
		}
		string evidence = purchaseFlag == true ? "外购属性明确标记为是"
			: classification.Evidence + "；疑似外购/标准件，外购标记须单独确认";
		if (input.IsAssembly) evidence += "；外购装配总成按整件供应，不把内部零件工序汇总成制造路线";
		return new[]
		{
			new RecognizedProcess { Name = "外购", Confidence = purchased ? 99 : 94, Evidence = evidence },
			new RecognizedProcess { Name = "来料检验", Confidence = 88, Evidence = "标准件/外购件入厂检验" },
			new RecognizedProcess { Name = "入库", Confidence = 86, Evidence = "标准件/外购件检验合格后入库" }
		};
	}

	private static IReadOnlyList<RecognizedProcess> AppendExplicitSurface(
		IReadOnlyList<RecognizedProcess> processes,
		ProcessRecognitionInput input,
		ProcessRecognitionConfig config)
	{
		string value = (input?.SurfaceTreatment ?? "").Trim();
		if (value.Length == 0)
		{
			return processes;
		}
		List<RecognizedProcess> result = processes.ToList();
		foreach (string name in SplitExplicitProcessNames(value))
		{
			if (result.Any(process => string.Equals(process.Name, name, StringComparison.CurrentCultureIgnoreCase))) continue;
			RecognizedProcess surface = new RecognizedProcess
			{
				Name = name,
				Confidence = 99,
				Evidence = "自定义属性“" + config.SurfacePropertyName + "”已明确指定"
			};
			int insertAt = result.FindIndex(process => ContainsAny(process.Name ?? "", "检验", "入库", "包装", "出货"));
			if (insertAt < 0) result.Add(surface);
			else result.Insert(insertAt, surface);
		}
		return result;
	}

	// Surface treatment is often entered as a small route, for example
	// “清洗+烤漆+丝印” or “拉丝/钝化”. Keep every explicit step as an
	// editable process row so downstream linking and ordering can work on it.
	private static IReadOnlyList<string> SplitExplicitProcessNames(string value)
	{
		return Regex.Split(value ?? "", @"[|>→/、,，;；+\r\n]+")
			.Select(item => item.Trim())
			.Where(item => item.Length > 0)
			.Distinct(StringComparer.CurrentCultureIgnoreCase)
			.ToArray();
	}

	public static bool IsDeclaredPurchasedOrStandard(ProcessRecognitionInput input)
    {
        if (input == null || PurchasedPartPolicy.ExplicitFlag(input.Purchase) == false) return false;
        return PurchasedPartPolicy.IsPurchased(input) || LooksLikeStandardHardware(input.DocumentTitle);
    }

	// Declared type first, then file-name keywords, then geometry. Callers use
	// this both to write the "类型" property and to pick a fallback chain.
	public static PartTypeMatch ResolvePartType(
		ProcessRecognitionInput input,
		bool sheetMetal,
		bool weldment,
		bool weld,
		bool tubeLike,
		bool revolve,
		bool milling,
		bool isAssembly)
	{
		PartTypeMatch declared = PartTypeCatalog.FromText(input?.PartType, input?.DocumentTitle, input?.ProfileDescription);
		PartTypeMatch hardware = PurchasedHardwareCatalog.Match(input);
		if (PurchasedPartPolicy.ExplicitFlag(input?.Purchase) == true)
		{
			if (!hardware.IsEmpty) return hardware;
			if (PurchasedPartPolicy.IsSupplyType(declared.Type)) return declared;
			return new PartTypeMatch { Type = PartTypes.Purchased, Confidence = 99, Evidence = "当前引用配置明确标记为外购，整件按供应身份分类" };
		}
		if (!hardware.IsEmpty) return hardware;
		if (!declared.IsEmpty && declared.Type != PartTypes.Other && !(PurchasedPartPolicy.ExplicitFlag(input?.Purchase) == false && PurchasedPartPolicy.IsSupplyType(declared.Type)))
		{
			bool genericDeclaration = string.IsNullOrWhiteSpace(input?.PartType) || string.Equals(input.PartType.Trim(), PartTypes.Other, StringComparison.OrdinalIgnoreCase);
			if (genericDeclaration && sheetMetal && weld && new[] { PartTypes.SheetMetal, PartTypes.Weldment, PartTypes.WeldAssembly }.Contains(declared.Type))
				return new PartTypeMatch { Type = PartTypes.Mixed, Confidence = Math.Max(74, declared.Confidence), Evidence = declared.Evidence + "；同时有钣金与明确焊接证据，细分为钣金焊接件" };
			return declared;
		}
		if (tubeLike && !weld && input?.Geometry?.SolidBodyCount == 1) weldment = false;
		return PartTypeCatalog.FromGeometry(sheetMetal, weldment, weld, tubeLike, revolve, milling, isAssembly);
	}

	private static void AddOutsourceCandidate(Dictionary<string, Candidate> candidates, ProcessRecognitionInput input)
	{
		string type = (input.PartType ?? "").Trim();
		string value = (input.Outsource ?? "").Trim();
		if (ContainsAny(type, "外加工件", "委外件"))
		{
			Add(candidates, "外加工", 70, 99, "零件类型已明确标记为外加工件");
			return;
		}
		if (!IsTrueFlag(value)) return;
		string process = PropertyFlag(value, "外加工");
		int order = ContainsAny(process, "表面处理", "表处", "拉丝", "喷粉", "喷塑", "喷漆", "烤漆", "电镀", "镀锌", "电泳", "钝化", "阳极", "抛光", "热处理") ? 80 : 70;
		Add(candidates, process, order, 99, "自定义外加工属性已明确指定“" + value + "”");
	}

	private static string PropertyFlag(string value, string fallback = "")
	{
		string normalized = (value ?? "").Trim();
		string lower = normalized.ToLowerInvariant();
		if (normalized.Length == 0 || lower == "否" || lower == "false" || lower == "no" || lower == "×" || lower == "关闭" || lower == "无" || lower == "0"
			|| ContainsAny(normalized, "非外购", "不外购", "否外购", "非采购", "不采购", "自制件", "自制", "自产", "自加工", "内制", "内加工", "厂内加工")) return "";
		if (lower == "是" || lower == "true" || lower == "yes" || lower == "√" || lower == "开启" || lower == "1") return fallback;
		return normalized;
	}

	private static bool IsTrueFlag(string value, string fallback = "外加工")
	{
		return PropertyFlag(value, fallback).Length > 0;
	}

	private static bool LooksLikeStandardHardware(string title)
	{
		string text = (title ?? "").ToLowerInvariant();
		if (text.Length == 0) return false;
		if (ContainsAny(text, "安装板", "支架", "组件", "总成", "底板", "面板", "螺母板", "螺钉板")) return false;
		return text.StartsWith("gb", StringComparison.OrdinalIgnoreCase)
			|| text.StartsWith("din", StringComparison.OrdinalIgnoreCase)
			|| text.StartsWith("iso", StringComparison.OrdinalIgnoreCase)
			|| ContainsAny(text, "螺栓", "螺母", "螺钉", "螺丝", "垫圈", "垫片", "轴承", "卡簧", "挡圈", "开口销", "圆柱销", "弹簧销", "铆钉", "铰链", "合页", "气弹簧", "脚轮", "拉手", "导轨", "风扇", "开关", "端子", "rivet", "bolt", "screw", "nut", "washer", "bearing", "hinge");
	}

	private static string GeometrySheetEvidence(ProcessGeometryFact geometry)
	{
		string thickness = geometry.ThicknessMm > 0 ? geometry.ThicknessMm.ToString("0.###") + "mm" : "未知板厚";
		return "实体几何检测到等厚薄板（" + thickness + "），无需依赖特征树";
	}

	public static string Join(IEnumerable<string> processes, string separator)
	{
		string actualSeparator = string.IsNullOrEmpty(separator) ? "-" : separator;
		return string.Join(actualSeparator, (processes ?? Enumerable.Empty<string>())
			.Select(value => (value ?? "").Trim())
			.Where(value => value.Length > 0 && !value.Equals("未识别", StringComparison.OrdinalIgnoreCase) && !value.Equals("未知", StringComparison.OrdinalIgnoreCase))
			.Distinct(StringComparer.CurrentCultureIgnoreCase));
	}

	public static string BuildCraftDescription(IEnumerable<RecognizedProcess> processes)
	{
		return string.Join("；", (processes ?? Enumerable.Empty<RecognizedProcess>())
			.Where(process => process != null && !string.IsNullOrWhiteSpace(process.Name) && process.Name != "未识别" && process.Name != "未知")
			.Select(process =>
			{
				string evidence = (process.Evidence ?? "").Trim();
				if (evidence.Length > 90) evidence = evidence.Substring(0, 90) + "…";
				string confidence = process.Confidence > 0 ? process.Confidence + "%" : "手动";
				return process.Name.Trim() + "（" + confidence + (evidence.Length == 0 ? "" : "，" + evidence) + "）";
			}));
	}

	public static IReadOnlyList<KeyValuePair<string, string>> BuildPropertyValues(
		IEnumerable<RecognizedProcess> processes,
		ProcessGeometryFact geometry,
		ProcessRecognitionConfig config,
		string summaryPropertyName = null,
		string separator = null,
		ProcessRecognitionInput input = null)
	{
		config ??= new ProcessRecognitionConfig();
		ProcessRecognitionConfig.Normalize(config);
		string processProperty = string.IsNullOrWhiteSpace(summaryPropertyName) ? config.SummaryPropertyName : summaryPropertyName.Trim();
		string summary = Join((processes ?? Enumerable.Empty<RecognizedProcess>()).Select(process => process?.Name), separator ?? config.Separator);
		List<KeyValuePair<string, string>> values = new List<KeyValuePair<string, string>>
		{
			new KeyValuePair<string, string>(processProperty, summary)
		};
		values.AddRange(ProcessPartClassification.PropertySuggestion(input, config, processes));
		if (config.WriteCraftDescription)
		{
			values.Add(new KeyValuePair<string, string>(config.CraftDescriptionPropertyName, BuildCraftDescription(processes)));
		}
		if (config.WriteGeometryProperties && geometry?.GeometryComplete == true && geometry.IsSheetLike)
		{
			// A representative thickness cannot describe mixed gauges or a
			// sheet/profile combination as one uniform sheet part.
			if (HasUniformSheetThickness(geometry))
				values.Add(new KeyValuePair<string, string>(config.ThicknessPropertyName, geometry.ThicknessMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
			values.Add(new KeyValuePair<string, string>(config.BendCountPropertyName, geometry.BendCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
			if (geometry.CutoutCountReliable)
				values.Add(new KeyValuePair<string, string>(config.CutoutCountPropertyName, geometry.CutoutCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
		}
		if (values.GroupBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase).Any(group => group.Select(pair => pair.Value).Distinct().Count() > 1))
			throw new InvalidOperationException("工序、工艺说明或几何属性使用了重复名称，请在设置中分开命名后再写入。");
		return values
			.Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
			.GroupBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase)
			.Select(group => group.Last())
			.ToList();
	}

	internal static bool HasUniformSheetThickness(ProcessGeometryFact geometry)
	{
		if (geometry == null || !geometry.GeometryComplete || !geometry.IsSheetLike || geometry.SolidBodyCount != geometry.SheetBodyCount) return false;
		return (geometry.BodyFacts ?? Array.Empty<ProcessGeometryFact>()).All(body => body != null && body.GeometryComplete
			&& body.IsSheetLike && Math.Abs(body.ThicknessMm - geometry.ThicknessMm) <= 0.02);
	}

	internal static IReadOnlyList<KeyValuePair<string, string>> BuildManualProcessGroupValues(string process, string groupName, ProcessRecognitionConfig config)
	{
		config ??= new ProcessRecognitionConfig();
		ProcessRecognitionConfig.Normalize(config);
		string summary = Join((process ?? "").Split(new[] { config.Separator }, StringSplitOptions.RemoveEmptyEntries), config.Separator);
		if (summary.Length == 0) throw new InvalidOperationException("工序不能为空。");
		bool hasGroup = !string.IsNullOrWhiteSpace(groupName);
		if (hasGroup && string.Equals(config.ProcessTypePropertyName, config.SummaryPropertyName, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("工序类型与工序属性名称重复，请修改设置。");
		if (config.WriteCraftDescription && (string.Equals(config.CraftDescriptionPropertyName, config.SummaryPropertyName, StringComparison.OrdinalIgnoreCase)
			|| (hasGroup && string.Equals(config.CraftDescriptionPropertyName, config.ProcessTypePropertyName, StringComparison.OrdinalIgnoreCase))))
			throw new InvalidOperationException("工序类型、工序或工艺说明属性名称重复，请修改设置。");
		List<RecognizedProcess> steps = CombinationStepsForManual(summary, config.Separator);
		List<KeyValuePair<string, string>> values = new List<KeyValuePair<string, string>>();
		if (hasGroup) values.Add(new KeyValuePair<string, string>(config.ProcessTypePropertyName, groupName.Trim()));
		values.Add(new KeyValuePair<string, string>(config.SummaryPropertyName, summary));
		if (config.WriteCraftDescription) values.Add(new KeyValuePair<string, string>(config.CraftDescriptionPropertyName, BuildCraftDescription(steps)));
		return values;
	}

	internal static Dictionary<string, string> MergeManualProcessGroupValues(
		IDictionary<string, string> stagedValues,
		string process,
		string groupName,
		ProcessRecognitionConfig config)
	{
		var values = new Dictionary<string, string>(stagedValues ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, string> property in BuildManualProcessGroupValues(process, groupName, config))
			values[property.Key] = property.Value;
		return values;
	}

	internal static string NormalizeManualProcessSequence(string value, string separator)
	{
		string outputSeparator = string.IsNullOrEmpty(separator) ? "-" : separator;
		string[] separators = new[] { outputSeparator, ">", "→" }.Where(item => !string.IsNullOrEmpty(item)).Distinct(StringComparer.Ordinal).ToArray();
		return Join((value ?? "").Split(separators, StringSplitOptions.RemoveEmptyEntries), outputSeparator);
	}

	private static List<RecognizedProcess> CombinationStepsForManual(string summary, string separator)
		=> summary.Split(new[] { string.IsNullOrEmpty(separator) ? "-" : separator }, StringSplitOptions.RemoveEmptyEntries)
			.Select(name => new RecognizedProcess { Name = name.Trim(), Evidence = "用户确认的工序组" }).ToList();

	private static ResolvedSurface ResolveSurface(ProcessRecognitionInput input, ProcessRecognitionConfig config)
	{
		string propertyValue = (input.SurfaceTreatment ?? "").Trim();
		if (propertyValue.Length > 0)
		{
			return new ResolvedSurface(propertyValue, 99, "自定义属性“" + config.SurfacePropertyName + "”已明确指定");
		}

		if (config.SurfaceMode == SurfaceRecognitionModes.Fixed)
		{
			return new ResolvedSurface(config.DefaultSurfaceProcess, 80, "使用设置中的固定表面处理工序");
		}

		if (config.SurfaceMode == SurfaceRecognitionModes.Material)
		{
			string material = (input.Material ?? "").Trim();
			foreach (KeyValuePair<string, string> rule in ProcessRecognitionConfig.ParseSurfaceMaterialRules(config.SurfaceMaterialRules).OrderByDescending(rule => rule.Key.Length))
			{
				if (MatchesMaterialRule(material, rule.Key))
				{
					return new ResolvedSurface(rule.Value, 90, "材料“" + material + "”命中表处设置“" + rule.Key + "=" + rule.Value + "”");
				}
			}
		}

		string source = config.SurfaceMode == SurfaceRecognitionModes.Property
			? "属性“" + config.SurfacePropertyName + "”未填写"
			: "材料“" + (string.IsNullOrWhiteSpace(input.Material) ? "未指定" : input.Material) + "”未命中表处设置";
		return new ResolvedSurface(config.DefaultSurfaceProcess, 60, source + "，按设置回退为“" + config.DefaultSurfaceProcess + "”");
	}

	internal static bool MatchesMaterialRule(string material, string keyword)
	{
		if (string.IsNullOrWhiteSpace(keyword)) return false;
		if (keyword.Length <= 3 && keyword.All(character => character >= 'A' && character <= 'Z' || character >= 'a' && character <= 'z'))
			return System.Text.RegularExpressions.Regex.IsMatch(material ?? "", "(?<![A-Za-z])" + System.Text.RegularExpressions.Regex.Escape(keyword) + "(?![A-Za-z])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
		return (material ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static void AddNamedProcessHints(Dictionary<string, Candidate> candidates, string text, string featureName, bool inferHemCompression = true)
	{
		string evidence = string.IsNullOrWhiteSpace(featureName) ? "特征名称命中工序关键字" : "特征“" + featureName.Trim() + "”命中工序关键字";
		if (ContainsAny(text, "激光下料", "激光切割")) Add(candidates, "激光下料", 10, 97, evidence);
		if (ContainsAny(text, "水切割", "水刀", "水射流")) Add(candidates, "水切下料", 10, 97, evidence);
		if (ContainsAny(text, "等离子切割", "等离子下料")) Add(candidates, "等离子下料", 10, 97, evidence);
		if (ContainsAny(text, "火焰切割", "气割", "火焰下料")) Add(candidates, "火焰下料", 10, 96, evidence);
		if (ContainsAny(text, "数冲", "数控冲")) Add(candidates, "数冲", 10, 97, evidence);
		if (ContainsAny(text, "剪板", "剪料")) Add(candidates, "剪板", 10, 95, evidence);
		if (ContainsAny(text, "锯切", "锯料", "带锯", "型材锯", "圆锯", "锯床")) Add(candidates, "锯切下料", 10, 95, evidence);
		if (ContainsAny(text, "外发下料", "外协下料")) Add(candidates, "外发下料", 10, 97, evidence);
		if (ContainsAny(text, "割管")) Add(candidates, "割管", 12, 97, evidence);
		if (ContainsAny(text, "去毛刺")) Add(candidates, "去毛刺", 15, 97, evidence);
		if (ContainsAny(text, "车削", "车床")) Add(candidates, "车削", 20, 96, evidence);
		if (ContainsAny(text, "铣削", "铣床")) Add(candidates, "铣削", 22, 96, evidence);
		if (ContainsAny(text, "钻孔", "孔向导", "异型孔")) Add(candidates, "钻孔", 30, 96, evidence);
		if (ContainsAny(text, "沉孔", "沉头孔", "锪孔")) Add(candidates, "沉孔", 35, 97, evidence);
		if (ContainsAny(text, "攻丝", "攻牙")) Add(candidates, "攻丝", 40, 96, evidence);
		else if (ContainsAny(text, "螺纹")) Add(candidates, "螺纹加工", 40, 90, evidence + "；内外螺纹加工方法仍待工艺确认");
		if (ContainsAny(text, "热熔孔", "热熔钻")) Add(candidates, "热熔孔", 41, 97, evidence);
		if (ContainsAny(text, "翻孔")) Add(candidates, "翻孔", 42, 97, evidence);
		if (ContainsAny(text, "压铆")) Add(candidates, "压铆", 45, 97, evidence);
		if (ContainsAny(text, "冲桥", "桥形")) Add(candidates, "冲桥", 45, 97, evidence);
		if (ContainsAny(text, "百叶")) Add(candidates, "冲百叶窗", 45, 97, evidence);
		if (ContainsAny(text, "凸包", "冲压凸台")) Add(candidates, "打凸包", 45, 97, evidence);
		if (ContainsAny(text, "冲压成形", "冲压成型")) Add(candidates, "冲压成形", 45, 96, evidence);
		if (ContainsAny(text, "龙门铣", "龙门加工中心", "龙门铣床")) Add(candidates, "龙门铣", 22, 97, evidence);
		if (ContainsAny(text, "装配", "组装", "总装", "试装")) Add(candidates, "装配", 90, 96, evidence);
		if (ContainsAny(text, "折弯", "边线法兰", "斜接法兰")) Add(candidates, "折弯", 50, 97, evidence);
		if (ContainsAny(text, "压死边", "死边") || (inferHemCompression && ContainsAny(text, "褶边"))) Add(candidates, "压死边", 52, 97, evidence);
		if (ContainsAny(text, "段差折弯", "段差", "z折")) Add(candidates, "段差折弯", 53, 97, evidence);
		if (ContainsAny(text, "种焊钉", "种焊螺钉")) Add(candidates, "种焊钉", 55, 97, evidence);
		if (ContainsAny(text, "点焊", "碰焊")) Add(candidates, "点焊", 60, 97, evidence);
		else if (ContainsAny(text, "氩弧焊")) Add(candidates, "氩弧焊", 60, 97, evidence);
		else if (ContainsAny(text, "二保焊", "气保焊")) Add(candidates, "二保焊", 60, 97, evidence);
		else if (ContainsAny(text, "激光焊")) Add(candidates, "激光焊", 60, 97, evidence);
		else if (ContainsAny(text, "焊接", "焊缝", "组焊")) Add(candidates, ContainsAny(text, "组焊") ? "组焊" : "焊接", 60, 97, evidence);
		if (ContainsAny(text, "拉铆")) Add(candidates, "拉铆", 65, 97, evidence);
		if (ContainsAny(text, "打磨", "磨光")) Add(candidates, "打磨", 70, 94, evidence);
		if (ContainsAny(text, "抛光")) Add(candidates, "抛光", 80, 96, evidence);
		if (ContainsAny(text, "表面处理", "表处")) Add(candidates, "表面处理", 80, 94, evidence);
		if (ContainsAny(text, "拉丝")) Add(candidates, "拉丝", 80, 96, evidence);
		if (ContainsAny(text, "喷粉")) Add(candidates, "喷粉", 80, 96, evidence);
		if (ContainsAny(text, "喷塑")) Add(candidates, "喷塑", 80, 96, evidence);
		if (ContainsAny(text, "喷漆", "烤漆")) Add(candidates, ContainsAny(text, "烤漆") ? "烤漆" : "喷漆", 80, 96, evidence);
		if (ContainsAny(text, "电镀", "镀锌")) Add(candidates, ContainsAny(text, "镀锌") ? "镀锌" : "电镀", 80, 96, evidence);
		if (ContainsAny(text, "电泳")) Add(candidates, "电泳", 80, 96, evidence);
		if (ContainsAny(text, "钝化")) Add(candidates, "钝化", 80, 96, evidence);
		if (ContainsAny(text, "阳极氧化", "阳极")) Add(candidates, "阳极氧化", 80, 96, evidence);
		if (ContainsAny(text, "热处理")) Add(candidates, "热处理", 76, 96, evidence);
		if (ContainsAny(text, "固溶处理")) Add(candidates, "固溶处理", 76, 96, evidence);
		if (ContainsAny(text, "时效处理", "人工时效", "自然时效")) Add(candidates, "时效处理", 77, 95, evidence);
		if (ContainsAny(text, "渗碳", "碳氮共渗")) Add(candidates, "渗碳处理", 77, 95, evidence);
		if (ContainsAny(text, "淬火", "调质")) Add(candidates, "淬火/调质", 77, 95, evidence);
		if (ContainsAny(text, "退火")) Add(candidates, "退火", 77, 94, evidence);
		if (ContainsAny(text, "注塑", "塑料成型", "塑胶成型", "挤出成型")) Add(candidates, "注塑/成型", 60, 94, evidence);
		if (ContainsAny(text, "铸造", "压铸", "砂型铸造", "熔模铸造")) Add(candidates, "铸造", 10, 94, evidence);
		if (ContainsAny(text, "锻造", "自由锻", "模锻", "热锻", "冷锻")) Add(candidates, "锻造", 10, 94, evidence);
		if (ContainsAny(text, "3d打印", "增材制造", "熔融沉积", "光固化打印", "粉末烧结", "选区熔化")
			|| Regex.IsMatch(text, @"(?<![a-z0-9])(?:slm|sls|sla|fdm)(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
			Add(candidates, "3D打印", 10, 96, evidence);
		if (ContainsAny(text, "去支撑", "去支撑结构")) Add(candidates, "去支撑", 15, 95, evidence);
		if (ContainsAny(text, "去浇口", "清砂", "落砂")) Add(candidates, "清砂/去浇口", 15, 93, evidence);
		if (ContainsAny(text, "丝印")) Add(candidates, "丝印", 85, 96, evidence);
		// Added with the process library extension. Each entry sits at the order
		// the step would realistically run, so the generated chain stays buildable.
		if (ContainsAny(text, "校平", "矫平", "整平")) Add(candidates, "校平", 16, 90, evidence);
		if (ContainsAny(text, "线切割", "慢走丝", "快走丝", "中走丝")) Add(candidates, "线切割", 24, 96, evidence);
		if (ContainsAny(text, "磨削", "磨床", "平面磨", "外圆磨", "研磨")) Add(candidates, "磨削", 26, 94, evidence);
		if (ContainsAny(text, "倒角")) Add(candidates, "倒角", 32, 93, evidence);
		if (ContainsAny(text, "钎焊", "铜焊", "锡焊", "火焰钎焊")) Add(candidates, "钎焊", 60, 95, evidence);
		if (ContainsAny(text, "卷圆", "滚圆", "卷板")) Add(candidates, "卷圆", 52, 94, evidence);
		if (ContainsAny(text, "清洗", "除油", "脱脂")) Add(candidates, "清洗", 76, 90, evidence);
		if (ContainsAny(text, "喷砂", "抛丸", "喷丸")) Add(candidates, "喷砂", 78, 94, evidence);
		if (ContainsAny(text, "磷化")) Add(candidates, "磷化", 79, 94, evidence);
		if (ContainsAny(text, "发黑", "发蓝")) Add(candidates, "发黑", 80, 94, evidence);
		if (ContainsAny(text, "激光打标", "打标", "镭雕")) Add(candidates, "激光打标", 86, 95, evidence);
		if (ContainsAny(text, "检验", "质检")) Add(candidates, "检验", 90, 94, evidence);
		if (ContainsAny(text, "装配")) Add(candidates, "装配", 95, 94, evidence);
		if (ContainsAny(text, "包装", "打包")) Add(candidates, "包装", 100, 94, evidence);
		if (ContainsAny(text, "出货")) Add(candidates, "出货", 110, 94, evidence);
	}

	private static bool IsSurfaceProcessCandidate(string name) => new[]
	{
		"表面处理", "抛光", "拉丝", "喷粉", "喷塑", "喷漆", "烤漆", "电镀", "镀锌", "电泳", "钝化", "阳极氧化", "发黑"
	}.Contains(name, StringComparer.OrdinalIgnoreCase);

	private static bool HasExplicitSurfaceCandidate(IEnumerable<KeyValuePair<string, Candidate>> candidates)
	{
		return candidates.Any(pair => IsSurfaceProcessCandidate(pair.Key));
	}

	private static void RemoveInferredSurfaceCandidates(Dictionary<string, Candidate> candidates)
	{
		foreach (string name in candidates.Where(pair => IsSurfaceProcessCandidate(pair.Key)).Select(pair => pair.Key).ToList())
		{
			candidates.Remove(name);
		}
	}

	internal static bool IsBendContainer(ProcessFeatureFact fact) => new[] { "processbends", "flattenbends", "flatpattern" }.Contains((fact.TypeName ?? "").ToLowerInvariant());
	internal static bool WizardHoleIsTapped(int? type)
	{
		if (!type.HasValue || !Enum.IsDefined(typeof(swWzdHoleTypes_e), type.Value)) return false;
		string name = ((swWzdHoleTypes_e)type.Value).ToString();
		return name.StartsWith("swTapBlind", StringComparison.Ordinal) || name.StartsWith("swTapThru", StringComparison.Ordinal)
			|| name.StartsWith("swPipeTap", StringComparison.Ordinal);
	}
	internal static bool WizardHoleHasRecess(int? type)
	{
		if (!type.HasValue || !Enum.IsDefined(typeof(swWzdHoleTypes_e), type.Value)) return false;
		string name = ((swWzdHoleTypes_e)type.Value).ToString();
		return name.IndexOf("Counter", StringComparison.OrdinalIgnoreCase) >= 0;
	}
	private static bool HasType(IEnumerable<ProcessFeatureFact> facts, params string[] tokens)
	{
		return facts.Any(fact => tokens.Any(token => string.Equals((fact.TypeName ?? "").Trim(), token, StringComparison.OrdinalIgnoreCase)));
	}

	private static bool HasTypeToken(IEnumerable<ProcessFeatureFact> facts, params string[] tokens)
	{
		return (facts ?? Array.Empty<ProcessFeatureFact>()).Any(fact =>
		{
			string type = (fact?.TypeName ?? "").Trim();
			return type.Length > 0 && tokens.Any(token => type.IndexOf(token ?? "", StringComparison.OrdinalIgnoreCase) >= 0);
		});
	}

	internal static bool HasMillingType(IEnumerable<ProcessFeatureFact> facts, IEnumerable<string> tokens)
	{
		var allowed = new HashSet<string>(tokens ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
		return (facts ?? Array.Empty<ProcessFeatureFact>()).Any(fact =>
		{
			string type = (fact?.TypeName ?? "").Trim();
			if (type.Length == 0) return false;
			if (allowed.Contains(type)) return true;
			// SOLIDWORKS may suffix a machining feature type, but CutListFolder
			// is a weldment bookkeeping node and must never imply milling.
			if (type.IndexOf("cutlist", StringComparison.OrdinalIgnoreCase) >= 0) return false;
			return allowed.Any(token => token.Length > 3 && type.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
		});
	}

	private static bool HasText(IEnumerable<ProcessFeatureFact> facts, params string[] tokens)
	{
		return facts.Any(fact => ContainsAny(((fact.Name ?? "") + " " + (fact.TypeName ?? "")).ToLowerInvariant(), tokens));
	}

	private static bool ContainsAny(string text, params string[] tokens)
	{
		return tokens.Any(token => text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
	}

	internal static void Add(Dictionary<string, Candidate> candidates, string name, int order, int confidence, string evidence)
	{
		if (string.IsNullOrWhiteSpace(name)) return;
		if (!candidates.TryGetValue(name, out Candidate candidate))
		{
			candidate = new Candidate { Name = name.Trim(), Order = order, Confidence = confidence };
			candidates.Add(candidate.Name, candidate);
		}
		candidate.Order = Math.Min(candidate.Order, order);
		candidate.Confidence = Math.Max(candidate.Confidence, confidence);
		if (!string.IsNullOrWhiteSpace(evidence)) candidate.Evidence.Add(evidence);
	}

	internal sealed class Candidate
	{
		public string Name { get; set; } = "";
		public int Order { get; set; } = int.MaxValue;
		public int Confidence { get; set; }
		public List<string> Evidence { get; } = new List<string>();
	}

	private sealed class ResolvedSurface
	{
		public string Name { get; }
		public int Confidence { get; }
		public string Evidence { get; }

		public ResolvedSurface(string name, int confidence, string evidence)
		{
			Name = name;
			Confidence = confidence;
			Evidence = evidence;
		}
	}
}

internal static class ProcessGeometryAnalyzer
{
	private const double ParallelTolerance = 0.998;

	public static ProcessGeometryFact AnalyzeBody(
		double[] bodyBoxMm,
		IReadOnlyList<ProcessPlaneGeometryFact> planes,
		IReadOnlyList<ProcessCylinderGeometryFact> cylinders,
		int faceCount,
		bool nativeSheetMetal = false,
		bool allFacesRead = false)
	{
		planes = (planes ?? Array.Empty<ProcessPlaneGeometryFact>()).Where(plane => plane != null && IsFinite(plane.AreaMm2)
			&& IsVector(plane.Normal) && IsVector(plane.PointMm)).ToArray();
		cylinders = (cylinders ?? Array.Empty<ProcessCylinderGeometryFact>()).Where(cylinder => cylinder != null
			&& IsFinite(cylinder.AreaMm2) && IsFinite(cylinder.RadiusMm) && IsFinite(cylinder.AxisLengthMm)
			&& IsFinite(cylinder.AxisCenterMm) && IsVector(cylinder.Axis) && IsVector(cylinder.AxisPointMm)).ToArray();
		double maxDimension = MaxDimension(bodyBoxMm);
		List<ThicknessCandidate> candidates = BuildThicknessCandidates(planes);
		ThicknessCluster best = Cluster(candidates)
			.OrderByDescending(cluster => cluster.Score)
			.ThenBy(cluster => cluster.ThicknessMm)
			.FirstOrDefault();
		double totalPlanarArea = planes.Sum(plane => Math.Max(0, plane.AreaMm2));
		bool isSheetLike = best != null
			&& best.ThicknessMm >= 0.2
			&& best.ThicknessMm <= 12
			&& maxDimension >= best.ThicknessMm * 4
			&& (best.PairCount >= 2 || best.Score >= totalPlanarArea * 0.18);
		double thickness = isSheetLike ? best.ThicknessMm : 0;
		double tubeThickness = 0;
		bool rectangularTube = !nativeSheetMetal && IsStraightRectangularTube(planes, cylinders, faceCount, out tubeThickness);
		bool roundTube = !nativeSheetMetal && !rectangularTube && IsStraightRoundTube(planes, cylinders, faceCount, out tubeThickness);
		bool straightTube = rectangularTube || roundTube;
		if (straightTube) thickness = tubeThickness;
		ProcessGeometryFact result = new ProcessGeometryFact
		{
			SolidBodyCount = 1,
			IsNativeSheetMetal = nativeSheetMetal,
			ProfileKind = rectangularTube ? "方通" : roundTube ? "圆管" : "",
			// Existing synthetic callers use faceCount to signal an intentionally
			// incomplete surface set. The SolidWorks reader separately knows when
			// every face was visited; unsupported fillets/cones are still complete
			// topology and must not erase the useful planar/cylindrical evidence.
			GeometryComplete = allFacesRead || faceCount == planes.Count + cylinders.Count,
			SheetBodyCount = isSheetLike && !straightTube ? 1 : 0,
			StraightTubeBodyCount = straightTube ? 1 : 0,
			FaceCount = Math.Max(0, faceCount),
			PlanarFaceCount = planes.Count,
			CylindricalFaceCount = cylinders.Count,
			ThicknessMm = thickness,
			BendCount = isSheetLike && !straightTube ? CountBends(cylinders, thickness) : 0,
			CutoutCount = 0,
			CutoutCountReliable = !isSheetLike || straightTube || planes.All(plane => plane.InnerLoopCount == 0),
			CutoutCountEvidence = "仅有平面/圆柱统计，尚未按相邻壁面核对内轮廓拓扑"
		};
		ApplyFastenerGeometryEvidence(result, bodyBoxMm, planes, cylinders, faceCount, isSheetLike, straightTube, thickness, allFacesRead);
		return result;
	}

	public static ProcessGeometryFact Merge(IEnumerable<ProcessGeometryFact> bodyFacts)
	{
		List<ProcessGeometryFact> bodies = (bodyFacts ?? Enumerable.Empty<ProcessGeometryFact>()).Where(body => body != null).ToList();
		ProcessGeometryFact representative = bodies
			.Where(body => body.ThicknessMm > 0)
			.OrderByDescending(body => body.IsSheetLike)
			.ThenByDescending(body => body.FaceCount)
			.FirstOrDefault();
		return new ProcessGeometryFact
		{
			ProfileKind = bodies.Select(body => body.ProfileKind).Distinct().Count() == 1 ? bodies.FirstOrDefault()?.ProfileKind ?? "" : "",
			BodyFacts = bodies.SelectMany(body => body.BodyFacts.Count > 0 ? body.BodyFacts : new[] { body }).ToArray(),
			GeometryComplete = bodies.All(body => body.GeometryComplete),
			IsNativeSheetMetal = bodies.Any(body => body.IsNativeSheetMetal),
			SolidBodyCount = bodies.Sum(body => body.SolidBodyCount),
			SheetBodyCount = bodies.Sum(body => body.SheetBodyCount),
			StraightTubeBodyCount = bodies.Sum(body => body.StraightTubeBodyCount),
			FaceCount = bodies.Sum(body => body.FaceCount),
			PlanarFaceCount = bodies.Sum(body => body.PlanarFaceCount),
			CylindricalFaceCount = bodies.Sum(body => body.CylindricalFaceCount),
			BendCount = bodies.Sum(body => body.BendCount),
			CutoutCount = bodies.Sum(body => body.CutoutCount),
			CutoutCountReliable = bodies.All(body => body.GeometryComplete && body.CutoutCountReliable),
			CutoutCountEvidence = string.Join("；", bodies.Select(body => body.CutoutCountEvidence).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct()),
			PressFitGeometryCount = bodies.Sum(body => body.PressFitGeometryCount),
			PressPinGeometryCount = bodies.Sum(body => body.PressPinGeometryCount),
			WeldRivetGeometryCount = bodies.Sum(body => body.WeldRivetGeometryCount),
			CounterboreGeometryCount = bodies.Sum(body => body.CounterboreGeometryCount),
			FastenerGeometryEvidence = string.Join("；", bodies.Select(body => body.FastenerGeometryEvidence)
				.Where(evidence => !string.IsNullOrWhiteSpace(evidence))
				.Distinct(StringComparer.CurrentCultureIgnoreCase)),
			ThicknessMm = representative?.ThicknessMm ?? 0
		};
	}

	private static bool IsStraightRectangularTube(IReadOnlyList<ProcessPlaneGeometryFact> planes,
		IReadOnlyList<ProcessCylinderGeometryFact> cylinders, int faceCount, out double thickness)
	{
		thickness = 0;
		// Complete topology and two actual annular openings are mandatory. Side
		// holes may add inner loops, so they must not be mistaken for tube ends.
		if (faceCount != planes.Count + cylinders.Count || planes.Count < 10
			|| planes.Any(plane => plane == null || plane.AreaMm2 <= 0 || !IsVector(plane.Normal) || !IsVector(plane.PointMm))) return false;
		// A rectangular extrusion has exactly three mutually perpendicular plane
		// directions. Group once before trying its two openings; a large body with
		// many hole loops must not cause cubic pair/whole-body scans on the UI STA.
		var directions = new List<List<ProcessPlaneGeometryFact>>();
		foreach (var plane in planes)
		{
			double[] normal = Normalize(plane.Normal);
			var direction = directions.FirstOrDefault(group => Math.Abs(Dot(normal, Normalize(group[0].Normal))) >= ParallelTolerance);
			if (direction == null)
			{
				if (directions.Count == 3 || directions.Any(group => Math.Abs(Dot(normal, Normalize(group[0].Normal))) > 0.02)) return false;
				direction = new List<ProcessPlaneGeometryFact>(); directions.Add(direction);
			}
			direction.Add(plane);
		}
		if (directions.Count != 3) return false;
		var possibleEnds = directions.Where(group => group.Count == 2 && group.All(plane => plane.InnerLoopCount == 1)).SelectMany(group => group).ToList();
		for (int first = 0; first < possibleEnds.Count; first++)
			for (int second = first + 1; second < possibleEnds.Count; second++)
			{
				var start = possibleEnds[first];
				var end = possibleEnds[second];
				double[] axis = Normalize(start.Normal);
				if (Math.Abs(Dot(axis, Normalize(end.Normal))) < ParallelTolerance) continue;
				var sides = planes.Where(plane => plane != start && plane != end).ToList();
				if (sides.Count < 8 || sides.Any(plane => Math.Abs(Dot(axis, Normalize(plane.Normal))) > 0.02)) continue;
				double[] firstNormal = Normalize(sides[0].Normal);
				var parallelSides = sides.Where(plane => Math.Abs(Dot(firstNormal, Normalize(plane.Normal))) >= ParallelTolerance).ToList();
				var perpendicularSides = sides.Except(parallelSides).ToList();
				if (perpendicularSides.Count < 4 || perpendicularSides.Any(plane => Math.Abs(Dot(firstNormal, Normalize(plane.Normal))) > 0.02)) continue;
				if (!TryWallThickness(parallelSides, firstNormal, out double firstThickness)
					|| !TryWallThickness(perpendicularSides, Normalize(perpendicularSides[0].Normal), out double secondThickness)
					|| Math.Abs(firstThickness - secondThickness) > Math.Max(0.15, firstThickness * 0.06)) continue;
				double wall = (firstThickness + secondThickness) / 2;
				double length = Math.Abs(Dot(axis, Subtract(end.PointMm, start.PointMm)));
				if (length < Math.Max(5, wall * 4)) continue;
				double endArea = Math.Max(start.AreaMm2, end.AreaMm2);
				if (Math.Abs(start.AreaMm2 - end.AreaMm2) > endArea * 0.02) continue;
				var longitudinal = cylinders.Where(cylinder => cylinder != null && IsVector(cylinder.Axis)
					&& Math.Abs(Dot(axis, Normalize(cylinder.Axis))) >= ParallelTolerance).ToList();
				if (longitudinal.Any(cylinder => Math.Abs(cylinder.AxisLengthMm - length) > Math.Max(0.5, length * 0.01)
					|| cylinder.ArcAngleRadians < 0.1 || cylinder.ArcAngleRadians > Math.PI / 2 + 0.15)) continue;
				// Each stock corner has coaxial inner/outer radii separated by the
				// certified wall thickness; unrelated fillets are not stock evidence.
				if (longitudinal.Count > 0 && CountBends(longitudinal, wall) * 2 != longitudinal.Count) continue;
				var holes = cylinders.Except(longitudinal).ToList();
				if (holes.Any(cylinder => cylinder == null || !IsVector(cylinder.Axis)
					|| Math.Abs(Dot(axis, Normalize(cylinder.Axis))) > 0.02
					|| cylinder.AxisLengthMm <= 0 || cylinder.AxisLengthMm > wall * 1.08 + 0.15
					|| Math.Abs(cylinder.ArcAngleRadians - Math.PI * 2) > 0.15)) continue;
				if (sides.Any(plane => plane.InnerLoopCount > 0) && holes.Count == 0) continue;
				thickness = wall;
				return true;
			}
		return false;
	}

	private static bool TryWallThickness(IReadOnlyList<ProcessPlaneGeometryFact> planes, double[] normal, out double thickness)
	{
		thickness = 0;
		var offsets = new List<double>();
		foreach (double offset in planes.Select(plane => Dot(normal, plane.PointMm)).OrderBy(value => value))
			if (offsets.Count == 0 || Math.Abs(offset - offsets[offsets.Count - 1]) > 0.05) offsets.Add(offset);
		if (offsets.Count != 4) return false;
		double first = offsets[1] - offsets[0];
		double last = offsets[3] - offsets[2];
		if (first < 0.2 || first > 100 || Math.Abs(first - last) > Math.Max(0.15, first * 0.06)
			|| offsets[2] - offsets[1] < first * 2) return false;
		thickness = (first + last) / 2;
		return true;
	}

	private static bool IsStraightRoundTube(IReadOnlyList<ProcessPlaneGeometryFact> planes,
		IReadOnlyList<ProcessCylinderGeometryFact> cylinders, int faceCount, out double thickness)
	{
		thickness = 0;
		if (faceCount != 4 || planes.Count != 2 || cylinders.Count != 2
			|| planes.Any(plane => plane == null || plane.InnerLoopCount != 1 || plane.AreaMm2 <= 0
				|| !IsVector(plane.Normal) || !IsVector(plane.PointMm))
			|| cylinders.Any(cylinder => cylinder == null || !IsVector(cylinder.Axis) || !IsVector(cylinder.AxisPointMm))) return false;
		double[] axis = Normalize(planes[0].Normal);
		if (Math.Abs(Dot(axis, Normalize(planes[1].Normal))) < ParallelTolerance
			|| cylinders.Any(cylinder => Math.Abs(Dot(axis, Normalize(cylinder.Axis))) < ParallelTolerance)) return false;
		double outer = cylinders.Max(cylinder => cylinder.RadiusMm);
		double inner = cylinders.Min(cylinder => cylinder.RadiusMm);
		double wall = outer - inner;
		double length = Math.Abs(Dot(axis, Subtract(planes[1].PointMm, planes[0].PointMm)));
		if (wall < 0.2 || wall > 100 || inner < wall * 2 || length < Math.Max(outer * 2, wall * 4)
			|| AxisLineDistance(cylinders[0].AxisPointMm, cylinders[1].AxisPointMm, axis) > Math.Max(0.1, wall * 0.04)
			|| cylinders.Any(cylinder => Math.Abs(cylinder.AxisLengthMm - length) > Math.Max(0.5, length * 0.01)
				|| Math.Abs(cylinder.ArcAngleRadians - Math.PI * 2) > 0.1)) return false;
		double expectedEndArea = Math.PI * (outer * outer - inner * inner);
		if (planes.Any(plane => Math.Abs(plane.AreaMm2 - expectedEndArea) > expectedEndArea * 0.02)) return false;
		thickness = wall;
		return true;
	}

	private static List<ThicknessCandidate> BuildThicknessCandidates(IReadOnlyList<ProcessPlaneGeometryFact> planes)
	{
		List<ProcessPlaneGeometryFact> significant = planes
			.Where(plane => plane != null && plane.AreaMm2 > 0 && IsVector(plane.Normal) && IsVector(plane.PointMm))
			.OrderByDescending(plane => plane.AreaMm2)
			.Take(300)
			.ToList();
		List<ThicknessCandidate> result = new List<ThicknessCandidate>();
		for (int first = 0; first < significant.Count; first++)
		{
			double[] firstNormal = Normalize(significant[first].Normal);
			for (int second = first + 1; second < significant.Count; second++)
			{
				double[] secondNormal = Normalize(significant[second].Normal);
				if (Math.Abs(Dot(firstNormal, secondNormal)) < ParallelTolerance) continue;
				double largerArea = Math.Max(significant[first].AreaMm2, significant[second].AreaMm2);
				double smallerArea = Math.Min(significant[first].AreaMm2, significant[second].AreaMm2);
				if (largerArea <= 0 || smallerArea / largerArea < 0.35) continue;
				double thickness = Math.Abs(Dot(firstNormal, Subtract(significant[second].PointMm, significant[first].PointMm)));
				if (thickness < 0.2 || thickness > 12) continue;
				result.Add(new ThicknessCandidate
				{
					ThicknessMm = thickness,
					Score = smallerArea * (0.5 + 0.5 * smallerArea / largerArea)
				});
			}
		}
		return result;
	}

	private static IEnumerable<ThicknessCluster> Cluster(IEnumerable<ThicknessCandidate> candidates)
	{
		List<ThicknessCluster> result = new List<ThicknessCluster>();
		foreach (ThicknessCandidate candidate in candidates.OrderBy(item => item.ThicknessMm))
		{
			ThicknessCluster cluster = result.LastOrDefault();
			double tolerance = Math.Max(0.15, candidate.ThicknessMm * 0.06);
			if (cluster == null || Math.Abs(candidate.ThicknessMm - cluster.ThicknessMm) > tolerance)
			{
				cluster = new ThicknessCluster();
				result.Add(cluster);
			}
			cluster.Add(candidate);
		}
		return result;
	}

	private static int CountBends(IReadOnlyList<ProcessCylinderGeometryFact> cylinders, double thicknessMm)
	{
		List<ProcessCylinderGeometryFact> usable = cylinders
			.Where(cylinder => cylinder != null
				&& cylinder.RadiusMm > 0.05
				&& cylinder.AxisLengthMm >= Math.Max(5, thicknessMm * 3)
				&& cylinder.ArcAngleRadians >= 0.15
				&& cylinder.ArcAngleRadians <= 3.6
				&& IsVector(cylinder.Axis)
				&& IsVector(cylinder.AxisPointMm))
			.OrderByDescending(cylinder => cylinder.AreaMm2)
			.Take(300)
			.ToList();
		List<BendPair> pairs = new List<BendPair>();
		for (int first = 0; first < usable.Count; first++)
		{
			double[] firstAxis = Normalize(usable[first].Axis);
			for (int second = first + 1; second < usable.Count; second++)
			{
				double[] secondAxis = Normalize(usable[second].Axis);
				if (Math.Abs(Dot(firstAxis, secondAxis)) < ParallelTolerance) continue;
				double radiusError = Math.Abs(Math.Abs(usable[first].RadiusMm - usable[second].RadiusMm) - thicknessMm);
				if (radiusError > Math.Max(0.25, thicknessMm * 0.12)) continue;
				double lengthRatio = Math.Min(usable[first].AxisLengthMm, usable[second].AxisLengthMm)
					/ Math.Max(usable[first].AxisLengthMm, usable[second].AxisLengthMm);
				if (lengthRatio < 0.65) continue;
				if (AxisLineDistance(usable[first].AxisPointMm, usable[second].AxisPointMm, firstAxis) > Math.Max(0.3, thicknessMm * 0.15)) continue;
				double alignedSecondCenter = usable[second].AxisCenterMm * (Dot(firstAxis, secondAxis) < 0 ? -1 : 1);
				if (Math.Abs(usable[first].AxisCenterMm - alignedSecondCenter) > Math.Max(0.6, Math.Abs(usable[first].AxisLengthMm - usable[second].AxisLengthMm))) continue;
				double angleError = Math.Abs(usable[first].ArcAngleRadians - usable[second].ArcAngleRadians);
				if (angleError > 0.45) continue;
				pairs.Add(new BendPair { First = first, Second = second, Error = radiusError + angleError });
			}
		}
		HashSet<int> used = new HashSet<int>();
		int count = 0;
		foreach (BendPair pair in pairs.OrderBy(pair => pair.Error))
		{
			if (used.Contains(pair.First) || used.Contains(pair.Second)) continue;
			used.Add(pair.First);
			used.Add(pair.Second);
			count++;
		}
		return count;
	}

	private static void ApplyFastenerGeometryEvidence(
		ProcessGeometryFact result,
		double[] bodyBoxMm,
		IReadOnlyList<ProcessPlaneGeometryFact> planes,
		IReadOnlyList<ProcessCylinderGeometryFact> cylinders,
		int faceCount,
		bool isSheetLike,
		bool straightTube,
		double thicknessMm,
		bool allFacesRead)
	{
		// A fastener installation claim needs a thin-plate body and an opening
		// loop on that plate. A small standalone turned part, washer, bushing or
		// tube is deliberately left out: its concentric cylinders describe its
		// shape, not an installation operation on this model.
		if (result == null || !isSheetLike || straightTube || !planes.Any(plane => plane.InnerLoopCount > 0)
			|| faceCount <= 0 || (!allFacesRead && faceCount != planes.Count + cylinders.Count)) return;
		List<ProcessCylinderGeometryFact> full = cylinders
			.Where(cylinder => cylinder.RadiusMm >= 0.35 && Math.Abs(cylinder.ArcAngleRadians - Math.PI * 2) <= 0.2)
			.OrderByDescending(cylinder => cylinder.AreaMm2)
			.Take(400)
			.ToList();
		if (full.Count < 2) return;
		double[] sheetNormal = FindSheetNormal(planes, thicknessMm);

		List<List<ProcessCylinderGeometryFact>> groups = new List<List<ProcessCylinderGeometryFact>>();
		foreach (ProcessCylinderGeometryFact cylinder in full)
		{
			double[] axis = Normalize(cylinder.Axis);
			List<ProcessCylinderGeometryFact> group = groups.FirstOrDefault(candidate =>
				Math.Abs(Dot(axis, Normalize(candidate[0].Axis))) >= ParallelTolerance
				&& AxisLineDistance(cylinder.AxisPointMm, candidate[0].AxisPointMm, axis) <= Math.Max(0.12, Math.Min(cylinder.RadiusMm, candidate[0].RadiusMm) * 0.08));
			if (group == null) { group = new List<ProcessCylinderGeometryFact>(); groups.Add(group); }
			group.Add(cylinder);
		}

		int pressFit = 0;
		int counterbore = 0;
		var evidence = new List<string>();
		foreach (List<ProcessCylinderGeometryFact> group in groups.Where(group => group.Count >= 2))
		{
			for (int first = 0; first < group.Count; first++)
			for (int second = first + 1; second < group.Count; second++)
			{
				ProcessCylinderGeometryFact smaller = group[first].RadiusMm <= group[second].RadiusMm ? group[first] : group[second];
				ProcessCylinderGeometryFact larger = ReferenceEquals(smaller, group[first]) ? group[second] : group[first];
				double radiusRatio = larger.RadiusMm / Math.Max(0.01, smaller.RadiusMm);
				if (radiusRatio < 1.25) continue;
				double[] axis = Normalize(smaller.Axis);
				double sign = Dot(axis, Normalize(larger.Axis)) < 0 ? -1 : 1;
				double smallCenter = smaller.AxisCenterMm;
				double largeCenter = larger.AxisCenterMm * sign;
				double smallStart = smallCenter - smaller.AxisLengthMm / 2;
				double smallEnd = smallCenter + smaller.AxisLengthMm / 2;
				double largeStart = largeCenter - larger.AxisLengthMm / 2;
				double largeEnd = largeCenter + larger.AxisLengthMm / 2;
				double overlap = Math.Max(0, Math.Min(smallEnd, largeEnd) - Math.Max(smallStart, largeStart));
				double intervalGap = Math.Max(0, Math.Max(smallStart, largeStart) - Math.Min(smallEnd, largeEnd));
				double minimumLength = Math.Min(smaller.AxisLengthMm, larger.AxisLengthMm);
				bool nestedWalls = overlap >= Math.Max(0.2, minimumLength * 0.35);
				bool endToEnd = intervalGap <= Math.Max(0.5, thicknessMm * 0.2);

				bool sameLengthInsert = minimumLength > 0.2
					&& Math.Min(smaller.AxisLengthMm, larger.AxisLengthMm) / Math.Max(smaller.AxisLengthMm, larger.AxisLengthMm) >= 0.68;
				bool shortCounterbore = thicknessMm > 0
					? minimumLength <= Math.Max(3, thicknessMm * 2.2)
					: minimumLength <= 6;
				bool smallOpening = planes.Any(plane => (plane.InnerCircles ?? Array.Empty<ProcessCircleGeometryFact>()).Any(circle => MatchesCircle(circle, smaller)));
				bool largeOpening = planes.Any(plane => (plane.InnerCircles ?? Array.Empty<ProcessCircleGeometryFact>()).Any(circle => MatchesCircle(circle, larger)));
				bool openingThroughSheet = sheetNormal.Length >= 3 && Math.Abs(Dot(axis, sheetNormal)) >= 0.8;
				// A press-fit candidate needs an inward bore wall and an outward
				// collar wall tied to the same actual planar opening. Two unrelated
				// cylinders elsewhere in a body are not enough.
				if (nestedWalls && openingThroughSheet && sameLengthInsert && radiusRatio >= 1.35 && smallOpening
					&& smaller.IsInwardKnown && larger.IsInwardKnown && smaller.IsInward && !larger.IsInward)
				{
					pressFit++;
					evidence.Add("同轴内孔与外圆柱/台阶实体（" + smaller.RadiusMm.ToString("0.##") + "→" + larger.RadiusMm.ToString("0.##") + " mm），疑似压铆件或焊铆件");
				}
				else if ((nestedWalls || endToEnd) && openingThroughSheet && shortCounterbore && radiusRatio >= 1.2
					&& smallOpening && largeOpening && smaller.IsInwardKnown && larger.IsInwardKnown
					&& smaller.IsInward && larger.IsInward)
				{
					counterbore++;
					evidence.Add("同轴分级圆柱且一段接近板厚，存在沉孔/锪孔几何证据");
				}
			}
		}
		result.PressFitGeometryCount = pressFit;
		result.CounterboreGeometryCount = counterbore;
		result.FastenerGeometryEvidence = string.Join("；", evidence.Distinct(StringComparer.CurrentCultureIgnoreCase));
	}

	private static double[] FindSheetNormal(IReadOnlyList<ProcessPlaneGeometryFact> planes, double thicknessMm)
	{
		ProcessPlaneGeometryFact firstBest = null;
		ProcessPlaneGeometryFact secondBest = null;
		double bestScore = 0;
		for (int first = 0; first < planes.Count; first++)
		{
			if (planes[first] == null || !IsVector(planes[first].Normal) || !IsVector(planes[first].PointMm)) continue;
			double[] firstNormal = Normalize(planes[first].Normal);
			for (int second = first + 1; second < planes.Count; second++)
			{
				if (planes[second] == null || !IsVector(planes[second].Normal) || !IsVector(planes[second].PointMm)) continue;
				if (Math.Abs(Dot(firstNormal, Normalize(planes[second].Normal))) < ParallelTolerance) continue;
				double distance = Math.Abs(Dot(firstNormal, Subtract(planes[second].PointMm, planes[first].PointMm)));
				if (Math.Abs(distance - thicknessMm) > Math.Max(0.25, thicknessMm * 0.12)) continue;
				double largerArea = Math.Max(planes[first].AreaMm2, planes[second].AreaMm2);
				double smallerArea = Math.Min(planes[first].AreaMm2, planes[second].AreaMm2);
				if (largerArea <= 0 || smallerArea / largerArea < 0.3 || smallerArea <= bestScore) continue;
				bestScore = smallerArea;
				firstBest = planes[first];
				secondBest = planes[second];
			}
		}
		return firstBest == null || secondBest == null ? Array.Empty<double>() : Normalize(firstBest.Normal);
	}

	private static bool MatchesCircle(ProcessCircleGeometryFact circle, ProcessCylinderGeometryFact cylinder)
	{
		if (circle == null || cylinder == null || circle.CenterMm == null || cylinder.AxisPointMm == null
			|| circle.Axis == null || cylinder.Axis == null || circle.CenterMm.Length < 3 || cylinder.AxisPointMm.Length < 3
			|| circle.Axis.Length < 3 || cylinder.Axis.Length < 3) return false;
		double[] axis = Normalize(cylinder.Axis);
		return Math.Abs(circle.RadiusMm - cylinder.RadiusMm) <= Math.Max(0.12, cylinder.RadiusMm * 0.04)
			&& Math.Abs(Dot(axis, Normalize(circle.Axis))) >= ParallelTolerance
			&& AxisLineDistance(circle.CenterMm, cylinder.AxisPointMm, axis) <= Math.Max(0.15, cylinder.RadiusMm * 0.08);
	}

	private static double MaxDimension(double[] box)
	{
		if (box == null || box.Length < 6) return 0;
		return Math.Max(Math.Abs(box[3] - box[0]), Math.Max(Math.Abs(box[4] - box[1]), Math.Abs(box[5] - box[2])));
	}

	private static double AxisLineDistance(double[] first, double[] second, double[] axis)
	{
		double[] delta = Subtract(second, first);
		double projection = Dot(delta, axis);
		double squared = Dot(delta, delta) - projection * projection;
		return Math.Sqrt(Math.Max(0, squared));
	}

	private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
	private static bool IsVector(double[] value) => value != null && value.Length >= 3
		&& IsFinite(value[0]) && IsFinite(value[1]) && IsFinite(value[2]);

	private static double[] Normalize(double[] value)
	{
		double length = Math.Sqrt(Dot(value, value));
		if (length <= 0.0000001) return new[] { 0d, 0d, 0d };
		return new[] { value[0] / length, value[1] / length, value[2] / length };
	}

	private static double Dot(double[] first, double[] second)
	{
		if (!IsVector(first) || !IsVector(second)) return 0;
		return first[0] * second[0] + first[1] * second[1] + first[2] * second[2];
	}

	private static double[] Subtract(double[] first, double[] second)
	{
		if (!IsVector(first) || !IsVector(second)) return new[] { 0d, 0d, 0d };
		return new[] { first[0] - second[0], first[1] - second[1], first[2] - second[2] };
	}

	private sealed class ThicknessCandidate
	{
		public double ThicknessMm { get; set; }
		public double Score { get; set; }
	}

	private sealed class ThicknessCluster
	{
		private double _weightedThickness;
		public double ThicknessMm => Score <= 0 ? 0 : _weightedThickness / Score;
		public double Score { get; private set; }
		public int PairCount { get; private set; }

		public void Add(ThicknessCandidate candidate)
		{
			_weightedThickness += candidate.ThicknessMm * candidate.Score;
			Score += candidate.Score;
			PairCount++;
		}
	}

	private sealed class BendPair
	{
		public int First { get; set; }
		public int Second { get; set; }
		public double Error { get; set; }
	}
}

internal static class SolidWorksProcessFeatureReader
{
	private const int MaxFeatures = 4000;
	private const int MaxGeometryFaces = 12000;

	public static ProcessRecognitionInput Read(IModelDoc2 document, bool deep, ProcessRecognitionConfig config = null, int maxGeometryFaces = MaxGeometryFaces)
	{
		if (document == null) throw new InvalidOperationException("当前没有打开模型。");
		config ??= new ProcessRecognitionConfig();
		ProcessRecognitionConfig.Normalize(config);
		int documentType = document.GetType();
		bool isPart = documentType == (int)swDocumentTypes_e.swDocPART;
		bool isAssembly = documentType == (int)swDocumentTypes_e.swDocASSEMBLY;
		ProcessRecognitionInput input = new ProcessRecognitionInput
		{
			DocumentTitle = document.GetTitle() ?? "",
			Material = ReadMaterial(document),
			SurfaceTreatment = PropertyIo.Read(document, "自定义属性", config.SurfacePropertyName),
			ExistingProcess = ReadFirstProperty(document, config.SummaryPropertyName, "工序", "工艺流程"),
			PartType = ReadFirstProperty(document, config.PartTypePropertyName, "零件类型", "物料类型", "零件类别", "零件分类", "产品类别", "部件类型", "类别", "分类", "类型"),
			ProfileDescription = ReadFirstProperty(document, "Profile Type", "Profile Description", "型材类型", "型材规格", "型材型号", "截面规格", "结构型材", "规格"),
			ItemDescription = ReadFirstProperty(document, "物料名称", "品名", "零件名称", "描述", "Description", "Part Description"),
			StandardCode = ReadFirstProperty(document, "标准号", "标准编号", "标准", "型号", "规格型号", "Standard", "Part Number"),
			ProcessType = ReadFirstProperty(document, config.ProcessTypePropertyName, "工序类型"),
			Outsource = ReadFirstProperty(document, config.OutsourcePropertyName, "是否外加工", "外加工", "是否外协", "外协", "是否委外", "委外加工"),
			PurchaseMarkerSnapshot = PurchasedPartPolicy.ReadSnapshot(document, document.ConfigurationManager?.ActiveConfiguration?.Name ?? ""),
			Purchase = PurchasedPartPolicy.ReadFlag(document),
			IsPart = isPart,
			IsAssembly = isAssembly
		};
		List<ProcessFeatureFact> facts = new List<ProcessFeatureFact>();
		if (isPart && !(!deep && ProcessRecognitionService.IsDeclaredPurchasedOrStandard(input)))
		{
			int featureLimit = MaxFeatures;
			int visited = 0;
			bool featureReadsComplete = true;
			// Quick recognition runs from idle callbacks and must return promptly.
			// An explicit deep recognition is user initiated and needs enough time
			// to walk feature trees on real production parts.
			int featureBudgetMs = deep ? 3000 : 500;
			var featureClock = System.Diagnostics.Stopwatch.StartNew();
			IFeature feature = document.FirstFeature() as IFeature;
			while (feature != null && facts.Count < featureLimit && visited++ < featureLimit && featureClock.ElapsedMilliseconds < featureBudgetMs)
			{
				if (!AddFeature(facts, feature, isSubFeature: false)) featureReadsComplete = false;
				if (deep && !AddSubFeatures(facts, feature, depth: 0, featureClock, featureBudgetMs)) featureReadsComplete = false;
				feature = feature.GetNextFeature() as IFeature;
			}
			input.FeatureReadComplete = featureReadsComplete && feature == null && featureClock.ElapsedMilliseconds < featureBudgetMs && facts.Count < featureLimit;
		}
		input.Features = facts;
		if (isPart && !(!deep && ProcessRecognitionService.IsDeclaredPurchasedOrStandard(input)))
		{
			ProcessGeometryFact lightweight = !deep ? ReadFeatureSheetGeometry(document, facts, config) : null;
			// Quick recognition is intentionally feature/property-only. Traversing
			// face geometry is reserved for an explicit deep recognition request so
			// an idle callback never monopolizes the SOLIDWORKS UI thread.
			input.Geometry = !deep
				? lightweight ?? new ProcessGeometryFact()
			: ReadGeometry(document, Math.Max(100, Math.Min(MaxGeometryFaces, maxGeometryFaces)), deep ? 30000 : 750);
		}
		else input.Geometry = new ProcessGeometryFact();
		if (isPart)
		{
			int stamp = document.GetUpdateStamp();
			string configuration = document.ConfigurationManager?.ActiveConfiguration?.Name ?? "";
			input.HoleFacts = ProcessHoleFactBridge.ReadMetadata(document, facts);
			if (ProcessHoleFactBridge.TryGet(document, configuration, stamp, out var previous))
				input.HoleFacts = input.HoleFacts.Concat(previous.Facts.Where(f => f.Source == "HoleGeometry")).ToArray();
			ProcessHoleFactBridge.Publish(document, configuration, stamp, input.HoleFacts, "ProcessRead");
		}
		return input;
	}

	private static string ReadFirstProperty(IModelDoc2 document, params string[] names)
	{
		foreach (string name in names.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.CurrentCultureIgnoreCase))
		{
			string value = PropertyIo.Read(document, "自定义属性", name);
			if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
		}
		return "";
	}

	private static ProcessGeometryFact ReadFeatureSheetGeometry(IModelDoc2 document, IReadOnlyList<ProcessFeatureFact> facts, ProcessRecognitionConfig config)
	{
		bool sheetMetal = facts.Any(fact =>
		{
			if (fact == null || fact.IsSuppressed) return false;
			string type = (fact.TypeName ?? "").ToLowerInvariant();
			string name = (fact.Name ?? "").ToLowerInvariant();
			return type.Contains("sheetmetal") || type.Contains("smbaseflange") || type.Contains("baseflange") || type.Contains("flatpattern")
				|| name.Contains("钣金") || name.Contains("平板型式") || name.Contains("基体法兰") || name.Contains("基体-法兰");
		});
		double thickness = sheetMetal ? ReadNumberProperty(document, config.ThicknessPropertyName, "板厚", "厚度", "SW-Sheet Metal Thickness") : 0;
		int bendCount = facts.Count(fact =>
		{
			if (ProcessRecognitionService.IsBendContainer(fact) || fact.IsSuppressed) return false;
			string type = (fact.TypeName ?? "").ToLowerInvariant();
			string name = (fact.Name ?? "").ToLowerInvariant();
			return type.Contains("onebend") || type.Contains("sketchbend") || type.Contains("edgeflange") || type.Contains("miterflange") || type.Contains("loftedbend") || type.Contains("hem") || type.Contains("jog")
				|| name.Contains("折弯") || name.Contains("边线法兰") || name.Contains("斜接法兰") || name.Contains("褶边") || name.Contains("卷边") || name.Contains("转折");
		});
		var bodies = new List<ProcessGeometryFact>();
		try
		{
			if (!(document is IPartDoc part)) return new ProcessGeometryFact { GeometryComplete = false };
			foreach (object bodyObject in Objects(part.GetBodies2((int)swBodyType_e.swSolidBody, false)))
			{
				if (!(bodyObject is IBody2 body)) continue;
				bool nativeSheet = body.IsSheetMetal();
				bodies.Add(new ProcessGeometryFact
				{
					BodyName = body.Name ?? "",
					SolidBodyCount = 1,
					SheetBodyCount = nativeSheet ? 1 : 0,
					IsNativeSheetMetal = nativeSheet,
					ThicknessMm = nativeSheet ? thickness : 0,
					GeometryComplete = false
				});
			}
		}
		catch (Exception ex) { Logger.Trace("Process recognition body inventory incomplete: " + ex.Message); }
		var result = ProcessGeometryAnalyzer.Merge(bodies);
		result.GeometryComplete = false;
		// Feature counts describe the document, not each individual body. Do not
		// multiply them or pretend to know which body owns a bend in quick mode.
		result.BendCount = sheetMetal ? bendCount : 0;
		result.CutoutCount = 0;
		result.CutoutCountReliable = false;
		result.CutoutCountEvidence = "快速识别未遍历内轮廓拓扑，原有计数属性不作为本次几何结果";
		return result;
	}

	private static double ReadNumberProperty(IModelDoc2 document, params string[] names)
	{
		string value = ReadFirstProperty(document, names);
		if (string.IsNullOrWhiteSpace(value)) return 0;
		string number = new string(value.Trim().TakeWhile(character => char.IsDigit(character) || character == '.' || character == ',' || character == '-').ToArray()).Replace(',', '.');
		return double.TryParse(number, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double result) ? Math.Max(0, result) : 0;
	}

	private static ProcessGeometryFact ReadGeometry(IModelDoc2 document, int maxGeometryFaces, int timeBudgetMs = 750)
	{
		try
		{
			if (!(document is IPartDoc part)) return new ProcessGeometryFact();
			List<ProcessGeometryFact> bodies = new List<ProcessGeometryFact>();
			int remainingFaces = maxGeometryFaces;
			var geometryClock = System.Diagnostics.Stopwatch.StartNew();
			foreach (object bodyObject in Objects(part.GetBodies2((int)swBodyType_e.swSolidBody, false)))
			{
				if (!(bodyObject is IBody2 body)) continue;
				var unread = new ProcessGeometryFact { BodyName = body.Name ?? "", SolidBodyCount = 1, GeometryComplete = false, FaceCount = body.GetFaceCount(), IsNativeSheetMetal = body.IsSheetMetal() };
				if (unread.IsNativeSheetMetal) unread.SheetBodyCount = 1;
				if (remainingFaces <= 0 || geometryClock.ElapsedMilliseconds >= timeBudgetMs) { bodies.Add(unread); continue; }
				try
				{
				List<ProcessPlaneGeometryFact> planes = new List<ProcessPlaneGeometryFact>();
				List<ProcessCylinderGeometryFact> cylinders = new List<ProcessCylinderGeometryFact>();
				int readFaces = 0;
				object[] faceObjects = Objects(body.GetFaces());
				foreach (object faceObject in faceObjects)
				{
					if (!(faceObject is IFace2 face) || remainingFaces <= 0 || geometryClock.ElapsedMilliseconds >= timeBudgetMs) break;
					ReadFaceGeometry(face, planes, cylinders);
					readFaces++; remainingFaces--;
				}
				bool allFacesRead = readFaces == unread.FaceCount && readFaces == faceObjects.Length;
				// Partial walls cannot prove a tube or a complete manufacturing
				// route. Keep inventory/native sheet evidence, discard inference.
				if (!allFacesRead)
				{
					Logger.Info("Process recognition geometry budget incomplete: body=" + unread.BodyName
						+ "; expected=" + unread.FaceCount + "; read=" + readFaces + "; planes=" + planes.Count
						+ "; cylinders=" + cylinders.Count + "; elapsedMs=" + geometryClock.ElapsedMilliseconds
						+ "; remaining=" + remainingFaces + "; arrayFaces=" + faceObjects.Length);
					bodies.Add(unread); continue;
				}
				var analyzed = ProcessGeometryAnalyzer.AnalyzeBody(
					Millimeters(body.GetBodyBox()),
					planes,
					cylinders,
					unread.FaceCount,
					body.IsSheetMetal(),
					allFacesRead);
				analyzed.BodyName = unread.BodyName;
				if (analyzed.IsNativeSheetMetal) analyzed.SheetBodyCount = 1;
				if (analyzed.IsSheetLike)
				{
					var cutouts = ProcessSheetCutoutTopology.Read(document, faceObjects.OfType<IFace2>(), analyzed.ThicknessMm, analyzed.BendCount > 0, Math.Max(1, timeBudgetMs - (int)geometryClock.ElapsedMilliseconds));
					analyzed.CutoutCount = cutouts.Count;
					analyzed.CutoutCountReliable = cutouts.Reliable;
					analyzed.CutoutCountEvidence = cutouts.Evidence;
				}
				bodies.Add(analyzed);
				}
				catch (Exception ex)
				{
					Logger.Trace("Process recognition body geometry incomplete: " + ex.Message);
					bodies.Add(unread);
				}
			}
			return ProcessGeometryAnalyzer.Merge(bodies);
		}
		catch (Exception ex)
		{
			Logger.Trace("Process recognition geometry read skipped: " + ex.Message);
			return new ProcessGeometryFact { GeometryComplete = false };
		}
	}

	internal static ProcessGeometryFact ReadGeometryForThickness(IModelDoc2 document, int maxGeometryFaces = 2000)
	{
		return ReadGeometry(document, Math.Max(100, Math.Min(MaxGeometryFaces, maxGeometryFaces)));
	}

	private static void ReadFaceGeometry(
		IFace2 face,
		ICollection<ProcessPlaneGeometryFact> planes,
		ICollection<ProcessCylinderGeometryFact> cylinders)
	{
		try
		{
			if (!(face.GetSurface() is ISurface surface)) return;
			double areaMm2 = Math.Max(0, face.GetArea() * 1000000d);
			if (surface.IsPlane())
			{
				double[] parameters = Doubles(surface.PlaneParams);
				if (parameters.Length < 6) return;
				var innerCircles = new List<ProcessCircleGeometryFact>();
				try
				{
					// Avoid COM edge enumeration for a plane without inner loops. This
					// is a large speedup on imported panels with many outer wall faces.
					if (face.GetLoopCount() > 1)
					foreach (ILoop2 loop in Objects(face.GetLoops()).OfType<ILoop2>())
					{
						if (loop.IsOuter()) continue;
						foreach (IEdge edge in Objects(loop.GetEdges()).OfType<IEdge>())
						{
							if (!(edge.GetCurve() is ICurve curve) || !curve.IsCircle()) continue;
							double[] circle = Doubles(curve.CircleParams);
							if (circle.Length < 7) continue;
							innerCircles.Add(new ProcessCircleGeometryFact
							{
								RadiusMm = Math.Abs(circle[6] * 1000),
								CenterMm = new[] { circle[0] * 1000, circle[1] * 1000, circle[2] * 1000 },
								Axis = Normalize(new[] { circle[3], circle[4], circle[5] })
							});
						}
					}
				}
				catch (Exception ex) { Logger.Trace("Process recognition skipped planar loop circles: " + ex.Message); }
				planes.Add(new ProcessPlaneGeometryFact
				{
					AreaMm2 = areaMm2,
					Normal = new[] { parameters[0], parameters[1], parameters[2] },
					PointMm = new[] { parameters[3] * 1000, parameters[4] * 1000, parameters[5] * 1000 },
					InnerLoopCount = Math.Max(0, face.GetLoopCount() - 1),
					InnerCircles = innerCircles
				});
				return;
			}
			if (!surface.IsCylinder()) return;
			double[] cylinder = Doubles(surface.CylinderParams);
			if (cylinder.Length < 7) return;
			double[] axis = Normalize(new[] { cylinder[3], cylinder[4], cylinder[5] });
			double[] boxMm = Millimeters(face.GetBox());
			ReadAxisExtent(face, axis, boxMm, out double axisLengthMm, out double axisCenterMm);
			bool inward = false;
			bool inwardKnown = false;
			try
			{
				double[] uv = Doubles(face.GetUVBounds());
				if (uv.Length >= 4)
				{
					double[] evaluated = Doubles(surface.Evaluate((uv[0] + uv[1]) / 2, (uv[2] + uv[3]) / 2, 0, 0));
					if (evaluated.Length >= 6)
					{
						double[] radial = Subtract(new[] { evaluated[0] * 1000, evaluated[1] * 1000, evaluated[2] * 1000 }, new[] { cylinder[0] * 1000, cylinder[1] * 1000, cylinder[2] * 1000 });
						double[] normal = Normalize(new[] { evaluated[3], evaluated[4], evaluated[5] });
						radial = Subtract(radial, axis.Select(value => value * Dot(radial, axis)).ToArray());
						inward = Dot(radial, normal) * (face.FaceInSurfaceSense() ? -1 : 1) < -0.0001;
						inwardKnown = true;
					}
				}
			}
			catch { }
			cylinders.Add(new ProcessCylinderGeometryFact
			{
				AreaMm2 = areaMm2,
				RadiusMm = Math.Abs(cylinder[6] * 1000),
				AxisPointMm = new[] { cylinder[0] * 1000, cylinder[1] * 1000, cylinder[2] * 1000 },
				Axis = axis,
				AxisLengthMm = axisLengthMm,
				AxisCenterMm = axisCenterMm,
				IsInward = inward,
				IsInwardKnown = inwardKnown
			});
		}
		catch (Exception ex)
		{
			Logger.Trace("Process recognition skipped unreadable face geometry: " + ex.Message);
		}
	}

	private static void ReadAxisExtent(IFace2 face, double[] axis, double[] boxMm, out double lengthMm, out double centerMm)
	{
		// Most SolidWorks cylinder faces are bounded by a world-aligned or
		// principal-axis box. Use it before walking every edge and vertex; the
		// edge fallback remains for rotated/degenerate boxes.
		if (boxMm.Length >= 6 && Math.Max(Math.Abs(axis[0]), Math.Max(Math.Abs(axis[1]), Math.Abs(axis[2]))) >= 0.95)
		{
			double dx = Math.Abs(boxMm[3] - boxMm[0]);
			double dy = Math.Abs(boxMm[4] - boxMm[1]);
			double dz = Math.Abs(boxMm[5] - boxMm[2]);
			lengthMm = Math.Abs(axis[0]) * dx + Math.Abs(axis[1]) * dy + Math.Abs(axis[2]) * dz;
			double[] center = { (boxMm[0] + boxMm[3]) / 2, (boxMm[1] + boxMm[4]) / 2, (boxMm[2] + boxMm[5]) / 2 };
			centerMm = Dot(center, axis);
			if (lengthMm > 0.01) return;
		}
		List<double> circleCenters = new List<double>();
		List<double> vertexProjections = new List<double>();
		try
		{
			foreach (object edgeObject in Objects(face.GetEdges()))
			{
				if (!(edgeObject is IEdge edge)) continue;
				foreach (object vertexObject in new[] { edge.GetStartVertex(), edge.GetEndVertex() })
					if (vertexObject is IVertex vertex)
					{
						double[] point = Millimeters(vertex.GetPoint());
						if (point.Length >= 3) vertexProjections.Add(Dot(point, axis));
					}
				if (!(edge.GetCurve() is ICurve curve) || !curve.IsCircle()) continue;
				double[] parameters = Doubles(curve.CircleParams);
				if (parameters.Length < 6 || Math.Abs(Dot(Normalize(new[] { parameters[3], parameters[4], parameters[5] }), axis)) < 0.998) continue;
				double[] center = { parameters[0] * 1000, parameters[1] * 1000, parameters[2] * 1000 };
				circleCenters.Add(Dot(center, axis));
			}
		}
		catch
		{
			circleCenters.Clear();
			vertexProjections.Clear();
		}
		if (circleCenters.Count >= 2 && circleCenters.Max() - circleCenters.Min() > 0.01)
		{
			lengthMm = circleCenters.Max() - circleCenters.Min();
			centerMm = (circleCenters.Max() + circleCenters.Min()) / 2;
			return;
		}
		if (vertexProjections.Count >= 2 && vertexProjections.Max() - vertexProjections.Min() > 0.01)
		{
			// Edge vertices project onto the real cylinder axis. Projecting the
			// world-aligned box would add the transverse width of a rotated tube.
			lengthMm = vertexProjections.Max() - vertexProjections.Min();
			centerMm = (vertexProjections.Max() + vertexProjections.Min()) / 2;
			return;
		}
		if (boxMm.Length >= 6)
		{
			double dx = Math.Abs(boxMm[3] - boxMm[0]);
			double dy = Math.Abs(boxMm[4] - boxMm[1]);
			double dz = Math.Abs(boxMm[5] - boxMm[2]);
			lengthMm = Math.Abs(axis[0]) * dx + Math.Abs(axis[1]) * dy + Math.Abs(axis[2]) * dz;
			double[] center = { (boxMm[0] + boxMm[3]) / 2, (boxMm[1] + boxMm[4]) / 2, (boxMm[2] + boxMm[5]) / 2 };
			centerMm = Dot(center, axis);
			return;
		}
		lengthMm = 0;
		centerMm = 0;
	}

	private static object[] Objects(object value)
	{
		if (!(value is Array array)) return Array.Empty<object>();
		return array.Cast<object>().ToArray();
	}

	private static double[] Doubles(object value)
	{
		if (!(value is Array array)) return Array.Empty<double>();
		return array.Cast<object>().Select(Convert.ToDouble).ToArray();
	}

	private static double[] Millimeters(object value)
	{
		return Doubles(value).Select(number => number * 1000).ToArray();
	}

	private static double[] Normalize(double[] value)
	{
		double length = Math.Sqrt(Dot(value, value));
		if (length <= 0.0000001) return new[] { 0d, 0d, 0d };
		return new[] { value[0] / length, value[1] / length, value[2] / length };
	}

	private static double Dot(double[] first, double[] second)
	{
		if (first == null || second == null || first.Length < 3 || second.Length < 3) return 0;
		return first[0] * second[0] + first[1] * second[1] + first[2] * second[2];
	}

	private static double[] Subtract(double[] first, double[] second)
	{
		if (first == null || second == null || first.Length < 3 || second.Length < 3) return new[] { 0d, 0d, 0d };
		return new[] { first[0] - second[0], first[1] - second[1], first[2] - second[2] };
	}

	private static bool AddSubFeatures(List<ProcessFeatureFact> facts, IFeature parent, int depth, System.Diagnostics.Stopwatch clock, int timeBudgetMs)
	{
		if (!TrySuppressed(parent, out bool suppressed)) return false;
		if (suppressed) return true;
		IFeature child = parent.GetFirstSubFeature() as IFeature;
		if (child == null) return true;
		if (depth >= 12 || facts.Count >= MaxFeatures) return false;
		bool complete = true;
		while (child != null && facts.Count < MaxFeatures && clock.ElapsedMilliseconds < timeBudgetMs)
		{
			if (!AddFeature(facts, child, isSubFeature: true)) complete = false;
			if (!AddSubFeatures(facts, child, depth + 1, clock, timeBudgetMs)) complete = false;
			child = child.GetNextSubFeature() as IFeature;
		}
		return complete && child == null;
	}

	private static bool AddFeature(List<ProcessFeatureFact> facts, IFeature feature, bool isSubFeature)
	{
		try
		{
			if (!TrySuppressed(feature, out bool suppressed)) return false;
			if (suppressed) return true;
			bool complete = true;
			ProcessFeatureFact fact = new ProcessFeatureFact
			{
				Name = feature.Name ?? "",
				TypeName = feature.GetTypeName2() ?? "",
				IsSubFeature = isSubFeature
			};
			if (fact.TypeName.Equals("HoleWzd", StringComparison.OrdinalIgnoreCase) || fact.TypeName.Equals("WizardHole", StringComparison.OrdinalIgnoreCase))
			{
				try { if (feature.GetDefinition() is IWizardHoleFeatureData2 hole) fact.HoleWizardType = hole.Type; else complete = false; }
				catch (Exception ex) { complete = false; Logger.Trace("Hole wizard process metadata unavailable: " + ex.Message); }
			}
			if (fact.TypeName.Equals("Hem", StringComparison.OrdinalIgnoreCase))
			{
				try { if (feature.GetDefinition() is IHemFeatureData hem) fact.HemType = hem.Type; else complete = false; }
				catch (Exception ex) { complete = false; Logger.Trace("Hem process metadata unavailable: " + ex.Message); }
			}
			facts.Add(fact);
			return complete;
		}
		catch (Exception ex)
		{
			Logger.Trace("Process recognition skipped unreadable feature: " + ex.Message);
			return false;
		}
	}
	private static bool TrySuppressed(IFeature feature, out bool suppressed)
	{
		suppressed = true;
		try
		{
			object[] values = Objects(feature.IsSuppressed2((int)swInConfigurationOpts_e.swThisConfiguration, null));
			if (values.Length == 0) return false;
			suppressed = values.Any(value => Convert.ToBoolean(value));
			return true;
		}
		catch (Exception ex) { Logger.Trace("Cannot verify process feature suppression: " + ex.Message); return false; }
	}

	private static string ReadMaterial(IModelDoc2 document)
	{
		try
		{
			if (!(document is IPartDoc part)) return "";
			string configuration = document.ConfigurationManager?.ActiveConfiguration?.Name ?? "";
			string material = part.GetMaterialPropertyName2(configuration, out string _);
			return string.IsNullOrWhiteSpace(material) ? PropertyIo.Read(document, "自定义属性", "材料") : material;
		}
		catch (Exception ex)
		{
			Logger.Trace("Process recognition material read skipped: " + ex.Message);
			return PropertyIo.Read(document, "自定义属性", "材料");
		}
	}
}
