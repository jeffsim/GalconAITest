using System;

public struct AITownData
{
    public TownData TownData;

    public AITownData(TownData townData)
    {
        TownData = townData;
    }

    // internal AITownData ApplyAction(AIAction action)
    // {
    // }

    // internal object CalculateStateValueForPlayer(PlayerData playerData)
    // {
    // }

    internal int PlayerBuildingCount(PlayerData player, string buildingDefnId)
    {
        return 0;
    }
}