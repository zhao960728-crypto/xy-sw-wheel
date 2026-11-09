using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;

namespace XY.SolidWorksPlugin;

internal static class JiaLiChuangFunctionCatalog
{
	internal const string AddinGuid = "{09a1056b-a871-4c51-9d1d-f20d5902b06b}";

	private const string AddinTypeName = "Ican工具箱.SwAddin";

	public static bool TryRead(out List<KeyboardShortcutRow> rows, out string message)
	{
		if (!TryGetActiveSolidWorks(out ISldWorks solidWorks, out message))
		{
			rows = new List<KeyboardShortcutRow>();
			return false;
		}
		return TryRead(solidWorks, out rows, out message);
	}

	// SettingsWindow already owns the validated SOLIDWORKS COM proxy. Prefer it
	// when ROT access is unavailable (elevated SOLIDWORKS, another desktop, or a
	// temporarily busy ROT) so reading Ican functions does not fail needlessly.
	public static bool TryRead(ISldWorks solidWorks, out List<KeyboardShortcutRow> rows, out string message)
	{
		rows = new List<KeyboardShortcutRow>();
		if (solidWorks == null)
		{
			message = "SOLIDWORKS 未连接。";
			return false;
		}
		if (!TryReadDefinitions(solidWorks, out List<JiaLiChuangCommandDefinition> definitions, out message))
		{
			return false;
		}

		rows = definitions
			.Select((JiaLiChuangCommandDefinition definition) => new KeyboardShortcutRow
			{
				Category = "嘉立创 Ican",
				Command = definition.DisplayName,
				Hotkey = "嘉立创功能",
				ActionKind = ActionKinds.JiaLiChuang,
				Value = definition.CallbackName
			})
			.OrderBy((KeyboardShortcutRow row) => row.Command, StringComparer.CurrentCultureIgnoreCase)
			.ToList();
		message = $"已读取 {rows.Count} 个嘉立创 Ican 功能。";
		return rows.Count > 0;
	}

	public static bool TryExecute(ISldWorks solidWorks, string callbackName, out string message, out bool callbackInvoked)
	{
		message = "";
		callbackInvoked = false;
		if (solidWorks == null)
		{
			message = "SOLIDWORKS 未连接。";
			return false;
		}
		if (!TryReadDefinitions(solidWorks, out List<JiaLiChuangCommandDefinition> definitions, out message))
		{
			return false;
		}

		string requested = (callbackName ?? "").Trim();
		JiaLiChuangCommandDefinition definition = definitions.FirstOrDefault((JiaLiChuangCommandDefinition item) =>
			item.CallbackName.Equals(requested, StringComparison.Ordinal));
		if (definition == null)
		{
			message = "嘉立创 Ican 当前版本中没有找到该功能，请重新读取嘉立创功能。";
			return false;
		}

		try
		{
			object addin = solidWorks.GetAddInObject(AddinGuid);
			if (addin == null)
			{
				message = "嘉立创 Ican 工具箱未加载，请先在 SOLIDWORKS 插件列表中启用它。";
				return false;
			}
			callbackInvoked = true;
			addin.GetType().InvokeMember(
				definition.CallbackName,
				BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
				null,
				addin,
				null);
			return true;
		}
		catch (TargetInvocationException ex)
		{
			Exception inner = ex.InnerException ?? ex;
			message = $"执行嘉立创功能“{definition.DisplayName}”失败：{inner.Message}";
			return false;
		}
		catch (Exception ex)
		{
			message = $"执行嘉立创功能“{definition.DisplayName}”失败：{ex.Message}";
			return false;
		}
	}

	internal static IReadOnlyList<JiaLiChuangCommandDefinition> ReadDefinitionsForTest(Type addinType)
	{
		return ReadDefinitions(addinType);
	}

	internal static string CleanDisplayNameForTest(string callbackName)
	{
		return CleanDisplayName(callbackName);
	}

	private static bool TryReadDefinitions(ISldWorks solidWorks, out List<JiaLiChuangCommandDefinition> definitions, out string message)
	{
		definitions = new List<JiaLiChuangCommandDefinition>();
		message = "";
		try
		{
			object addin = solidWorks.GetAddInObject(AddinGuid);
			if (addin == null)
			{
				message = "嘉立创 Ican 工具箱未加载，请先在 SOLIDWORKS 插件列表中启用它。";
				return false;
			}

			Type addinType = FindLoadedAddinType();
			if (addinType == null)
			{
				message = "嘉立创 Ican 已加载，但无法读取当前版本的功能定义。";
				return false;
			}
			definitions = ReadDefinitions(addinType).ToList();
			if (definitions.Count == 0)
			{
				message = "嘉立创 Ican 当前版本没有可直接读取的中文功能。";
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			message = "读取嘉立创 Ican 功能失败：" + ex.Message;
			return false;
		}
	}

	private static Type FindLoadedAddinType()
	{
		foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			try
			{
				Type type = assembly.GetType(AddinTypeName, throwOnError: false);
				if (type != null)
				{
					return type;
				}
			}
			catch
			{
			}
		}
		return null;
	}

	private static IReadOnlyList<JiaLiChuangCommandDefinition> ReadDefinitions(Type addinType)
	{
		if (addinType == null)
		{
			return Array.Empty<JiaLiChuangCommandDefinition>();
		}

		return addinType
			.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
			.Where(IsDirectFunctionCallback)
			.Select((MethodInfo method) => new JiaLiChuangCommandDefinition(CleanDisplayName(method.Name), method.Name))
			.Where((JiaLiChuangCommandDefinition definition) => definition.DisplayName.Length > 0)
			.GroupBy((JiaLiChuangCommandDefinition definition) => definition.CallbackName, StringComparer.Ordinal)
			.Select((IGrouping<string, JiaLiChuangCommandDefinition> group) => group.First())
			.OrderBy((JiaLiChuangCommandDefinition definition) => definition.DisplayName, StringComparer.CurrentCultureIgnoreCase)
			.ToList();
	}

	private static bool IsDirectFunctionCallback(MethodInfo method)
	{
		return method != null
			&& !method.IsStatic
			&& method.ReturnType == typeof(void)
			&& method.GetParameters().Length == 0
			&& ContainsChinese(method.Name);
	}

	private static bool ContainsChinese(string value)
	{
		foreach (char character in value ?? "")
		{
			if ((character >= '\u3400' && character <= '\u9fff')
				|| (character >= '\uf900' && character <= '\ufaff'))
			{
				return true;
			}
		}
		return false;
	}

	private static string CleanDisplayName(string callbackName)
	{
		string name = (callbackName ?? "").Trim();
		if (name.EndsWith("Callback", StringComparison.Ordinal))
		{
			name = name.Substring(0, name.Length - "Callback".Length);
		}
		else if (name.EndsWith("Show", StringComparison.Ordinal))
		{
			name = name.Substring(0, name.Length - "Show".Length);
		}
		return name.Replace("回调", "入口").Trim();
	}

	private static bool TryGetActiveSolidWorks(out ISldWorks solidWorks, out string message)
	{
		solidWorks = null;
		message = "";
		try
		{
			object activeObject = Marshal.GetActiveObject("SldWorks.Application");
			solidWorks = activeObject as ISldWorks;
			if (solidWorks == null)
			{
				message = "没有连接到正在运行的 SOLIDWORKS。";
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			message = "连接 SOLIDWORKS 失败：" + ex.Message;
			return false;
		}
	}
}

internal sealed class JiaLiChuangCommandDefinition
{
	public JiaLiChuangCommandDefinition(string displayName, string callbackName)
	{
		DisplayName = displayName ?? "";
		CallbackName = callbackName ?? "";
	}

	public string DisplayName { get; }

	public string CallbackName { get; }
}
