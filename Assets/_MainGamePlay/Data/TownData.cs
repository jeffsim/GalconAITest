using System;
using System.Collections.Generic;
using UnityEngine;

public class BuildingData
{
    public BuildingDefn Defn;

    public BuildingData(BuildingDefn defn)
    {
        Defn = defn;
    }
}

public class PlayerData
{
    public WorkerDefn WorkerDefn;
    public string Name;
    public Color Color = Color.white;
    public bool ControlledByAI;
}

public class TownData
{
    public List<PlayerData> Players = new();
    public List<NodeData> Nodes = new();
    public List<WorkerData> Workers = new();

    public TownData(TownDefn townDefn, WorkerDefn testWorkerDefn)
    {
        // Create players
        Players.Add(null); // no player (unowned)
        Players.Add(new PlayerData() { Name = "Player R", Color = Color.red, ControlledByAI = true, WorkerDefn = testWorkerDefn });
        Players.Add(new PlayerData() { Name = "Player G", Color = Color.green, ControlledByAI = true, WorkerDefn = testWorkerDefn });
        Players.Add(new PlayerData() { Name = "Player B", Color = Color.blue, ControlledByAI = true, WorkerDefn = testWorkerDefn });

        foreach (var nodeDefn in townDefn.Nodes)
            Nodes.Add(new NodeData(nodeDefn, Nodes.Count, Players[nodeDefn.OwnedByPlayerId]));

        // Create Node Connections
        foreach (var nodeConnectionDefn in townDefn.NodeConnections)
            Nodes[nodeConnectionDefn.Nodes.x].ConnectedNodes.Add(new NodeConnection() { Start = Nodes[nodeConnectionDefn.Nodes.x], End = Nodes[nodeConnectionDefn.Nodes.y], TravelCost = 1 });

        // Create Workers
        foreach (var node in Nodes)
        {
            for (int i = 0; i < node.NumWorkers; i++)
            {
                var worker = new WorkerData(node.WorldLoc, node.OwnedBy);
                Workers.Add(worker);
            }
        }
    }

    public void Update()
    {
    }
}
