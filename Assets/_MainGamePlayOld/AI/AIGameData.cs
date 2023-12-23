using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

// Abridged version of full gamedata used for AI evaluation
public partial class AIGameData
{
    internal string GetHash()
    {
        var hash = "";
        foreach (var node in Nodes)
            hash += node.GetHash();
        foreach (var player in Players)
            hash += player.GetHash();
        return hash;
    }

    public List<AIPlayer> Players = new List<AIPlayer>(5);
    public List<AINode> Nodes = new List<AINode>(100);
    public Dictionary<int, AINode> NodeDict = new Dictionary<int, AINode>();
    public int FakeTime;
    public int CurrentPlayerId;
    public AIPlayer CurrentPlayer;

    public TownData Town;

    public CurrentPlayerBuildingData PlayerBuildingData = new CurrentPlayerBuildingData();

    public AINode getNode(int nodeId) => NodeDict[nodeId];

    #region Pooling
    static Queue<AIGameData> _pool = new Queue<AIGameData>(Settings.AIGameDataPoolSize);
    static public void WarmUpPool()
    {
        for (int i = 0; i < Settings.PoolSizes; i++)
            _pool.Enqueue(new AIGameData());
    }
    static AIGameData Get() => _pool.Count > 0 ? _pool.Dequeue() : new AIGameData();
    static public AIGameData Get(TownData town, int currentPlayerId) => Get().Initialize(town, currentPlayerId);
    static public AIGameData Get(AIGameData sourceData) => Get().Initialize(sourceData);

    public void ReturnToPool()
    {
        foreach (var player in Players) player?.ReturnToPool();
        foreach (var node in Nodes) node.ReturnToPool();
        _pool.Enqueue(this);
    }
    static public void ResetPool() => _pool.Clear();
    #endregion

    // This should only happen during Town create/load (technically: during pool warmup)
    // This is run for every AIGameData this is created and prepopulated into the pool...
    private AIGameData()
    {
        Town = ConstantAIGameData.Town;

        // == Nodes
        Profiler.BeginSample("Nodes");
        for (int i = 0; i < Town.Nodes.Count; i++)
        {
            var newNode = AINode.Get(this, Town.Nodes[i]);
            Nodes.Add(newNode);
            NodeDict[newNode.Id] = newNode;
        }
        foreach (var node in Nodes)
            node.PopulateNearbyNodes(ConstantAIGameData.NearbyNodeIds[node.Id]);

        Profiler.EndSample();

        // == Node Connections
        Profiler.BeginSample("Connections");
        for (int i = 0; i < Town.Nodes.Count; i++)
        {
            var node = Nodes[i];
            node.ConnectedNodes.Clear();
            var townConnectedNodes = Town.Nodes[i].ConnectedNodes;
            for (int j = 0; j < townConnectedNodes.Count; j++)
                node.ConnectedNodes.Add(getNode(townConnectedNodes[j].Id));
        }
        Profiler.EndSample();

        // == Players
        Profiler.BeginSample("Players");
        foreach (var player in Town.Players.Values)
            if (player != null && !player.Race.WorkersSitIdle) // null in town.Players is the 'no player' player
                Players.Add(AIPlayer.Get(this, player));
        Profiler.EndSample();
    }

    /// <summary>
    /// Initializes the AIGameData with the given Town data; this is done only once at the start of an AI evaluation.
    /// </summary>
    private AIGameData Initialize(TownData town, int currentPlayerId)
    {
        // Debug_RemoveThis_BoardAbove = null;

        CurrentPlayerId = currentPlayerId;
        CurrentPlayer = GetPlayerById(CurrentPlayerId);

        // == Nodes
        Profiler.BeginSample("Nodes");
        for (int i = 0; i < Nodes.Count; i++)
            Nodes[i].CopyFrom(town.Nodes[i]);
        Profiler.EndSample();

        // == Players
        Profiler.BeginSample("Players");
        for (int i = 0; i < Players.Count; i++)
            if (town.Players[i] != null)
                Players[i].CopyFrom(town.Players[i], town);
        Profiler.EndSample();

        PlayerBuildingData.Initialize(this);

        UpdateNearbyEnemies();

        UpdatePlayerItems(CurrentPlayer);

        return this;
    }

    /// <summary>
    /// Initializes the AIGameData from the specificed source AIGameData.  This is called many times during an AI evaluation
    /// </summary>
    private AIGameData Initialize(AIGameData sourceData)
    {
        // Debug_RemoveThis_BoardAbove = sourceData;
        CurrentPlayerId = sourceData.CurrentPlayerId;
        CurrentPlayer = GetPlayerById(CurrentPlayerId);
        FakeTime = sourceData.FakeTime;

        // == Players
        Profiler.BeginSample("Players");
        for (int i = 0; i < Players.Count; i++)
            Players[i].CopyFrom(sourceData.Players[i]);
        Profiler.EndSample();

        // == Nodes
        Profiler.BeginSample("Nodes");
        for (int i = 0; i < Nodes.Count; i++)
            Nodes[i].CopyFrom(sourceData.Nodes[i]);
        Profiler.EndSample();

        // == Items
        Profiler.BeginSample("Items");
        numItemsPromisedForConstruction.Clear();
        foreach (var item in sourceData.numItemsPromisedForConstruction) // TODO: Change from gneeric dict to specifics; numWood, etc
            numItemsPromisedForConstruction[item.Key] = item.Value;
        Profiler.EndSample();

        Profiler.BeginSample("PlayerBuildingData");
        PlayerBuildingData.CopyFrom(sourceData.PlayerBuildingData);

        // TODO: Move following into PlayerBuildingData
        CurrentPlayer.BuildingsCanConstruct.Clear();
        CurrentPlayer.BuildingsCanConstruct.AddRange(sourceData.CurrentPlayer.BuildingsCanConstruct);
        Profiler.EndSample();

        return this;
    }

    public void UpdateNearbyEnemies()
    {
        foreach (var node in Nodes)
            node.UpdateNearbyEnemies();
    }

    public void UpdatePlayerItemsOnBuildingConstruction(AIPlayer player, BuildingDefn buildingDefn2)
    {
        foreach (var itemDefn in buildingDefn2.ItemsNeededToConstruct)
            numItemsPromisedForConstruction[itemDefn.Key.ItemType] -= itemDefn.Value;

        player.BuildingsCanConstruct.Clear();
        foreach (var buildingDefn in GameDefns.Instance.BuildingDefns.Values)
            if (buildingDefn.CanBeOwned && hasNecessaryResourcesToConstruct(player, buildingDefn))
                player.BuildingsCanConstruct.Add(buildingDefn);
    }

    public void UpdatePlayerItems(AIPlayer player)
    {
        // determine which buildings we have resources to construct
        numItemsPromisedForConstruction.Clear();
        foreach (var itemDefn in GameDefns.Instance.ItemDefns.Values)
            numItemsPromisedForConstruction[itemDefn.ItemType] = numOfItemPromisedForConstruction(player, itemDefn.ItemType);

        player.BuildingsCanConstruct.Clear();
        foreach (var buildingDefn in GameDefns.Instance.BuildingDefns.Values)
            if (buildingDefn.CanBeOwned && hasNecessaryResourcesToConstruct(player, buildingDefn))
                player.BuildingsCanConstruct.Add(buildingDefn);
    }

    Dictionary<ItemType, int> numItemsPromisedForConstruction = new Dictionary<ItemType, int>(20);

    public bool hasNecessaryResourcesToConstruct(AIPlayer player, BuildingDefn buildingDefn)
    {
        if (Settings.DoAssertsInDebugMode)
        {
            Debug.Assert(buildingDefn != null, "null buildingDefn");
            Debug.Assert(buildingDefn.ItemsNeededToConstruct != null, "null buildingDefn.ItemsNeededToConstruct for " + buildingDefn.Id);
            Debug.Assert(player != null, "null player.  Id = " + player.Id);
        }

        foreach (var item in buildingDefn.ItemsNeededToConstruct.Keys)
        {
            // Deduct items for buildings are planned on being built 
            var numOfItem = player.NumItemsOwned(item.ItemType) - numItemsPromisedForConstruction[item.ItemType];
            if (numOfItem < buildingDefn.ItemsNeededToConstruct[item])
                return false;
        }
        return true;
    }

    private int numOfItemPromisedForConstruction(AIPlayer player, ItemType itemType)
    {
        int count = 0;
        foreach (var node in Nodes)
            if (node.HasUnderConstructionBuilding(player.Id) && node.NodeState != NodeState.WaitingForConstructionMaterials && node.NodeState != NodeState.UnderConstruction)
            {
                var buildingDefnId = node.PendingConstructionByPlayer[player.Id];
                if (Settings.DoAssertsInDebugMode)
                    Debug.Assert(buildingDefnId != null, "null buildingDefnId for node " + node.Id + " and player " + player.Id);
                var buildingDefn = GameDefns.Instance.BuildingDefns[buildingDefnId];
                foreach (var mat in buildingDefn.ItemsNeededToConstruct.Keys)
                    if (mat.ItemType == itemType)
                        count += buildingDefn.ItemsNeededToConstruct[mat];
            }
        return count;
    }

    // public AIGameData Debug_RemoveThis_BoardAbove;

    public AIPlayer GetPlayerById(int playerId)
    {
        for (int i = 0; i < Players.Count; i++)
            if (Players[i].Id == playerId)
                return Players[i];
        return null;
    }

    public void ChangePlayer()
    {
        Debug.Assert(Players.Count == 2, "Can only be used in 2player game");
        CurrentPlayer = CurrentPlayerId == Players[0].Id ? Players[1] : Players[0];
        CurrentPlayerId = CurrentPlayer.Id;
    }

    internal bool isGameOver()
    {
        int player = 0;
        foreach (var node in Nodes)
        {
            if (node.OwnedById == 0) continue;
            if (player == 0) player = node.OwnedById;
            else if (player != node.OwnedById) return false;
        }
        return true;
    }

    // public List<AINode> GetNodePath(AINode startNode, AINode endNode, out float distance)
    // {
    //     if (Settings.DoAssertsInDebugMode)
    //     {
    //         Debug.Assert(startNode != null, "null startNode");
    //         Debug.Assert(endNode != null, "null endNode");
    //         Debug.Assert(startNode != endNode, "start == end " + startNode.Id);
    //     }

    //     // TODO (PERF): Cache path, only recalc if map updated since last time path was calc'ed between node1 and node2
    //     var path = new List<AINode>();
    //     var startNodeOwnerId = startNode.OwnedById;
    //     var pathDistances = Dijkstra.DijkstraAlgorithm(ConstantData.directConnections, startNode.Id, endNode.Id, (int nodetoCheckId) =>
    //     {
    //         var nodeToCheck = getNode(nodetoCheckId);
    //         if (Settings.DoAssertsInDebugMode)
    //             Debug.Assert(nodeToCheck != null, "failed to find node " + nodetoCheckId);
    //         var nodeToCheckOwnerId = nodeToCheck.OwnedById;

    //         // If the owner of the node we're checking is null, then it must be the startNode or endNode to be valid.  Workers can't walk through unowned Nodes
    //         if (nodeToCheckOwnerId == 0)
    //             return nodeToCheck == startNode || nodeToCheck == endNode;

    //         // If the owner of the node we're checking is an enemy of the start node's owner, then it must be the endNode to be valid.  Workers can't walk through enemy Nodes
    //         if (nodeToCheckOwnerId != startNodeOwnerId)// .Hates(startNodeOwner))
    //             return nodeToCheck == endNode;

    //         // else, valid
    //         return true;
    //     });

    //     distance = 0;
    //     if (pathDistances != null)
    //     {
    //         // start from 1 - don't add initial node
    //         for (var i = 0; i < pathDistances.Count; i++)
    //         {
    //             var node1 = getNode(pathDistances[i]);
    //             path.Add(node1);

    //             if (i < pathDistances.Count - 1)
    //             {
    //                 var node2 = getNode(pathDistances[i + 1]);
    //                 var nodeDist = node1.DistanceTo(node2);
    //                 if (Settings.DoAssertsInDebugMode)
    //                     Debug.Assert(nodeDist < float.MaxValue, "failed to find conn between " + node1.Id + " and " + node2.Id);
    //                 distance += nodeDist;
    //             }
    //         }
    //     }
    //     return path;
    // }
}