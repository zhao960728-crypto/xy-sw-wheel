using System;

namespace XY.SolidWorksPlugin;

internal static class WheelIconPreference
{
    private const string ExplicitCataloguePrefix = "xy-library:";
    internal static string ExplicitCatalogueData(string id) => ExplicitCataloguePrefix + (id ?? "").Trim();
    internal static bool IsExplicitCatalogue(MenuItemConfig item) =>
        !string.IsNullOrWhiteSpace(item.IconId)
        && string.Equals(item.IconData, ExplicitCatalogueData(item.IconId), StringComparison.OrdinalIgnoreCase);
}
