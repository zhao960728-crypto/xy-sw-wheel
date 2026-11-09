using System;

namespace XY.SolidWorksPlugin;

internal static class ProcessConfigurationReadPolicy
{
	internal static bool CanReadTarget(string activeConfiguration, string targetConfiguration, bool documentDirty, bool openedForBatch, out string reason)
	{
		string active = (activeConfiguration ?? "").Trim();
		string target = (targetConfiguration ?? "").Trim();
		if (target.Length == 0 || string.Equals(active, target, StringComparison.OrdinalIgnoreCase))
		{
			reason = "";
			return true;
		}
		if (documentDirty && !openedForBatch)
		{
			reason = "引用配置“" + target + "”与当前配置“" + active + "”不同，且模型有未保存更改；为避免丢失会话状态，已跳过该行。请先保存或取消更改，再重新读取。";
			return false;
		}
		reason = "";
		return true;
	}
}
