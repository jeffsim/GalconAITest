using System;
using System.Collections.Generic;
using System.Diagnostics;

public class AI_NodeState
{
    public NodeData RealNode;
    public List<AI_NodeState> NeighborNodes = new();
    public int NumNeighbors;
    public int NumWorkers;
    public int MaxWorkers;
    public int WorkersGeneratedPerTurn = 5; // bump this up from 1 to exaggerate value of gen'ing workers
                                            // public int aiOrigNumWorkers;

    public int WorkersAdded;

    public int NumEnemiesInNeighborNodes;
    public bool IsOnTerritoryEdge;

    public PlayerData OwnedBy;
    public int NodeId;
    internal bool IsResourceNode => CanBeGatheredFrom;

    public Dictionary<GoodType, int> DistanceToGatherableResource = new();

    public bool IsVisited;

    // Buildings
    public bool HasBuilding;
    public int TurnBuildingWasBuilt;    // used to determine how long we've owned the building; e.g. building a woodcutter sooner rahter than later is better
    public BuildingDefn BuildingDefn;
    public bool CanGoGatherResources;
    public GoodType ResourceThisNodeCanGoGather;
    public int BuildingLevel;

    public bool CanBeGatheredFrom;
    public GoodType ResourceGatheredFromThisNode;

    public bool CanGenerateWorkers;
    public WorkerDefn WorkerGenerated;

    public void ClearBuilding() => HasBuilding = false;

    // public BuildingDefn BuildingInNode;
    public void SetResourceNode(BuildingDefn buildingDefn, int turnNumber)
    {
        BuildingDefn = buildingDefn;
        Debug.Assert(buildingDefn.CanBeGatheredFrom);
        CanBeGatheredFrom = buildingDefn.CanBeGatheredFrom;
        ResourceGatheredFromThisNode = buildingDefn.ResourceGatheredFromThisNode.GoodType;
    }

    public void SetBuilding(BuildingDefn buildingDefn, int turnNumber)
    {
        Debug.Assert(!buildingDefn.CanBeGatheredFrom);
        BuildingDefn = buildingDefn;
        HasBuilding = true;
        BuildingLevel = 1;

        // NOTE: If update this then need to update elsewhere too.  grep on TODO-042
        MaxWorkers = 10 * (int)Math.Pow(2, BuildingLevel - 1);

        TurnBuildingWasBuilt = turnNumber;
        CanGoGatherResources = buildingDefn.CanGatherResources;
        if (CanGoGatherResources)
            ResourceThisNodeCanGoGather = buildingDefn.ResourceThisNodeCanGoGather.GoodType;

        // This can only happen at start; e.g. this is a forest - don't need to handle every time a building is built
        // ^ that's only true if we don't allow resource nodes to be built
        // CanBeGatheredFrom = buildingDefn.CanBeGatheredFrom;
        // if (CanBeGatheredFrom)
        //     ResourceGatheredFromThisNode = buildingDefn.ResourceGatheredFromThisNode.GoodType;
        // DistanceToClosestGatherableResourceNode = findClosestResourceNode(ResourceThisNodeCanGoGather);

        CanGenerateWorkers = buildingDefn.CanGenerateWorkers;
        if (CanGenerateWorkers)
            WorkerGenerated = buildingDefn.GeneratableWorker;
    }

    public AI_NodeState(NodeData nodeData)
    {
        // set static fields
        RealNode = nodeData;
        NodeId = nodeData.NodeId;
        Update();
    }

    internal void SetDistanceToResources()
    {
        //   DistanceToClosestGatherableResourceNode = findClosestResourceNode(ResourceThisNodeCanGoGather);
        DistanceToGatherableResource[GoodType.Wood] = findClosestResourceNode(GoodType.Wood);
        DistanceToGatherableResource[GoodType.Stone] = findClosestResourceNode(GoodType.Stone);
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
        // Set properties that change
        if (RealNode.Building == null)
            ClearBuilding();
        else
        {
            if (RealNode.Building.Defn.CanBeGatheredFrom)
                SetResourceNode(RealNode.Building.Defn, 0);
            else
                SetBuilding(RealNode.Building.Defn, 0);
            BuildingLevel = RealNode.Building.Level;
        }

        NumWorkers = RealNode.NumWorkers;
        MaxWorkers = RealNode.Building?.MaxWorkers ?? 0;
        WorkersGeneratedPerTurn = RealNode.Building?.WorkersGeneratedPerTurn ?? 0;
        OwnedBy = RealNode.OwnedBy;
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

    internal int DistanceToClosestGatherableResourceNode(GoodType goodType) => DistanceToGatherableResource[goodType];
}