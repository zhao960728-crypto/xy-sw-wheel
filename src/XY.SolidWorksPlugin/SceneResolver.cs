using SolidWorks.Interop.sldworks;

namespace XY.SolidWorksPlugin;

internal static class SceneResolver
{
	internal static bool IsEnabled(PluginConfig config, string key)
	{
		if (config?.RadialMenuEnabled != true || config.Menus == null
			|| !config.Menus.TryGetValue(key, out var scene) || scene == null) return false;
		return scene.Enabled ?? (key != SceneKeys.Sketch || config.SketchWheelEnabled);
	}

	internal static string Resolve(PluginConfig config, int documentType, bool isEditingSketch)
	{
		// An explicitly disabled sketch page must not fall through to the part or
		// assembly wheel. Omitted switches retain the old automatic-switch choice.
		bool sketchPage = config?.Menus != null && config.Menus.TryGetValue(SceneKeys.Sketch, out var scene)
			&& scene?.Enabled != null;
		return Resolve(documentType, sketchPage || config?.SketchWheelEnabled == true, isEditingSketch);
	}

	public static string Resolve(int documentType, bool sketchWheelEnabled, bool isEditingSketch)
	{
		// Engineering drawings can expose a sketch object while editing views or
		// annotations.  That is not the part/assembly sketch editing context the
		// radial sketch wheel is for, so the drawing scene must keep priority.
		if (sketchWheelEnabled && isEditingSketch && (documentType == 1 || documentType == 2))
		{
			return SceneKeys.Sketch;
		}

		return documentType switch
		{
			1 => SceneKeys.Part,
			2 => SceneKeys.Assembly,
			3 => SceneKeys.Drawing,
			_ => SceneKeys.Blank,
		};
	}

	public static bool IsEditingSketch(IModelDoc2 document)
	{
		if (document == null)
		{
			return false;
		}
		if (HasActiveSketch(document))
		{
			return true;
		}
		try
		{
			if (document.GetType() == 2 && document is IAssemblyDoc assembly)
			{
				IModelDoc2 editTarget = assembly.GetEditTarget() as IModelDoc2;
				return editTarget != null && HasActiveSketch(editTarget);
			}
		}
		catch
		{
		}
		return false;
	}

	private static bool HasActiveSketch(IModelDoc2 document)
	{
		try
		{
			ISketchManager sketchManager = document.SketchManager;
			if (sketchManager?.ActiveSketch != null)
			{
				return true;
			}
		}
		catch
		{
		}
		try
		{
			return document.GetActiveSketch2() != null;
		}
		catch
		{
			return false;
		}
	}
}
