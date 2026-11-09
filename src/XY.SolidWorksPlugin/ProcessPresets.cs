using System;
using System.Collections.Generic;
using System.Linq;

namespace XY.SolidWorksPlugin;

internal sealed class ProcessCatalogEntry
{
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string Logic { get; set; } = "";
}

internal static class ProcessPresets
{
    public const string DefaultCombinations = "总焊=组焊>打磨>检验|总装=装配>检验|平板加工=激光下料>去毛刺>检验>包装|钣金折弯=激光下料>去毛刺>折弯>检验>包装|折弯后焊接=激光下料>去毛刺>折弯>焊接>打磨>检验>包装|多实体拆分制造=拆分制造>分件下料>去毛刺>分件折弯>组焊>打磨>检验|方通下料=锯切下料>去毛刺>检验>包装|圆管下料=锯切下料>去毛刺>检验>包装|角钢下料=锯切下料>去毛刺>检验>包装|槽钢下料=锯切下料>去毛刺>检验>包装|工字钢下料=锯切下料>去毛刺>检验>包装|H型钢下料=锯切下料>去毛刺>检验>包装|C型钢下料=锯切下料>去毛刺>检验>包装|Z型钢下料=锯切下料>去毛刺>检验>包装|T型钢下料=锯切下料>去毛刺>检验>包装|铝型材加工=锯切下料>去毛刺>检验>包装|机加通用=备料>机加工>去毛刺>检验>包装|钻孔攻丝=钻孔>攻丝>去毛刺>检验|钣金压铆=激光下料>去毛刺>钻孔>压铆>折弯>检验|不锈钢焊接=下料>折弯>氩弧焊>打磨>拉丝>检验|碳钢焊接=下料>折弯>二保焊>打磨>喷粉>检验|外购入库=外购>来料检验>入库|外加工回厂=外加工>回厂检验>入库|激光沉孔攻牙=激光>去毛刺>沉孔>攻牙>检验|百叶凸包成形=数冲>冲百叶窗>打凸包>去毛刺>折弯>检验|钣金加工全流程=激光>去毛刺>沉孔>攻牙>压铆>折弯>组焊>打磨>检验|龙门铣精加工=备料>龙门铣>钻孔>攻牙>去毛刺>检验|不锈钢拉丝总装=激光>去毛刺>折弯>组焊>打磨>抛光>拉丝>装配>检验|烤漆丝印总装=激光>去毛刺>折弯>组焊>打磨>清洗>烤漆>丝印>装配>检验|装配交付=装配>试装>检验>包装>出货|管材开孔焊接=锯切下料>去毛刺>钻孔>攻牙>组焊>打磨>焊缝检验";
    public const string DefaultTypeGroups = "方通=方通下料|圆管=圆管下料|角钢=角钢下料|槽钢=槽钢下料|工字钢=工字钢下料|H型钢=H型钢下料|C型钢=C型钢下料|Z型钢=Z型钢下料|T型钢=T型钢下料|铝型材=铝型材加工|外购件=外购入库";
    public const string DefaultCategories = "焊接组件=焊接|装配组件=装配|散件焊接=焊接封角-打磨|方管组件=方管|总焊=总焊|总装=总装|平板加工=平板加工|钣金折弯=钣金折弯|折弯后焊接=折弯后焊接|多实体拆分制造=多实体拆分制造|钣金压铆=钣金压铆|外购入库=外购入库|机加通用=机加通用|沉孔攻牙板件=激光沉孔攻牙|百叶凸包成形=百叶凸包成形|钣金综合加工=钣金加工全流程|大型机加=龙门铣精加工|不锈钢表处装配=不锈钢拉丝总装|烤漆丝印装配=烤漆丝印总装|装配交付=装配交付|管材焊接=管材开孔焊接";

    private static readonly string[] BuiltInNames = {
        "备料", "下料", "激光", "锯切下料", "激光下料", "水切下料", "等离子下料", "火焰下料", "数冲", "剪板", "外发下料", "割管",
        "去毛刺", "车削", "铣削", "机加", "机加工", "钻孔", "沉孔", "倒角", "螺纹加工", "攻丝", "热熔孔", "翻孔",
        "攻牙", "龙门铣", "压铆", "压钉", "焊铆", "冲桥", "冲百叶窗", "打凸包", "冲压成形", "折弯", "压死边", "段差折弯", "种焊钉", "丝印",
        "焊接", "组焊", "焊接封角", "点焊", "氩弧焊", "二保焊", "激光焊", "拉铆", "装配", "打磨", "抛光", "表面处理", "拉丝",
        "喷粉", "喷塑", "喷漆", "烤漆", "电镀", "镀锌", "电泳", "钝化", "阳极氧化", "热处理", "固溶处理", "时效处理", "渗碳处理",
        "铸造", "清砂/去浇口", "锻造", "注塑/成型", "3D打印", "去支撑", "打磨/后处理",
        "外购", "外加工", "来料检验", "回厂检验", "入库", "检验", "包装", "出货",
        "拆分制造", "分件下料", "分件折弯", "定位组对", "拼焊", "封口", "校直", "校形", "清洗", "酸洗", "探伤", "焊缝检验", "首件检验", "试装", "切边",
        "工字钢下料", "H型钢下料", "C型钢下料", "Z型钢下料", "T型钢下料",
        "开槽", "扩孔", "铰孔", "镗孔", "磨削", "线切割", "滚压"
    };

    // Explicit UI action only: never called by configuration load/Normalize.
    public static void AddMissing(ProcessRecognitionConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        string groups = Merge(config.ProcessCombinations, DefaultCombinations, true);
        string types = Merge(config.ProcessTypeGroups, DefaultTypeGroups, false, ProcessTypeGroupPolicy.CanonicalType);
        string categories = Merge(config.ProcessCategories, DefaultCategories, false);
        string chains = Merge(config.PartTypeChains, PartTypeCatalog.DefaultChains, true);
        config.ProcessCombinations = groups;
        config.ProcessTypeGroups = types;
        config.ProcessCategories = categories;
        config.PartTypeChains = chains;
    }

    public static IReadOnlyList<ProcessCatalogEntry> Catalog(ProcessRecognitionConfig config)
    {
        var entries = BuiltInNames.Select(name => new ProcessCatalogEntry { Name = name, Source = "内置候选", Logic = BuiltInLogic(name) }).ToList();
        Action<string, string, string> add = (name, source, logic) => {
            if (string.IsNullOrWhiteSpace(name)) return;
            var found = entries.FirstOrDefault(item => string.Equals(item.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (found == null) entries.Add(new ProcessCatalogEntry { Name = name.Trim(), Source = source, Logic = logic });
            else { if (!found.Source.Contains(source)) found.Source += "；" + source; if (!found.Logic.Contains(logic)) found.Logic += "；" + logic; }
        };
        if (config == null) return entries;
        foreach (string name in (config.AvailableProcessNames ?? "").Split(new[] { '|', '\r', '\n', ';', '；', '，', ',' }, StringSplitOptions.RemoveEmptyEntries))
            add(name, "用户目录", "仅供下拉选择，不自动追加；需工序组、判断规则或人工应用。");
        foreach (string name in ProcessRecognitionConfig.ParseAdditionalProcesses(config.AdditionalProcesses)) add(name, "全局补充", "设置中的 AdditionalProcesses 会追加到识别链。");
        foreach (var pair in ProcessRecognitionConfig.ParseProcessNameMappings(config.ProcessNameMappings)) { add(pair.Key, "名称映射源", "自动工序“" + pair.Key + "”映射为“" + pair.Value + "”。"); add(pair.Value, "名称映射目标", "由自动工序“" + pair.Key + "”重命名而来。"); }
        foreach (var group in ProcessRecognitionConfig.ParseCombinations(config.ProcessCombinations))
            foreach (string name in ProcessRecognitionConfig.CombinationSteps(group.Value)) add(name, "工序组", "工序组“" + group.Key + "”：" + group.Value + "（人工应用或规则套组）。");
        foreach (var pair in ProcessRecognitionConfig.ParseCombinations(config.PartTypeChains))
            foreach (string name in ProcessRecognitionConfig.CombinationSteps(pair.Value)) add(name, "类型链", "类型“" + pair.Key + "”的缺少加工证据时参考链：" + pair.Value + "。");
        foreach (var rule in config.CustomRules ?? new List<ProcessRecognitionRule>())
            if (rule != null && rule.Action == "append") foreach (string name in ProcessRecognitionConfig.CombinationSteps(rule.ProcessNames)) add(name, "用户判断规则", "规则“" + rule.Name + "”：" + ProcessRuleEngine.Describe(rule));
        add(config.DeburrProcessName, "可配置收尾", "IncludeDeburr 启用时，下料后增加。");
        add(config.InspectionProcessName, "可配置收尾", "IncludeInspection 启用时，按表面处理前后配置增加。");
        add(config.PackagingProcessName, "可配置收尾", "IncludePackaging 启用时增加。");
        add(config.ShippingProcessName, "可配置收尾", "IncludeShipping 启用时增加。");
        add(config.DefaultSurfaceProcess, "表面处理默认", "按当前表面处理模式和材料/属性规则建议。");
        foreach (var pair in ProcessRecognitionConfig.ParseSurfaceMaterialRules(config.SurfaceMaterialRules)) add(pair.Value, "材料表处规则", "材料包含“" + pair.Key + "”时的表面处理建议。");
        return entries;
    }

    private static string BuiltInLogic(string name)
    {
        if (name == "激光") return "独立工序名称预设，对应激光下料；可用于完整工序组，也可把自动名称‘激光下料’映射为‘激光’。";
        if (name == "攻牙") return "独立工序名称预设，对应攻丝；可用于完整工序组，也可把自动名称‘攻丝’映射为‘攻牙’。";
        if (name == "螺纹加工") return "Generic Thread / CosmeticThread 证明存在螺纹，但内外螺纹及加工方法仍待确认；只有明确攻丝孔向导类型或特征名称注明攻牙/攻丝，才生成攻丝工序。";
        if (name == "打凸包") return "凸包成形的独立工序名称预设，对应冲压成形。由明确成形证据、工序组或用户判断规则应用，不与百叶/折弯合并成一道工序。";
        if (name == "龙门铣") return "大型铣削设备工序预设。机床选择取决于图纸、尺寸和设备能力；由工序组或用户判断规则指定，不凭模型外形自动认定设备。";
        if (new[] { "工字钢下料", "H型钢下料", "C型钢下料", "Z型钢下料", "T型钢下料" }.Contains(name)) return "型钢规格/截面属性命中对应类型默认组后建议锯切下料；仅有字母数字规格时仍需核对实际截面和供料方式。";
        if (name == "丝印") return "独立表面标识工序预设，常用于表面处理后、装配前；由图纸要求、工序组或用户判断规则指定。";
        if (new[] { "拆分制造", "分件下料", "分件折弯", "定位组对", "拼焊", "封口", "校直", "校形", "清洗", "酸洗", "探伤", "焊缝检验", "首件检验", "试装", "切边", "开槽", "扩孔", "铰孔", "镗孔", "磨削", "线切割", "滚压" }.Contains(name))
            return "常用制造工序预设。通过工序组、用户判断规则或人工加入；没有对应专用内置识别器，不会仅凭实体形状自动认定。";
        if (name == "折弯") return "有效折弯/法兰特征或实体折弯面证据；平板或单纯钣金容器不证明折弯。";
        if (name == "焊接" || name == "组焊") return "明确焊缝、声明焊接工序/类型，或需确认的组合制造证据；单根结构型材不能仅凭焊件容器证明焊接。";
        if (name == "压铆" || name == "压钉" || name == "焊铆") return "制造件未抑制特征名称中的对应安装证据；外购紧固件本身不生成安装工序。";
        if (name == "锯切下料") return "型材声明/规格或管类几何证据，结合实际加工特征；非所有多实体均为型材。";
        if (name == "激光下料") return "钣金特征/等厚板几何，或明确下料特征名称。";
        if (name == "钻孔" || name == "攻丝" || name == "沉孔") return "有效孔向导、原生孔/螺纹类型或明确加工特征名称。";
        if (name == "外购" || name == "来料检验" || name == "入库") return "外购标记优先，名称/类型可提供采购身份线索；人工非外购标记阻止名称推断。";
        return "目录为可用工序候选；自动生成须有对应未抑制特征、类型/属性或配置证据。仅目录中存在不代表已识别或可自动执行。";
    }

    private static string Merge(string current, string preset, bool sequence, Func<string, string> canonical = null)
    {
        var original = StrictPairs(current, sequence);
        var additions = StrictPairs(preset, sequence);
        Func<string, string> key = canonical ?? (value => value);
        foreach (var pair in additions) if (!original.Any(existing => string.Equals(key(existing.Key), key(pair.Key), StringComparison.OrdinalIgnoreCase))) original.Add(pair);
        if (original.Count > 50) throw new InvalidOperationException("补充预设后超过 50 条，未修改任何配置；请先合并或减少已有条目。");
        return string.Join("|", original.Select(pair => pair.Key + "=" + pair.Value));
    }

    private static List<KeyValuePair<string, string>> StrictPairs(string text, bool sequence)
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (string row in (text ?? "").Split(new[] { '|', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = row.Split(new[] { '=' }, 2);
            if (pair.Length != 2 || string.IsNullOrWhiteSpace(pair[0]) || string.IsNullOrWhiteSpace(pair[1]) || pair[0].Trim().Length > 64 || pair[0].IndexOfAny(new[] { ';', '；' }) >= 0)
                throw new InvalidOperationException("现有条目格式不完整，未补充或截断配置，请先修正：" + row);
            string[] values = sequence ? pair[1].Split(new[] { '>', '→' }) : new[] { pair[1] };
            if (values.Length > 30 || values.Any(value => string.IsNullOrWhiteSpace(value) || value.Trim().Length > 64 || value.IndexOfAny(new[] { '=', ';', '；' }) >= 0))
                throw new InvalidOperationException("现有条目含空步骤、超长内容或无效分隔符，未补充配置：" + pair[0]);
            if (result.Any(item => string.Equals(item.Key, pair[0].Trim(), StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("现有名称重复，未补充配置：" + pair[0]);
            result.Add(new KeyValuePair<string, string>(pair[0].Trim(), pair[1].Trim()));
        }
        return result;
    }
}
