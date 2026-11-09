using System;
using System.Text.RegularExpressions;

namespace XY.SolidWorksPlugin;

// Product identifiers are matched as complete tokens, never arbitrary numbers
// or a supplier name. A supplier may also manufacture a custom part for us.
internal static class PurchasedHardwareCatalog
{
    private static readonly Regex FastenerStandard = new Regex(@"(?<![A-Z0-9])(?:DIN\s*(?:912|913|914|915|916|931|933|934|965|966|7991)|ISO\s*(?:4014|4017|4032|4762|7045|7046|10642)|GB\s*(?:/\s*T\s*)?(?:70\.1|70\.2|70\.3|5782|5783|6170|819(?:\.1|\.2)?|820|97\.1|93))(?=$|[\s_\-×x*/(（])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PemCode = new Regex(@"(?<![A-Z0-9])(?:CLS|CLSS|CLA|FH|FHS|FHA|SO|SOS|SOA|BSO|BSOS|BSOA|S|SS|SP)-M\d+(?:[.-]\d+)?-\d+(?![A-Z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static PartTypeMatch Match(ProcessRecognitionInput input)
    {
        if (input == null || PurchasedPartPolicy.ExplicitFlag(input.Purchase) == false) return new PartTypeMatch();
        string declared = (input.PartType ?? "").Trim();
        // A real declared manufacturing type remains authoritative.
        if (declared.Length > 0 && declared != PartTypes.Other && !PurchasedPartPolicy.IsSupplyType(PartTypeCatalog.FromText(declared, "").Type))
            return new PartTypeMatch();
        string description = (input.ItemDescription ?? "").Trim();
        var named = PartTypeCatalog.FromText("", description);
        if (PurchasedPartPolicy.IsSupplyType(named.Type))
            return new PartTypeMatch { Type = named.Type, Confidence = named.Confidence, Evidence = "物料名称/描述：" + named.Evidence };
        string identifiers = (input.StandardCode ?? "") + " " + (input.DocumentTitle ?? "") + " " + description;
        if (FastenerStandard.IsMatch(identifiers))
            return new PartTypeMatch { Type = PartTypes.Standard, Confidence = 94, Evidence = "命中已知紧固件标准号完整标识，外购身份待确认" };
        if (PemCode.IsMatch(identifiers))
            return new PartTypeMatch { Type = PartTypes.Standard, Confidence = 94, Evidence = "命中压铆件完整型号（含螺纹与尺寸码），外购身份待确认" };
        return new PartTypeMatch();
    }
}
