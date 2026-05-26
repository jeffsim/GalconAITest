using System.Collections.Generic;

/// <summary>
/// Buildings the human player (and AI build generators) may construct on empty neutrals.
/// </summary>
public static class PlayerBuildingCatalog
{
    public static List<BuildingDefn> GetPlayerBuildableDefns()
    {
        var result = new List<BuildingDefn>();
        if (GameDefns.Instance == null) return result;

        foreach (var settings in GameDefns.Instance.GameSettingsDefns.Values)
        {
            if (settings != null && settings.PlayerBuildableBuildings.Count > 0)
            {
                result.AddRange(settings.PlayerBuildableBuildings);
                return result;
            }
        }

        foreach (var bd in GameDefns.Instance.BuildingDefns.Values)
            if (bd.CanBeBuiltByPlayer)
                result.Add(bd);
        return result;
    }
}
