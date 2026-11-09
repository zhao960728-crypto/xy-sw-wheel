using System.Collections.Generic;

namespace XY.SolidWorksPlugin;

internal static class TextEffects
{
    public const string None = "none";
    public const string Shadow = "shadow";
    public const string Outline = "outline";
    public const string WordArt = "word-art";

    public static readonly IReadOnlyList<KeyValuePair<string, string>> All = new[]
    {
        new KeyValuePair<string, string>(None, "无特效（默认）"),
        new KeyValuePair<string, string>(Shadow, "阴影"),
        new KeyValuePair<string, string>(Outline, "描边"),
        new KeyValuePair<string, string>(WordArt, "艺术字")
    };

    public static string Normalize(string value)
    {
        return (value ?? "").Trim().ToLowerInvariant() switch
        {
            Shadow => Shadow,
            Outline => Outline,
            WordArt => WordArt,
            _ => None
        };
    }

    public static float OverflowPadding(string value)
    {
        return Normalize(value) switch
        {
            Shadow => 2f,
            Outline => 2f,
            WordArt => 3f,
            _ => 0f
        };
    }
}
