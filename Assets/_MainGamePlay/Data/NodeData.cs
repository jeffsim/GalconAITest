using System.Collections.Generic;
using UnityEngine;

public class NodeConnection
{
    public NodeData Start;
    public NodeData End;
    public float TravelCost;
}

public class NodeData
{
    public PlayerData OwnedBy;

    public Vector3 WorldLoc;

    public List<NodeConnection> ConnectedNodes = new();
    public BuildingData Building;
}
