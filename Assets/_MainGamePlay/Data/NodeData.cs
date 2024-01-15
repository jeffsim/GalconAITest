using System;
using System.Collections.Generic;
using UnityEngine;

public class NodeConnection
{
    public NodeData Start;
    public NodeData End;
    public float TravelCost;
    public bool IsTwoWay = true;
}

public class NodeData
{
    public PlayerData OwnedBy;
    public int NodeId;
    public Vector3 WorldLoc;

    public List<NodeConnection> NodeConnections = new();
    public BuildingData Building;
    public int NumWorkers;
    public Dictionary<GoodDefn, int> Inventory = new();

    public NodeData(NodeDefn nodeDefn, int nodeId, PlayerData player)
    {
        OwnedBy = player;
        WorldLoc = nodeDefn.WorldLoc;
        NodeId = nodeId;
        NumWorkers = nodeDefn.NumStartingWorkers;
        if (nodeDefn.StartingBuilding != null)
            Building = new BuildingData(nodeDefn.StartingBuilding);

        // Populate starting inventory
        foreach (var kvp in nodeDefn.StartingInventory)
            Inventory[kvp.Key] = kvp.Value;
    }
}