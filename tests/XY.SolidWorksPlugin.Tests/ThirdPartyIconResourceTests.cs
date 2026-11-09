using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public class ThirdPartyIconResourceTests
{
    private static MemoryStream Picture(Color color)
    {
        var stream=new MemoryStream();
        using(var bitmap=new Bitmap(32,32)){using(var g=Graphics.FromImage(bitmap))g.Clear(color);bitmap.Save(stream,ImageFormat.Png);}
        stream.Position=0;return stream;
    }

    [Fact]
    public void IdenticalAssetsKeepOneIconAndConflictingAssetsStayUnknown()
    {
        var icons=new Dictionary<string,Bitmap>();var ambiguous=new HashSet<string>();
        try
        {
            using(var a=Picture(Color.Green))ThirdPartyIconResources.AddImage(icons,ambiguous,"Feature",a);
            var first=icons["Feature"];
            using(var duplicate=Picture(Color.Green))ThirdPartyIconResources.AddImage(icons,ambiguous,"Feature",duplicate);
            Assert.Same(first,icons["Feature"]);Assert.Empty(ambiguous);
            using(var conflict=Picture(Color.Red))ThirdPartyIconResources.AddImage(icons,ambiguous,"Feature",conflict);
            Assert.Empty(icons);Assert.Contains("Feature",ambiguous);
            using(var retry=Picture(Color.Green))ThirdPartyIconResources.AddImage(icons,ambiguous,"Feature",retry);
            Assert.Empty(icons);
        }
        finally{foreach(var image in icons.Values)image.Dispose();}
    }

    [Fact]
    public void BrokenImageDoesNotEraseVerifiedIcon()
    {
        var icons=new Dictionary<string,Bitmap>();var ambiguous=new HashSet<string>();
        try
        {
            using(var a=Picture(Color.Green))ThirdPartyIconResources.AddImage(icons,ambiguous,"Feature",a);
            using(var broken=new MemoryStream(new byte[]{1,2,3}))ThirdPartyIconResources.AddImage(icons,ambiguous,"Feature",broken);
            Assert.Equal(Color.Green.ToArgb(),icons["Feature"].GetPixel(1,1).ToArgb());Assert.Empty(ambiguous);
        }
        finally{foreach(var image in icons.Values)image.Dispose();}
    }
}
