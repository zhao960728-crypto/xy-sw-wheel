using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace XY.SolidWorksPlugin;

// Canonical part types written to the configured "类型" property and used to
// pick a process chain when a model carries no usable machining evidence.
internal static class PartTypes
{
	public const string SheetMetal = "钣金件";
	public const string Machined = "机加件";
	public const string WeldAssembly = "组焊件";
	public const string Weldment = "焊接件";
	public const string Profile = "型材件";
	public const string FootPad = "脚垫类";
	public const string Purchased = "外购件";
	public const string Standard = "标准件";
	public const string Outsourced = "外加工件";
	public const string Mixed = "钣金焊接件";
	public const string Casting = "铸造件";
	public const string Forging = "锻造件";
	public const string Plastic = "塑胶件";
	public const string Additive = "增材制造件";
	public const string Other = "其他";

	public static readonly string[] All =
	{
		SheetMetal, Machined, WeldAssembly, Weldment, Profile,
		FootPad, Purchased, Standard, Outsourced, Mixed, Casting, Forging, Plastic, Additive, Other
	};

	public static string Normalize(string value)
	{
		string type = (value ?? "").Trim();
		return All.FirstOrDefault(item => string.Equals(item, type, StringComparison.OrdinalIgnoreCase)) ?? "";
	}
}

internal sealed class PartTypeMatch
{
	public string Type { get; set; } = "";

	public int Confidence { get; set; }

	public string Evidence { get; set; } = "";

	public bool IsEmpty => string.IsNullOrEmpty(Type);
}

internal static class PartTypeCatalog
{
	// Keywords are checked against the declared type property first and then the
	// document title, so a naming convention alone is enough to classify a part.
	private static readonly PartTypeRule[] Rules =
	{
		new PartTypeRule(PartTypes.Standard, "紧固与压铆标准件", 94,
			"压铆螺母", "压铆螺柱", "压铆螺钉", "压铆销", "涨铆螺母", "拉铆螺母", "盲孔压铆螺母", "浮动螺母", "面板紧固件", "隔离柱", "螺纹衬套", "钢丝螺套",
			"自锁螺母", "防松螺母", "蝶形螺母", "盖形螺母", "六角螺栓", "内六角螺钉", "沉头螺钉", "自攻螺钉", "钻尾螺钉", "紧定螺钉", "牙条", "双头螺柱",
			"self-clinching nut", "self clinching nut", "clinching stud", "rivet nut", "blind rivet", "hex bolt", "socket head screw", "threaded insert", "PEM螺母", "PEM螺柱"),
		new PartTypeRule(PartTypes.Purchased, "支承、导向与传动外购件", 92,
			"交叉滚子轴承", "滚针轴承", "推力轴承", "带座轴承", "直线轴承", "轴承座单元", "滑动轴承", "滚珠花键", "直线导轨", "滚柱导轨", "微型导轨", "线性导轨", "导轨滑块", "直线模组", "电动滑台",
			"滚珠丝杠", "梯形丝杠", "滚珠丝杆", "丝杠螺母", "谐波减速器", "行星减速器", "蜗轮减速器", "胀紧套", "弹性联轴器", "同步带轮", "链轮", "齿轮箱", "万向节", "阻尼器", "气弹簧", "缓冲器",
			"linear guide", "linear bearing", "ball screw", "bearing unit", "coupling", "gearbox", "gas spring", "damper"),
		new PartTypeRule(PartTypes.Purchased, "门柜五金与密封外购件", 92,
			"柜门铰链", "门铰链", "暗铰链", "脱卸铰链", "铰链", "合页", "转舌锁", "连杆锁", "三点锁", "压缩锁", "平面锁", "门锁", "柜锁", "搭扣锁", "磁吸门扣", "门吸", "抽屉滑轨", "锁扣", "拉手", "把手", "搭扣",
			"密封条", "密封胶条", "发泡密封", "橡胶密封", "门缝密封", "U型胶条", "护边条", "防尘塞", "堵头", "通风过滤器", "万向轮",
			"cabinet hinge", "door hinge", "cam lock", "compression latch", "quarter turn latch", "door handle", "drawer slide", "sealing strip", "gasket", "caster"),
		new PartTypeRule(PartTypes.Purchased, "电气、气动与液压外购件", 92,
			"伺服电机", "步进电机", "直流电机", "交流电机", "电机", "气缸", "油缸", "电缸", "电磁阀", "换向阀", "调压阀", "节流阀", "单向阀", "减压阀", "安全阀", "液压泵", "液压马达", "液压站", "蓄能器", "气源处理器", "气动三联件", "快速接头", "气管接头", "气管", "油管", "压力表", "压力传感器", "流量计",
			"接近传感器", "光电传感器", "限位开关", "编码器", "继电器", "断路器", "接触器", "变频器", "开关电源", "接线端子", "端子排", "端子台", "急停按钮", "按钮开关", "指示灯", "警示灯", "工业插头", "航空插头", "电缆接头", "接线盒", "触摸屏", "PLC模块",
			"servo motor", "stepper motor", "solenoid valve", "pneumatic cylinder", "hydraulic cylinder", "circuit breaker", "relay", "terminal block", "power supply", "proximity sensor"),
		new PartTypeRule(PartTypes.FootPad, "脚垫类（脚垫、调整脚、地脚、脚杯、脚轮等）", 96,
			"脚垫", "调整脚", "调节脚", "可调脚", "地脚", "脚杯", "万向脚", "脚轮", "重型脚",
			"支撑脚", "减震垫", "防滑垫", "橡胶垫", "垫脚", "蹄脚", "脚盘", "福马轮"),
		new PartTypeRule(PartTypes.Standard, "标准件/紧固件", 94,
			"标准件", "紧固件", "螺栓", "螺丝", "螺钉", "螺帽", "螺母", "垫圈", "平垫", "弹垫",
			"挡圈", "卡簧", "铆钉", "拉铆", "销轴", "圆柱销", "定位销", "圆锥销", "开口销",
			"O型圈", "密封圈", "油封", "弹簧", "键条", "平键", "压铆螺母", "压铆螺柱", "压铆螺钉", "压铆销", "PEM紧固件", "PEM螺母", "PEM螺柱"),
		new PartTypeRule(PartTypes.Purchased, "外购件（电机、气缸、导轨、电器等）", 92,
			"外购件", "外购", "采购件", "电机", "马达", "减速机", "减速器", "气缸", "电缸",
			"导轨", "滑块", "滚珠丝杆", "滚珠丝杠", "丝杆", "丝杠", "轴承", "联轴器", "同步带", "链轮", "链条", "齿条",
			"直线模组", "直线导轨", "真空发生器", "真空吸盘", "气动阀", "液压阀", "油缸", "气管接头",
			"铰链", "合页", "门锁", "把手", "拉手",
			"风扇", "风机", "过滤器", "触摸屏", "PLC", "开关电源", "端子", "线缆", "线束",
			"传感器", "光电开关", "接近开关", "编码器", "安全光栅", "电磁阀", "变频器", "伺服驱动器", "伺服", "步进电机", "步进", "水泵", "油泵",
			"工控机", "交换机", "断路器", "接触器", "继电器", "变压器", "加热器", "温控表",
			"显示器", "键盘", "鼠标", "脚轮总成", "万向轮"),
		new PartTypeRule(PartTypes.Outsourced, "外加工件", 90,
			"外加工件", "外加工", "委外件", "委外", "外协件", "外协"),
		new PartTypeRule(PartTypes.Casting, "铸造件（砂铸、压铸、熔模铸造等）", 89,
			"铸造件", "铸件", "铸钢件", "铸铁件", "铸铝件", "铸铝", "铸铁壳体", "砂铸", "砂型铸造", "熔模铸造", "精密铸造", "压铸件", "铝压铸", "锌合金压铸", "die cast", "casting"),
		new PartTypeRule(PartTypes.Forging, "锻造件（自由锻、模锻等）", 88,
			"锻造件", "锻件", "自由锻", "模锻", "热锻", "冷锻", "锻钢", "forging", "forged"),
		new PartTypeRule(PartTypes.Plastic, "塑胶件（注塑、挤出成型等）", 87,
			"塑胶件", "塑料件", "注塑件", "注塑", "塑胶", "工程塑料", "挤出塑料", "塑料挤出", "橡胶件", "硅胶件", "橡胶制品", "rubber", "plastic"),
		new PartTypeRule(PartTypes.Additive, "增材制造件（3D 打印）", 86,
			"3d打印", "3d 打印", "增材制造", "增材件", "熔融沉积", "光固化打印", "粉末烧结", "slm打印", "sls打印", "sla打印", "fdm打印", "additive manufacturing"),
		new PartTypeRule(PartTypes.Profile, "型材件（方通、圆管、角钢、槽钢等）", 90,
			"方通", "方管", "方钢管", "方形钢管", "空心方管", "矩形管", "矩形钢管", "矩形空心管", "矩形方通", "矩管", "扁通", "扁管", "圆管", "钢管", "无缝管", "焊管", "管材",
			"角钢", "角铁", "槽钢", "工字钢", "I型钢", "I型梁", "H钢", "工字梁", "H型钢", "C型钢", "Z型钢", "T型钢",
			"扁钢", "圆钢", "六角钢", "方钢", "冷弯型钢", "钢轨",
			"铝型材", "工业型材", "欧标型材", "型材", "square tube", "rectangular tube", "box tube", "square hollow section", "rectangular hollow section", "hollow section"),
		new PartTypeRule(PartTypes.WeldAssembly, "组焊件/焊接组件", 88,
			"组焊", "焊接件", "焊接组件", "焊接总成", "焊合件", "焊合", "总焊", "机架",
			"框架", "焊接机架", "底座焊", "骨架"),
		new PartTypeRule(PartTypes.Weldment, "焊接件（含焊缝/结构构件）", 82,
			"焊件", "焊缝", "焊接零件"),
		new PartTypeRule(PartTypes.SheetMetal, "钣金件", 84,
			"钣金", "钣金件", "板件", "门板", "盖板", "面板", "侧板", "底板", "顶板",
			"背板", "挡板", "安装板", "折弯件", "冲压件", "机箱", "机柜", "壳体", "罩壳"),
		new PartTypeRule(PartTypes.Machined, "机加件", 78,
			"机加", "机加件", "加工件", "车削", "车件", "轴", "法兰", "法兰盘", "衬套",
			"导套", "连接块", "固定座", "支座", "安装座", "垫块", "定位块", "滑块座"),
	};

	// Shared by the viewer and classifier, so displayed presets reflect active logic.
	public static IReadOnlyList<PartTypeRule> Presets => Rules;

	// Declared property wins over the file name; both beat a geometry guess.
	public static PartTypeMatch FromText(string typeProperty, string documentTitle, string profileDescription = "")
	{
		string declared = (typeProperty ?? "").Trim();
		if (declared.Length > 0)
		{
			PartTypeRule rule = FindBestRule(declared);
			if (rule != null)
			{
				return new PartTypeMatch { Type = rule.Type, Confidence = 97, Evidence = "自定义属性已声明" + rule.Evidence };
			}
			string canonical = PartTypes.Normalize(declared);
			if (canonical.Length > 0 && canonical != PartTypes.Other)
			{
				return new PartTypeMatch { Type = canonical, Confidence = 96, Evidence = "自定义属性已声明类型“" + declared + "”" };
			}
			PartTypeRule namedRule = FindBestRule(documentTitle);
			if (namedRule != null)
				return new PartTypeMatch { Type = namedRule.Type, Confidence = namedRule.Confidence, Evidence = "通用类型属性“" + declared + "”未覆盖文件名线索；文件名命中" + namedRule.Evidence };
			PartTypeRule profileRule = FindBestRule(profileDescription);
			if (profileRule != null)
				return new PartTypeMatch { Type = profileRule.Type, Confidence = profileRule.Confidence, Evidence = "通用类型属性“" + declared + "”未覆盖型材规格线索；型材属性命中" + profileRule.Evidence };
			return new PartTypeMatch { Type = canonical.Length > 0 ? canonical : declared,
				Confidence = canonical.Length > 0 ? 60 : 90,
				Evidence = canonical.Length > 0 ? "自定义属性已声明通用类型“" + declared + "”" : "自定义属性已声明自定义类型“" + declared + "”" };
		}
		string title = (documentTitle ?? "").Trim();
		PartTypeRule titleRule = FindBestRule(title);
		if (titleRule == null)
		{
			PartTypeRule profileRule = FindBestRule(profileDescription);
			return profileRule == null ? new PartTypeMatch() : new PartTypeMatch
			{
				Type = profileRule.Type,
				Confidence = profileRule.Confidence,
				Evidence = "型材规格属性命中" + profileRule.Evidence
			};
		}
		return new PartTypeMatch
		{
			Type = titleRule.Type,
			Confidence = titleRule.Confidence,
			Evidence = "文件名命中" + titleRule.Evidence
		};
	}

	private static PartTypeRule FindBestRule(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return null;
		// Common stock profiles are frequently stored as a dimensional
		// specification (L50x50x5, C100x50x20x2, H200x200x8x12) without a
		// localized type name. Treat the section code as a profile signal; the
		// type-group policy will refine it to angle/channel/H/I steel.
		if (Regex.IsMatch(text, @"(?<![A-Za-z0-9])L\s*\d{2,4}(?:\s*[x×*]\s*\d{1,4}){1,2}(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
			return new PartTypeRule(PartTypes.Profile, "L 型材规格", 89, "L 型材规格");
		if (Regex.IsMatch(text, @"(?<![A-Za-z0-9])(?:C|U|Z|T|H|I)\s*\d{2,4}(?:\s*[x×*]\s*\d{1,4}){1,3}(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
			return new PartTypeRule(PartTypes.Profile, "型钢截面规格", 88, "型钢截面规格");
		// "直线导轨安装板" or "气缸固定座" is a manufactured accessory. A hardware
		// token inside that name does not establish purchased identity for the accessory.
		if (!new[] { "外购", "采购", "标准件", "外协", "委外" }.Any(word => text.Contains(word)))
		{
			Match accessory = Regex.Match(text.Trim(), @"(安装板|连接板|防护罩|盖板|安装座|固定座|连接块|支架)(?:[-_\s]*\d+)?(?:\.(?:sldprt|sldasm))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
			if (accessory.Success && Rules.Any(rule => (rule.Type == PartTypes.Purchased || rule.Type == PartTypes.Standard || rule.Type == PartTypes.FootPad) && rule.Matches(text)))
			{
				bool machined = new[] { "安装座", "固定座", "连接块" }.Contains(accessory.Groups[1].Value);
				return new PartTypeRule(machined ? PartTypes.Machined : PartTypes.SheetMetal, "制造附件名称“" + accessory.Groups[1].Value + "”", 82, accessory.Groups[1].Value);
			}
		}
		return Rules.Select((rule, index) => new
			{
				Rule = rule,
				Index = index,
				Length = Math.Max(
					Math.Max(rule.Keywords.Where(keyword => text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
						.Select(keyword => keyword.Length).DefaultIfEmpty(0).Max(),
						rule.Type == PartTypes.Profile && Regex.IsMatch(text, @"(?<![A-Z0-9])(?:SHS|RHS|CHS)(?![A-Z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ? 3 : 0),
					rule.Type == PartTypes.Additive && Regex.IsMatch(text, @"(?<![A-Z0-9])(?:SLM|SLS|SLA|FDM)(?![A-Z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ? 3 : 0)
			})
			.Where(match => match.Length > 0)
			.OrderByDescending(match => match.Length)
			.ThenBy(match => match.Index)
			.Select(match => match.Rule)
			.FirstOrDefault();
	}

	// Geometry is the weakest signal: it only classifies when nothing declared a type.
	public static PartTypeMatch FromGeometry(bool sheetMetal, bool weldment, bool weld, bool tubeLike, bool revolve, bool milling, bool isAssembly)
	{
		if (isAssembly)
		{
			return new PartTypeMatch { Type = PartTypes.WeldAssembly, Confidence = 62, Evidence = "装配体按组件聚合判定" };
		}
		if (sheetMetal && (weldment || weld))
		{
			return new PartTypeMatch { Type = PartTypes.Mixed, Confidence = 74, Evidence = weld ? "同时检测到钣金与明确焊接证据" : "同时检测到结构构件与钣金特征" };
		}
		if (weldment || weld)
		{
			return new PartTypeMatch { Type = PartTypes.Weldment, Confidence = 70, Evidence = "检测到结构构件或焊缝特征" };
		}
		if (tubeLike)
		{
			return new PartTypeMatch { Type = PartTypes.Profile, Confidence = 72, Evidence = "实体几何为闭合等截面直管" };
		}
		if (sheetMetal)
		{
			return new PartTypeMatch { Type = PartTypes.SheetMetal, Confidence = 70, Evidence = "检测到钣金特征或等厚板几何" };
		}
		if (revolve || milling)
		{
			return new PartTypeMatch { Type = PartTypes.Machined, Confidence = 64, Evidence = "检测到旋转或铣削类特征" };
		}
		return new PartTypeMatch();
	}

	// Default chain per type. Users can override any entry through config.
	// Every chain ends where the part leaves the shop - a check, a store or a
	// dispatch step - because a chain that stops mid-process reads as finished
	// work when it is only half routed.
	public static IReadOnlyList<string> DefaultChain(string type)
	{
		switch (PartTypes.Normalize(type))
		{
			case PartTypes.SheetMetal: return new[] { "激光下料", "去毛刺", "检验", "包装" };
			case PartTypes.Machined: return new[] { "备料", "机加", "去毛刺", "检验", "包装" };
			case PartTypes.WeldAssembly: return new[] { "锯切下料", "去毛刺", "组焊", "打磨", "检验", "包装" };
			case PartTypes.Weldment: return new[] { "锯切下料", "去毛刺", "焊接", "打磨", "检验", "包装" };
			case PartTypes.Mixed: return new[] { "激光下料", "去毛刺", "折弯", "组焊", "打磨", "检验", "包装" };
			case PartTypes.Profile: return new[] { "锯切下料", "去毛刺", "检验", "包装" };
			case PartTypes.FootPad: return new[] { "外购", "来料检验", "入库" };
			case PartTypes.Purchased: return new[] { "外购", "来料检验", "入库" };
			case PartTypes.Standard: return new[] { "外购", "来料检验", "入库" };
			case PartTypes.Outsourced: return new[] { "外购", "外加工", "回厂检验", "入库" };
			case PartTypes.Casting: return new[] { "铸造", "清砂/去浇口", "热处理", "机加工", "检验" };
			case PartTypes.Forging: return new[] { "锻造", "热处理", "机加工", "去毛刺", "检验" };
			case PartTypes.Plastic: return new[] { "注塑/成型", "去毛刺", "检验", "包装" };
			case PartTypes.Additive: return new[] { "3D打印", "去支撑", "打磨/后处理", "检验" };
		}
		return Array.Empty<string>();
	}

	public static string DefaultChains => string.Join("|", PartTypes.All
		.Select(type => new KeyValuePair<string, string>(type, string.Join(">", DefaultChain(type))))
		.Where(pair => pair.Value.Length > 0)
		.Select(pair => pair.Key + "=" + pair.Value));
}

internal sealed class PartTypeRule
{
	public PartTypeRule(string type, string evidence, int confidence, params string[] keywords)
	{
		Type = type;
		Evidence = evidence;
		Confidence = confidence;
		Keywords = keywords ?? Array.Empty<string>();
	}

	public string Type { get; }

	public string Evidence { get; }

	public int Confidence { get; }

	public string[] Keywords { get; }

	public bool Matches(string text)
	{
		if (string.IsNullOrEmpty(text)) return false;
		return Keywords.Any(keyword => text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
	}
}

// Fastener installation steps. They follow drilling and tapping and precede
// bending, so a part that is drilled, tapped and then pressed keeps that order.
internal static class FastenerProcesses
{
	public const string PressRivet = "压铆";
	public const string PressPin = "压钉";
	public const string WeldRivet = "焊铆";

	private static readonly string[] PressRivetKeywords =
	{
		"压铆", "PEM", "涨铆", "涨铆螺母", "无铆", "自扣", "自夹紧", "压铆螺母", "压铆螺柱", "压铆螺钉", "压铆销"
	};

	private static readonly string[] PressPinKeywords =
	{
		"压钉", "压销", "压入销", "直销", "销钉", "圆柱销压入", "弹性销", "滚花销"
	};

	private static readonly string[] WeldRivetKeywords =
	{
		"焊铆", "种焊", "植焊", "凸焊", "点焊", "储能焊", "螺柱焊", "焊螺母", "焊螺柱", "焊接螺母", "焊接螺柱"
	};

	public static int Order => 44;

	public static void Add(Dictionary<string, ProcessRecognitionService.Candidate> candidates, IEnumerable<ProcessFeatureFact> facts, ProcessGeometryFact geometry = null)
	{
		AddMatch(candidates, facts, PressRivetKeywords, PressRivet, "检测到压铆类特征（压铆螺母/螺柱/螺钉或涨铆）", 92);
		AddMatch(candidates, facts, PressPinKeywords, PressPin, "检测到压钉类特征（销钉压入）", 88);
		AddMatch(candidates, facts, WeldRivetKeywords, WeldRivet, "检测到焊铆类特征（种焊/凸焊/螺柱焊）", 90);
		if (geometry == null) return;
		if (geometry.PressFitGeometryCount > 0)
		{
			string evidence = string.IsNullOrWhiteSpace(geometry.FastenerGeometryEvidence)
				? "实体几何检测到同轴内孔与外圆柱/台阶实体，疑似压铆件或焊铆件，连接方式待确认"
				: geometry.FastenerGeometryEvidence + "；连接方式待确认";
			ProcessRecognitionService.Add(candidates, PressRivet, Order, 72, evidence);
		}
		if (geometry.PressPinGeometryCount > 0)
			ProcessRecognitionService.Add(candidates, PressPin, Order + 1, 68, "实体几何检测到压入销/台阶销候选，直径和压入方式待确认");
		if (geometry.WeldRivetGeometryCount > 0)
			ProcessRecognitionService.Add(candidates, WeldRivet, Order + 2, 68, "实体几何检测到凸台与同轴杆部候选，焊铆方式待确认");
		if (geometry.CounterboreGeometryCount > 0)
			ProcessRecognitionService.Add(candidates, "沉孔", 35, 80, "实体几何检测到同轴分级圆柱且一段接近板厚，存在沉孔/锪孔证据");
	}

	private static void AddMatch(
		Dictionary<string, ProcessRecognitionService.Candidate> candidates,
		IEnumerable<ProcessFeatureFact> facts,
		string[] keywords,
		string process,
		string evidence,
		int confidence)
	{
		if (candidates == null || facts == null) return;
		foreach (ProcessFeatureFact fact in facts)
		{
			if (fact == null) continue;
			string text = ((fact.Name ?? "") + " " + (fact.TypeName ?? ""));
			if (keywords.Any(keyword => text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0))
			{
				ProcessRecognitionService.Add(candidates, process, Order, confidence, evidence);
				return;
			}
		}
	}
}
