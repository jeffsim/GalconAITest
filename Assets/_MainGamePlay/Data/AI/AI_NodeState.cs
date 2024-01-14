using System;
using System.Collections.Generic;

public class AI_NodeState
{
    private NodeData nodeData;
    public List<AI_NodeState> NeighborNodes = new();
    public int NumNeighbors;
    public int NumWorkers;

    public PlayerData OwnedBy;
    public int NodeId;
    internal bool IsResourceNode => HasBuilding && CanBeGatheredFrom;

    //  public int DistanceToClosestGatherableResourceNode;
    public int DistanceToGatherableResource_Wood;
    public int DistanceToGatherableResource_Stone;

    // Buildings
    public bool HasBuilding;

    public bool CanGoGatherResources;
    public GoodType ResourceThisNodeCanGoGather;

    public bool CanBeGatheredFrom;
    public GoodType ResourceGatheredFromThisNode;

    public bool CanGenerateWorkers;
    public WorkerDefn WorkerGenerated;

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
        // DistanceToClosestGatherableResourceNode = findClosestResourceNode(ResourceThisNodeCanGoGather);

        CanGenerateWorkers = buildingDefn.CanGenerateWorkers;
        if (CanGenerateWorkers)
            WorkerGenerated = buildingDefn.GeneratableWorker;
    }

    public AI_NodeState(NodeData nodeData)
    {
        // set static fields
        this.nodeData = nodeData;
        NodeId = nodeData.NodeId;
        Update();
    }

    internal void SetDistanceToResources()
    {
        //   DistanceToClosestGatherableResourceNode = findClosestResourceNode(ResourceThisNodeCanGoGather);
        DistanceToGatherableResource_Wood = findClosestResourceNode(GoodType.Wood);
        DistanceToGatherableResource_Stone = findClosestResourceNode(GoodType.Stone);
    }

    private int findClosestResourceNode(GoodType gatherableResource)
    {
        // For now, only look at neighboring nodes.  Need to recurse out.  PriorityQueue/super-simple A*
        for (int i = 0; i < NumNeighbors; i++)
        {
            var neighbor = NeighborNodes[i];
            if (neighbor.HasBuilding && neighbor.CanBeGatheredFrom && neighbor.ResourceGatheredFromThisNode == gatherableResource)
                return 1;
        }
        return int.MaxValue;
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

    internal int DistanceToClosestEnemyNode(PlayerData player)
    {
        // TODO: cache; but: need to update on various actions
        for (int i = 0; i < NumNeighbors; i++)
        {
            var neighbor = NeighborNodes[i];
            // if neighbor is owned by someone other than player, then return 1
            if (neighbor.OwnedBy != null && neighbor.OwnedBy != player)
                return 1;
        }
        return int.MaxValue;
    }

    internal int DistanceToClosestGatherableResourceNode(GoodType goodType)
    {
        switch (goodType)
        {
            case GoodType.Wood: return DistanceToGatherableResource_Wood;
            case GoodType.Stone: return DistanceToGatherableResource_Stone;
            default:
                throw new NotImplementedException();
        }
    }
}

// public int DistanceToClosestEnemyNode
//     {
//         // TODO: cache; but: need to update on various actions
//         get
//         {
//             // TODO: Only looks at neighbors, not neighbors of neighbors, etc
//             for (int i = 0; i < NumNeighbors; i++)
//             {
//                 var neighbor = NeighborNodes[i];
//                 // if neighbor isn't owned, or is owned by someone other than OwnedBy, then return 1
//                 if (neighbor.OwnedBy == null || neighbor.OwnedBy != OwnedBy)
//                     return 1;
//             }
//             return int.MaxValue;
//         }
//     }
