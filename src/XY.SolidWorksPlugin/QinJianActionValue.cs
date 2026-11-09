using System;

namespace XY.SolidWorksPlugin;

internal static class QinJianActionValue
{
	private const string Separator = " > ";

	public static string Format(string tabName, string commandName)
	{
		return (tabName ?? "").Trim() + Separator + (commandName ?? "").Trim();
	}

	public static bool TryParse(string value, out string tabName, out string commandName)
	{
		tabName = "";
		commandName = "";
		string text = (value ?? "").Trim();
		if (text.StartsWith("琴剑系列 > ", StringComparison.CurrentCultureIgnoreCase))
			text = text.Substring("琴剑系列 > ".Length).Trim();
		int separator = text.IndexOf(Separator, StringComparison.Ordinal);
		int separatorLength = Separator.Length;
		if (separator < 0)
		{
			separator = text.IndexOf('>');
			separatorLength = 1;
		}
		if (separator <= 0 || separator + separatorLength >= text.Length)
		{
			return false;
		}
		tabName = text.Substring(0, separator).Trim();
		commandName = text.Substring(separator + separatorLength).Trim();
		return tabName.Length > 0 && commandName.Length > 0;
	}
}
