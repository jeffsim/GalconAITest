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
            Nodes[nodeConnectionDefn.Nodes.x].NodeConnections.Add(new NodeConnection() { Start = Nodes[nodeConnectionDefn.Nodes.x], End = Nodes[nodeConnectionDefn.Nodes.y], TravelCost = 1 });

        // Create Workers
        foreach (var node in Nodes)
            for (int i = 0; i < node.NumWorkers; i++)
                Workers.Add(new WorkerData(node.WorldLoc, node.OwnedBy));

        foreach (var player in Players)
            player?.InitializeStaticData(this);
    }

    public void Update()
    {
        // testing one player for now
        Players[2].Update(this);
        // foreach (var player in Players)
        //     player?.Update(this);
    }
}
