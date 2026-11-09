using System;
using System.Linq;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace XY.SolidWorksPlugin;

// Count-only, read-only preflight. Never calls GetFaces, opens components or
// switches configurations; all host calls remain on the owning STA thread.
internal sealed class ProcessRecognitionWorkload
{
    internal const int AutomaticMaximumFaces = 400;
    internal const int AutomaticMaximumBodies = 8;
    internal const int MaximumBodiesToCount = 64;
    public int BodyCount { get; private set; }
    public int FaceCount { get; private set; }
    public int ComponentCount { get; private set; }
    public int FeatureCount { get; private set; }
    public bool IsKnown { get; private set; }
    public bool IsAssembly { get; private set; }
    public bool AllowAutomaticDeep => IsKnown && !IsAssembly && BodyCount <= AutomaticMaximumBodies && FaceCount <= AutomaticMaximumFaces && FeatureCount <= 250;
    public bool RequiresConfirmation => !IsKnown || (IsAssembly ? ComponentCount >= 12 : FaceCount >= 300 || BodyCount > AutomaticMaximumBodies || FeatureCount > 250);
    public int EstimatedMinSeconds { get; private set; }
    public int EstimatedMaxSeconds { get; private set; }

    internal static ProcessRecognitionWorkload FromCounts(int bodies, int faces, int components = 0, bool known = true, bool assembly = false, int features = 0)
    {
        var result = new ProcessRecognitionWorkload { BodyCount = Math.Max(0, bodies), FaceCount = Math.Max(0, faces), ComponentCount = Math.Max(0, components), FeatureCount = Math.Max(0, features), IsKnown = known && bodies >= 0 && faces >= 0 && components >= 0 && features >= 0, IsAssembly = assembly };
        double effort = assembly ? components * 0.8 : faces * 0.008 + bodies * 0.08 + features * 0.006;
        result.EstimatedMinSeconds = (int)Math.Min(36000, Math.Max(1, Math.Ceiling(effort)));
        result.EstimatedMaxSeconds = (int)Math.Min(72000, Math.Max(result.EstimatedMinSeconds + 2, Math.Ceiling(effort * 3)));
        return result;
    }
    public static ProcessRecognitionWorkload Read(IModelDoc2 document)
    {
        if (document == null) return FromCounts(0, 0, known: false);
        try
        {
            if (document.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY && document is IAssemblyDoc assembly)
                return FromCounts(0, 0, assembly.GetComponentCount(false), assembly: true);
            if (!(document is IPartDoc part)) return FromCounts(0, 0, known: false);
            int featureCount = document.GetFeatureCount();
            object value = part.GetBodies2((int)swBodyType_e.swSolidBody, false);
            if (value == null) return FromCounts(0, 0, features: featureCount);
            if (!(value is Array bodies)) return FromCounts(0, 0, known: false);
            long faces = 0; int visited = 0;
            foreach (var raw in bodies)
            {
                if (++visited > MaximumBodiesToCount) return FromCounts(bodies.Length, (int)Math.Min(int.MaxValue, faces), known: false);
                if (!(raw is IBody2 body)) return FromCounts(bodies.Length, (int)Math.Min(int.MaxValue, faces), known: false);
                int count = body.GetFaceCount(); if (count < 0) return FromCounts(bodies.Length, (int)Math.Min(int.MaxValue, faces), known: false);
                faces += count;
            }
            return FromCounts(bodies.Length, (int)Math.Min(int.MaxValue, faces), features: featureCount);
        }
        catch (Exception ex) { Logger.Trace("Recognition workload count unavailable: " + ex.Message); return FromCounts(0, 0, known: false); }
    }
    public string DescribeEstimate()
    {
        if (!IsKnown) return "工作量未能完整读取，耗时无法可靠估计，可能较长。";
        string scale = IsAssembly ? ComponentCount + " 个组件" : BodyCount + " 个实体、约 " + FaceCount + " 个面、" + FeatureCount + " 个特征";
        return scale + "；按数量粗估约 " + Duration(EstimatedMinSeconds) + "～" + Duration(EstimatedMaxSeconds) + "（仅供参考，实际取决于模型复杂度和机器性能）。";
    }
    private static string Duration(int seconds) => seconds < 120 ? seconds + " 秒" : Math.Ceiling(seconds / 60d) + " 分钟";
    internal string DescribeOperation(string operation)
    {
        if ((operation ?? "").Contains("工艺孔")) return DescribeEstimate() + "\r\n完整孔扫描按实体分片，可取消剩余任务；单个宿主调用期间需等待返回。";
        string estimate = IsAssembly ? "每个已加载组件约 1～5 秒，合计按组件数粗估约 " + Duration(Math.Max(1, ComponentCount)) + "～" + Duration((int)Math.Min(72000L, Math.Max(5L, ComponentCount * 5L))) : "本次受限读取粗估约 1～5 秒";
        return estimate + "（仅供参考）。每次几何读取有数量和时间预算，超限返回部分结果，需后续复核；不承诺一次完成全部几何。";
    }
    public bool Confirm(IWin32Window owner, string operation, Func<ProcessRecognitionWorkload, string, bool> confirmation = null)
    {
        if (!RequiresConfirmation) return true;
        if (confirmation != null) return confirmation(this, operation);
        return MessageBox.Show(owner, operation + "工作量提示。\r\n\r\n" + DescribeOperation(operation) + "\r\n\r\n运行期间可能暂时影响 SOLIDWORKS 响应；取消会保留当前结果和人工草稿。是否继续？", "识别工作量提示", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }
    internal const string AutomaticFallbackText = "模型工作量较大或未能完整估计，后台已采用轻量识别；几何折弯/管材仍待核对，可点击深度识别确认后继续。";
}
