using System;
using System.Collections.Generic;
using UnityEngine;

public class AINode
{
    internal string GetHash()
    {
        var hash = "";
        hash += Level + "," + OwnedById + "," + NumWorkersInNode;
        hash += "," + (HasCompletedBuilding ? CompletedBuildingDefn.Id : "");
        foreach (var key in PendingConstructionByPlayer.Keys)
            hash += "," + key + "," + PendingConstructionByPlayer[key];
        return hash;
    }

    // ==== Base properties ====
    public int Id;
    public NodeDefn Defn;
    public Vector3 WorldLoc;
    public List<AINode> ConnectedNodes = new List<AINode>(8);
    public List<AINode> NearbyNodes = new List<AINode>(50);
    public int Level = 1;
    public NodeState NodeState;

    public int NumHopsToClosestEnemy;               // TowerNPC = enemy (so we attack), but isn't aggressive (so we don't build .e.g barracks next to it)
    public int NumHopsToClosestAggressiveEnemy;
    public int NumEnemyNodesNearby;

    // ==== Node Owner ====
    public int OwnedById;
    public AIPlayer Owner;
    internal bool IsOwnedBy(int playerId) => OwnedById == playerId;

    public bool CanWalkThrough(int playerId) => OwnedById == playerId && HasCompletedBuilding;

    // ==== Workers ====
    public int NumWorkersInNode;
    public int MaxNumWorkers => Level * 10;
    public float WorkerAttackDamage;
    public float WorkerDefensePower;

    // ==== Completed Building ====
    public BuildingDefn CompletedBuildingDefn { private set; get; }

    // public bool HasCompletedBuilding => CompletedBuildingDefn != null;    // SIGH, ... Unity/.Net op_inequality.
    public bool HasCompletedBuilding { private set; get; }

    // ==== Under construction Building ====
    public SmallIntStringDictionary PendingConstructionByPlayer = new SmallIntStringDictionary();
    public string GetPendingConstructionByPlayer(int playerId) => PendingConstructionByPlayer[playerId];

    // ==== Miscellaneous ====
    public bool IsGathererNode => HasCompletedBuilding && CompletedBuildingDefn.BuildingClass == BuildingClass.Gatherer;
    internal bool GathersResource(string resourceId) => IsGathererNode && CompletedBuildingDefn.GatherableResource.Id == resourceId;

    public AIGameData GameData;

    #region Pooling
    static Queue<AINode> _pool = new Queue<AINode>(Settings.PoolSizes);
    static public void WarmUpPool()
    {
        for (int i = 0; i < Settings.PoolSizes; i++)
            _pool.Enqueue(new AINode());
    }

    static AINode Get() => _pool.Count > 0 ? _pool.Dequeue() : new AINode();

    internal void PopulateNearbyNodes(List<int> list)
    {
        foreach (var nodeId in list)
            NearbyNodes.Add(GameData.getNode(nodeId));
    }

    static public AINode Get(AIGameData gameData, NodeData node) => Get().OnetimeInitialize(gameData, node);

    public void ReturnToPool() => _pool.Enqueue(this);
    static public void ResetPool() => _pool.Clear();
    #endregion

    AINode()
    {
    }

    // This should only happen during Town create/load
    public AINode OnetimeInitialize(AIGameData gameData, NodeData node)
    {
        GameData = gameData;
        Id = node.Id;
        Defn = node.Defn;
        Debug.Assert(Defn != null, "null defn");
        WorldLoc = node.WorldLoc;

        return this;
    }

    // One time per AI evaluation
    public AINode CopyFrom(NodeData node)
    {
        OwnedById = node.OwnedBy == null ? 0 : node.OwnedBy.Id;
        Owner = GameData.GetPlayerById(OwnedById);
        NumWorkersInNode = node.Workers.Count;
        Level = node.Level;
        NodeState = node.NodeState;
        CompletedBuildingDefn = ReferenceEquals(node.BuildingInNode, null) && !node.HasBuildingUnderConstruction ? null : node.BuildingInNode.Defn;
        HasCompletedBuilding = !ReferenceEquals(CompletedBuildingDefn, null);

        if (HasCompletedBuilding && !ReferenceEquals(CompletedBuildingDefn.WorkerDefn, null))
        {
            WorkerAttackDamage = CompletedBuildingDefn.WorkerDefn.AttackDamage;
            WorkerDefensePower = CompletedBuildingDefn.WorkerDefn.AttackDamage;
        }

        PendingConstructionByPlayer.Reset();
        foreach (var playerId in node.PendingConstructionByPlayer.Keys)
            PendingConstructionByPlayer[playerId] = node.PendingConstructionByPlayer[playerId];

        foreach (var item in node.ItemsInNode)
            Owner.AddItem(item.Key, item.Value);

        // Don't do Owner here; we'll fixup once all AIPlayers are created
        return this;
    }

    /// <summary>
    /// Initializes the AIPlayer from the specificed source AINode.  This is called many times during an AI evaluation
    /// </summary>
    public AINode CopyFrom(AINode sourceData)
    {
        OwnedById = sourceData.OwnedById;
        Owner = GameData.GetPlayerById(OwnedById);      // todo: passin from caller
        NumWorkersInNode = sourceData.NumWorkersInNode;
        Level = sourceData.Level;
        NodeState = sourceData.NodeState;
        CompletedBuildingDefn = sourceData.CompletedBuildingDefn;
        HasCompletedBuilding = sourceData.HasCompletedBuilding;

        NumEnemyNodesNearby = sourceData.NumEnemyNodesNearby;
        NumHopsToClosestEnemy = sourceData.NumHopsToClosestEnemy;
        NumHopsToClosestAggressiveEnemy = sourceData.NumHopsToClosestAggressiveEnemy; ;

        WorkerAttackDamage = sourceData.WorkerAttackDamage;
        WorkerDefensePower = sourceData.WorkerDefensePower;

        PendingConstructionByPlayer.CopyFrom(sourceData.PendingConstructionByPlayer);

        // Following don't change
        // GameData = gameData;
        // Id = sourceData.Id;
        // Defn = sourceData.Defn;
        // WorldLoc = sourceData.WorldLoc;

        return this;
    }

    internal void ConstructBuilding(BuildingDefn buildingToConstruct, int numWorkersToMove, AINode sourceNode)
    {
        SetOwner(sourceNode.Owner);

        if (Settings.DoAssertsInDebugMode)
        {
            Debug.Assert(buildingToConstruct != null, "null BuildingToConstruct");
            Debug.Assert(sourceNode != null, "null sourceNode");
            Debug.Assert(sourceNode.OwnedById != 0, "PlayerIdMakingMove is 0");
            Debug.Assert(numWorkersToMove != 0, "NumWorkersToMove is 0");
            Debug.Assert(!HasCompletedBuilding, "Node " + Id + " already has completedBuilding");
            Debug.Assert(!HasUnderConstructionBuilding(sourceNode.OwnedById), "Node " + Id + " already has under construction building " + GetPendingConstructionByPlayer(sourceNode.OwnedById) + " by " + sourceNode.OwnedById);

            // Verify has materials to construct
            foreach (var mat in buildingToConstruct.ItemsNeededToConstruct)
                Debug.Assert(Owner.ItemsOwned[mat.Key.ItemType] >= mat.Value, "Building " + buildingToConstruct.Id + " in " + Id + " by player " + sourceNode.OwnedById + "; needs " + mat.Value + " of " + mat.Key.Id + " but only has " + Owner.ItemsOwned[mat.Key.ItemType]);
        }

        sourceNode.NumWorkersInNode -= numWorkersToMove;
        NumWorkersInNode = numWorkersToMove;
        CompletedBuildingDefn = buildingToConstruct;
        HasCompletedBuilding = true;

        // Consume required resources
        foreach (var mat in buildingToConstruct.ItemsNeededToConstruct)
            Owner.ItemsOwned[mat.Key.ItemType] -= mat.Value;
    }

    internal void UpdateNearbyEnemies()
    {
        // Get # of nearby enemy nodes
        NumEnemyNodesNearby = 0;
        NumHopsToClosestEnemy = int.MaxValue;
        foreach (var node in NearbyNodes)
            if (node.Owner != null)
            {
                if (GameData.CurrentPlayerHatesPlayer(node.OwnedById))
                {
                    NumEnemyNodesNearby++;
                    var numHops = ConstantAIGameData.HopsToNode[Id, node.Id];
                    if (numHops > 0) // 0 = "no path exists within N hops"
                    {
                        NumHopsToClosestEnemy = Math.Min(NumHopsToClosestEnemy, numHops);
                        if (node.Owner.IsAggressive)
                            NumHopsToClosestAggressiveEnemy = Math.Min(NumHopsToClosestAggressiveEnemy, numHops);
                    }
                }
            }
    }

    public void SetOwner(AIPlayer owner)
    {
        Owner = owner;
        OwnedById = owner == null ? 0 : owner.Id;
    }

    public bool HasUnderConstructionBuilding(int playerId)
    {
        if (PendingConstructionByPlayer[playerId] != null)
            return true; //  player has a pending construction

        // player doesn't have a pending construction but COULD have an under construction building
        if (NodeState == NodeState.UnderConstruction && Owner.Id == playerId)
            return true;
        return false;
    }

    internal void UpgradeBuilding()
    {
        Level++;
        NumWorkersInNode /= 2;
    }

    internal bool HasCompletedBuildingOfClass(BuildingClass buildingClass)
    {
        return HasCompletedBuilding && CompletedBuildingDefn.BuildingClass == buildingClass;
    }

    internal void DestroyBuilding()
    {
        Debug.Assert(HasCompletedBuilding, "Destroying building in " + Id + " by player " + OwnedById + " but doesn't have completed building");
        CompletedBuildingDefn = null;
        HasCompletedBuilding = false;
    }

    // internal float DistanceTo(AINode node2)
    // {
    //     foreach (var conn in ConnectedNodes)
    //         if (conn.Id == node2.Id)
    //             return 20; // TODO conn.TravelCost;
    //     return float.MaxValue;
    // }
}