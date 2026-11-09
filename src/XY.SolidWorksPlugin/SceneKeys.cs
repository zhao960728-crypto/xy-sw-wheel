namespace XY.SolidWorksPlugin;

internal static class SceneKeys
{
	public const string Blank = "sw.blank";

	public const string Part = "sw.part";

	public const string Assembly = "sw.assembly";

	public const string Drawing = "sw.drawing";

	public const string Sketch = "sw.sketch";

	public static readonly string[] DocumentScenes = new string[4] { Blank, Part, Assembly, Drawing };

	public static readonly string[] All = new string[5] { Blank, Part, Assembly, Drawing, Sketch };

	public static string Display(string key)
	{
		return key switch
		{
			"sw.blank" => "SW 空白页", 
			"sw.part" => "SW 零件", 
			"sw.assembly" => "SW 装配体", 
			"sw.drawing" => "SW 工程图", 
			"sw.sketch" => "SW 草图页", 
			_ => key, 
		};
	}
}
