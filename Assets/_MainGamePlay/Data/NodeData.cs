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

    // Gatherer workers dispatched from this node (tracked here, not on BuildingData, so
    // returns still resolve correctly if the gatherer building is destroyed/replaced).
    public List<GatheringWorkerData> GatheringWorkers = new();

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

    // Static resource-adjacency bitmasks (see MapTopologyAnalysis). Computed once at map
    // load and never mutated -- they describe terrain, not ownership.
    //   LocalGatherableMask : bits set for resources THIS node yields when stood on
    //                         (Forest -> Wood, StoneMine -> Stone, ...).
    //   AdjacentResourceMask: OR of every direct neighbor's LocalGatherableMask. Used by
    //                         the AI to gate gatherer-building proposals at O(1) instead of
    //                         walking the neighbor list each time.
    public uint LocalGatherableMask;
    public uint AdjacentResourceMask;

    // Static graph-structural facts (MapTopologyAnalysis Phase 2). Topology doesn't change
    // during play, so these are set once at map load and never mutated.
    //   Degree   : number of distinct neighbors (de-dup'd against duplicate connections).
    //   Role     : coarse degree-bucket classification used by build-site heuristics.
    //   DistanceTo[j]: hop distance from THIS node to the node at index `j` in
    //                  TownData.Nodes. int.MaxValue means unreachable. Indexed by position
    //                  in TownData.Nodes, which matches AI_NodeState.Index on the mirror.
    public int Degree;
    public NodeRole Role;
    public int[] DistanceTo;

    // Static articulation / bridge facts (MapTopologyAnalysis Phase 3).
    //   IsArticulationPoint    : removing THIS node disconnects the graph. Defending an
    //                            owned articulation point is structurally more valuable
    //                            than defending a leaf; capturing an enemy articulation
    //                            point amputates their territory.
    //   BridgeNeighborIndices  : indices (within TownData.Nodes) of neighbors connected
    //                            via a bridge edge -- removing that edge disconnects the
    //                            graph. Used by region decomposition (Phase 6).
    public bool IsArticulationPoint;
    public HashSet<int> BridgeNeighborIndices;

    // 2-edge-connected component id (MapTopologyAnalysis Phase 6). Two nodes share a
    // RegionId iff a non-bridge path exists between them: a cycle is one region; a
    // tree assigns every node its own region. Generators use this to prefer in-region
    // sources for reinforcements (don't drain the other room).
    public int RegionId;

    // Static spawn-distance / race-margin maps (MapTopologyAnalysis Phase 4). All arrays
    // are indexed by PlayerData.Id (slot 0 = "no player"; slots 1..N are real players).
    //   PlayerSpawnDistance[slot]: min hop distance from any of player `slot`'s starting
    //                              camps to THIS node. int.MaxValue if unreachable.
    //   OwnerOfNearestSpawn      : slot of the player whose nearest starting camp wins
    //                              the race to this node. -1 on a tie or unreachable.
    //   RaceMargin[slot]         : nearestNonSelfSpawnDist - PlayerSpawnDistance[slot]
    //                              from player `slot`'s POV. >0 means uncontested
    //                              expansion for me; <0 means I'm chasing the enemy.
    public int[] PlayerSpawnDistance;
    public int OwnerOfNearestSpawn;
    public int[] RaceMargin;

    public NodeData(NodeDefn nodeDefn, PlayerData player)
    {
        OwnedBy = player;
        WorldLoc = nodeDefn.WorldLoc;
        NodeId = nodeDefn.NodeId;
        NumWorkers = nodeDefn.NumStartingWorkers;
        if (nodeDefn.StartingBuilding != null)
        {
            Building = new BuildingData(nodeDefn.StartingBuilding);
            ResourceGathering.OnGathererBuildingConstructed(this);
        }

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
        ResourceGathering.OnGathererBuildingConstructed(this);
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
