using System;
using System.Linq;
using System.Web.Script.Serialization;

namespace XY.SolidWorksPlugin;

// Concrete arrays keep the legacy JavaScriptSerializer compatible with the
// read-only collection interfaces used by the recognition engine.
internal sealed class ProcessRecognitionSnapshot
{
    public bool Deep { get; set; }
    public InputRow Input { get; set; }
    public RecognizedProcess[] Processes { get; set; } = Array.Empty<RecognizedProcess>();
    internal string Encode() => new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(this);
    internal static ProcessRecognitionSnapshot Decode(string json) => new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<ProcessRecognitionSnapshot>(json);
    internal static ProcessRecognitionSnapshot Create(ProcessRecognitionInput input, System.Collections.Generic.IEnumerable<RecognizedProcess> processes, bool deep)
        => new ProcessRecognitionSnapshot { Deep = deep, Input = InputRow.From(input), Processes = processes.Select(p => new RecognizedProcess { Name = p.Name, Confidence = p.Confidence, Evidence = p.Evidence }).ToArray() };

    public sealed class InputRow
    {
        public string DocumentTitle { get; set; }
        public string Material { get; set; }
        public string SurfaceTreatment { get; set; }
        public string PartType { get; set; }
        public string ProcessType { get; set; }
        public string ExistingProcess { get; set; }
        public string Outsource { get; set; }
        public string Purchase { get; set; }
        public PropertyWriteBatch.Snapshot PurchaseMarkerSnapshot { get; set; }
        public string ProfileDescription { get; set; }
        public string ItemDescription { get; set; }
        public string StandardCode { get; set; }
        public bool IsPart { get; set; }
        public bool IsAssembly { get; set; }
        public bool FeatureReadComplete { get; set; } = true;
        public ProcessFeatureFact[] Features { get; set; }
        public ProcessHoleFact[] HoleFacts { get; set; }
        public GeometryRow Geometry { get; set; }
        internal static InputRow From(ProcessRecognitionInput input) => new InputRow {
            DocumentTitle = input.DocumentTitle, Material = input.Material, SurfaceTreatment = input.SurfaceTreatment, PartType = input.PartType,
            ProcessType = input.ProcessType, ExistingProcess = input.ExistingProcess, Outsource = input.Outsource, Purchase = input.Purchase,
            PurchaseMarkerSnapshot = input.PurchaseMarkerSnapshot, ProfileDescription = input.ProfileDescription, ItemDescription = input.ItemDescription,
            StandardCode = input.StandardCode, IsPart = input.IsPart, IsAssembly = input.IsAssembly, FeatureReadComplete = input.FeatureReadComplete,
            Features = (input.Features ?? Array.Empty<ProcessFeatureFact>()).ToArray(), HoleFacts = (input.HoleFacts ?? Array.Empty<ProcessHoleFact>()).ToArray(), Geometry = GeometryRow.From(input.Geometry) };
        internal ProcessRecognitionInput Restore() => new ProcessRecognitionInput {
            DocumentTitle = DocumentTitle ?? "", Material = Material ?? "", SurfaceTreatment = SurfaceTreatment ?? "", PartType = PartType ?? "",
            ProcessType = ProcessType ?? "", ExistingProcess = ExistingProcess ?? "", Outsource = Outsource ?? "", Purchase = Purchase ?? "",
            PurchaseMarkerSnapshot = PurchaseMarkerSnapshot, ProfileDescription = ProfileDescription ?? "", ItemDescription = ItemDescription ?? "",
            StandardCode = StandardCode ?? "", IsPart = IsPart, IsAssembly = IsAssembly, FeatureReadComplete = FeatureReadComplete,
            Features = Features ?? Array.Empty<ProcessFeatureFact>(), HoleFacts = HoleFacts ?? Array.Empty<ProcessHoleFact>(), Geometry = Geometry?.Restore() ?? new ProcessGeometryFact { GeometryComplete = false } };
    }

    public sealed class GeometryRow
    {
        public string BodyName { get; set; }
        public string ProfileKind { get; set; }
        public bool GeometryComplete { get; set; }
        public bool IsNativeSheetMetal { get; set; }
        public int SolidBodyCount { get; set; }
        public int SheetBodyCount { get; set; }
        public int StraightTubeBodyCount { get; set; }
        public int FaceCount { get; set; }
        public int PlanarFaceCount { get; set; }
        public int CylindricalFaceCount { get; set; }
        public int BendCount { get; set; }
        public int CutoutCount { get; set; }
        public bool CutoutCountReliable { get; set; }
        public string CutoutCountEvidence { get; set; }
        public int PressFitGeometryCount { get; set; }
        public int PressPinGeometryCount { get; set; }
        public int WeldRivetGeometryCount { get; set; }
        public int CounterboreGeometryCount { get; set; }
        public string FastenerGeometryEvidence { get; set; }
        public double ThicknessMm { get; set; }
        public GeometryRow[] BodyFacts { get; set; }
        internal static GeometryRow From(ProcessGeometryFact fact) => fact == null ? null : new GeometryRow {
            BodyName = fact.BodyName, ProfileKind = fact.ProfileKind, GeometryComplete = fact.GeometryComplete, IsNativeSheetMetal = fact.IsNativeSheetMetal,
            SolidBodyCount = fact.SolidBodyCount, SheetBodyCount = fact.SheetBodyCount, StraightTubeBodyCount = fact.StraightTubeBodyCount,
            FaceCount = fact.FaceCount, PlanarFaceCount = fact.PlanarFaceCount, CylindricalFaceCount = fact.CylindricalFaceCount,
            BendCount = fact.BendCount, CutoutCount = fact.CutoutCount, CutoutCountReliable = fact.CutoutCountReliable, CutoutCountEvidence = fact.CutoutCountEvidence, PressFitGeometryCount = fact.PressFitGeometryCount,
            PressPinGeometryCount = fact.PressPinGeometryCount, WeldRivetGeometryCount = fact.WeldRivetGeometryCount,
            CounterboreGeometryCount = fact.CounterboreGeometryCount, FastenerGeometryEvidence = fact.FastenerGeometryEvidence,
            ThicknessMm = fact.ThicknessMm,
            BodyFacts = (fact.BodyFacts ?? Array.Empty<ProcessGeometryFact>()).Select(From).ToArray() };
        internal ProcessGeometryFact Restore() => new ProcessGeometryFact {
            BodyName = BodyName ?? "", ProfileKind = ProfileKind ?? "", GeometryComplete = GeometryComplete, IsNativeSheetMetal = IsNativeSheetMetal,
            SolidBodyCount = SolidBodyCount, SheetBodyCount = SheetBodyCount, StraightTubeBodyCount = StraightTubeBodyCount,
            FaceCount = FaceCount, PlanarFaceCount = PlanarFaceCount, CylindricalFaceCount = CylindricalFaceCount,
            BendCount = BendCount, CutoutCount = CutoutCount, CutoutCountReliable = CutoutCountReliable, CutoutCountEvidence = CutoutCountEvidence ?? "", PressFitGeometryCount = PressFitGeometryCount,
            PressPinGeometryCount = PressPinGeometryCount, WeldRivetGeometryCount = WeldRivetGeometryCount,
            CounterboreGeometryCount = CounterboreGeometryCount, FastenerGeometryEvidence = FastenerGeometryEvidence ?? "",
            ThicknessMm = ThicknessMm,
            BodyFacts = (BodyFacts ?? Array.Empty<GeometryRow>()).Where(b => b != null).Select(b => b.Restore()).ToArray() };
    }
}
