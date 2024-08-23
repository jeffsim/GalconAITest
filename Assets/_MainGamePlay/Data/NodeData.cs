using System;
using System.Collections.Generic;
using UnityEngine;

public class NodeConnection
{
    public NodeData Start;
    public NodeData End;
    public float TravelCost;
    public bool IsBidirectional = true;
}

public class NodeData
{
    public PlayerData OwnedBy;
    public int NodeId;
    public Vector3 WorldLoc;

    public Action OnBuildingConstructed;

    public List<NodeConnection> NodeConnections = new();
    public BuildingData Building;
    public int NumWorkers;

    public SerializedDictionary<GoodType, int> Inventory = new();

    public NodeData(NodeDefn nodeDefn, PlayerData player)
    {
        OwnedBy = player;
        WorldLoc = nodeDefn.WorldLoc;
        NodeId = nodeDefn.NodeId;
        NumWorkers = nodeDefn.NumStartingWorkers;
        if (nodeDefn.StartingBuilding != null)
            Building = new BuildingData(nodeDefn.StartingBuilding);

        // Populate starting inventory. force keys to exist
        // foreach (var value in GameDefns.Instance.GoodDefns.Values)
        //     Inventory[value] = 0;

        Inventory[GoodType.Wood] = 0;
        Inventory[GoodType.Stone] = 0;

        foreach (var kvp in nodeDefn.StartingInventory)
            Inventory[kvp.Key.GoodType] = kvp.Value;
    }

    public void ConstructBuilding(BuildingData building)
    {
        Building = building;
        OnBuildingConstructed?.Invoke();
    }
}