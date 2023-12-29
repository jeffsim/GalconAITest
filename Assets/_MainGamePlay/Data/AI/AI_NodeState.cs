using System;
using System.Collections.Generic;

public class AI_NodeState
{
    private NodeData nodeData;
    public List<AI_NodeState> NeighborNodes = new();
    public int NumWorkers;

    public PlayerData OwnedBy;
    public int NodeId;
    internal bool IsResourceNode => HasBuilding && CanBeGatheredFrom;

    public int DistanceToClosestResourceNode_Wood;
    public int DistanceToClosestResourceNode_Stone;
    public int DistanceToClosestResourceNode_StoneWoodPlank;

    // Buildings
    public bool HasBuilding;
    public bool CanBeGatheredFrom;

    public bool CanGoGatherResources;

    public GoodType ResourceThisNodeCanGoGather;
    public GoodType ResourceGatheredFromThisNode;

    public void ClearBuilding() => HasBuilding = false;

    public void SetBuilding(BuildingDefn buildingDefn)
    {
        HasBuilding = true;
        CanGoGatherResources = buildingDefn.CanGatherResources;
        if (CanGoGatherResources)
            ResourceThisNodeCanGoGather = buildingDefn.ResourceThisNodeCanGoGather.GoodType;

        CanBeGatheredFrom = buildingDefn.CanBeGatheredFrom;
        if (CanBeGatheredFrom)
            ResourceGatheredFromThisNode = buildingDefn.ResourceGatheredFromThisNode.GoodType;
    }

    public AI_NodeState(NodeData nodeData)
    {
        // set static fields
        this.nodeData = nodeData;
        NodeId = nodeData.NodeId;
        Update();
    }

    internal void UpdateDistanceToResource()
    {
        // find the closest node for each gatherable resourcetype
        DistanceToClosestResourceNode_Wood = findClosestResourceNode(GoodType.Wood);
        DistanceToClosestResourceNode_Stone = findClosestResourceNode(GoodType.Stone);
        DistanceToClosestResourceNode_StoneWoodPlank = findClosestResourceNode(GoodType.StoneWoodPlank);
    }

    private int findClosestResourceNode(GoodType gatherableResource)
    {
        var dist = int.MaxValue;

        // For now, only look at neighboring nodes.  Need to recurse out.  PriorityQueue/super-simple A*
        foreach (var neighbor in NeighborNodes)
            if (neighbor.HasBuilding && neighbor.CanBeGatheredFrom && neighbor.ResourceGatheredFromThisNode == gatherableResource)
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
