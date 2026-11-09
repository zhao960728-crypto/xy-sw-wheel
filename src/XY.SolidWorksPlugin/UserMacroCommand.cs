namespace XY.SolidWorksPlugin;

internal sealed class UserMacroCommand
{
    public string Id { get; set; } = "";
    public int CommandUserId { get; set; }
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string ModuleName { get; set; } = "";
    public string ProcedureName { get; set; } = "";
}

internal sealed class UserMacroEntryPoint
{
    public string ModuleName { get; set; } = "";
    public string ProcedureName { get; set; } = "";
    public override string ToString() => string.IsNullOrEmpty(ModuleName) ? ProcedureName : ModuleName + "." + ProcedureName;
}
