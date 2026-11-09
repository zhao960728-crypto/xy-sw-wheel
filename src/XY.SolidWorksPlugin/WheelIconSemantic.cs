using System;
using System.Collections.Generic;
using System.Drawing;

namespace XY.SolidWorksPlugin;

// Links the icon library back to commands, so a wheel cell or a command library
// row can receive artwork without the user picking it by hand. Matching never
// rewrites a command identity and never overrides an explicit user image.
internal static partial class WheelIconCatalog
{
    // Basic actions have a stable value even when the user edits the caption.
    // Keep their artwork bound to that value first; caption matching remains a
    // fallback for commands discovered from SOLIDWORKS or third-party add-ins.
    private static readonly Dictionary<string, string> BasicActionIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["settings"] = "gear",
        ["hole-color"] = "brush",
        ["hole-process"] = "drill",
        ["batch-sheet-metal-correction"] = "sheetbend",
        ["convert-to-sheet-metal-auto-corners"] = "sheetbend",
        ["property-operations"] = "properties.edit",
        ["process-recognition"] = "toolbox",
        ["explode-view"] = "explode",
        ["explode-all"] = "explode",
        ["panel-gap-adjustment"] = "door",
        ["panel-gap-display"] = "door",
        ["new-part"] = "part",
        ["new-assembly"] = "assembly",
        ["new-drawing"] = "drawing",
        ["rebuild"] = "gear",
        ["force-rebuild"] = "gear",
        ["normal-to"] = "front",
        ["zoom-fit"] = "extents",
        ["clear-selection"] = "eraser",
        ["view-front"] = "front",
        ["view-back"] = "front",
        ["view-left"] = "side",
        ["view-right"] = "side",
        ["view-top"] = "top",
        ["view-bottom"] = "top",
        ["view-isometric"] = "isometric"
    };

    internal static bool TryGetBasicActionIconId(string value, out string id)
    {
        id = "";
        return !string.IsNullOrWhiteSpace(value) && BasicActionIcons.TryGetValue(value.Trim(), out id);
    }

    private static readonly Dictionary<string, string> SemanticKeywords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // 通用与文件
        ["new"] = "新建|new", ["open"] = "打开|open", ["save"] = "保存|save", ["saveas"] = "另存为|saveas",
        ["plot"] = "打印|出图|print|plot", ["export"] = "导出|export", ["import"] = "导入|import",
        ["undo"] = "撤销|undo", ["redo"] = "重做|redo", ["properties"] = "属性|property", ["purge"] = "清理|purge",
        ["folder"] = "文件夹|目录|folder", ["cloud"] = "云端|云|cloud", ["download"] = "下载|download",
        ["upload"] = "上传|upload", ["link"] = "链接|link", ["search"] = "搜索|查找|search",
        // 视图
        ["zoom-in"] = "放大|zoom", ["zoom-out"] = "缩小|zoom", ["pan"] = "平移|pan", ["extents"] = "全图|适应|extents",
        ["orbit"] = "旋转视图|环绕|orbit", ["isometric"] = "等轴测|isometric", ["wireframe"] = "线框|wireframe",
        ["shade"] = "着色|shaded", ["display"] = "显示|display",
        // 标注与测量
        ["dimlinear"] = "标注|尺寸|dimension", ["measure"] = "测量|测距|measure", ["text"] = "文字|文本|text",
        ["table"] = "表格|table", ["tolerance"] = "公差|tolerance", ["centerline"] = "中心线|centerline",
        ["leader"] = "引线|leader", ["hatchpattern"] = "剖面线|hatch",
        ["tolerancegrade"] = "公差等级|尺寸公差", ["roughness"] = "粗糙度|ra", ["surfaceroughness"] = "表面粗糙度",
        ["geometrictolerance"] = "形位公差|gd&t", ["weldsymbolnote"] = "焊接符号|焊缝符号",
        // SW 文档
        ["part"] = "零件|part", ["assembly"] = "装配体|assembly", ["drawing"] = "工程图|drawing",
        ["sketch"] = "草图|sketch", ["configuration"] = "配置|configuration", ["material"] = "材质|材料|material",
        ["refplane"] = "基准面|参考面", ["refaxis"] = "基准轴|参考轴", ["refcsys"] = "坐标系", ["origin"] = "原点",
        ["designtable"] = "设计表|系列零件", ["equation"] = "方程式|equation",
        // 特征
        ["bossextrude"] = "拉伸凸台|拉伸|extrude", ["cutextrude"] = "拉伸切除|切除|cut",
        ["bossrevolve"] = "旋转凸台|旋转", ["cutrevolve"] = "旋转切除", ["sweep"] = "扫描|sweep",
        ["loft"] = "放样|loft", ["rib"] = "筋|加强筋", ["shell"] = "抽壳|shell", ["dome"] = "圆顶|dome",
        ["holewizard"] = "异型孔|孔向导", ["simplehole"] = "简单直孔|直孔", ["thread"] = "螺纹|thread",
        ["cosmeticthread"] = "装饰螺纹", ["chamferfeature"] = "倒角|chamfer", ["filletfeature"] = "圆角|fillet",
        ["draft"] = "拔模|draft", ["pattern"] = "阵列|pattern", ["mirrorfeature"] = "镜像|mirror",
        ["split"] = "分割|split", ["combine"] = "组合|combine", ["thicken"] = "加厚|thicken",
        // 钣金
        ["baseflange"] = "基体法兰|钣金", ["edgeflange"] = "边线法兰|法兰", ["hem"] = "褶边|卷边",
        ["jog"] = "转折|jog", ["unfold"] = "展开|unfold", ["fold"] = "折叠|fold", ["formtool"] = "成形工具",
        ["sheetbend"] = "折弯|bend", ["sheet-unfold"] = "钣金展开|展开|放样", ["vent"] = "通风口", ["louver"] = "百叶窗",
        ["gusset-stiffener"] = "角撑板|加强筋", ["rib-stiffener"] = "加强筋",
        // 焊件与型材
        ["weldment"] = "焊件|weldment", ["structuralmember"] = "结构构件|型材", ["weldbead"] = "焊缝|焊道",
        ["filletweld"] = "角焊缝", ["weldsymbol"] = "焊接符号", ["endcap"] = "顶端盖",
        ["squaretube"] = "方通|方管|square tube", ["recttube"] = "矩形管|矩形方通", ["roundtube"] = "圆管|圆通",
        ["anglesteel"] = "角钢|角铁", ["channelsteel"] = "槽钢", ["ibeam"] = "工字钢",
        ["flatsteel"] = "扁钢|扁通", ["roundsteel"] = "圆钢|圆棒",
        // 下料与机加工
        ["lasercut"] = "激光|下料|切割|laser", ["nc-punch"] = "数冲|冲压|数控冲", ["sawcut"] = "锯切|锯床|下料",
        ["deburr"] = "去毛刺|倒角去刺", ["drilling"] = "钻孔|钻|drill", ["tapping"] = "攻牙|攻丝|tap",
        ["countersink"] = "沉孔|沉头", ["spotface"] = "锪孔|锪平", ["milling"] = "铣削|铣|mill",
        ["turning"] = "车削|车|lathe", ["grinding"] = "磨削|磨|grind", ["wirecut"] = "线切割|慢走丝",
        ["edm"] = "电火花|edm", ["polishing"] = "抛光|polish", ["brushing"] = "拉丝|brushed",
        ["sandblasting"] = "喷砂|sandblast",
        // 焊接与检验
        ["assembly-weld"] = "组焊|总焊|焊接组件", ["spotweld"] = "点焊", ["fullweld"] = "满焊|满焊",
        ["stitchweld"] = "段焊|断续焊", ["sanding"] = "打磨|sand", ["straightening"] = "校正|校平",
        ["trialfit"] = "试装", ["disassembly"] = "拆装|拆卸", ["commissioning"] = "调试",
        ["inspection"] = "检验|检查|inspection", ["gauge"] = "量具|卡尺|gauge", ["cmm"] = "三坐标|cmm",
        ["firstarticle"] = "首件", ["patrolinspection"] = "巡检", ["finalinspection"] = "终检|终检",
        ["certificate"] = "合格证|证书",
        // 表面与热处理
        ["surfacetreatment"] = "表面处理|表处", ["bakingpaint"] = "烤漆|baking", ["powdercoat"] = "喷粉|喷塑|powder",
        ["spraypaint"] = "喷漆|spray", ["electroplating"] = "电镀|plating", ["galvanizing"] = "镀锌|galvanize",
        ["anodizing"] = "阳极氧化|anodize", ["blackening"] = "发黑|black", ["phosphating"] = "磷化",
        ["passivation"] = "钝化", ["heattreatment"] = "热处理|heat treat", ["quenching"] = "淬火|quench",
        ["tempering"] = "回火|temper", ["carburizing"] = "渗碳", ["nitriding"] = "氮化", ["aging"] = "时效",
        // 装配与紧固
        ["assemble"] = "装配|总装|assemble", ["mate"] = "配合|mate", ["explodedview"] = "爆炸图|explode",
        ["explodeline"] = "爆炸线", ["interference"] = "干涉检查", ["holealignment"] = "孔对齐",
        ["smartfastener"] = "智能扣件|扣件", ["boltconnection"] = "螺栓连接|螺栓", ["screw"] = "螺钉|螺丝|screw",
        ["washer"] = "垫圈|平垫|washer", ["springwasher"] = "弹垫|spring washer", ["circlip"] = "卡簧|挡圈",
        ["pinfastener"] = "销|定位销|cyl pin", ["rivet"] = "铆钉|rivet", ["blindrivet"] = "拉铆|抽芯铆钉",
        ["pressrivet"] = "压铆|压铆螺母|压铆螺柱|压铆螺钉", ["weldrivet"] = "焊铆|凸焊|种焊|点焊螺母",
        ["presspin"] = "压钉|直销压入|压销", ["weldstud"] = "种焊螺柱|焊螺柱", ["weldnut"] = "焊接螺母|焊螺母",
        ["bolt"] = "螺栓|bolt", ["nut"] = "螺母|nut", ["drill"] = "钻头|钻|drill",
        // 传动与气动
        ["bearing"] = "轴承|bearing", ["spring"] = "弹簧|spring", ["sealring"] = "密封圈|o型圈|seal",
        ["chain"] = "链条|chain", ["belt"] = "皮带|belt", ["coupling"] = "联轴器|coupling",
        ["motor"] = "电机|马达|motor", ["gearbox"] = "减速机|减速器|gearbox", ["cylinder"] = "气缸|cylinder",
        ["solenoidvalve"] = "电磁阀|solenoid", ["hydrauliccylinder"] = "液压缸|油缸", ["pump"] = "泵|pump",
        ["gear"] = "齿轮|gear", ["guiderail"] = "导轨|线轨", ["slider"] = "滑块", ["leadscrew"] = "丝杆|丝杠",
        ["servo"] = "伺服|servo", ["inverter"] = "变频器|inverter",
        // 电气
        ["plc"] = "plc|可编程", ["touchscreen"] = "触摸屏|hmi", ["sensor"] = "传感器|sensor",
        ["photoelectric"] = "光电开关|光电", ["relay"] = "继电器|relay", ["contactor"] = "接触器",
        ["breaker"] = "断路器|空开", ["switchingpower"] = "开关电源|电源", ["transformer"] = "变压器",
        ["terminal"] = "端子|terminal", ["cable"] = "线缆|线束|cable",
        // 五金与门柜
        ["hinge"] = "铰链|hinge", ["doorlock"] = "门锁|锁|lock", ["handle"] = "把手|handle",
        ["pull"] = "拉手|pull", ["pullhandle"] = "长拉手|拉手",
        ["footpad"] = "脚垫|垫脚", ["levelingfoot"] = "调整脚|调节脚|可调脚", ["anchorfoot"] = "地脚|地脚螺栓",
        ["footcup"] = "脚杯", ["caster"] = "脚轮|caster", ["swivelcaster"] = "万向轮|万向脚轮",
        ["shockpad"] = "减震垫|减震", ["antislippad"] = "防滑垫|防滑", ["supportfoot"] = "支撑脚",
        ["fuma-wheel"] = "福马轮", ["nylonwheel"] = "尼龙轮",
        // 流体与管件
        ["fan"] = "风扇|风机|fan", ["filter"] = "过滤器|filter", ["heatsink"] = "散热器|散热片",
        ["waterpump"] = "水泵", ["oilpump"] = "油泵", ["valvebody"] = "阀门|阀|valve",
        ["pipefitting"] = "管接头|接头", ["flange"] = "法兰|flange", ["elbow"] = "弯头|elbow",
        ["tee"] = "三通|tee", ["clamp"] = "卡箍|卡箍", ["gasket"] = "密封垫|垫片",
        ["oilseal"] = "油封", ["airtube"] = "气管|气管", ["fitting"] = "接头|fitting",
        ["quickconnector"] = "快插|快插接头",
        // 材料
        ["copperbar"] = "铜排|铜母排", ["aluminumprofile"] = "铝型材|型材", ["acrylic"] = "亚克力|有机玻璃",
        ["rubberpart"] = "橡胶件|橡胶", ["castiron"] = "铸铁件|铸铁", ["nonmetal"] = "非金属",
        ["insulator"] = "绝缘件|绝缘", ["sponge"] = "海绵", ["foamtape"] = "泡棉|泡棉胶",
        ["tape"] = "胶带|tape", ["woodboard"] = "木板|木材", ["stone"] = "石材|大理石",
        ["glass"] = "玻璃", ["paperboard"] = "纸皮|纸板", ["foam"] = "泡沫|珍珠棉",
        ["carton"] = "包装箱|纸箱|包装",
        // 清单与图纸
        ["cutlist"] = "切割清单|切割", ["bom"] = "bom|物料清单|明细", ["itemcode"] = "物料编码|编码|编号",
        ["watermark"] = "水印|watermark", ["process"] = "工序|工艺", ["routecard"] = "工艺卡|工艺路线",
        ["blueprint"] = "图纸|图档", ["partslist"] = "明细表|零件表", ["titleblock"] = "标题栏",
        ["technicalrequirement"] = "技术要求",
        // 供应链
        ["purchase"] = "外购|采购|purchase", ["procurement"] = "采购|procurement",
        ["inbound"] = "入库|inbound", ["outbound"] = "出库|outbound", ["inventory"] = "库存|inventory",
        ["stocktake"] = "盘点|stocktake", ["logistics"] = "物流|logistics", ["freight"] = "货运|运输",
        ["supplier"] = "供应商|supplier", ["quotation"] = "报价|quotation", ["cost"] = "成本|cost",
        ["pricing"] = "核价|定价", ["order"] = "订单|order", ["delivery"] = "交期|纳期|delivery",
        ["progress"] = "进度|progress", ["kanban"] = "看板|kanban",
        // 通用生活
        ["clock"] = "时钟|时间", ["calendar"] = "日历|日期", ["user"] = "用户|人员", ["team"] = "团队|班组",
        ["mail"] = "邮件|邮箱", ["phone"] = "电话|联系", ["lock"] = "锁定|lock", ["star"] = "收藏|星标",
        ["globe"] = "地球|全球", ["pin"] = "定位|位置", ["compass"] = "指南针|方向",
        ["wrench"] = "扳手|工具", ["hammer"] = "锤子|敲击", ["screwdriver"] = "螺丝刀|起子",
        ["ruler"] = "尺子|量尺", ["toolbox"] = "工具箱|工具", ["helmet"] = "安全帽|安全",
        // 建筑
        ["wall"] = "墙体|墙", ["door"] = "门|door", ["window"] = "窗|window", ["stairs"] = "楼梯|阶梯",
        ["roof"] = "屋顶|屋面", ["house"] = "房屋|建筑", ["column"] = "柱|立柱", ["beam"] = "梁|横梁",
        ["foundation"] = "基础|地基", ["road"] = "道路|路面", ["bridge"] = "桥梁", ["pipe"] = "管道|pipe",
        ["valve"] = "水龙头|valve", ["tap"] = "水龙头|龙头", ["socket"] = "插座|socket", ["lamp"] = "灯具|灯",
    };

    // Exact command tokens only. A partial or inferred match would attach artwork
    // to a command the user did not pick, which is worse than a plain fallback.
    public static bool TryGetCommandIconId(string command, out string id)
    {
        id = "";
        string token = NormalizeCommandToken(command);
        if (token.Length == 0) return false;
        foreach (string key in Keys)
        {
            if (string.Equals(key, token, StringComparison.OrdinalIgnoreCase))
            {
                id = key;
                return true;
            }
        }
        return false;
    }

    // Metadata is a fallback for the command token only. Nothing here can change
    // what a cell executes, and an explicit user image is never replaced.
    public static bool TryGetSemanticIconId(string command, string name, string description, string category, out string id)
    {
        if (TryGetCommandIconId(command, out id)) return true;
        int best = 0;
        id = "";
        for (int i = 0; i < Keys.Length; i++)
        {
            int score = SemanticScore(name, Names[i], Keys[i]) * 4
                + SemanticScore(description, Names[i], Keys[i]) * 2
                + SemanticScore(category, Names[i], Keys[i]);
            if (score > best) { best = score; id = Keys[i]; }
        }
        return best > 0;
    }

    public static bool TryDrawSemantic(Graphics g, string command, string name, string description, string category, PointF center, float size)
    {
        if (g == null || !TryGetSemanticIconId(command, name, description, category, out string id)) return false;
        Draw(g, Index[id], center, size);
        return true;
    }

    public static bool TryDrawCommand(Graphics g, string command, PointF center, float size)
    {
        if (g == null || !TryGetCommandIconId(command, out string id)) return false;
        Draw(g, Index[id], center, size);
        return true;
    }

    private static string NormalizeCommandToken(string command)
    {
        string text = (command ?? "").Trim();
        // Command names arrive with menu punctuation and accelerators.
        text = text.Replace("&", "").Replace("...", "").Replace("…", "");
        return text.Trim();
    }

    private static int SemanticScore(string value, string name, string key)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        int score = string.Equals(value.Trim(), name, StringComparison.OrdinalIgnoreCase) ? 16 : 0;
        if (name.Length > 1 && ContainsKeyword(value, name)) score = Math.Max(score, name.Length + 2);
        if (ContainsKeyword(value, key)) score = Math.Max(score, key.Length + 2);
        if (SemanticKeywords.TryGetValue(key, out string keywords))
        {
            foreach (string keyword in keywords.Split('|'))
            {
                if (keyword.Length > 0 && ContainsKeyword(value, keyword)) score = Math.Max(score, keyword.Length + 2);
            }
        }
        return score;
    }

    private static bool ContainsKeyword(string value, string keyword)
    {
        int start = 0;
        while (start < value.Length)
        {
            int p = value.IndexOf(keyword, start, StringComparison.OrdinalIgnoreCase);
            if (p < 0) return false;
            bool latin = keyword[0] < 128;
            if (!latin
                || ((p == 0 || !IsLatinWord(value[p - 1]))
                    && (p + keyword.Length == value.Length || !IsLatinWord(value[p + keyword.Length])))) return true;
            start = p + keyword.Length;
        }
        return false;
    }

    private static bool IsLatinWord(char c) => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9';
}
