using System;
using System.Collections.Generic;
using UnityEngine;

/// Per-player, per-node mirror of NodeData. One AI_NodeState exists per (PlayerAI, NodeData);
/// the AI never mutates this from inside a "search" any more -- the AI is now a single-pass
/// utility evaluator (no simulate/undo, no recursion). The mirror is refreshed once per AI
/// tick by AIWorldView.Refresh.
///
/// Every field here is either:
///   (a) Static -- set once at construction and never changed (NodeId, NeighborNodes,
///       ChokepointScore, terrain-resource flags).
///   (b) Per-tick refresh -- copied from RealNode + in-flight projections inside Refresh.
///
/// There is intentionally no `IsVisited`, no per-search undo state, no per-search action
/// pool; the old depth-limited tree search has been removed.
public class AI_NodeState
{
    public override string ToString() => $"Node {NodeId} ({OwnedBy?.Name[^1]})";

    // ============================================================================
    // Static identity / topology
    // ============================================================================
    public NodeData RealNode;
    public int NodeId;
    /// Position of this mirror in AIWorldView.Nodes[]. NOT the same as NodeId -- NodeId is
    /// a stable map-level identifier (potentially non-zero, potentially sparse), Index is a
    /// dense [0, NumNodes) array slot. Every StrategicAnalysis array is sized to NumNodes
    /// and indexed by Index, never by NodeId.
    public int Index;
    public List<AI_NodeState> NeighborNodes = new();
    public int NumNeighbors;
    /// Static [0, 1] score from ChokepointAnalysis. Chokepoint topology never changes
    /// during play, so we mirror RealNode.ChokepointScore exactly once at construction.
    public float ChokepointScore;

    // ============================================================================
    // Dynamic, refreshed each tick by Refresh()
    // ============================================================================

    public PlayerData OwnedBy;

    /// Physical worker count standing at this node right now. The only number the worker
    /// dispatch executor should trust when deciding "how many workers can leave from here?".
    public int NumWorkers;
    public int MaxWorkers;
    public int WorkersGeneratedPerTurn;

    /// Snapshot of my own in-flight reinforcements heading TO this node, captured at the
    /// start of Refresh. Used by destination-side checks ("does this node already have
    /// enough help arriving?"); MUST NOT be folded into NumWorkers since it is not yet
    /// dispatchable from this node as a source.
    public int IncomingFriendlyWorkers;

    /// Projected garrison after pre-existing in-flight friendlies arrive. Use anywhere the
    /// question is "is this node about to be sufficiently staffed?"; NEVER use as a source
    /// of workers to dispatch -- that path must read NumWorkers directly.
    public int EffectiveDefenseGarrison => NumWorkers + IncomingFriendlyWorkers;

    /// Forward-looking hostile-wave count: sum of every non-viewer player's in-flight
    /// workers currently targeting this node. Predictive (an inbound wave that has not
    /// yet landed still shows as pressure here).
    public int IncomingHostileWorkers;

    /// Decaying memory of recent hostile arrivals at this node (post-hoc pressure). Bumped
    /// in TownData.ResolveWorkerArrival; decayed each realtime tick.
    public float AttackHeat;

    /// True when an enemy node already has enough of our in-flight attackers to CAPTURE
    /// it (i.e. strictly more than effective defense + expected travel-time regen). The
    /// attack generator skips additional waves while this holds.
    public bool AttackAlreadySufficient;

    /// For an enemy-owned node: raw effective defense before we credit any of our own
    /// in-flight attackers against it. This is `RealNode.NumWorkers + defender's
    /// in-flight reinforcements`, NOT reduced by our own attack waves. Sizing a NEW
    /// attack wave must use this + travel regen + 1 (capture flip), then credit our
    /// in-flight attackers against the result. The legacy `NumWorkers` field on the
    /// mirror is post-deducted (so frontier-pressure heuristics still see "this enemy
    /// is being eaten") and MUST NOT be used to size attacks.
    public int EnemyEffectiveDefense;

    /// Our own attackers currently in flight to this node (read directly from
    /// RealNode.IncomingByPlayer[viewerPlayer] when the node is enemy-owned).
    public int IncomingMyAttackers;

    /// True for a NEUTRAL node that currently has a capture wave from `viewerPlayer` in flight
    /// (RealNode.PendingCaptureBy == viewerPlayer). The node is NOT yet owned -- OwnedBy
    /// remains null and NumWorkers reflects reality -- but the capture/build generators must
    /// suppress further proposals against this target until the in-flight wave resolves
    /// (otherwise we'd double-dispatch). Reinforce/attack generators ignore the node naturally
    /// via the standard "OwnedBy == player" / "OwnedBy is enemy" filters.
    public bool PendingMyCapture;

    // Buildings
    public bool HasBuilding;
    public BuildingDefn BuildingDefn;
    public int BuildingLevel;
    /// World-time (in realtime seconds) when the building here was constructed/captured.
    /// Used by upgrade scoring to prefer freshly-built buildings less than long-standing
    /// ones with sunk investment.
    public int TurnBuildingWasBuilt;
    public bool CanGoGatherResources;
    public GoodType ResourceThisNodeCanGoGather;
    public bool CanBeGatheredFrom;
    public GoodType ResourceGatheredFromThisNode;
    public bool CanGenerateWorkers;
    public WorkerDefn WorkerGenerated;

    /// Distance (in hops) to the closest map node that yields each resource. Set once at
    /// startup by SetDistanceToResources -- current implementation only checks 1-hop
    /// neighbors, which is enough for the buildings the current game supports.
    public Dictionary<GoodType, int> DistanceToGatherableResource = new();

    public bool IsResourceNode => CanBeGatheredFrom;

    // ============================================================================
    // Strategic facts -- written by StrategicAnalysis once per tick. Cached on the
    // node mirror itself so generators and scorers don't have to thread an analysis
    // object through every call.
    // ============================================================================

    /// True when at least one neighbor is owned by someone other than me (or is a contested
    /// neutral). The frontier shifts every tick, so this is recomputed each refresh.
    public bool IsOnTerritoryEdge;

    /// Worker count of enemy-owned neighbors (sum). Used for sizing defensive reinforcement
    /// and for upgrade-safety checks.
    public int NumEnemiesInNeighborNodes;

    /// Neutral neighbors that themselves touch an enemy -- e.g. a neutral wedged between us
    /// and someone hostile. Captures of these gateways tend to be contested and pull more
    /// aggression from the scorer.
    public int NumContestedNeutralWorkersNearby;

    // ============================================================================
    // Construction / refresh
    // ============================================================================

    public AI_NodeState(NodeData nodeData)
    {
        RealNode = nodeData;
        NodeId = nodeData.NodeId;
        ChokepointScore = nodeData.ChokepointScore;
        Refresh(null);
    }

    /// Set once at world build-time after the neighbor graph is wired. Current
    /// implementation only checks 1-hop neighbors; that's sufficient for the present
    /// game where every gatherer building sits adjacent to its resource deposit.
    internal void SetDistanceToResources()
    {
        DistanceToGatherableResource[GoodType.Wood] = FindClosestResourceNode(GoodType.Wood);
        DistanceToGatherableResource[GoodType.Stone] = FindClosestResourceNode(GoodType.Stone);
    }

    int FindClosestResourceNode(GoodType good)
    {
        for (int i = 0; i < NumNeighbors; i++)
        {
            var n = NeighborNodes[i];
            if (n.HasBuilding && n.CanBeGatheredFrom && n.ResourceGatheredFromThisNode == good)
                return 1;
        }
        return int.MaxValue;
    }

    public int DistanceToClosestGatherableResourceNode(GoodType good) =>
        DistanceToGatherableResource.TryGetValue(good, out int d) ? d : int.MaxValue;

    /// Copy mutable RealNode state into the mirror. When viewerPlayer is non-null, project
    /// the in-flight worker state from that player's perspective (capture intents become
    /// virtual ownership, my own reinforcements land in IncomingFriendlyWorkers separately,
    /// enemy garrisons add their incoming defenders).
    public void Refresh(PlayerData viewerPlayer)
    {
        if (RealNode.Building == null)
        {
            ClearBuilding();
        }
        else
        {
            if (RealNode.Building.Defn.CanBeGatheredFrom)
                SetResourceNode(RealNode.Building.Defn);
            else
                SetBuilding(RealNode.Building.Defn, 0);
            BuildingLevel = RealNode.Building.Level;
        }

        OwnedBy = RealNode.OwnedBy;
        NumWorkers = RealNode.NumWorkers;
        MaxWorkers = RealNode.Building?.MaxWorkers ?? 0;
        WorkersGeneratedPerTurn = RealNode.Building?.WorkersGeneratedPerTurn ?? 0;
        AttackHeat = RealNode.AttackHeat;
        IncomingFriendlyWorkers = 0;
        IncomingHostileWorkers = 0;
        AttackAlreadySufficient = false;
        PendingMyCapture = false;
        EnemyEffectiveDefense = 0;
        IncomingMyAttackers = 0;

        if (viewerPlayer == null)
            return;

        // Capture-intent suppression. Earlier versions of this Refresh used to overwrite
        // OwnedBy = PendingCaptureBy when a capture was in flight, with the intent of
        // suppressing duplicate Capture proposals. That hack caused a real bug: with the
        // node now looking "owned by me" to the generators, ReinforceGenerator would emit
        // SendWorkersToOwnedNode to a node that the executor still saw as a neutral with
        // no building -- and ResolveWorkerArrival kills any non-CaptureAndConstruct arrival
        // at an empty neutral. The result was a steady stream of dead reinforcement waves
        // until a real CaptureNeutralNode action finally landed and flipped ownership.
        //
        // The fix: leave OwnedBy/NumWorkers/Building untouched (so the world looks like the
        // real game state) and use a dedicated PendingMyCapture flag that ONLY the capture
        // and build generators consult to skip duplicate proposals.
        if (OwnedBy == null && RealNode.PendingCaptureBy == viewerPlayer)
            PendingMyCapture = true;

        if (OwnedBy == viewerPlayer)
        {
            IncomingFriendlyWorkers = RealNode.GetIncomingFor(viewerPlayer);

            int hostile = 0;
            foreach (var kvp in RealNode.IncomingByPlayer)
            {
                if (kvp.Key == null || kvp.Key == viewerPlayer) continue;
                hostile += kvp.Value;
            }
            IncomingHostileWorkers = hostile;
        }
        else if (OwnedBy != null)
        {
            // Enemy node: include the defender's reinforcements in perceived garrison.
            int defenderReinforcements = RealNode.GetIncomingFor(OwnedBy);
            int effectiveDefense = NumWorkers + defenderReinforcements;
            int incomingAttackers = RealNode.GetIncomingFor(viewerPlayer);

            EnemyEffectiveDefense = effectiveDefense;
            IncomingMyAttackers = incomingAttackers;

            // Capture rule (TownData.ResolveWorkerArrival): each attacker trades 1:1 with a
            // defender (both die); after defenders reach 0, the NEXT attacker captures. So
            // capturing requires strictly MORE attackers than defenders -- not equal. The
            // generator additionally accounts for travel-time regen, but at the very least
            // we should not flag "sufficient" until incoming exceeds raw defense.
            AttackAlreadySufficient = incomingAttackers > effectiveDefense;

            // Floor at 1 while the enemy still owns the node so frontier pressure on
            // adjacent friendly nodes never fully vanishes mid-attack. NOTE: this
            // post-deducted NumWorkers is used by StrategicAnalysis to compute
            // FrontierPressure; do NOT use it to size attacks. Use EnemyEffectiveDefense
            // and IncomingMyAttackers above instead.
            NumWorkers = Math.Max(1, effectiveDefense - incomingAttackers);
        }
    }

    // ============================================================================
    // Building helpers (used by Refresh; AIWorldView never simulates building changes)
    // ============================================================================

    public void ClearBuilding()
    {
        HasBuilding = false;
        BuildingDefn = null;
        BuildingLevel = 0;
        MaxWorkers = 0;
        TurnBuildingWasBuilt = 0;
        CanGoGatherResources = false;
        ResourceThisNodeCanGoGather = GoodType.Unset;
        CanGenerateWorkers = false;
        WorkerGenerated = null;
        // CanBeGatheredFrom / ResourceGatheredFromThisNode describe terrain and are set once.
    }

    public void SetResourceNode(BuildingDefn buildingDefn)
    {
        BuildingDefn = buildingDefn;
        HasBuilding = true;
        Debug.Assert(buildingDefn.CanBeGatheredFrom);
        CanBeGatheredFrom = buildingDefn.CanBeGatheredFrom;
        ResourceGatheredFromThisNode = buildingDefn.ResourceGatheredFromThisNode.GoodType;
    }

    public void SetBuilding(BuildingDefn buildingDefn, int turnNumber)
    {
        Debug.Assert(!buildingDefn.CanBeGatheredFrom);
        BuildingDefn = buildingDefn;
        HasBuilding = true;
        BuildingLevel = 1;
        MaxWorkers = 10 * (int)Math.Pow(2, BuildingLevel - 1);
        TurnBuildingWasBuilt = turnNumber;
        CanGoGatherResources = buildingDefn.CanGatherResources;
        if (CanGoGatherResources)
            ResourceThisNodeCanGoGather = buildingDefn.ResourceThisNodeCanGoGather.GoodType;
        CanGenerateWorkers = buildingDefn.CanGenerateWorkers;
        if (CanGenerateWorkers)
            WorkerGenerated = buildingDefn.GeneratableWorker;
    }
}
