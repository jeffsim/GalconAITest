using System.Collections.Generic;
using UnityEngine;

public class BuildingData
{
}

public class NodeConnection
{
    public NodeData Start;
    public NodeData End;
    public float TravelCost;
}

public class Player
{
}

public class NodeData
{
    public Player OwnedBy;

    public List<NodeConnection> ConnectedNodes;
    public BuildingData Building;
}

public class TownData
{
    public List<NodeData> Nodes;
    
    public TownData()
    {
        Nodes = new List<NodeData>();
    }

    public void Update()
    {
    }
}
