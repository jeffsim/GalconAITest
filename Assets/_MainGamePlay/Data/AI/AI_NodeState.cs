using System;
using System.Collections.Generic;
using System.Diagnostics;

public class AI_NodeState
{
    public override string ToString() => $"Node {NodeId} ({OwnedBy?.Name[^1]})";
    public NodeData RealNode;
    public List<AI_NodeState> NeighborNodes = new();
    public int NumNeighbors;
    public int NumWorkers;
    public int MaxWorkers;
    public int WorkersGeneratedPerTurn = 5; // bump this up from 1 to exaggerate value of gen'ing workers
                                            // public int aiOrigNumWorkers;

    public int WorkersAdded;

    // Snapshot of my own in-flight friendly reinforcements heading TO this node, captured
    // at the start of each real-game Update. Read by EffectiveDefenseGarrison so destination-
    // side buttress checks ("does this node already have enough help arriving?") account
    // for the wave-in-flight without over-counting it as "available to send" — the previous
    // design folded incoming into NumWorkers and that lie let GetButtressSourceNode pick a
    // node with 1 physical worker and 17 incoming as a 9-worker source, producing the
    // impossible "Support 9 #22 -> #7" plan that the realtime executor then clamped to 0.
    //
    // Snapshot only — the recursive search MUST NOT mutate this during simulation. Workers
    // we hypothetically dispatch in-search are modeled by mutating NumWorkers directly
    // (instant-arrival approximation); IncomingFriendlyWorkers always represents the real-
    // game in-flight count that existed when this Update began.
    public int IncomingFriendlyWorkers;

    // Projected garrison after pre-existing in-flight friendly reinforcements arrive. Use
    // this anywhere the question is "is this node about to be sufficiently defended/staffed?"
    // (buttress destination checks, frontier deficit, overload-for-upgrade demand, etc.).
    // Do NOT use this for "how many workers can leave this node?" — those callers must read
    // NumWorkers directly, which is the physical count actually available to dispatch.
    public int EffectiveDefenseGarrison => NumWorkers + IncomingFriendlyWorkers;

    public int NumEnemiesInNeighborNodes;
    // Neutral neighbors that also touch an enemy — e.g. #11 between Red #12 and Blue #27.
    public int NumContestedNeutralWorkersNearby;
    public bool IsOnTerritoryEdge;

    // Mirror of RealNode.AttackHeat at the time of the last UpdateState. Read by the buttress
    // and frontier heuristics so a node hit repeatedly by attackers shows demand for reinforcement
    // even when the current snapshot of enemy neighbors looks light.
    public float AttackHeat;

    // Forward-looking hostile-wave count: sum of every non-viewer player's in-flight workers
    // currently targeting this node, populated only when Update is called with the owning
    // viewerPlayer. AttackHeat is post-hoc (memory of arrivals); this is predictive (an
    // inbound wave that hasn't started landing yet still shows as defense pressure). Used by
    // GetEffectiveFrontierPressure so the buttress and upgrade heuristics can prepare for
    // telegraphed attacks rather than only react to them.
    public int IncomingHostileWorkers;

    // Static [0, 1] score from ChokepointAnalysis -- mirrored from RealNode.ChokepointScore
    // once at construction (chokepoint topology never changes during play). Used by the
    // capture / attack / buttress heuristics to prioritize map-level structural chokepoints
    // over equally-valued non-chokepoint candidates.
    public float ChokepointScore;

    // True when an enemy node already has enough of our in-flight attackers to beat its
    // effective defense (garrison + defender reinforcements). The attack task should not
    // commit additional waves when this is set — it would just drip-feed workers that
    // arrive after the node is already captured.
    public bool AttackAlreadySufficient;

    public PlayerData OwnedBy;
    public int NodeId;
    internal bool IsResourceNode => CanBeGatheredFrom;

    public Dictionary<GoodType, int> DistanceToGatherableResource = new();

    public bool IsVisited;

    // Buildings
    public bool HasBuilding;
    public int TurnBuildingWasBuilt;    // used to determine how long we've owned the building; e.g. building a woodcutter sooner rahter than later is better
    public BuildingDefn BuildingDefn;
    public bool CanGoGatherResources;
    public GoodType ResourceThisNodeCanGoGather;
    public int BuildingLevel;

    public bool CanBeGatheredFrom;
    public GoodType ResourceGatheredFromThisNode;

    public bool CanGenerateWorkers;
    public WorkerDefn WorkerGenerated;

    // Fully reset all building-derived state. The previous "HasBuilding = false" alone left
    // BuildingDefn / MaxWorkers / BuildingLevel / CanGoGatherResources / CanGenerateWorkers
    // / WorkerGenerated polluted with whatever was last set via SetBuilding / SetResourceNode.
    // That pollution mattered in two paths:
    //   - Undo_SendWorkersToConstructBuildingInEmptyNode calls this after undoing a hypothetical
    //     Construct, leaving the AI node looking like it still had the would-be building.
    //   - Update() (per real-game Update) calls this when RealNode.Building is null, so a
    //     captured neutral whose AI mirror had been polluted earlier in the search would still
    //     read BuildingDefn != null next turn -- making AITask_UpgradeBuilding fire on it and
    //     handing TownData.Debug_WorldTurn a real node with Building == null (NRE).
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
        // CanBeGatheredFrom / ResourceGatheredFromThisNode describe the underlying terrain
        // (Forest / mineral deposit) and are set once at startup; do not reset them here.
    }

    // public BuildingDefn BuildingInNode;
    public void SetResourceNode(BuildingDefn buildingDefn, int turnNumber)
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

        // NOTE: If update this then need to update elsewhere too.  grep on TODO-042
        MaxWorkers = 10 * (int)Math.Pow(2, BuildingLevel - 1);

        TurnBuildingWasBuilt = turnNumber;
        CanGoGatherResources = buildingDefn.CanGatherResources;
        if (CanGoGatherResources)
            ResourceThisNodeCanGoGather = buildingDefn.ResourceThisNodeCanGoGather.GoodType;

        // This can only happen at start; e.g. this is a forest - don't need to handle every time a building is built
        // ^ that's only true if we don't allow resource nodes to be built
        // CanBeGatheredFrom = buildingDefn.CanBeGatheredFrom;
        // if (CanBeGatheredFrom)
        //     ResourceGatheredFromThisNode = buildingDefn.ResourceGatheredFromThisNode.GoodType;
        // DistanceToClosestGatherableResourceNode = findClosestResourceNode(ResourceThisNodeCanGoGather);

        CanGenerateWorkers = buildingDefn.CanGenerateWorkers;
        if (CanGenerateWorkers)
            WorkerGenerated = buildingDefn.GeneratableWorker;
    }

    public AI_NodeState(NodeData nodeData)
    {
        // set static fields
        RealNode = nodeData;
        NodeId = nodeData.NodeId;
        // Static for the lifetime of the map; ChokepointAnalysis runs once in TownData ctor
        // before any AI_NodeState is constructed, so RealNode.ChokepointScore is final here.
        ChokepointScore = nodeData.ChokepointScore;
        Update();
    }

    internal void SetDistanceToResources()
    {
        //   DistanceToClosestGatherableResourceNode = findClosestResourceNode(ResourceThisNodeCanGoGather);
        DistanceToGatherableResource[GoodType.Wood] = findClosestResourceNode(GoodType.Wood);
        DistanceToGatherableResource[GoodType.Stone] = findClosestResourceNode(GoodType.Stone);
    }

    private int findClosestResourceNode(GoodType gatherableResource)
    {
        // For now, only look at neighboring nodes.  Need to recurse out.  PriorityQueue/super-simple A*
        for (int i = 0; i < NumNeighbors; i++)
        {
            var neighbor = NeighborNodes[i];
            if (neighbor.HasBuilding && neighbor.CanBeGatheredFrom && neighbor.ResourceGatheredFromThisNode == gatherableResource)
                return 1;
        }
        return int.MaxValue;
    }

    public void Update()
    {
        Update(null);
    }

    // viewerPlayer: when non-null (realtime mode AI search), the node mirror is adjusted from
    // that player's perspective so in-flight workers and capture intents are visible to the
    // AI. This is what suppresses the "send another 10 immediately because the enemy node
    // still shows full strength" pathology.
    public void Update(PlayerData viewerPlayer)
    {
        if (RealNode.Building == null)
            ClearBuilding();
        else
        {
            if (RealNode.Building.Defn.CanBeGatheredFrom)
                SetResourceNode(RealNode.Building.Defn, 0);
            else
                SetBuilding(RealNode.Building.Defn, 0);
            BuildingLevel = RealNode.Building.Level;
        }

        OwnedBy = RealNode.OwnedBy;
        NumWorkers = RealNode.NumWorkers;
        MaxWorkers = RealNode.Building?.MaxWorkers ?? 0;
        WorkersGeneratedPerTurn = RealNode.Building?.WorkersGeneratedPerTurn ?? 0;
        AttackHeat = RealNode.AttackHeat;
        // Default to 0/false; the per-player branches below populate these when viewerPlayer is set.
        IncomingHostileWorkers = 0;
        IncomingFriendlyWorkers = 0;
        AttackAlreadySufficient = false;

        if (viewerPlayer != null)
        {
            // Capture intent: a neutral / empty node that one of my in-flight construct or
            // capture groups is targeting should look "already mine" so AITask_ConstructBuilding
            // and AITask_AttackToNode skip it and don't pile on duplicates.
            if (OwnedBy == null && RealNode.PendingCaptureBy != null)
            {
                OwnedBy = RealNode.PendingCaptureBy;
                // Pending-capture targets have no physical workers of our own yet — the
                // incoming wave IS the only "garrison" they have, so it doubles as
                // NumWorkers AND IncomingFriendlyWorkers's job here (the destination-side
                // helper just adds them together; double-counting them would be wrong).
                // Treat the wave as physical so EffectiveDefenseGarrison still reports the
                // right total via the standard NumWorkers + IncomingFriendlyWorkers sum.
                NumWorkers = RealNode.GetIncomingFor(RealNode.PendingCaptureBy);
                if (RealNode.PendingCaptureBy == viewerPlayer && RealNode.PendingConstructBuilding != null)
                {
                    // Make the would-be building visible so the AI's own search treats this
                    // site as already serving that role and doesn't queue redundant work.
                    HasBuilding = true;
                    BuildingDefn = RealNode.PendingConstructBuilding;
                    BuildingLevel = 1;
                    MaxWorkers = 10;
                    CanGoGatherResources = RealNode.PendingConstructBuilding.CanGatherResources;
                    if (CanGoGatherResources)
                        ResourceThisNodeCanGoGather = RealNode.PendingConstructBuilding.ResourceThisNodeCanGoGather.GoodType;
                    CanGenerateWorkers = RealNode.PendingConstructBuilding.CanGenerateWorkers;
                    if (CanGenerateWorkers)
                        WorkerGenerated = RealNode.PendingConstructBuilding.GeneratableWorker;
                }
            }
            else if (OwnedBy == viewerPlayer)
            {
                // Friendly node: track my own incoming reinforcements SEPARATELY from
                // NumWorkers so destination-side checks (buttress, frontier deficit,
                // upgrade overload) can include them via EffectiveDefenseGarrison without
                // making the same workers look "available to dispatch" from this node as
                // a source. The previous design (NumWorkers += incoming) caused #22 with
                // 1 physical + 17 in-flight to be picked as a 9-worker buttress source,
                // producing impossible Support plans the realtime executor then clamped
                // to zero workers.
                IncomingFriendlyWorkers = RealNode.GetIncomingFor(viewerPlayer);

                // Predictive hostile-wave awareness: sum every non-viewer player's incoming
                // workers targeting this node. Used by GetEffectiveFrontierPressure so a
                // telegraphed wave shows up as defense pressure BEFORE the first attacker
                // resolves and bumps AttackHeat. Without this, a friendly node could see
                // 10 enemies in flight and still think frontierPressure = 0.
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
                // Enemy node: include the defender's own incoming reinforcements in the
                // perceived garrison — they'll arrive and bolster the defense, so sizing
                // an attack purely against the current snapshot leads to drip-feeding
                // insufficient waves into a node that's being constantly resupplied.
                int defenderReinforcements = RealNode.GetIncomingFor(OwnedBy);
                int effectiveDefense = NumWorkers + defenderReinforcements;

                // Subtract our committed attackers (1:1 trade assumption).
                int incomingAttackers = RealNode.GetIncomingFor(viewerPlayer);
                AttackAlreadySufficient = incomingAttackers >= effectiveDefense;

                // Floor at 1 while the enemy still owns the node so frontier pressure on
                // adjacent friendly nodes never fully vanishes — otherwise the buttress
                // heuristic goes blind to depleted frontier nodes whose enemy neighbor
                // "looks" neutralized by in-flight attackers that haven't arrived yet.
                NumWorkers = Math.Max(1, effectiveDefense - incomingAttackers);
            }
        }
    }

    internal int DistanceToClosestEnemyNode(PlayerData player)
    {
        // TODO: cache; but: need to update on various actions
        for (int i = 0; i < NumNeighbors; i++)
        {
            var neighbor = NeighborNodes[i];
            // if neighbor is owned by someone other than player, then return 1
            if (neighbor.OwnedBy != null && neighbor.OwnedBy != player)
                return 1;
        }
        return int.MaxValue;
    }

    internal int DistanceToClosestGatherableResourceNode(GoodType goodType) => DistanceToGatherableResource[goodType];
}