using System;
using System.Collections.Generic;
using UnityEngine;

public class TownData
{
    public static TownData Instance;
    public List<PlayerData> Players = new();
    public List<NodeData> Nodes = new();
    public List<WorkerData> Workers = new();

    public Action<int> OnAIDebuggerUpdate { get; internal set; }

    public TownData(TownDefn townDefn, WorkerDefn testWorkerDefn)
    {
        Instance = this;

        // Create players
        Players.Add(null); // no player (e.g. for unowned Node)
        Players.Add(new PlayerData() { Id = 1, Name = "Player R", Color = Color.red, ControlledByAI = true, WorkerDefn = testWorkerDefn });
        Players.Add(new PlayerData() { Id = 2, Name = "Player G", Color = Color.green, ControlledByAI = true, WorkerDefn = testWorkerDefn });
        Players.Add(new PlayerData() { Id = 3, Name = "Player B", Color = Color.blue, ControlledByAI = true, WorkerDefn = testWorkerDefn });

        // Create Nodes
        foreach (var nodeDefn in townDefn.Nodes)
            Nodes.Add(new NodeData(nodeDefn, Nodes.Count, Players[nodeDefn.OwnedByPlayerId]));

        // Create Node Connections
        foreach (var nodeConnectionDefn in townDefn.NodeConnections)
        {
            var fromNode = GetNodeById(nodeConnectionDefn.Nodes.x);
            var toNode = GetNodeById(nodeConnectionDefn.Nodes.y);
            fromNode.NodeConnections.Add(new NodeConnection() { Start = fromNode, End = toNode, TravelCost = 1, IsBidirectional = nodeConnectionDefn.IsBidirectional });
        }

        // Create Workers
        foreach (var node in Nodes)
            for (int i = 0; i < node.NumWorkers; i++)
                Workers.Add(new WorkerData(node.WorldLoc, node.OwnedBy));

        foreach (var player in Players)
            player?.InitializeStaticData(this);
    }

    private NodeData GetNodeById(int nodeId)
    {
        // TODO: Dictionary.  However: only used (currently) in TownData ctor, so not a big deal.
        foreach (var node in Nodes)
            if (node.NodeId == nodeId)
                return node;
        return null;
    }

    public void Update()
    {
        foreach (var player in Players)
            player?.Update(this);

        // or test just one player:
        // Players[2].Update(this);
    }

    internal void Debug_WorldTurn()
    {
        // Update resource gathering nodes
        foreach (var node in Nodes)
        {
            if (node.Building == null || !node.Building.Defn.CanGatherResources) continue;
            
            // TODO: assume a resource node is nearby and not depleted
            if (node.Inventory.ContainsKey(node.Building.Defn.ResourceThisNodeCanGoGather))
                node.Inventory[node.Building.Defn.ResourceThisNodeCanGoGather] += 1; // TODO: node.Building.Defn.ResourceProducedPerTurn;
        }

        // not how this will normally be done, but fine for testing purposes
        foreach (var player in Players)
        {
            if (player == null) continue;
            var moveToMake = player.AI.BestNextActionToTake;

            // Convert from ai node data to real node data
            var fromNode = moveToMake.SourceNode.RealNode;
            var toNode = moveToMake.DestNode.RealNode;
            switch (moveToMake.Type)
            {
                case AIActionType.AttackFromNode:
                    break;

                case AIActionType.ConstructBuildingInEmptyNode:
                    // First verify that the action is still valid; e.g. another player hasn't captured the target node, the source node still has workers and is owned by player, etc

                    // Can player still send enough workers from source node?
                    if (fromNode.NumWorkers < moveToMake.Count || fromNode.OwnedBy != player) continue;

                    // Is target node still capturabl?
                    if (moveToMake.DestNode.OwnedBy != null) continue;

                    // Does player still have the necessary resources to build the building?
                    // TODO: Assume so for now

                    // Construct the building, move workers, etc
                    fromNode.NumWorkers -= moveToMake.Count;
                    toNode.OwnedBy = player;
                    toNode.NumWorkers = moveToMake.Count;
                    
                    var building = new BuildingData(moveToMake.BuildingToConstruct);
                    toNode.ConstructBuilding(building);
                    break;

                case AIActionType.SendWorkersToOwnedNode:
                    break;
            }
        }
    }
}
