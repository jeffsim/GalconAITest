using System.Collections.Generic;

public class ConstructBuildingAction : Action2
{
    private AI_NodeState node;
    private BuildingDefn buildingDefn;

    public ConstructBuildingAction(AI_NodeState node, BuildingDefn buildingDefn)
    {
        this.node = node;
        this.buildingDefn = buildingDefn;

        Name = $"ConstructBuilding:{buildingDefn.BuildingType} at Node:{node.NodeId}";

        Preconditions = new HashSet<string>
        {
            $"OwnsNode:{node.NodeId}"
            // Add preconditions for required items
        };

        foreach (var req in buildingDefn.ConstructionRequirements)
        {
            Preconditions.Add($"HasItem:{req.Good.GoodType}");
        }

        Effects = new HashSet<string>
        {
            $"HasBuilding:{buildingDefn.BuildingType}:{node.NodeId}"
        };

        // Set the cost based on building requirements
        Cost = 5.0f;
    }

    public override void Perform(AI_TownState townState, int playerId)
    {
        // Implement the action using townState methods
        GoodType resource1;
        int resource1Amount;
        GoodType resource2;
        int resource2Amount;
        int numSent;

        townState.SendWorkersToConstructBuildingInEmptyNode(
            node, node, buildingDefn, 0, out resource1, out resource1Amount, out resource2, out resource2Amount, 1.0f, out numSent);
    }
}