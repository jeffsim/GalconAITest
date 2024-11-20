using System.Collections.Generic;
using System.Linq;

public abstract class Goal
{
    public string Name { get; set; }
    public abstract float CalculateUtility(AI_TownState townState, int playerId);
    public abstract HashSet<string> GetGoalState();
}

public class ConstructBuildingGoal : Goal
{
    private BuildingDefn buildingDefn;

    public ConstructBuildingGoal(BuildingType buildingType)
    {
        Name = $"ConstructBuilding:{buildingType}";
        buildingDefn = GameDefns.Instance.BuildingDefnsByType[buildingType];//(buildingType);
    }

    public override float CalculateUtility(AI_TownState townState, int playerId)
    {
        // Determine utility based on whether the player already has the building
        bool hasBuilding = townState.Nodes.Any(n => n.OwnedBy == townState.player && n.HasBuilding && n.BuildingDefn.BuildingType == buildingDefn.BuildingType);

        return hasBuilding ? 0 : 50.0f;
    }

    public override HashSet<string> GetGoalState()
    {
        // Goal is to have the building
        return new HashSet<string> { $"HasBuilding:{buildingDefn.BuildingType}" };
    }
}

// Implement other Goal subclasses as needed
