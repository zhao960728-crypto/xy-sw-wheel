using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public class WheelIconQualityTests
{
    [Theory]
    [InlineData(72)]
    [InlineData(96)]
    [InlineData(144)]
    public void SourceCopyAndSameSizePaintKeepEdgePixelsRegardlessOfPngDpi(int dpi)
    {
        using var source = new Bitmap(128, 128);
        source.SetResolution(dpi, dpi);
        source.SetPixel(0, 0, Color.Red); source.SetPixel(127, 127, Color.Blue);
        source.SetPixel(64, 64, Color.Green);
        using var copy = WheelIconDrawing.CopySource(source);
        using var output = new Bitmap(128, 128);
        using var graphics = Graphics.FromImage(output);
        graphics.InterpolationMode = InterpolationMode.Bilinear;
        graphics.PixelOffsetMode = PixelOffsetMode.None;
        WheelIconDrawing.Draw(graphics, copy, new RectangleF(0, 0, 128, 128));
        Assert.Equal(Color.Red.ToArgb(), output.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), output.GetPixel(127, 127).ToArgb());
        Assert.Equal(Color.Green.ToArgb(), output.GetPixel(64, 64).ToArgb());
        Assert.Equal(InterpolationMode.Bilinear, graphics.InterpolationMode);
        Assert.Equal(PixelOffsetMode.None, graphics.PixelOffsetMode);
    }

    [Fact]
    public void LargeImagesKeepAspectRatioWithinBoundedCache()
    {
        using var source = new Bitmap(1024, 512);
        using var copy = WheelIconDrawing.CopySource(source);
        Assert.Equal(new Size(256, 128), copy.Size);
    }

    [Fact]
    public void NamedThirdPartyIconKeepsHighResolutionDetail()
    {
        var icons = new Dictionary<string, Bitmap>();
        try
        {
            using var source = new Bitmap(128, 128);
            source.SetPixel(127, 127, Color.Red); source.SetPixel(64, 64, Color.Blue);
            using var stream = new MemoryStream(); source.Save(stream, ImageFormat.Png); stream.Position = 0;
            ThirdPartyIconResources.AddImage(icons, new HashSet<string>(), "ExactFeature", stream);
            Assert.Equal(source.Size, icons["ExactFeature"].Size);
            Assert.Equal(Color.Red.ToArgb(), icons["ExactFeature"].GetPixel(127, 127).ToArgb());
            Assert.Equal(Color.Blue.ToArgb(), icons["ExactFeature"].GetPixel(64, 64).ToArgb());
        }
        finally { foreach (var image in icons.Values) image.Dispose(); }
    }

    [Fact]
    public void HighestNativeResourceSizeIsPreferredBeforeSmallerVariants()
    {
        var candidates = SolidWorksIconResources.PngCandidates("PNG_SKETCH_LINE_40").ToArray();
        Assert.Equal("PNG_SKETCH_LINE_256", candidates[0]);
        Assert.True(System.Array.IndexOf(candidates, "PNG_SKETCH_LINE_128") < System.Array.IndexOf(candidates, "PNG_SKETCH_LINE_40"));
    }
}
