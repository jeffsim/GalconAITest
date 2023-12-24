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
    public int NodeId;
    public Vector3 WorldLoc;

    public List<NodeConnection> ConnectedNodes = new();
    public BuildingData Building;
    public int NumWorkers;
    private NodeDefn nodeDefn;

    public NodeData(NodeDefn nodeDefn, int nodeId, PlayerData player)
    {
        this.nodeDefn = nodeDefn;
        OwnedBy = player;
        WorldLoc = nodeDefn.WorldLoc;
        NodeId = nodeId;
        NumWorkers = nodeDefn.NumStartingWorkers;
        if (nodeDefn.StartingBuilding != null)
            Building = new BuildingData(nodeDefn.StartingBuilding);
    }
}