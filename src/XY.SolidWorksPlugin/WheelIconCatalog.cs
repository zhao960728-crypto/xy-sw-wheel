using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace XY.SolidWorksPlugin;

internal sealed class WheelIconEntry
{
    public string Id { get; }
    public string Name { get; }
    public string Category { get; }
    public string Keywords { get; }
    internal WheelIconEntry(string id, string name, string category, string keywords)
    { Id = id; Name = name; Category = category; Keywords = keywords; }
}

// Stable IDs are independent of display language. Geometry uses a transparent
// 32-unit canvas, with semantic operation badges rather than color-only variants.
internal static partial class WheelIconCatalog
{
    private static readonly string[] Names = (
        "直线|多段线|圆|圆弧|矩形|椭圆|多边形|样条曲线|构造线|射线|点|圆环|填充|边界|螺旋|修订云线|" +
        "移动|复制|旋转|缩放|镜像|偏移|修剪|延伸|圆角|倒角|拉伸|分解|合并|打断|删除|阵列|" +
        "线性标注|对齐标注|角度标注|半径标注|直径标注|坐标标注|弧长标注|连续标注|基线标注|引线|多行文字|单行文字|表格|公差|中心线|测量|" +
        "新建图纸|打开文件|保存图纸|另存为|打印|导出|导入|撤销|重做|剪切|粘贴|图层|块|外部参照|属性|清理|" +
        "放大|缩小|平移|全图|上个视图|三维环绕|俯视|前视|侧视|等轴测|线框|着色|相机|视口|坐标系|显示|" +
        "墙体|门|窗|楼梯|屋顶|房屋|柱|梁|基础|道路|桥梁|管道|阀门|水龙头|插座|灯具|" +
        "扳手|锤子|螺丝刀|钻头|齿轮|螺栓|螺母|尺子|圆规|铅笔|画笔|橡皮|磁铁|水平仪|工具箱|安全帽|" +
        "文件夹|云端|下载|上传|链接|地球|定位|指南针|时钟|日历|用户|团队|邮件|电话|音乐|爱心|钣金展开|" +
        // SOLIDWORKS 文档与建模
        "零件|装配体|工程图|草图|配置|方程式|设计表|材质|外观|光源|贴图|基准面|基准轴|坐标系|原点|参考几何|" +
        "拉伸凸台|旋转凸台|扫描|放样|边界凸台|筋|抽壳|圆顶|异型孔|简单直孔|螺纹线|装饰螺纹|倒角特征|圆角特征|拔模|阵列特征|镜像特征|" +
        "拉伸切除|旋转切除|扫描切除|放样切除|分割|组合|移动面|删除面|替换面|加厚|曲面展平|填充曲面|等距曲面|延伸曲面|修剪曲面|缝合曲面|" +
        // 钣金
        "基体法兰|边线法兰|斜接法兰|褶边|转折|展开|折叠|成形工具|闭合角|断裂边角|焊接的边角|通风口|百叶窗|加强筋|角撑板|钣金折弯|" +
        // 焊件与型材
        "焊件|结构构件|剪裁结构构件|角撑板焊件|顶端盖|焊缝|圆角焊缝|焊接符号|方通|矩形管|圆管|角钢|槽钢|工字钢|扁钢|圆钢|" +
        // 机加工与工序
        "激光下料|数冲|锯切下料|去毛刺|钻孔加工|攻牙|沉孔|锪孔|铣削|车削|磨削|线切割|电火花|抛光|拉丝|喷砂|" +
        "组焊|点焊|满焊|段焊|打磨|校正|试装|拆装|调试|检验|量具|三坐标|首件|巡检|终检|合格证|" +
        "表面处理|烤漆|喷粉|喷漆|电镀|镀锌|阳极氧化|发黑|磷化|钝化|热处理|淬火|回火|渗碳|氮化|时效|" +
        // 装配、紧固件与外购
        "装配|配合|爆炸图|爆炸线|干涉检查|孔对齐|智能扣件|螺栓连接|螺钉|垫圈|弹垫|卡簧|销|铆钉|拉铆|压铆件|" +
        "焊铆件|压钉|种焊螺柱|焊接螺母|轴承|弹簧|密封圈|链条|皮带|联轴器|电机|减速机|气缸|电磁阀|液压缸|泵|" +
        "导轨|滑块|丝杆|伺服|变频器|PLC|触摸屏|传感器|光电开关|继电器|接触器|断路器|开关电源|变压器|端子|线缆|" +
        "铰链|合页|门锁|把手|拉手|脚垫|调整脚|地脚|脚杯|脚轮|万向轮|减震垫|防滑垫|支撑脚|福马轮|尼龙轮|" +
        "风扇|过滤器|散热器|水泵|油泵|阀门|管接头|法兰|弯头|三通|卡箍|密封垫|油封|气管|接头|快插|" +
        // 材料、数据与管理
        "铜排|铝型材|亚克力|橡胶件|铸铁件|非金属|绝缘件|海绵|泡棉|胶带|木板|石材|玻璃|纸皮|泡沫|包装箱|" +
        "切割清单|BOM表|物料编码|属性|水印|工序|工艺卡|图纸|明细表|标题栏|技术要求|公差|粗糙度|形位公差|焊接符号标注|表面粗糙度|剖面线|" +
        "外购|采购|入库|出库|库存|盘点|物流|货运|供应商|报价|成本|核价|订单|交期|进度|看板").Split('|');
    private static readonly string[] Keys = (
        "line|polyline|circle|arc|rectangle|ellipse|polygon|spline|xline|ray|point|donut|hatch|boundary|helix|revcloud|" +
        "move|copy|rotate|scale|mirror|offset|trim|extend|fillet|chamfer|stretch|explode|join|break|erase|array|" +
        "dimlinear|dimaligned|dimangular|dimradius|dimdiameter|dimordinate|dimarc|dimcontinue|dimbaseline|leader|mtext|text|table|tolerance|centerline|measure|" +
        "new|open|save|saveas|plot|export|import|undo|redo|cut|paste|layer|block|xref|properties|purge|" +
        "zoom-in|zoom-out|pan|extents|previous-view|orbit|top|front|side|isometric|wireframe|shade|camera|viewport|ucs|display|" +
        "wall|door|window|stairs|roof|house|column|beam|foundation|road|bridge|pipe|valve|tap|socket|lamp|" +
        "wrench|hammer|screwdriver|drill|gear|bolt|nut|ruler|compass-tool|pencil|brush|eraser|magnet|level|toolbox|helmet|" +
        "folder|cloud|download|upload|link|globe|pin|compass|clock|calendar|user|team|mail|phone|music|heart|sheet-unfold|" +
        "part|assembly|drawing|sketch|configuration|equation|designtable|material|appearance|light|decal|refplane|refaxis|refcsys|origin|refgeometry|" +
        "bossextrude|bossrevolve|sweep|loft|boundaryboss|rib|shell|dome|holewizard|simplehole|thread|cosmeticthread|chamferfeature|filletfeature|draft|pattern|mirrorfeature|" +
        "cutextrude|cutrevolve|cutsweep|cutloft|split|combine|moveface|deleteface|replaceface|thicken|flattensurface|fillsurface|offsetsurface|extendsurface|trimsurface|knitsurface|" +
        "baseflange|edgeflange|miterflange|hem|jog|unfold|fold|formtool|closedcorner|breakcorner|weldedcorner|vent|louver|rib-stiffener|gusset-stiffener|sheetbend|" +
        "weldment|structuralmember|trim-extend|gusset|endcap|weldbead|filletweld|weldsymbol|squaretube|recttube|roundtube|anglesteel|channelsteel|ibeam|flatsteel|roundsteel|" +
        "lasercut|nc-punch|sawcut|deburr|drilling|tapping|countersink|spotface|milling|turning|grinding|wirecut|edm|polishing|brushing|sandblasting|" +
        "assembly-weld|spotweld|fullweld|stitchweld|sanding|straightening|trialfit|disassembly|commissioning|inspection|gauge|cmm|firstarticle|patrolinspection|finalinspection|certificate|" +
        "surfacetreatment|bakingpaint|powdercoat|spraypaint|electroplating|galvanizing|anodizing|blackening|phosphating|passivation|heattreatment|quenching|tempering|carburizing|nitriding|aging|" +
        "assemble|mate|explodedview|explodeline|interference|holealignment|smartfastener|boltconnection|screw|washer|springwasher|circlip|pinfastener|rivet|blindrivet|pressrivet|" +
        "weldrivet|presspin|weldstud|weldnut|bearing|spring|sealring|chain|belt|coupling|motor|gearbox|cylinder|solenoidvalve|hydrauliccylinder|pump|" +
        "guiderail|slider|leadscrew|servo|inverter|plc|touchscreen|sensor|photoelectric|relay|contactor|breaker|switchingpower|transformer|terminal|cable|" +
        "hinge|doorlock|handle|pull|pullhandle|footpad|levelingfoot|anchorfoot|footcup|caster|swivelcaster|shockpad|antislippad|supportfoot|fuma-wheel|nylonwheel|" +
        "fan|filter|heatsink|waterpump|oilpump|valvebody|pipefitting|flange|elbow|tee|clamp|gasket|oilseal|airtube|fitting|quickconnector|" +
        "copperbar|aluminumprofile|acrylic|rubberpart|castiron|nonmetal|insulator|sponge|foamtape|tape|woodboard|stone|glass|paperboard|foam|carton|" +
        "cutlist|bom|itemcode|property|watermark|process|routecard|blueprint|partslist|titleblock|technicalrequirement|tolerancegrade|roughness|geometrictolerance|weldsymbolnote|surfaceroughness|hatchpattern|" +
        "purchase|procurement|inbound|outbound|inventory|stocktake|logistics|freight|supplier|quotation|cost|pricing|order|delivery|progress|kanban").Split('|');
    private static readonly string[] Categories = { "草图与绘制", "修改与编辑", "尺寸与标注", "文件与管理", "视图与显示", "建筑与设施", "工具与机械", "通用与生活", "钣金工具",
        "SW 文档与建模", "SW 特征建模", "SW 切除与曲面", "钣金特征", "焊件与型材", "下料与机加工", "焊接与检验", "表面与热处理", "装配与紧固", "传动与气动", "电气与控制",
        "五金与门柜", "流体与管件", "脚垫与脚轮", "材料与包装", "清单与图纸", "供应链与管理" };
    // Operation badges are drawn on top of a shape; styles change how the shape
    // itself is rendered. Both multiply the shape count into a large library.
    private static readonly string[] Actions = { "", "新增", "编辑", "复制", "删除", "查找", "锁定", "收藏",
        "警告", "完成", "禁止", "问号", "计时", "快速", "刷新", "链接", "下载", "上传", "设置", "精选" };
    private static readonly string[] ActionKeys = { "", "add", "edit", "copy", "delete", "search", "lock", "star",
        "warning", "done", "forbidden", "help", "timer", "fast", "refresh", "linkbadge", "get", "put", "settings", "featured" };
    private static readonly string[] Styles = { "", "实心", "双色" };
    private static readonly string[] StyleKeys = { "", "solid", "duo" };
    internal const int StyleCount = 3;
    internal const int BadgeCount = 20;
    private static readonly Dictionary<string, int> Index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public static IReadOnlyList<WheelIconEntry> Entries { get; }

    static WheelIconCatalog()
    {
        var entries = new List<WheelIconEntry>(20480);
        for (int style = 0; style < StyleCount; style++)
        for (int operation = 0; operation < BadgeCount; operation++)
        for (int i = 0; i < Names.Length; i++)
        {
            // The plain id and the "<key>.<badge>" id keep their historic meaning,
            // so a configuration saved before styles existed still resolves.
            var id = Keys[i]
                + (operation == 0 ? "" : "." + ActionKeys[operation])
                + (style == 0 ? "" : "." + StyleKeys[style]);
            Index[id] = i * (BadgeCount * StyleCount) + operation * StyleCount + style;
            var label = Names[i]
                + (operation == 0 ? "" : " · " + Actions[operation])
                + (style == 0 ? "" : " · " + Styles[style]);
            entries.Add(new WheelIconEntry(id, label, CategoryOf(i),
                Keys[i] + " " + Names[i] + " " + Actions[operation] + " " + ActionKeys[operation] + " " + Styles[style] + " " + StyleKeys[style]));
        }
        Entries = entries.AsReadOnly();
    }

    // The first nine categories cover the original 16-shape groups; the extra
    // "钣金工具" entry belongs to the tail shape, and the rest are 16 wide.
    private static string CategoryOf(int shape)
    {
        if (shape < 128) return Categories[shape / 16];
        if (shape == 128) return Categories[8];
        int extra = shape - 129;
        int slot = 9 + extra / 16;
        return slot < Categories.Length ? Categories[slot] : Categories[Categories.Length - 1];
    }

    public static int ShapeCount => Names.Length;

    public static Bitmap Render(string id, int size)
    {
        int index;
        if (id == null || !Index.TryGetValue(id, out index)) throw new ArgumentException("未知图标。", nameof(id));
        if (size <= 0 || size > 1024) throw new ArgumentOutOfRangeException(nameof(size));
        // Dense wheels and high-DPI previews can give a cell an icon box below
        // the catalogue's normal raster size.  Render at the smallest useful
        // canvas and let the caller scale it into the cell; throwing here makes
        // the entire WinForms preview paint fail and shows the red error X.
        size = Math.Max(16, size);
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        try { using (var g = Graphics.FromImage(bitmap)) Draw(g, index, new PointF(size / 2f, size / 2f), size); return bitmap; }
        catch { bitmap.Dispose(); throw; }
    }

    private static void Draw(Graphics g, int index, PointF center, float size)
    {
        if (size <= 0 || float.IsNaN(size) || float.IsInfinity(size)) return;
        var state = g.Save();
        try
        {
            g.TranslateTransform(center.X - size / 2, center.Y - size / 2);
            g.ScaleTransform(size / 32, size / 32);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // index = shape * (badges * styles) + badge * styles + style
            int style = index % StyleCount;
            int operation = (index / StyleCount) % BadgeCount;
            int shape = index / (BadgeCount * StyleCount);
            float width = style == 1 ? 2.8f : 1.9f;
            using (var green = new Pen(Color.FromArgb(36, 156, 107), width))
            using (var purple = new Pen(style == 2 ? Color.FromArgb(153, 105, 211) : Color.FromArgb(125, 86, 188), width))
            using (var accent = new Pen(Color.FromArgb(153, 105, 211), width))
            {
                green.StartCap = green.EndCap = purple.StartCap = purple.EndCap = accent.StartCap = accent.EndCap = LineCap.Round;
                green.LineJoin = purple.LineJoin = accent.LineJoin = LineJoin.Round;
                // The filled style sits on a tinted disc; the duo style adds an
                // offset accent copy so the artwork reads as two ink colours.
                if (style == 1)
                {
                    using var fill = new SolidBrush(Color.FromArgb(38, 36, 156, 107));
                    g.FillEllipse(fill, 2, 2, 28, 28);
                }
                if (operation != 0) g.ScaleTransform(.82f, .82f);
                var body = style == 2 ? green : green;
                if (style == 2)
                {
                    using var ghost = new Pen(Color.FromArgb(90, 153, 105, 211), width);
                    ghost.StartCap = ghost.EndCap = LineCap.Round;
                    ghost.LineJoin = LineJoin.Round;
                    g.TranslateTransform(1.1f, 1.1f);
                    Shape(g, ghost, ghost, ghost, shape);
                    g.TranslateTransform(-1.1f, -1.1f);
                }
                Shape(g, body, purple, accent, shape);
                if (operation != 0) { g.ScaleTransform(1 / .82f, 1 / .82f); Badge(g, accent, operation); }
            }
        }
        finally { g.Restore(state); }
    }

    private static PointF[] Points(params float[] xy)
    { var p = new PointF[xy.Length / 2]; for (int i = 0; i < p.Length; i++) p[i] = new PointF(xy[2 * i], xy[2 * i + 1]); return p; }
    private static void Lines(Graphics g, Pen p, params float[] xy) { g.DrawLines(p, Points(xy)); }
    private static void Poly(Graphics g, Pen p, params float[] xy) { g.DrawPolygon(p, Points(xy)); }
    private static void Arrow(Graphics g, Pen p, float x1, float y1, float x2, float y2)
    {
        g.DrawLine(p, x1, y1, x2, y2);
        double a = Math.Atan2(y2 - y1, x2 - x1);
        g.DrawLine(p, x2, y2, x2 - 4 * (float)Math.Cos(a - .55), y2 - 4 * (float)Math.Sin(a - .55));
        g.DrawLine(p, x2, y2, x2 - 4 * (float)Math.Cos(a + .55), y2 - 4 * (float)Math.Sin(a + .55));
    }
    private static void Box(Graphics g, Pen p, float x = 7, float y = 7, float w = 18, float h = 18) { g.DrawRectangle(p, x, y, w, h); }
    private static void Cube(Graphics g, Pen p)
    { Poly(g, p, 16,4,27,10,27,23,16,29,5,23,5,10); Lines(g,p,5,10,16,16,27,10); Lines(g,p,16,16,16,29); }
    private static void Page(Graphics g, Pen p)
    { Poly(g,p,7,4,20,4,26,10,26,28,7,28); Lines(g,p,20,4,20,10,26,10); }
    private static void Star(Graphics g, Pen p, float cx, float cy, float r)
    {
        var points = new PointF[10];
        for (int i=0;i<10;i++) { double a = -Math.PI/2+i*Math.PI/5; float d=i%2==0?r:r*.44f; points[i]=new PointF(cx+d*(float)Math.Cos(a),cy+d*(float)Math.Sin(a)); }
        g.DrawPolygon(p,points);
    }
    private static void Badge(Graphics g, Pen p, int operation)
    {
        using(var fill = new SolidBrush(Color.FromArgb(245, 28, 42, 58))) g.FillEllipse(fill,19,19,13,13);
        float x=25.3f,y=25.3f;
        switch(operation)
        {
            case 1: g.DrawLine(p,x-3,y,x+3,y);g.DrawLine(p,x,y-3,x,y+3); break;
            case 2: Lines(g,p,22,28,23,25,27,21,29,23,25,27,22,28); break;
            case 3: Box(g,p,22,22,5,5);Lines(g,p,25,24,29,24,29,29,24,29);break;
            case 4: Lines(g,p,22,22,28,28);Lines(g,p,28,22,22,28);break;
            case 5: g.DrawEllipse(p,21,21,6,6);g.DrawLine(p,26,26,29,29);break;
            case 6: Box(g,p,22,25,7,5);g.DrawArc(p,23,20,5,8,180,180);break;
            case 7: Star(g,p,x,y,4.5f);break;
            case 8: Poly(g,p,25,20,30,29,20,29);g.DrawLine(p,25,23,25,26);g.DrawLine(p,25,27.5f,25,27.6f);break;
            case 9: Lines(g,p,21,25,24,28,28,22);break;
            case 10: g.DrawEllipse(p,21,21,8,8);g.DrawLine(p,23,23,27,27);break;
            case 11: g.DrawArc(p,22,20,7,7,0,270);g.DrawLine(p,25.5f,27,25.5f,29);g.DrawLine(p,25.5f,29,25.4f,29);break;
            case 12: g.DrawEllipse(p,21,21,8,8);g.DrawLine(p,25,21,25,25);g.DrawLine(p,25,25,28,26);break;
            case 13: Poly(g,p,26,20,22,25,25,25,23,29,28,25,25,25,28,20);break;
            case 14: g.DrawArc(p,21,22,8,7,40,300);Lines(g,p,28,20,29,24,28,25);break;
            case 15: g.DrawLine(p,22,27,26,23);g.DrawLine(p,26,23,26,27);g.DrawArc(p,27,23,5,4,250,220);g.DrawLine(p,23,22,27,26);break;
            case 16: g.DrawLine(p,25,20,25,26);Lines(g,p,22,24,25,27,28,24);g.DrawLine(p,21,29,29,29);break;
            case 17: g.DrawLine(p,25,29,25,23);Lines(g,p,22,25,25,22,28,25);g.DrawLine(p,21,20,29,20);break;
            case 18: g.DrawEllipse(p,22,22,6,6);for(int i=0;i<6;i++){double t=i*Math.PI/3;g.DrawLine(p,25+5*(float)Math.Cos(t),25+5*(float)Math.Sin(t),25+8*(float)Math.Cos(t),25+8*(float)Math.Sin(t));}break;
            case 19: Star(g,p,x,y,3.2f);g.DrawEllipse(p,20,20,10,10);break;
        }
    }

    private static void Shape(Graphics g, Pen b, Pen c, Pen a, int s)
    {
        switch(s)
        {
            case 0: Lines(g,b,6,25,26,7); Box(g,c,4,23,4,4);Box(g,c,24,5,4,4);break;
            case 1: Lines(g,b,5,25,10,8,21,22,27,6); foreach(var p in Points(5,25,10,8,21,22,27,6))g.DrawEllipse(c,p.X-1.5f,p.Y-1.5f,3,3);break;
            case 2: g.DrawEllipse(b,5,5,22,22);g.DrawLine(c,13,16,19,16);g.DrawLine(c,16,13,16,19);break;
            case 3: g.DrawArc(b,5,6,23,23,185,165);g.DrawEllipse(c,3,14,4,4);g.DrawEllipse(c,25,14,4,4);break;
            case 4: Box(g,b,4,8,24,17);Box(g,c,4,21,4,4);break;
            case 5: g.DrawEllipse(b,3,9,26,14);g.DrawLine(c,6,16,26,16);break;
            case 6: Poly(g,b,16,4,27,12,23,26,9,26,5,12);break;
            case 7: g.DrawBezier(b,4,23,10,-5,22,39,28,8);Box(g,c,2,21,3,3);Box(g,c,27,6,3,3);break;
            case 8: g.DrawLine(b,3,29,29,3);g.DrawLine(c,3,3,29,29);g.DrawEllipse(a,13,13,6,6);break;
            case 9: Arrow(g,b,6,25,28,5);g.DrawEllipse(c,4,23,4,4);break;
            case 10: g.DrawEllipse(a,13,13,6,6);g.DrawLine(b,16,4,16,10);g.DrawLine(b,16,22,16,28);g.DrawLine(b,4,16,10,16);g.DrawLine(b,22,16,28,16);break;
            case 11: g.DrawEllipse(b,4,4,24,24);g.DrawEllipse(c,10,10,12,12);break;
            case 12: Box(g,b,5,5,22,22);for(int x=7;x<27;x+=5)g.DrawLine(c,x,25,25,x);break;
            case 13: Poly(g,b,5,5,24,5,24,13,28,13,28,26,5,26);Poly(g,c,10,10,19,10,19,18,23,18,23,21,10,21);break;
            case 14: for(int i=0;i<4;i++)g.DrawArc(i%2==0?b:c,5,4+i*5,22,10,0,300);break;
            case 15: g.DrawArc(b,3,10,11,12,90,230);g.DrawArc(b,10,4,13,13,170,230);g.DrawArc(b,20,10,10,13,230,220);g.DrawArc(b,9,18,16,10,0,180);break;
            case 16: Arrow(g,b,16,16,16,4);Arrow(g,b,16,16,28,16);Arrow(g,b,16,16,16,28);Arrow(g,b,16,16,4,16);break;
            case 17: Box(g,b,5,5,15,15);Box(g,c,12,12,15,15);break;
            case 18: g.DrawArc(b,5,5,22,22,30,290);Lines(g,c,22,3,27,8,21,10);Box(g,a,12,12,8,8);break;
            case 19: Box(g,b,4,17,11,11);Box(g,c,13,4,15,15);Arrow(g,a,9,23,24,8);break;
            case 20: Poly(g,b,5,8,12,8,12,24,5,24,8,16);Poly(g,c,27,8,20,8,20,24,27,24,24,16);g.DrawLine(a,16,3,16,29);break;
            case 21: Lines(g,b,4,25,4,7,21,7);Lines(g,c,10,27,10,13,27,13);Arrow(g,a,18,21,25,21);break;
            case 22: Lines(g,b,5,7,27,7);Lines(g,b,9,4,9,28);Lines(g,c,9,23,27,23);g.DrawLine(a,19,17,26,28);g.DrawLine(a,26,17,19,28);break;
            case 23: g.DrawLine(b,26,4,26,28);g.DrawLine(c,4,18,15,18);Arrow(g,a,16,18,25,18);break;
            case 24: Lines(g,b,5,27,5,15);g.DrawArc(b,5,5,20,20,180,90);Lines(g,b,15,5,27,5);g.DrawArc(c,12,12,10,10,180,90);break;
            case 25: Lines(g,b,5,27,5,15,15,5,27,5);g.DrawLine(c,10,22,22,10);break;
            case 26: Poly(g,b,4,9,18,9,26,24,4,24);Arrow(g,c,15,16,28,16);break;
            case 27: Lines(g,b,5,12,5,5,12,5);Lines(g,b,20,5,27,5,27,12);Lines(g,c,27,20,27,27,20,27);Lines(g,c,12,27,5,27,5,20);g.DrawLine(a,12,12,20,20);g.DrawLine(a,20,12,12,20);break;
            case 28: Lines(g,b,4,9,12,9,16,16);Lines(g,c,16,16,21,23,28,23);g.DrawEllipse(a,12,12,8,8);break;
            case 29: Lines(g,b,4,24,13,15);Lines(g,c,19,9,28,0+4);Lines(g,a,12,9,19,16);break;
            case 30: Lines(g,b,8,9,10,28,23,28,25,9);Lines(g,c,5,8,28,8);Lines(g,c,12,8,12,4,21,4,21,8);g.DrawLine(b,14,13,14,23);g.DrawLine(b,20,13,20,23);break;
            case 31: for(int y=0;y<3;y++)for(int x=0;x<3;x++)Box(g,(x+y)%2==0?b:c,4+x*9,4+y*9,6,6);break;
            case 32: g.DrawLine(b,6,8,6,28);g.DrawLine(b,26,8,26,28);Arrow(g,c,16,13,6,13);Arrow(g,c,16,13,26,13);g.DrawLine(a,11,24,21,24);break;
            case 33: g.DrawLine(b,3,18,13,28);g.DrawLine(b,19,2,29,12);Arrow(g,c,16,16,8,24);Arrow(g,c,16,16,24,8);break;
            case 34: Lines(g,b,27,26,5,26,21,5);g.DrawArc(c,0,9,32,32,305,55);break;
            case 35: g.DrawEllipse(b,5,5,22,22);Arrow(g,c,16,16,25,9);g.DrawEllipse(a,15,15,2,2);break;
            case 36: g.DrawEllipse(b,5,5,22,22);Arrow(g,c,16,16,7,25);Arrow(g,c,16,16,25,7);break;
            case 37: Lines(g,b,5,4,5,27,28,27);Lines(g,c,12,16,20,16,20,6,28,6);g.DrawEllipse(a,10,14,4,4);break;
            case 38: g.DrawArc(b,4,9,24,24,190,150);g.DrawArc(c,4,3,24,24,190,150);g.DrawLine(a,3,12,8,15);g.DrawLine(a,24,15,29,12);break;
            case 39: for(int x=5;x<=27;x+=11)g.DrawLine(b,x,7,x,27);Arrow(g,c,15,14,5,14);Arrow(g,c,16,14,27,14);g.DrawLine(a,5,24,27,24);break;
            case 40: Lines(g,b,5,5,5,27,27,27);Arrow(g,c,5,9,17,9);Arrow(g,c,5,17,27,17);g.DrawLine(b,17,6,17,22);break;
            case 41: Lines(g,b,28,7,17,7,6,25);Lines(g,c,5,18,6,25,13,22);g.DrawLine(a,20,12,28,12);break;
            case 42: Lines(g,b,4,25,4,6,11,17,18,6,18,25);g.DrawLine(c,22,8,28,8);g.DrawLine(c,22,15,28,15);g.DrawLine(c,22,22,28,22);break;
            case 43: Lines(g,b,5,7,27,7);g.DrawLine(b,16,7,16,27);g.DrawLine(c,11,27,21,27);break;
            case 44: Box(g,b,4,5,24,22);g.DrawLine(c,4,12,28,12);g.DrawLine(b,4,20,28,20);g.DrawLine(b,12,12,12,27);g.DrawLine(b,21,12,21,27);break;
            case 45: Box(g,b,3,10,26,13);g.DrawLine(c,11,10,11,23);g.DrawLine(c,22,10,22,23);g.DrawEllipse(a,5,14,4,4);Lines(g,a,14,19,17,13,20,19);g.DrawLine(a,25,14,25,19);break;
            case 46: g.DrawEllipse(b,7,7,18,18);g.DrawLine(c,16,3,16,29);g.DrawLine(c,3,16,29,16);break;
            case 47: Lines(g,b,5,7,5,26,27,26);for(int x=9;x<28;x+=4)g.DrawLine(c,x,22,x,26);g.DrawLine(a,9,8,25,8);g.DrawLine(a,9,5,9,11);g.DrawLine(a,25,5,25,11);break;
            case 48: Page(g,b);g.DrawLine(c,11,18,22,18);g.DrawLine(c,16,13,16,23);break;
            case 49: Lines(g,b,4,26,4,8,12,8,15,12,28,12);Poly(g,c,4,26,9,16,29,16,24,26);break;
            case 50: Poly(g,b,5,5,24,5,28,9,28,28,5,28);Box(g,c,10,5,12,8);Box(g,c,10,20,13,8);break;
            case 51: Page(g,b);Lines(g,c,13,25,15,19,25,9,29,13,19,23,13,25);break;
            case 52: Box(g,b,4,11,24,13);Box(g,c,9,4,14,7);Box(g,c,9,20,14,9);g.DrawLine(a,23,15,25,15);break;
            case 53: Page(g,b);Arrow(g,c,13,19,29,19);break;
            case 54: Page(g,b);Arrow(g,c,29,19,13,19);break;
            case 55: g.DrawArc(b,7,10,20,17,200,250);Lines(g,c,5,7,5,17,14,17);break;
            case 56: g.DrawArc(b,5,10,20,17,90,250);Lines(g,c,27,7,27,17,18,17);break;
            case 57: g.DrawEllipse(b,4,21,8,8);g.DrawEllipse(b,20,21,8,8);g.DrawLine(c,8,23,26,4);g.DrawLine(c,24,23,6,4);break;
            case 58: Box(g,b,7,6,19,23);Box(g,c,11,3,11,6);g.DrawLine(c,12,16,22,16);g.DrawLine(c,12,22,22,22);break;
            case 59: Poly(g,b,3,11,16,4,29,11,16,18);Lines(g,c,3,17,16,24,29,17);Lines(g,a,3,23,16,30,29,23);break;
            case 60: Cube(g,b);g.DrawEllipse(c,12,10,8,8);break;
            case 61: Page(g,b);Box(g,c,3,17,11,11);g.DrawLine(a,12,20,21,12);break;
            case 62: for(int y=7;y<29;y+=8)g.DrawLine(b,4,y,28,y);Box(g,c,10,4,5,6);Box(g,c,21,12,5,6);Box(g,c,7,20,5,6);break;
            case 63: Lines(g,b,22,4,13,16);Poly(g,c,12,14,22,22,16,29,4,21);g.DrawLine(a,8,22,14,27);g.DrawLine(a,12,19,18,24);break;
            case 64: g.DrawEllipse(b,4,4,17,17);g.DrawLine(b,19,19,28,28);g.DrawLine(c,8,12,17,12);g.DrawLine(c,12,8,12,17);break;
            case 65: g.DrawEllipse(b,4,4,17,17);g.DrawLine(b,19,19,28,28);g.DrawLine(c,8,12,17,12);break;
            case 66: Lines(g,b,7,18,7,12,10,10,13,14,13,5,16,4,18,6,18,13,22,9,25,11,24,15,28,14,29,18,24,27,13,27,6,20,7,18);break;
            case 67: Arrow(g,b,12,12,4,4);Arrow(g,b,20,12,28,4);Arrow(g,b,12,20,4,28);Arrow(g,b,20,20,28,28);Box(g,c,12,12,8,8);break;
            case 68: Box(g,b,4,6,24,21);Arrow(g,c,23,16,9,16);break;
            case 69: g.DrawEllipse(b,9,5,14,22);g.DrawEllipse(c,3,11,26,10);Lines(g,a,24,8,28,13,23,16);break;
            case 70: Box(g,b,5,5,22,22);g.DrawLine(c,5,12,27,12);Arrow(g,a,16,23,16,15);break;
            case 71: Box(g,b,5,5,22,22);g.DrawLine(c,5,22,27,22);Arrow(g,a,16,8,16,19);break;
            case 72: Box(g,b,5,5,22,22);g.DrawLine(c,21,5,21,27);Arrow(g,a,8,16,18,16);break;
            case 73: Cube(g,b);Arrow(g,c,16,16,16,5);break;
            case 74: Cube(g,b);Lines(g,c,5,23,16,17,27,23);g.DrawLine(c,16,4,16,17);break;
            case 75: Cube(g,b);using(var fill=new SolidBrush(Color.FromArgb(110,c.Color)))g.FillPolygon(fill,Points(16,16,27,10,27,23,16,29));break;
            case 76: Poly(g,b,3,10,9,10,12,6,21,6,24,10,29,10,29,27,3,27);g.DrawEllipse(c,10,12,13,12);break;
            case 77: Box(g,b,3,5,26,23);Box(g,c,7,9,12,11);Box(g,a,17,18,9,7);break;
            case 78: Arrow(g,b,8,24,8,4);Arrow(g,c,8,24,28,24);Arrow(g,a,8,24,23,9);break;
            case 79: Box(g,b,3,5,26,19);g.DrawLine(c,16,24,16,29);g.DrawLine(c,10,29,22,29);break;
            case 80: Box(g,b,4,7,24,20);g.DrawLine(c,4,14,28,14);g.DrawLine(c,4,21,28,21);g.DrawLine(c,12,7,12,14);g.DrawLine(c,21,14,21,21);g.DrawLine(c,12,21,12,27);break;
            case 81: Lines(g,b,6,28,6,4,25,4,25,28);Poly(g,c,8,6,20,10,20,28,8,26);g.DrawEllipse(a,16,17,2,2);break;
            case 82: Box(g,b,4,5,24,23);g.DrawLine(c,16,5,16,28);g.DrawLine(c,4,16,28,16);break;
            case 83: Lines(g,b,3,28,3,23,9,23,9,17,15,17,15,11,21,11,21,5,28,5);Arrow(g,c,6,16,18,4);break;
            case 84: Poly(g,b,3,20,16,5,29,20);g.DrawLine(c,8,20,8,26);g.DrawLine(c,24,20,24,26);g.DrawLine(c,8,26,24,26);break;
            case 85: Lines(g,b,3,15,16,4,29,15);Lines(g,c,7,14,7,28,25,28,25,14);Box(g,a,13,19,6,9);break;
            case 86: Box(g,b,10,8,12,18);Box(g,c,6,4,20,4);Box(g,c,6,26,20,3);g.DrawLine(b,14,10,14,24);g.DrawLine(b,18,10,18,24);break;
            case 87: Box(g,b,4,6,24,5);Box(g,b,4,23,24,5);Box(g,c,13,11,6,12);break;
            case 88: Box(g,b,12,4,8,14);Box(g,c,8,18,16,5);Box(g,b,3,23,26,6);break;
            case 89: Lines(g,b,4,29,11,3);Lines(g,b,28,29,21,3);g.DrawLine(c,16,5,16,10);g.DrawLine(c,16,14,16,19);g.DrawLine(c,16,23,16,28);break;
            case 90: Lines(g,b,3,25,3,9,29,9,29,25);g.DrawArc(c,6,15,20,22,180,180);g.DrawLine(b,3,14,29,14);g.DrawLine(a,9,5,9,9);g.DrawLine(a,23,5,23,9);break;
            case 91: Lines(g,b,4,26,4,12,12,4,28,4);Lines(g,c,11,26,11,16,16,11,28,11);g.DrawLine(a,3,23,12,23);g.DrawLine(a,24,3,24,12);break;
            case 92: Poly(g,b,4,12,16,20,28,12,28,27,16,20,4,27);g.DrawLine(c,16,20,16,6);g.DrawLine(c,10,6,22,6);break;
            case 93: Lines(g,b,4,25,4,15,20,15,20,22,28,22,28,10,12,10);g.DrawLine(c,12,5,22,5);g.DrawLine(c,17,5,17,10);g.DrawArc(a,22,24,6,6,0,180);break;
            case 94: Box(g,b,5,5,22,22);g.DrawEllipse(c,8,8,16,16);g.DrawLine(a,12,13,12,17);g.DrawLine(a,20,13,20,17);g.DrawLine(c,15,21,17,21);break;
            case 95: g.DrawArc(b,7,4,18,19,150,240);Lines(g,c,9,19,12,24,20,24,23,19);g.DrawLine(a,12,28,20,28);g.DrawLine(c,16,14,16,23);break;
            case 96: Lines(g,b,23,4,18,9,23,14,28,9);g.DrawArc(b,16,3,13,13,0,90);Poly(g,c,18,13,5,25,8,28,21,16);g.DrawEllipse(a,6,24,2,2);break;
            case 97: Poly(g,b,4,8,12,3,24,6,28,12,20,14,16,10,8,14);Poly(g,c,13,12,18,15,10,29,5,26);break;
            case 98: Poly(g,b,21,3,27,9,16,20,10,14);Lines(g,c,12,18,4,26,6,28,14,20);g.DrawLine(a,19,7,23,11);break;
            case 99: Poly(g,b,4,7,21,7,24,16,12,16,12,28,6,28,6,16,4,16);g.DrawLine(c,24,10,29,10);g.DrawLine(c,24,14,29,14);g.DrawLine(a,11,11,16,11);break;
            case 100: for(int i=0;i<8;i++){double t=i*Math.PI/4;g.DrawLine(c,16+10*(float)Math.Cos(t),16+10*(float)Math.Sin(t),16+13*(float)Math.Cos(t),16+13*(float)Math.Sin(t));}g.DrawEllipse(b,6,6,20,20);g.DrawEllipse(c,12,12,8,8);break;
            case 101: Poly(g,b,9,3,23,3,26,8,23,13,9,13,6,8);Box(g,c,12,13,8,15);for(int y=16;y<29;y+=4)g.DrawLine(a,11,y,21,y-2);break;
            case 102: Poly(g,b,10,4,23,4,29,16,23,28,10,28,3,16);g.DrawEllipse(c,10,10,12,12);break;
            case 103: Poly(g,b,4,23,23,4,29,10,10,29);for(int i=0;i<4;i++)g.DrawLine(c,10+i*4,19-i*4,13+i*4,22-i*4);break;
            case 104: g.DrawEllipse(b,13,3,6,6);g.DrawLine(b,16,9,5,28);g.DrawLine(c,16,9,27,28);g.DrawArc(a,5,12,22,13,10,160);break;
            case 105: Poly(g,b,5,27,8,19,24,3,29,8,13,24);g.DrawLine(c,8,19,13,24);g.DrawLine(a,21,6,26,11);break;
            case 106: Poly(g,b,14,18,23,3,28,6,19,22);g.DrawBezier(c,14,18,5,17,13,27,3,28);g.DrawBezier(c,3,28,13,30,19,28,19,22);break;
            case 107: Poly(g,b,4,19,18,4,29,15,16,28,12,28);g.DrawLine(c,12,11,23,22);g.DrawLine(a,16,28,29,28);break;
            case 108: Lines(g,b,5,5,5,19);g.DrawArc(b,5,11,22,18,0,180);g.DrawLine(b,27,19,27,5);g.DrawLine(c,5,11,11,11);g.DrawLine(c,21,11,27,11);Lines(g,c,11,5,11,19);g.DrawArc(c,11,15,10,8,0,180);g.DrawLine(c,21,19,21,5);break;
            case 109: Box(g,b,3,10,26,13);g.DrawEllipse(c,11,12,10,9);g.DrawLine(a,16,14,16,19);g.DrawLine(b,6,14,6,19);g.DrawLine(b,26,14,26,19);break;
            case 110: Box(g,b,3,11,26,17);Lines(g,c,11,11,11,5,21,5,21,11);g.DrawLine(b,3,18,29,18);Box(g,a,13,16,6,5);break;
            case 111: g.DrawArc(b,5,6,22,29,180,180);Lines(g,c,3,23,3,27,29,27,29,23,3,23);g.DrawLine(a,13,6,13,19);g.DrawLine(a,19,6,19,19);break;
            case 112: Poly(g,b,3,8,13,8,16,12,29,12,29,27,3,27);g.DrawLine(c,5,16,27,16);break;
            case 113: g.DrawArc(b,3,13,12,13,90,190);g.DrawArc(b,9,6,16,18,170,210);g.DrawArc(b,20,14,10,12,250,200);g.DrawLine(c,9,26,25,26);break;
            case 114: Arrow(g,c,16,3,16,22);Lines(g,b,4,21,4,28,28,28,28,21);break;
            case 115: Arrow(g,c,16,23,16,4);Lines(g,b,4,21,4,28,28,28,28,21);break;
            case 116: g.DrawArc(b,3,13,18,14,30,290);g.DrawArc(c,12,4,18,14,200,290);g.DrawLine(a,11,21,23,10);break;
            case 117: g.DrawEllipse(b,3,3,26,26);g.DrawEllipse(c,10,3,12,26);g.DrawLine(c,3,16,29,16);g.DrawArc(c,3,5,26,10,0,180);g.DrawArc(c,3,17,26,10,180,180);break;
            case 118: g.DrawArc(b,6,3,20,20,180,180);Lines(g,b,6,13,16,29,26,13);g.DrawEllipse(c,12,9,8,8);break;
            case 119: g.DrawEllipse(b,3,3,26,26);Poly(g,c,22,8,18,19,9,24,13,13);g.DrawLine(a,13,13,18,19);break;
            case 120: g.DrawEllipse(b,3,3,26,26);Lines(g,c,16,7,16,16,23,20);break;
            case 121: Box(g,b,4,7,24,22);g.DrawLine(c,4,14,28,14);g.DrawLine(c,10,3,10,10);g.DrawLine(c,22,3,22,10);Box(g,a,10,19,4,4);g.DrawLine(b,19,21,23,21);break;
            case 122: g.DrawEllipse(b,10,3,12,12);g.DrawArc(c,5,17,22,22,180,180);g.DrawLine(c,5,28,27,28);break;
            case 123: g.DrawEllipse(b,11,3,10,10);g.DrawEllipse(c,2,8,7,7);g.DrawEllipse(c,23,8,7,7);g.DrawArc(b,8,16,16,20,180,180);g.DrawArc(c,1,18,8,16,180,130);g.DrawArc(c,23,18,8,16,230,130);break;
            case 124: Box(g,b,3,7,26,20);Lines(g,c,3,8,16,19,29,8);g.DrawLine(b,3,27,11,19);g.DrawLine(b,29,27,21,19);break;
            case 125: Poly(g,b,5,4,11,4,14,11,10,14,18,22,21,18,28,21,28,27,23,29,14,25,7,18,3,9);break;
            case 126: g.DrawEllipse(c,3,22,8,6);g.DrawEllipse(c,21,18,8,6);Lines(g,b,11,24,11,7,29,3,29,20);g.DrawLine(b,11,12,29,8);break;
            case 127: g.DrawBezier(b,16,28,-8,12,8,-1,16,10);g.DrawBezier(c,16,10,24,-1,40,12,16,28);break;
            case 128:
                Poly(g,b,3,9,10,5,17,9,10,13);Lines(g,b,10,13,10,19,17,15,17,9);
                Arrow(g,a,19,7,27,13);Box(g,c,14,21,15,7);g.DrawLine(b,21,21,21,28);break;
            default: ShapeExtended(g, b, c, a, s); break;
        }
    }
}
