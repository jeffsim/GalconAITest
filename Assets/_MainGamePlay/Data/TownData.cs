using System;
using System.Collections.Generic;
using UnityEngine;

public class TownData
{
    public static TownData Instance;
    public List<PlayerData> Players = new();
    public List<NodeData> Nodes = new();
    public List<WorkerData> Workers = new();

    public Action OnAIDebuggerUpdate { get; internal set; }

    public TownData(TownDefn townDefn, WorkerDefn testWorkerDefn)
    {
        Instance = this;

        // Create players
        Players.Add(null); // no player (e.g. for unowned Node)
        Players.Add(new PlayerData() { Name = "Player R", Color = Color.red, ControlledByAI = true, WorkerDefn = testWorkerDefn });
        Players.Add(new PlayerData() { Name = "Player G", Color = Color.green, ControlledByAI = true, WorkerDefn = testWorkerDefn });
        Players.Add(new PlayerData() { Name = "Player B", Color = Color.blue, ControlledByAI = true, WorkerDefn = testWorkerDefn });

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
        // testing one player for now
        Players[2].Update(this);
        // foreach (var player in Players)
        //     player?.Update(this);
    }
}
