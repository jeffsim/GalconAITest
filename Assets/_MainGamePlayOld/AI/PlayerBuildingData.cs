using System.Collections.Generic;

public class CurrentPlayerBuildingData
{
    public int NumBuildings;
    public Dictionary<BuildingType, int> BuildingCounts = new Dictionary<BuildingType, int>(20);

    // This is done once at the start of AI Evaluation, not in the recurion
    internal void Initialize(AIGameData gameData)
    {
        UpdateCurrentPlayerBuildingCounts(gameData);
    }

    internal void CopyFrom(CurrentPlayerBuildingData sourceData)
    {
        NumBuildings = sourceData.NumBuildings;
        foreach (var building in sourceData.BuildingCounts)
            BuildingCounts[building.Key] = building.Value;
    }

    public void UpdateCurrentPlayerBuildingCounts(AIGameData gameData)
    {
        for (int i = 0; i < ConstantAIGameData.buildingEnums.Length; i++)
            BuildingCounts[(BuildingType)i] = 0;

        NumBuildings = 0;
        foreach (var node in gameData.Nodes)
        {
            if (node.OwnedById == gameData.CurrentPlayerId && node.HasCompletedBuilding)
            {
                var buildingType = node.CompletedBuildingDefn.BuildingType;
                if (!BuildingCounts.ContainsKey(buildingType))
                    BuildingCounts[buildingType] = 0;
                BuildingCounts[buildingType]++;
                NumBuildings++;
            }
        }
    }

    internal void AddBuildingCountForPlayer(BuildingDefn completedBuildingDefn, int playerId, AIGameData gameData)
    {
        if (playerId == gameData.CurrentPlayerId)
        {
            NumBuildings++;
            BuildingCounts[completedBuildingDefn.BuildingType]++;
        }
    }

    internal void DeductBuildingCountForPlayer(BuildingDefn completedBuildingDefn, int playerId, AIGameData gameData)
    {
        if (playerId == gameData.CurrentPlayerId)
        {
            NumBuildings--;
            BuildingCounts[completedBuildingDefn.BuildingType]--;
        }
    }
}