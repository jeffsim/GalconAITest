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

    // Realtime: how many in-flight workers are currently heading to this node, keyed by their
    // owner. Used by the AI mirror so a player who already has 10 workers en route to capture
    // a node won't immediately dispatch another 10 thinking the node is still empty.
    public Dictionary<PlayerData, int> IncomingByPlayer = new Dictionary<PlayerData, int>();

    // Realtime: at most one player can have a "capture intent" toward an empty/neutral node
    // at a time (the first to commit). Treat the node, for AI search purposes, as already
    // owned by that player with a virtual worker count equal to IncomingByPlayer[that player].
    // Cleared when no in-flight workers carrying capture intent remain.
    public PlayerData PendingCaptureBy;
    public BuildingDefn PendingConstructBuilding;

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

    public int GetIncomingFor(PlayerData player)
    {
        if (player == null) return 0;
        return IncomingByPlayer.TryGetValue(player, out var n) ? n : 0;
    }

    public void AddIncoming(PlayerData player, int delta)
    {
        if (player == null) return;
        IncomingByPlayer.TryGetValue(player, out var cur);
        IncomingByPlayer[player] = Math.Max(0, cur + delta);
    }
}
