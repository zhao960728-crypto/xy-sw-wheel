using System.Linq;
using XY.SolidWorksPlugin;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public class PurchasedHardwareCatalogTests
{
    [Theory]
    [InlineData("ISO 4762 M6x20")]
    [InlineData("DIN912-M8x30")]
    [InlineData("GB/T 70.1 M5x12")]
    [InlineData("CLS-M4-2")]
    [InlineData("BSO-M3-10")]
    public void FullStandardOrProductCodesSupplyIdentityWithoutNamedFile(string code)
    {
        var input = new ProcessRecognitionInput { IsPart = true, DocumentTitle = "000031", StandardCode = code };
        Assert.True(PurchasedPartPolicy.IsPurchased(input));
        Assert.Equal(new[] { "外购", "来料检验", "入库" }, ProcessRecognitionService.Recognize(input, true).Select(p => p.Name));
        input.Purchase = "否";
        Assert.False(PurchasedPartPolicy.IsPurchased(input));
    }

    [Theory]
    [InlineData("ISO 47620 M6")]
    [InlineData("Part91234")]
    [InlineData("Q235-ISO9001")]
    [InlineData("PEM")]
    [InlineData("CLS-M4")]
    public void IncompleteOrUnrelatedCodesDoNotProveSupplyIdentity(string code)
        => Assert.False(PurchasedPartPolicy.IsPurchased(new ProcessRecognitionInput { IsPart = true, StandardCode = code, DocumentTitle = "000032" }));

    [Fact]
    public void DeclaredManufacturingTypeAndNonPurchasedFlagProtectCustomHardware()
    {
        var input = new ProcessRecognitionInput { IsPart = true, DocumentTitle = "定制件", PartType = PartTypes.Machined, StandardCode = "ISO 4762 M6x20" };
        Assert.False(PurchasedPartPolicy.IsPurchased(input));
        input.PartType = ""; input.ItemDescription = "压铆螺柱";
        Assert.True(PurchasedPartPolicy.IsPurchased(input));
        input.Purchase = "否";
        Assert.False(PurchasedPartPolicy.IsPurchased(input));
    }

    [Fact]
    public void ProfileDefaultGroupCannotHideBendingAndWeldingEvidence()
    {
        var config = new ProcessRecognitionConfig { ProcessCombinations = "管材=锯切下料>检验", ProcessTypeGroups = "方通=管材" };
        var input = new ProcessRecognitionInput { IsPart = true, PartType = "方通", Features = new[] {
            new ProcessFeatureFact { TypeName = "EdgeFlange" }, new ProcessFeatureFact { TypeName = "WeldBead" } } };
        var actual = ProcessRecognitionService.Recognize(input, true, config).Select(p => p.Name).ToList();
        Assert.True(actual.IndexOf("折弯") > actual.IndexOf("锯切下料"));
        Assert.True(actual.IndexOf("焊接") > actual.IndexOf("折弯"));
        Assert.True(actual.IndexOf("检验") > actual.IndexOf("焊接"));
    }
}
