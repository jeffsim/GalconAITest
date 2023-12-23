using System;
using System.Collections.Generic;
using UnityEngine;

public class TownData
{
    private TownDefn _defn;
    public TownDefn Defn
    {
        get
        {
            if (_defn == null)
                _defn = GameDefns.Instance.TownDefns[DefnId];
            return _defn;
        }
    }
    public string DefnId;

    // key = player id 
    public Dictionary<int, PlayerData> Players = new Dictionary<int, PlayerData>();

    public PlayerData GetPlayerById(int playerId) => Players[playerId];

    // Current Map
    public List<NodeData> Nodes = new List<NodeData>();

    public Dictionary<int, NodeData> NodeDict = new Dictionary<int, NodeData>();
    public List<PlayerAIData> AIs = new List<PlayerAIData>();
    int[,] graph;

    public TownData(TownDefn townDefn)
    {
        DefnId = townDefn.Id;

        for (int i = 0; i < Defn.PlayerRaces.Count; i++)
        {
            if (i == 0) // null/no-player
                Players[i] = null;
            // else if (!Defn.PlayerRaces[i].IsComputerAI) //i == Defn.HumanPlayerIndex)
            // {
            //     Players[i] = GameData.Instance.HumanPlayer;
            //     GameData.Instance.HumanPlayer.Id = i;
            //     Players[i].Town = this;
            // }
            else
            {
                var player = new PlayerData()
                {
                    Town = this,
                    Id = i,
                    RaceDefnId = Defn.PlayerRaces[i].Id,
                    // Color = colors[i]
                };
                Players[player.Id] = player;
                if (player.Race.IsComputerAI)
                    AIs.Add(new PlayerAIData(player, this));
            }
        }

        Nodes.Clear();
        foreach (var townNode in Defn.Nodes)
            AddNode(townNode);

        foreach (var nodeConn in Defn.NodeConnections)
        {
            var node1 = GetNodeById(nodeConn.Node1Id);
            var node2 = GetNodeById(nodeConn.Node2Id);

            node1.AddConnection(node2, nodeConn, true);
            if (nodeConn.IsBidirectional)
                node2.AddConnection(node1, nodeConn, false);
        }

        UpdatePaths();

        ConstantAIGameData.Initialize(this);
    }

    public void AddNode(Town_NodeDefn townNode)
    {
        var node = new NodeData(townNode, this);
        Nodes.Add(node);
        NodeDict[node.Id] = node;

        if (townNode.IsBuildingEnabled)
        {
            if (townNode.StartingItem1 != null) node.AddItem(townNode.StartingItem1.ItemType, townNode.StartingItemCount1);
            if (townNode.StartingItem2 != null) node.AddItem(townNode.StartingItem2.ItemType, townNode.StartingItemCount2);
            if (townNode.StartingItem3 != null) node.AddItem(townNode.StartingItem3.ItemType, townNode.StartingItemCount3);
            // foreach (var item in townNode.StartingItemCounts)
            // {
            //     var itemType = GameDefns.Instance.ItemDefns[item.Key].ItemType;
            //     node.AddItem(itemType, item.Value);
            // }
        }
    }

    public void Update()
    {
        // == Update computer players' strategies
        foreach (var ai in AIs)
            ai.Update();

        // == Update players' needs
        foreach (var player in Players.Values)
            if (player != null)
                player.UpdateNeeds();

        foreach (var node in Nodes)
            node.Update();
    }

    public void UpdatePaths()
    {
        graph = new int[Nodes.Count, Nodes.Count];
        foreach (var node in Nodes)
        {
            foreach (var conn in node.Connections)
            {
                NodeData node1 = getNode(conn.Node1Id);
                NodeData node2 = getNode(conn.Node2Id);
                Debug.Assert(node1 != null, "failed to find conn1 node " + conn.Node1Id);
                Debug.Assert(node2 != null, "failed to find conn2 node " + conn.Node2Id);
                Debug.Assert(node1.Id < Nodes.Count, "node1 " + node1.Id + " wrong. num Nodes = " + Nodes.Count);
                Debug.Assert(node2.Id < Nodes.Count, "node2 " + node2.Id + " wrong. num Nodes = " + Nodes.Count);
                var distance = Vector3.Distance(node1.WorldLoc, node2.WorldLoc);
                graph[conn.Node1Id, conn.Node2Id] = (int)(distance * conn.TraversalCostMultiplier * 10);
            }
        }
    }

    public List<NodeData> GetNodePath(NodeData startNode, NodeData endNode)
    {
        Debug.Assert(startNode != null, "null startNode");
        Debug.Assert(endNode != null, "null endNode");
        Debug.Assert(startNode != endNode, "start == end " + startNode.Id);

        // TODO (PERF): Cache path, only recalc if map updated since last time path was calc'ed between node1 and node2
        var path = new List<NodeData>();
        var startNodeOwner = startNode.OwnedBy;
        var endNodeOwner = endNode.OwnedBy;
        var pathDistances = Dijkstra.DijkstraAlgorithm(graph, startNode.Id, endNode.Id, (int nodetoCheckId) =>
        {
            var nodeToCheck = getNode(nodetoCheckId);
            var nodeToCheckOwner = nodeToCheck.OwnedBy;
            var isLastNodeInPath = nodetoCheckId == endNode.Id;

            // If the owner of the node we're checking is null, then it must be the startNode or endNode to be valid.  Workers can't walk through unowned Nodes
            if (nodeToCheckOwner == null)
                return nodeToCheck == startNode || isLastNodeInPath;

            // If the owner of the node we're checking is an enemy of the start node's owner, then it must be the endNode to be valid.  Workers can't walk through enemy Nodes
            if (nodeToCheckOwner.Hates(startNodeOwner))
                return isLastNodeInPath;

            // Must have a completed building to traverse through node
            if (!isLastNodeInPath && nodeToCheck.NodeState != NodeState.ConstructionCompleted)
                return false;

            // else, valid
            return true;
        });

        if (pathDistances != null)
        {
            // start from 1 - don't add initial node
            for (var i = 0; i < pathDistances.Count; i++)
                path.Add(getNode(pathDistances[i]));
        }
        return path;
    }

    public bool BuildingResourcesAreAvailable(BuildingDefn buildingDefn, PlayerData player)
    {
        Debug.Assert(buildingDefn.ItemsNeededToConstruct != null, "huh " + buildingDefn.Id);
        foreach (var neededItem in buildingDefn.ItemsNeededToConstruct)
            if (NumAvailableItems(neededItem.Key.ItemType, player) < neededItem.Value)
                return false;
        return true;
    }

    public int NumAvailableItems(ItemType itemType, PlayerData player)
    {
        // TODO: Store in AllAvailableItems Dict.
        int count = 0;
        foreach (var node in Nodes)
            if (node.OwnedBy == player)
                count += node.NumItemInNode(itemType);
        return count;
    }

    // special case for scenario where worker is gathering in a node and when done, they can't get back to assignednode (e.g. interim
    // node was captured).  In that case, we just pick the shortest path back ignoring who owns each node
    public List<NodeData> GetShortestNodePathIgnoreNodeOwners(NodeData startNode, NodeData endNode)
    {
        Debug.Assert(startNode != null, "null startNode");
        Debug.Assert(endNode != null, "null endNode");
        Debug.Assert(startNode != endNode, "start == end");

        // TODO (PERF): Cache path, only recalc if map updated since last time path was calc'ed between node1 and node2
        var path = new List<NodeData>();
        var startNodeOwner = startNode.OwnedBy;
        var endNodeOwner = endNode.OwnedBy;
        var pathDistances = Dijkstra.DijkstraAlgorithm(graph, startNode.Id, endNode.Id, (int nodetoCheckId) => true);
        if (pathDistances != null)
        {
            // start from 1 - don't add initial node
            for (var i = 0; i < pathDistances.Count; i++)
                path.Add(getNode(pathDistances[i]));
        }
        return path;
    }

    public bool PathBetweenNodesExists(NodeData startNode, NodeData endNode)
    {
        // TODO: Keep track of (a) global "last map updated time" (e.g. any player took over any node), and (b) last time that path between start and end was calculated
        //       if the time of last map update is LESS than last time we calc'ed this path, then return cached value
        //       i.e. create bool[,] CachedPathExists and float[,] LastCachedPathUpdateTime which mirror graph[,]
        var path = GetNodePath(startNode, endNode);
        return path.Count > 0;
    }

    public int DistanceBetweenNodes(NodeData startNode, NodeData endNode)
    {
        Debug.Assert(startNode != null, "null startNode");
        Debug.Assert(endNode != null, "null endNode");

        if (startNode == endNode)
            return 0;

        // TODO: Keep track of (a) global "last map updated time" (e.g. any player took over any node), and (b) last time that path between start and end was calculated
        //       if the time of last map update is LESS than last time we calc'ed this path, then return cached value
        //       i.e. create int[,] CachedDistanceBetweenNodes and float[,] LastCachedDistanceBetweenNodesUpdateTime which mirror graph[,]
        var path = GetNodePath(startNode, endNode);
        if (path.Count == 0)
            return int.MaxValue;

        int pathLength = 0;
        for (int i = 0; i < path.Count - 1; i++)
            pathLength += graph[path[i].Id, path[i + 1].Id];
        return pathLength;
    }

    private NodeData getNode(int nodeId)
    {
        foreach (var node in Nodes)
            if (node.Id == nodeId)
                return node;
        return null;
    }

    internal List<NodeData> GetPlayerNodes(PlayerData player)
    {
        // TODO: Cache
        var list = new List<NodeData>();
        foreach (var node in Nodes)
            if (node.OwnedBy == player)
                list.Add(node);
        return list;
    }

    public NodeData GetNodeById(int nodeId)
    {
        if (NodeDict.ContainsKey(nodeId))
            return NodeDict[nodeId];
        Debug.Assert(false, "Failed to find node Id " + nodeId);
        return null;
    }

    internal NodeConnectionData GetNodeConnection(NodeData currentNode, NodeData currentDestNode)
    {
        var node1Id = currentNode.Id;
        var node2Id = currentDestNode.Id;
        foreach (var node in Nodes)
        {
            foreach (var nodeConn in node.Connections)
            {
                if ((nodeConn.Node1Id == node1Id && nodeConn.Node2Id == node2Id) ||
                    (nodeConn.Node2Id == node1Id && nodeConn.Node1Id == node2Id))
                    return nodeConn;
            }
        }
        Debug.Assert(false, "Failed to find connection");
        return null;
    }

    internal AIConstraint getAIConstraint(AIConstraintType constraintType, int nodeId = -1)
    {
        foreach (var constraint in Defn.AIConstraints)
            if (constraint.ConstraintType == constraintType)
                if (nodeId == -1 || constraint.NodeId == nodeId)
                    return constraint;
        return null;
    }

    internal void SendWorkersToNode(NodeData sourceNode, List<NodeData> path, int v, bool v1)
    {
    }

    internal void TransportItemFromNodeToNode(NeedData need, NodeData closestNodeWithNeededItem, NodeData nodeWithNeed)
    {
        throw new NotImplementedException();
    }

    internal void SendWorkersToNode(NodeData townSourceNode, List<NodeData> path, int numWorkersToMove)
    {
        throw new NotImplementedException();
    }
}