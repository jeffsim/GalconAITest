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

    // "Attack heat": rolling memory of recent hostile pressure on this node. Incremented when
    // an enemy worker arrives and attacks (defender lost, attacker died, or node flipped), and
    // decayed each realtime tick. Lets the AI recognize "this node is a chokepoint being hit
    // repeatedly" even when the current frontier snapshot looks calm (e.g. attackers have
    // already arrived and resolved, but more are inbound). Mirrored into AI_NodeState so the
    // recursive search can read it.
    public float AttackHeat;

    // Static structural chokepoint score in [0, 1]. Computed once at map load by
    // ChokepointAnalysis based on inter-camp betweenness centrality: a node with score 1.0
    // sits on essentially every shortest path between starting camps; 0 means it's off
    // those routes entirely. AI heuristics multiply Capture / Attack / Buttress priorities
    // by (1 + score * scale) so chokepoints draw heavier offense AND defense than ordinary
    // nodes regardless of who currently owns them.
    public float ChokepointScore;

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

    // === Core game rule ===
    // A node must NEVER reach 0 workers. A node with 0 workers is considered captured
    // (or dead) and immediately flips ownership / is lost. Therefore the maximum number
    // of workers that can leave a node in any single send / attack / capture / construct
    // dispatch is (NumWorkers - 1). All worker-dispatch code (real-time executor,
    // step-mode executor, and AI simulation) must clamp against this value.
    public static int GetMaxSendableWorkers(int numWorkers)
    {
        return numWorkers > 1 ? numWorkers - 1 : 0;
    }

    // Half-send rule for human "drag from node" dispatch: round down, but never exceed
    // GetMaxSendableWorkers (which enforces the >=1 garrison invariant).
    public static int GetHalfSendableWorkers(int numWorkers)
    {
        return Math.Min(numWorkers / 2, GetMaxSendableWorkers(numWorkers));
    }
}
