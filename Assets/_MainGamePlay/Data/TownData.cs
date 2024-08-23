using System;
using System.Collections.Generic;
using PlasticPipe.PlasticProtocol.Messages;
using UnityEngine;

public class TownData
{
    public static TownData Instance;
    public List<PlayerData> Players = new();
    public List<NodeData> Nodes = new();
    public List<WorkerData> Workers = new();

    public Action<int> OnAIDebuggerUpdate { get; internal set; }
    public int TestOnePlayerId = 1;

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
            // if (nodeDefn.Enabled)
                Nodes.Add(new NodeData(nodeDefn, Players[nodeDefn.OwnedByPlayerId]));

        // Create Node Connections
        foreach (var nodeConnectionDefn in townDefn.NodeConnections)
        {
            var fromNode = GetNodeById(nodeConnectionDefn.Nodes.x);
            var toNode = GetNodeById(nodeConnectionDefn.Nodes.y);
            if (fromNode == null || toNode == null) continue;
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
        Debug.Log("Failed to find node with Id " + nodeId);
        return null;
    }

    public void Update()
    {
        if (TestOnePlayerId == 0)
        {
            foreach (var player in Players)
                player?.Update(this);
        }
        else
        {
            // or test just one player:
            Players[TestOnePlayerId].Update(this);
        }
    }

    internal void Debug_WorldTurn()
    {
        // Update resource gathering nodes
        foreach (var node in Nodes)
        {
            if (node.Building == null) continue;

            if (node.Building.Defn.CanGatherResources)
            {
                // TODO: assume a resource node is nearby and not depleted
                if (node.Inventory.ContainsKey(node.Building.Defn.ResourceThisNodeCanGoGather.GoodType))
                    node.Inventory[node.Building.Defn.ResourceThisNodeCanGoGather.GoodType] += 3; // TODO: node.Building.Defn.ResourceProducedPerTurn;
            }
            if (node.Building.Defn.CanGenerateWorkers)
            {
                node.NumWorkers += 1; // TODO: node.Building.Defn.WorkersGeneratedPerTurn; 
            }

        }

        // not how this will normally be done, but fine for testing purposes
        foreach (var player in Players)
        {
            if (player == null) continue;
            var moveToMake = player.AI.BestNextActionToTake;
            if (moveToMake == null || moveToMake.Type == AIActionType.DoNothing) continue; // wasn't updated

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

                    // Is target node still capturable?
                    if (toNode.OwnedBy != null) continue;

                    // Does player still have the necessary resources to build the building?
                    // TODO: Assume so for now

                    // Construct the building, move workers, etc
                    fromNode.NumWorkers -= moveToMake.Count;
                    toNode.OwnedBy = player;
                    toNode.NumWorkers = moveToMake.Count;

                    var building = new BuildingData(moveToMake.BuildingToConstruct);
                    toNode.ConstructBuilding(building);

                    // consume resources needed to construct the building
                    foreach (var req in moveToMake.BuildingToConstruct.ConstructionRequirements)
                    {
                        // hack
                        var remainingNeeded = req.Amount;
                        while (remainingNeeded > 0)
                        {
                            var node = getClosestNodeWithResource(player, toNode, req.Good.GoodType);
                            if (node == null)
                            {
                                // shouldn't get here; should get caught by necessary-resource validationa bove
                                Debug.LogError("Error: couldn't find node with resource " + req.Good.GoodType);
                                break;
                            }
                            var amountToTake = Math.Min(remainingNeeded, node.Inventory[req.Good.GoodType]);
                            node.Inventory[req.Good.GoodType] -= amountToTake;
                            remainingNeeded -= amountToTake;
                        }
                    }

                    break;

                case AIActionType.SendWorkersToOwnedNode:

                    // Can player still send enough workers from source node?
                    if (fromNode.NumWorkers < moveToMake.Count || fromNode.OwnedBy != player) continue;

                    fromNode.NumWorkers -= moveToMake.Count;
                    toNode.NumWorkers += moveToMake.Count;
                    break;
            }
        }
    }

    private NodeData getClosestNodeWithResource(PlayerData player, NodeData startNode, GoodType goodType)
    {
        // for now, just find any node that the player owns and has > 0 of the resource
        foreach (var node in Nodes)
            if (node.OwnedBy == player && node.Inventory[goodType] > 0)
                return node;
        return null;
    }
}
