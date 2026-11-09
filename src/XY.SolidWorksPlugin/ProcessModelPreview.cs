using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace XY.SolidWorksPlugin;

// Ownership of Image transfers to the editor. This reads only the preview stored
// by SOLIDWORKS; it never activates configurations, changes views or saves models.
internal sealed class ProcessModelPreview : IDisposable
{
    internal Image Image { get; }
    internal string Description { get; }
    internal ProcessModelPreview(Image image, string description)
    { Image = image; Description = description ?? ""; }
    internal static ProcessModelPreview Read(ISldWorks host, string path, string configuration)
    {
        if (string.IsNullOrWhiteSpace(path)) return new ProcessModelPreview(null, "此零件尚未保存，暂无模型预览。");
        if (!File.Exists(path)) return new ProcessModelPreview(null, "模型文件不可访问，暂时无法显示预览。");
        if (host == null) return new ProcessModelPreview(null, "SOLIDWORKS 未连接，暂时无法显示预览。");
        object picture = null;
        try
        {
            picture = host.GetPreviewBitmap(path, configuration ?? "");
            if (picture == null) return new ProcessModelPreview(null, "此配置没有已保存的模型预览。");
            using Image source = PreviewPicture.Convert(picture);
            if (source == null) return new ProcessModelPreview(null, "无法解码此配置的模型预览。");
            return new ProcessModelPreview(new Bitmap(source), "上次保存的模型预览" + (string.IsNullOrEmpty(configuration) ? "" : " · " + configuration));
        }
        catch (Exception ex)
        {
            Logger.Trace("Read process model preview failed: " + ex.Message);
            return new ProcessModelPreview(null, "模型预览读取失败；工序仍可编辑。");
        }
        finally
        {
            if (picture != null && Marshal.IsComObject(picture))
                try { Marshal.ReleaseComObject(picture); } catch (Exception ex) { Logger.Trace("Release process preview failed: " + ex.Message); }
        }
    }
    public void Dispose() => Image?.Dispose();
    private sealed class PreviewPicture : AxHost
    {
        private PreviewPicture() : base("{00000000-0000-0000-0000-000000000000}") { }
        internal static Image Convert(object picture) => GetPictureFromIPicture(picture);
    }
}
