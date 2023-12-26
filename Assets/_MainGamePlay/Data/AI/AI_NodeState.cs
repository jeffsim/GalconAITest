using System.Collections.Generic;

public class AI_BuildingState
{
    public BuildingDefn buildingDefn;

    public AI_BuildingState(BuildingData building)
    {
        buildingDefn = building?.Defn;
    }
}

public class AI_NodeState
{
    private NodeData nodeData;
    public List<AI_NodeState> NeighborNodes = new();
    public int NumWorkers;

    public AI_BuildingState Building;
    public bool HasBuilding => Building.buildingDefn != null;
    public PlayerData OwnedBy;
    public int NodeId;
    internal bool IsResourceNode => HasBuilding && Building.buildingDefn.CanBeGatheredFrom;

    public AI_NodeState(NodeData nodeData)
    {
        this.nodeData = nodeData;
        NodeId = nodeData.NodeId;
        Building = new AI_BuildingState(nodeData.Building);
        NumWorkers = nodeData.NumWorkers;
        OwnedBy = nodeData.OwnedBy;
    }
}
