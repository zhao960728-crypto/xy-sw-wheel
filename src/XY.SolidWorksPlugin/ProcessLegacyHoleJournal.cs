using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace XY.SolidWorksPlugin;

// Read-only legacy model metadata for process recognition. Numeric enum values
// and validation are retained for existing documents; no modeling or editing API.
internal enum ProcessLegacyHoleKind { 底孔, 攻牙, 压铆螺母, 压铆螺柱, 压铆螺钉, 定位销, 焊铆螺母, 焊铆螺柱, 压钉 }
internal enum ProcessLegacyHoleStyle { 旧版自定义, 压铆螺母, 通孔压铆螺母柱, 盲孔压铆螺母柱, 平头压铆螺钉, 压铆导向销, 圆柱定位销, 焊铆螺母, 焊铆螺柱, 压钉 }

internal sealed class ProcessLegacyHoleSpec
{
    public string Name { get; set; } = "新规格";
    public string Model { get; set; } = "";
    public ProcessLegacyHoleStyle Style { get; set; }
    public double ShankDiameterMm { get; set; } = 4.2;
    public double AcrossFlatsMm { get; set; } = 4.8;
    public double BoreDepthMm { get; set; } = 3.2;
    public double TipDiameterMm { get; set; } = 2.11;
    public double TipAngleDeg { get; set; } = 64;
    public string Material { get; set; } = "";
    public bool MergeBody { get; set; }
    public ProcessLegacyHoleKind Kind { get; set; }
    public double DiameterMm { get; set; } = 5;
    public double MinimumThicknessMm { get; set; }
    public double MaximumThicknessMm { get; set; } = 1000;
    public double MinimumEdgeDistanceMm { get; set; }
    public string ThreadNote { get; set; } = "M6×1";
    public double ThreadDiameterMm { get; set; } = 6;
    public double ThreadDepthMm { get; set; }
    public double OutsideDiameterMm { get; set; } = 9;
    public double LengthMm { get; set; } = 6;
    public double BoreDiameterMm { get; set; } = 5;
    public double InsertionMm { get; set; } = 1;
    public double HeadDiameterMm { get; set; } = 12;
    public double HeadHeightMm { get; set; } = 2;
    internal static ProcessLegacyHoleKind KindOf(ProcessLegacyHoleStyle style) =>
        style == ProcessLegacyHoleStyle.压铆螺母 ? ProcessLegacyHoleKind.压铆螺母
        : style == ProcessLegacyHoleStyle.通孔压铆螺母柱 || style == ProcessLegacyHoleStyle.盲孔压铆螺母柱 ? ProcessLegacyHoleKind.压铆螺柱
        : style == ProcessLegacyHoleStyle.平头压铆螺钉 ? ProcessLegacyHoleKind.压铆螺钉
        : style == ProcessLegacyHoleStyle.焊铆螺母 ? ProcessLegacyHoleKind.焊铆螺母
        : style == ProcessLegacyHoleStyle.焊铆螺柱 ? ProcessLegacyHoleKind.焊铆螺柱
        : style == ProcessLegacyHoleStyle.压钉 ? ProcessLegacyHoleKind.压钉
        : ProcessLegacyHoleKind.定位销;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 100 || !Enum.IsDefined(typeof(ProcessLegacyHoleKind), Kind)) throw new InvalidOperationException("请填写有效规格名称和类别。");
        if ((Material ?? "").Length > 100 || (Model ?? "").Length > 100 || !Enum.IsDefined(typeof(ProcessLegacyHoleStyle), Style)) throw new InvalidOperationException("型号、材质或结构型式无效。");
        Positive(DiameterMm, "底孔直径"); Nonnegative(MinimumThicknessMm, "最小厚度"); Positive(MaximumThicknessMm, "最大厚度");
        if (MaximumThicknessMm < MinimumThicknessMm) throw new InvalidOperationException("最大适用厚度不能小于最小厚度。");
        if (Kind == ProcessLegacyHoleKind.攻牙)
        {
            Positive(ThreadDiameterMm, "螺纹直径"); Nonnegative(ThreadDepthMm, "螺纹深度");
            if (ThreadDiameterMm <= DiameterMm || string.IsNullOrWhiteSpace(ThreadNote) || ThreadNote.Length > 100) throw new InvalidOperationException("螺纹公称直径必须大于底孔直径，且必须填写螺纹标注。");
        }
        if (Kind > ProcessLegacyHoleKind.攻牙)
        {
            if (Style != ProcessLegacyHoleStyle.旧版自定义 && KindOf(Style) != Kind) throw new InvalidOperationException("结构型式与工艺类别不一致。");
            Positive(OutsideDiameterMm, "外径"); Positive(LengthMm, "伸出长度"); Nonnegative(InsertionMm, "插入深度");
            if (Kind == ProcessLegacyHoleKind.压铆螺母 || Kind == ProcessLegacyHoleKind.压铆螺柱)
            {
                Positive(BoreDiameterMm, "内孔直径");
                if (BoreDiameterMm >= Math.Min(DiameterMm, OutsideDiameterMm)) throw new InvalidOperationException("安装件内孔必须小于底孔及安装件外径。");
            }
            if (Kind == ProcessLegacyHoleKind.压铆螺钉)
            {
                Positive(HeadDiameterMm, "头部直径"); Positive(HeadHeightMm, "头部高度");
                if (HeadDiameterMm < OutsideDiameterMm || HeadHeightMm >= LengthMm) throw new InvalidOperationException("螺钉头部直径须不小于杆部外径，头部高度须小于伸出总长度。");
            }
            if (Style == ProcessLegacyHoleStyle.压铆螺母) { Positive(ShankDiameterMm, "压入段直径"); if (ShankDiameterMm <= BoreDiameterMm || ShankDiameterMm > DiameterMm || ShankDiameterMm >= OutsideDiameterMm || InsertionMm <= 0) throw new InvalidOperationException("螺母压入段须大于内孔、小于本体，并能插入底孔。"); }
            if (Style == ProcessLegacyHoleStyle.通孔压铆螺母柱 || Style == ProcessLegacyHoleStyle.盲孔压铆螺母柱) { Positive(AcrossFlatsMm, "头部对边"); if (AcrossFlatsMm <= OutsideDiameterMm || OutsideDiameterMm > DiameterMm) throw new InvalidOperationException("螺母柱头部对边应大于杆径，杆径不能大于安装底孔。"); }
            if (Style >= ProcessLegacyHoleStyle.通孔压铆螺母柱 && Style <= ProcessLegacyHoleStyle.压铆导向销) { Positive(HeadHeightMm, "简化铆入头厚"); if (HeadHeightMm >= LengthMm) throw new InvalidOperationException("铆入头厚须小于总长。"); }
            if (Style == ProcessLegacyHoleStyle.盲孔压铆螺母柱) { Positive(BoreDepthMm, "盲孔深度"); if (BoreDepthMm >= LengthMm - HeadHeightMm) throw new InvalidOperationException("盲孔须保留封闭端厚度。"); }
            if (Style == ProcessLegacyHoleStyle.压铆导向销 || Style == ProcessLegacyHoleStyle.圆柱定位销) { Positive(TipDiameterMm, "端部直径"); if (TipDiameterMm >= OutsideDiameterMm || TipAngleDeg <= 0 || TipAngleDeg >= 90 || double.IsNaN(TipAngleDeg)) throw new InvalidOperationException("导向端直径须小于杆径，角度须在 0～90°之间。"); }
        }
    }
    private static void Positive(double value, string name) { if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0 || value > 10000) throw new InvalidOperationException(name + "须大于 0 且不超过 10000 mm。"); }
    private static void Nonnegative(double value, string name) { if (value != 0) Positive(value, name); }
}

internal sealed class ProcessLegacyHoleRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public List<string> Walls { get; set; } = new List<string>();
    public string ResizeFeature { get; set; }
    public string RepairFeature { get; set; }
    public string FillFeature { get; set; }
    public string RemovalFeature { get; set; }
    public string ProcessFeature { get; set; }
    public string ProcessFeatureName { get; set; }
    public string MergeFeature { get; set; }
    public ProcessLegacyHoleLocation Location { get; set; }
    public double OriginalDiameterMm { get; set; }
    public double AppliedDiameterMm { get; set; }
    public ProcessLegacyHoleSpec Spec { get; set; }
    public bool Reverse { get; set; }
}

internal sealed class ProcessLegacyHoleLocation
{
    public double[] Start { get; set; }
    public double[] End { get; set; }
    public double[] Axis { get; set; }
    public string BodyReference { get; set; }
    public double LengthMm { get; set; }
    public bool ReverseAllowed { get; set; }
    public int EndCondition { get; set; }
}

internal sealed class ProcessLegacyHoleJournal
{
    public int Version { get; set; } = 1;
    public string FolderReference { get; set; }
    public List<ProcessLegacyHoleRecord> Records { get; set; } = new List<ProcessLegacyHoleRecord>();
    public static ProcessLegacyHoleJournal Parse(string json)
    {
        if (string.IsNullOrEmpty(json)) return new ProcessLegacyHoleJournal();
        var journal = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Deserialize<ProcessLegacyHoleJournal>(json);
        if (journal == null || journal.Version != 1 || journal.Records == null || journal.Records.Count > 20000 || journal.Records.Any(r => r == null || r.Walls == null || r.Walls.Count == 0 || r.Walls.Any(string.IsNullOrWhiteSpace) || r.Spec == null || !Guid.TryParseExact(r.Id, "N", out _) || !ValidDiameter(r.OriginalDiameterMm) || !ValidDiameter(r.AppliedDiameterMm)) || journal.Records.Select(r => r.Id).Distinct().Count() != journal.Records.Count)
            throw new InvalidOperationException("工艺孔记录损坏或版本不支持，未修改模型。");
        foreach (var record in journal.Records)
        {
            record.Spec.Validate();
            if (!string.IsNullOrEmpty(record.MergeFeature) && (record.Spec.Kind <= ProcessLegacyHoleKind.攻牙 || !record.Spec.MergeBody || record.Location == null)) throw new InvalidOperationException("合并工艺定位记录缺失。");
            if (record.Location != null)
            {
                var p = record.Location;
                if (!ValidVector(p.Start) || !ValidVector(p.End) || !ValidVector(p.Axis) || !ValidDiameter(p.LengthMm) || string.IsNullOrEmpty(p.BodyReference) || Math.Abs(p.Axis.Sum(x => x * x) - 1) > 1e-6 || Math.Abs(Math.Sqrt(p.Start.Zip(p.End, (a, b) => (a - b) * (a - b)).Sum()) * 1000 - p.LengthMm) > .002) throw new InvalidOperationException("工艺孔定位记录无效。");
            }
        }
        return journal;
    }
    private static bool ValidDiameter(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0 && value <= 10000;
    private static bool ValidVector(double[] values) => values != null && values.Length == 3 && values.All(v => !double.IsNaN(v) && !double.IsInfinity(v));
}
