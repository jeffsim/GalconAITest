using System.Collections.Generic;

public static class ActionFactory
{
    public static List<Action2> GetAvailableActions(AI_TownState townState, int playerId)
    {
        var actions = new List<Action2>();

        // Generate actions based on the town state
        foreach (var node in townState.Nodes)
        {
            if (node.OwnedBy == townState.player && !node.HasBuilding)
            {
                // For example, add ConstructBuildingAction
                var buildingDefn = GameDefns.Instance.BuildingDefnsByType[BuildingType.Woodcutter];
                if (townState.ConstructionResourcesCanBeReachedFromNode(node, buildingDefn.ConstructionRequirements))
                {
                    actions.Add(new ConstructBuildingAction(node, buildingDefn));
                }
            }

            // Add other actions like Attack, MoveUnits, etc.
        }

        return actions;
    }
}
