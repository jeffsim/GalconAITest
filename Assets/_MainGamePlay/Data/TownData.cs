using System.Collections.Generic;
using UnityEngine;

public class BuildingData
{
}

public class PlayerData
{
    public string Name;
    public Color Color;
    public bool ControlledByAI;
}

public class TownData
{
    public List<PlayerData> Players = new();
    public List<NodeData> Nodes = new();

    public TownData()
    {
        // Create players
        Players.Add(new PlayerData() { Name = "Player R", Color = Color.red, ControlledByAI = true });
        Players.Add(new PlayerData() { Name = "Player G", Color = Color.green, ControlledByAI = true });
        Players.Add(new PlayerData() { Name = "Player B", Color = Color.blue, ControlledByAI = true });

        // Create Nodes
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                Nodes.Add(new NodeData() { OwnedBy = null, WorldLoc = new Vector3(x * 5, 0, y * 5) });

        // Assign node owners
        Nodes[0].OwnedBy = Players[0];
        Nodes[5].OwnedBy = Players[1];
        Nodes[8].OwnedBy = Players[2];

        // Create Node Connections
        Nodes[0].ConnectedNodes.Add(new NodeConnection() { Start = Nodes[0], End = Nodes[1], TravelCost = 1 });
        Nodes[0].ConnectedNodes.Add(new NodeConnection() { Start = Nodes[1], End = Nodes[8], TravelCost = 1 });
    }

    public void Update()
    {
    }
}
