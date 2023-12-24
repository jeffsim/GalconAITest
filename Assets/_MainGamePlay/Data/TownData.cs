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
    public string Name;
    public Color Color = Color.white;
    public bool ControlledByAI;
}

public class TownData
{
    public List<PlayerData> Players = new();
    public List<NodeData> Nodes = new();

    public TownData(TownDefn townDefn)
    {
        // Create players
        Players.Add(null); // no player (unowned)
        Players.Add(new PlayerData() { Name = "Player R", Color = Color.red, ControlledByAI = true });
        Players.Add(new PlayerData() { Name = "Player G", Color = Color.green, ControlledByAI = true });
        Players.Add(new PlayerData() { Name = "Player B", Color = Color.blue, ControlledByAI = true });

        foreach (var nodeDefn in townDefn.Nodes)
            Nodes.Add(new NodeData(nodeDefn, Nodes.Count, Players[nodeDefn.OwnedByPlayerId]));

        // Create Node Connections
        foreach (var nodeConnectionDefn in townDefn.NodeConnections)
            Nodes[nodeConnectionDefn.Nodes.x].ConnectedNodes.Add(new NodeConnection() { Start = Nodes[nodeConnectionDefn.Nodes.x], End = Nodes[nodeConnectionDefn.Nodes.y], TravelCost = 1 });
    }

    public void Update()
    {
    }
}
