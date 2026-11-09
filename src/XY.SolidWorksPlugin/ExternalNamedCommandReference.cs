using System;

namespace XY.SolidWorksPlugin;

internal static class ExternalNamedCommandReference
{
	private const string Prefix = "sw-external-named:";
	public static string Encode(int documentType, string category, string name) => Prefix + documentType + "/" + Uri.EscapeDataString(category ?? "") + "/" + Uri.EscapeDataString(name ?? "");
	public static bool TryDecode(string text, out int documentType, out string category, out string name)
	{
		documentType = 0; category = name = "";
		if (!(text ?? "").StartsWith(Prefix, StringComparison.Ordinal)) return false;
		string[] parts = text.Substring(Prefix.Length).Split('/');
		if (parts.Length != 3 || !int.TryParse(parts[0], out documentType) || documentType < 1 || documentType > 3) return false;
		try { category = Uri.UnescapeDataString(parts[1]); name = Uri.UnescapeDataString(parts[2]); return category.Length > 0 && name.Length > 0; } catch { return false; }
	}
	internal static string PluginName(string category) => (category ?? "").Replace("第三方插件 > ", "").Trim();
}
