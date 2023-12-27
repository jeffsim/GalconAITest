using System.Collections.Generic;

public class AI_NodeState
{
    private NodeData nodeData;
    public List<AI_NodeState> NeighborNodes = new();
    public int NumWorkers;

    public PlayerData OwnedBy;
    public int NodeId;
    internal bool IsResourceNode => HasBuilding && CanBeGatheredFrom;

    // public Dictionary<string, int> DistanceToClosestResourceNode = new();
    public int DistanceToClosestResourceNode_Wood;
    public int DistanceToClosestResourceNode_Stone;
    public int DistanceToClosestResourceNode_StoneWoodPlank;
    
    // Buildings
    public bool HasBuilding;
    public bool CanBeGatheredFrom;

    public bool CanGatherResources;

    public GoodType GatherableResourceType;
    public GoodType GatheredResourceType;

    public void ClearBuilding() => HasBuilding = false;

    public void SetBuilding(BuildingDefn buildingDefn)
    {
        HasBuilding = true;
        CanGatherResources = buildingDefn.CanGatherResources;
        if (CanGatherResources)
            GatherableResourceType = buildingDefn.GatherableResource.GoodType;

        CanBeGatheredFrom = buildingDefn.CanBeGatheredFrom;
        if (CanBeGatheredFrom)
            GatheredResourceType = buildingDefn.GatheredResource.GoodType;
    }

    public AI_NodeState(NodeData nodeData)
    {
        // set static fields
        this.nodeData = nodeData;
        NodeId = nodeData.NodeId;

        // find the closest node for each gatherable resourcetype
        DistanceToClosestResourceNode_Wood = findClosestResourceNode(GoodType.Wood);
        DistanceToClosestResourceNode_Stone = findClosestResourceNode(GoodType.Stone);
        DistanceToClosestResourceNode_StoneWoodPlank = findClosestResourceNode(GoodType.StoneWoodPlank);
    }

    private int findClosestResourceNode(GoodType gatherableResource)
    {
        var dist = 1;

        // For now, only look at neighboring nodes.  Need to recurse out.  PriorityQueue/super-simple A*
        foreach (var neighbor in NeighborNodes)
            if (neighbor.HasBuilding && neighbor.CanBeGatheredFrom && neighbor.GatheredResourceType == gatherableResource)
                dist = 1;
        return dist;
    }

    public void Update()
    {
        //set properties that change
        if (nodeData.Building == null)
            ClearBuilding();
        else
            SetBuilding(nodeData.Building.Defn);
        NumWorkers = nodeData.NumWorkers;
        OwnedBy = nodeData.OwnedBy;
    }
}
