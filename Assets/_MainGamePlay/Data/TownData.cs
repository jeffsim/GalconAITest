using System;
using System.Collections.Generic;
using UnityEngine;

public class TownData
{
    public static TownData Instance;
    [HideInInspector] public List<PlayerData> Players = new();
    [HideInInspector] public List<NodeData> Nodes = new();

    // Realtime in-flight workers. List is the source of truth; the scene mirrors it 1:1 onto
    // Worker GameObjects each frame. Spawned by ExecuteRealtimeAction, advanced and resolved
    // by RealtimeTick.
    [HideInInspector] public List<WorkerData> WorkersInFlight = new();

    // Default WorkerDefn used when an AI dispatches workers in realtime mode. The scene seeds
    // this from the field set on AITestScene; PlayerData also keeps a per-player one which we
    // use first if present.
    [HideInInspector] public WorkerDefn DefaultWorkerDefn;

    public Action<int> OnAIDebuggerUpdate { get; internal set; }
    public Action<WorkerData> OnWorkerSpawned;
    public Action<WorkerData> OnWorkerArrived;

    // Bumped any time the world state changes in a way the AI cares about (worker dispatch,
    // arrivals, captures). PlayerAI caches its decision against this and skips the full
    // search when the revision is unchanged, so the search no longer runs every Update frame.
    public int WorldRevision = 0;

    // Realtime accumulated game seconds. Advanced by RealtimeTick(dt) where dt is already
    // scaled by AITestScene.GameSpeed. Used for AI decision scheduling and worker spawn delays.
    public float WorldTime;

    // Spawn cadence within a single dispatched group: how long after the previous worker in
    // the group spawns before the next one does. Keeps a column of cubes visible instead of a
    // single overlapping cluster.
    public const float WorkerSpawnStaggerSeconds = 0.08f;

    public TownData(TownDefn townDefn, WorkerDefn testWorkerDefn, PlayerAIDefn[] playerAIDefns)
    {
        Instance = this;
        DefaultWorkerDefn = testWorkerDefn;

        // Create players
        Players.Add(null); // no player (e.g. for unowned Node)
        for (int i = 0; i < 3; i++)
        {
            var aiDefn = playerAIDefns != null && i < playerAIDefns.Length ? playerAIDefns[i] : null;
            Players.Add(new PlayerData(i + 1, aiDefn, testWorkerDefn));
        }

        // Create Nodes
        foreach (var nodeDefn in townDefn.Nodes)
            // if (nodeDefn.Enabled)
            Nodes.Add(new NodeData(nodeDefn, Players[nodeDefn.OwnedByPlayerId]));

        // Create Node Connections
        foreach (var nodeConnectionDefn in townDefn.NodeConnections)
        {
            var fromNode = GetNodeById(nodeConnectionDefn.Nodes.x);
            var toNode = GetNodeById(nodeConnectionDefn.Nodes.y);
            if (fromNode == null || toNode == null) continue;
            fromNode.NodeConnections.Add(new NodeConnection() { Start = fromNode, End = toNode, TravelCost = 1, IsBidirectional = nodeConnectionDefn.IsBidirectional });
            if (nodeConnectionDefn.IsBidirectional)
                toNode.NodeConnections.Add(new NodeConnection() { Start = toNode, End = fromNode, TravelCost = 1, IsBidirectional = true });
        }

        // One-time chokepoint scoring -- must run AFTER connections are wired and BEFORE
        // any AI_NodeState mirrors are built (player.InitializeStaticData below), since
        // those copy NodeData.ChokepointScore into AI_NodeState.ChokepointScore once.
        ChokepointAnalysis.Compute(this);

        // Static topology preprocessing: resource-adjacency bitmasks, distance matrix,
        // articulation points, regions, etc. Same once-at-load timing as chokepoint
        // scoring; mirror also flows into AI_NodeState in InitializeStaticData below.
        MapTopologyAnalysis.Compute(this);

        foreach (var player in Players)
            player?.InitializeStaticData(this);
    }

    private NodeData GetNodeById(int nodeId)
    {
        // TODO: Dictionary.  However: only used (currently) in TownData ctor, so not a big deal.
        foreach (var node in Nodes)
            if (node.NodeId == nodeId)
                return node;
        Debug.Log("Failed to find node with Id " + nodeId);
        return null;
    }

    public void Update()
    {
        // Normalize any owned-but-no-building nodes before AI search sees them. Same rationale
        // as the RealtimeTick path: the dump diagnostics and the per-player AI search both
        // read RealNode.Building; an inconsistent node skews heuristics (zero MaxWorkers,
        // garbage frontier pressure) and surfaces in the simulation dump as "(no building)".
        EnforceOwnedNodesHaveBuilding();

        // Tests run headless without AITestScene.Instance; in that case there's no
        // "debug player" to defer, just update each AI player in declaration order.
        // Human players have no AI and are skipped.
        var debugPlayer = AITestScene.Instance != null ? AITestScene.Instance.DebugPlayerToViewDetailsOn : null;
        foreach (var player in Players)
        {
            if (player == null || player.IsHuman) continue;
            if (player == debugPlayer) continue;
            player.Update(this);
        }
        if (debugPlayer != null && !debugPlayer.IsHuman)
            debugPlayer.Update(this);
    }

    // Realtime driver. Called from AITestScene.Update with deltaSeconds = Time.deltaTime
    // * GameSpeed.
    //
    // Order of operations (each per-tick):
    //  1) Advance WorldTime.
    //  2) Tick per-building resource and worker generation.
    //  3) Advance in-flight workers, spawning queued ones whose SpawnAtTime has elapsed and
    //     resolving any that arrived.
    //  4) Drive each AI's scheduled decision: when WorldTime crosses a player's
    //     NextRealtimeDecisionTime, run its full search and execute the chosen action by
    //     spawning new in-flight workers.
    public void RealtimeTick(float deltaSeconds, float gameSpeed)
    {
        if (deltaSeconds <= 0f) return;

        WorldTime += deltaSeconds;

        // Belt-and-suspenders: if any node ended last tick in the forbidden "owned but no
        // building" state, normalize it back to neutral BEFORE any AI search or production
        // can read that bogus state. ResolveWorkerArrival prevents new occurrences; this
        // catches legacy state and any future regressions.
        EnforceOwnedNodesHaveBuilding();

        TickBuildingProduction(deltaSeconds);
        TickAttackHeatDecay(deltaSeconds);
        AdvanceInFlightWorkers(deltaSeconds, gameSpeed);
        DriveRealtimeAI();
    }

    void TickBuildingProduction(float deltaSeconds)
    {
        bool somethingChanged = false;
        for (int i = 0; i < Nodes.Count; i++)
        {
            var node = Nodes[i];
            if (node.OwnedBy == null) continue;
            var building = node.Building;
            if (building == null) continue;
            var defn = building.Defn;

            if (ResourceProduction.ProducesResources(defn))
            {
                int produced = ResourceProduction.Tick(deltaSeconds, ref building.ResourceProductionAccum, defn, node.NumWorkers);
                if (produced > 0)
                {
                    var goodType = ResourceProduction.GetProducedGoodType(defn);
                    ResourceProduction.CreditInventory(node.Inventory, goodType, produced);
                    somethingChanged = true;
                }
            }

            if (defn.CanGenerateWorkers && defn.SecondsPerWorkerGenerated > 0f)
            {
                building.WorkerGenerationAccum += deltaSeconds;
                while (building.WorkerGenerationAccum >= defn.SecondsPerWorkerGenerated)
                {
                    building.WorkerGenerationAccum -= defn.SecondsPerWorkerGenerated;
                    if (node.NumWorkers < building.MaxWorkers)
                    {
                        node.NumWorkers++;
                        somethingChanged = true;
                    }
                    else if (node.NumWorkers > building.MaxWorkers)
                    {
                        node.NumWorkers--;
                        somethingChanged = true;
                    }
                }
            }
        }
        if (somethingChanged)
            WorldRevision++;
    }

    void AdvanceInFlightWorkers(float deltaSeconds, float gameSpeed)
    {
        if (WorkersInFlight.Count == 0) return;

        bool anyResolved = false;
        for (int i = WorkersInFlight.Count - 1; i >= 0; i--)
        {
            var worker = WorkersInFlight[i];

            // Stagger spawn: until SpawnAtTime arrives, the worker is parked at FromNode and
            // not yet contributing to incoming counts.
            if (!worker.HasSpawned)
            {
                if (WorldTime >= worker.SpawnAtTime)
                {
                    worker.HasSpawned = true;
                    worker.WorldLoc = worker.FromNode.WorldLoc;
                    worker.ToNode.AddIncoming(worker.OwnedBy, +1);
                    OnWorkerSpawned?.Invoke(worker);
                }
                continue;
            }

            bool crossedNode = worker.AdvanceTowardDestination(deltaSeconds, gameSpeed);

            // Resolution rules:
            //  - Final destination reached (Progress >= 1): always resolve at ToNode.
            //  - Crossed onto an intermediate node: resolve there if hostile (not owned by
            //    worker.OwnedBy); otherwise keep walking.
            // The Path itself is fixed at dispatch time and is never mutated here.
            bool reachedFinal = worker.Progress >= 1f;
            NodeData currentNode = worker.Path != null && worker.PathIndex < worker.Path.Count
                ? worker.Path[worker.PathIndex]
                : null;

            bool hostileIntercept = !reachedFinal
                && crossedNode
                && currentNode != null
                && currentNode.OwnedBy != worker.OwnedBy;

            if (reachedFinal || hostileIntercept)
            {
                var resolveAt = reachedFinal ? worker.ToNode : currentNode;
                // Always decrement the original ToNode's incoming bookkeeping; if the worker
                // got intercepted en route, the projection that "this worker is heading to
                // ToNode" is no longer true.
                worker.ToNode.AddIncoming(worker.OwnedBy, -1);
                ResolveWorkerArrival(worker, resolveAt, reachedFinal);
                worker.ArrivedThisTick = true;
                OnWorkerArrived?.Invoke(worker);
                WorkersInFlight.RemoveAt(i);
                anyResolved = true;
            }
        }

        if (anyResolved)
        {
            CleanupResolvedCaptureIntents();
            WorldRevision++;
        }
    }

    // Galcon-style 1:1 resolution. Each worker independently decides what happens based on
    // the current state of the node it stopped at -- which may be the original final
    // destination (reachedFinal=true) or a hostile intermediate node along the path
    // (reachedFinal=false). Building intents only fire at the final destination so an
    // intercepted construct group attacks the intermediate without dropping a building there.
    //
    // INVARIANT: an owned node must always have a Building. We never flip ownership to an
    // arriving player unless the resulting node will satisfy that invariant -- either the
    // node already has a building we inherit on capture, or this worker carries a
    // CaptureAndConstruct intent and is resolving at its final destination (so we can drop
    // its planned building right now). All other "would-be captures" cause the worker to
    // die on impact with no ownership change. The pre-fix bug allowed Attack/Reinforce
    // workers (or CaptureAndConstruct workers intercepted at an intermediate empty neutral)
    // to flip an empty neutral to themselves with no building, leaving a node owned by P
    // with Building=null and a few workers -- a state the rest of the game treats as nonsense
    // (max workers = 0, no production, no upgrade target).
    void ResolveWorkerArrival(WorkerData worker, NodeData dest, bool reachedFinal)
    {
        var arrivingPlayer = worker.OwnedBy;

        // Friendly destination: just merge in (capped soft so we don't overflow MaxWorkers
        // grossly, but realtime allows temporary overflow because that's expected when funneling
        // many groups into a node).
        if (dest.OwnedBy == arrivingPlayer)
        {
            dest.NumWorkers++;
            return;
        }

        bool canConstructOnCapture = reachedFinal
            && worker.Intent == WorkerIntent.CaptureAndConstruct
            && worker.ConstructBuildingIntent != null
            && dest.Building == null;
        bool captureWouldLeaveBuilding = dest.Building != null || canConstructOnCapture;

        // Neutral / unowned destination: fight any unowned garrison 1:1 before claiming. Once
        // defenders are cleared, the next arrival captures (same trade rules as enemy nodes).
        if (dest.OwnedBy == null)
        {
            if (dest.NumWorkers > 0)
            {
                dest.NumWorkers--;
                return;
            }

            if (!captureWouldLeaveBuilding)
            {
                // Worker cannot legally claim this empty neutral (no building to inherit and
                // no construct intent to drop one). Treat as a wasted arrival: worker dies,
                // node stays neutral and empty so a future CaptureAndConstruct can still
                // arrive here without inheriting a forbidden mid-capture state.
                return;
            }

            dest.OwnedBy = arrivingPlayer;
            dest.NumWorkers = 1;
            if (canConstructOnCapture)
            {
                var building = new BuildingData(worker.ConstructBuildingIntent);
                dest.ConstructBuilding(building);
            }
            return;
        }

        // Enemy destination: 1:1 trade. If a defender exists we kill one and the attacker dies.
        // If defenders are zero, ownership flips to the arriving worker.
        // In both branches the defending node took a hostile arrival -- bump its AttackHeat so
        // the owner's AI can recognize this node has been under attack and prioritize defense.
        dest.AttackHeat += AttackHeatPerHostileArrival;
        if (dest.NumWorkers > 0)
        {
            dest.NumWorkers--;
            return;
        }

        // Capturing an empty enemy node: under the invariant the enemy always had a building,
        // which we inherit. Defensive guard: if the node somehow has no building (e.g. legacy
        // bad state being resolved before EnforceOwnedNodesHaveBuilding cleans it up), refuse
        // to capture so we don't propagate the forbidden state to a new owner.
        if (!captureWouldLeaveBuilding)
            return;

        dest.OwnedBy = arrivingPlayer;
        dest.NumWorkers = 1;
    }

    // Repair any node that has slipped into the forbidden "owned but no building" state
    // (legacy data from before ResolveWorkerArrival enforced the invariant, or any future
    // regression). Reverts the node back to neutral with 0 workers so the next capture
    // attempt goes through the proper neutral path (which now requires a building).
    void EnforceOwnedNodesHaveBuilding()
    {
        for (int i = 0; i < Nodes.Count; i++)
        {
            var node = Nodes[i];
            if (node.OwnedBy == null) continue;
            if (node.Building != null) continue;

            Debug.LogWarning(
                $"Node #{node.NodeId} was owned by {node.OwnedBy.Name} with no building -- " +
                "reverting to neutral. This is a game-invariant repair; please report if the " +
                "underlying cause repeats.");
            node.OwnedBy = null;
            node.NumWorkers = 0;
            node.AttackHeat = 0f;
            node.PendingCaptureBy = null;
            node.PendingConstructBuilding = null;
            WorldRevision++;
        }
    }

    // Per-arrival heat added to a defender's AttackHeat. Tuned so a sustained stream of attacks
    // (e.g. 1 attacker/sec) accumulates noticeably faster than the decay below depletes it.
    public const float AttackHeatPerHostileArrival = 1f;

    // Per-second exponential decay rate applied to every node's AttackHeat. 0.5 means heat
    // halves every second; a single arrival yields ~0 heat in ~5s of quiet.
    public const float AttackHeatDecayPerSecond = 0.5f;

    void TickAttackHeatDecay(float deltaSeconds)
    {
        if (deltaSeconds <= 0f) return;
        // Exponential decay: heat *= e^(-k * dt). For small dt this is well approximated by
        // (1 - k*dt) but we use the exact form to stay stable across large GameSpeed multipliers.
        float factor = Mathf.Exp(-AttackHeatDecayPerSecond * deltaSeconds);
        for (int i = 0; i < Nodes.Count; i++)
        {
            var n = Nodes[i];
            if (n.AttackHeat <= 0f) continue;
            n.AttackHeat *= factor;
            if (n.AttackHeat < 0.01f) n.AttackHeat = 0f;
        }
    }

    // After a wave of arrivals, recompute PendingCaptureBy on each node: a node still has a
    // capture intent only as long as it remains neutral AND at least one worker carrying that
    // intent is still in flight to it.
    void CleanupResolvedCaptureIntents()
    {
        for (int i = 0; i < Nodes.Count; i++)
        {
            var node = Nodes[i];
            if (node.PendingCaptureBy == null) continue;
            if (node.OwnedBy != null)
            {
                node.PendingCaptureBy = null;
                node.PendingConstructBuilding = null;
                continue;
            }
            // If no in-flight worker still targets this node from the pending owner, clear.
            bool stillIncoming = false;
            for (int w = 0; w < WorkersInFlight.Count; w++)
            {
                var wf = WorkersInFlight[w];
                if (wf.ToNode == node && wf.OwnedBy == node.PendingCaptureBy)
                {
                    stillIncoming = true;
                    break;
                }
            }
            if (!stillIncoming)
            {
                node.PendingCaptureBy = null;
                node.PendingConstructBuilding = null;
            }
        }
    }

    void DriveRealtimeAI()
    {
        // Process all AI players, debug-viewed player last so its decision record reflects the
        // most recent search when the dump panel reads it. Human players have no AI.
        var debugPlayer = AITestScene.Instance != null ? AITestScene.Instance.DebugPlayerToViewDetailsOn : null;
        for (int i = 0; i < Players.Count; i++)
        {
            var p = Players[i];
            if (p == null || p == debugPlayer) continue;
            if (p.AI == null) continue;
            if (WorldTime >= p.AI.NextRealtimeDecisionTime)
            {
                RunRealtimeDecisionFor(p);
                p.AI.ScheduleNextRealtimeDecision(WorldTime);
            }
        }
        if (debugPlayer != null && debugPlayer.AI != null && WorldTime >= debugPlayer.AI.NextRealtimeDecisionTime)
        {
            RunRealtimeDecisionFor(debugPlayer);
            debugPlayer.AI.ScheduleNextRealtimeDecision(WorldTime);
        }
    }

    void RunRealtimeDecisionFor(PlayerData player)
    {
        // Force a fresh search; in-flight arrivals between scheduled decisions don't bump
        // WorldRevision in a way that's guaranteed to invalidate the per-player cache.
        player.AI.InvalidateDecisionCache();
        player.Update(this);

        var action = player.AI.BestNextActionToTake;
        if (action == null || action.Type == AIActionType.DoNothing)
            return;

        ExecuteRealtimeAction(player, action);
    }

    // Realtime executor: turns an AI-chosen action into in-flight workers (which resolve at
    // their destinations later) by spawning into WorkersInFlight.
    void ExecuteRealtimeAction(PlayerData player, AIAction action)
    {
        switch (action.Type)
        {
            case AIActionType.UpgradeBuilding:
                {
                    // Upgrade is local to the source node: no travel involved, resolve now.
                    var fromNode = action.SourceNode?.RealNode;
                    if (fromNode == null || fromNode.Building == null || fromNode.OwnedBy != player) return;
                    fromNode.Building.Upgrade();
                    fromNode.NumWorkers /= 2;
                    WorldRevision++;
                }
                break;

            case AIActionType.SendWorkersToOwnedNode:
                {
                    var fromNode = action.SourceNode?.RealNode;
                    var toNode = action.DestNode?.RealNode;
                    if (fromNode == null || toNode == null) return;
                    if (fromNode.OwnedBy != player) return;
                    // Game rule: source must retain at least 1 worker (NodeData.GetMaxSendableWorkers).
                    // Workers may have left the source between AI plan time and execute time, so
                    // re-clamp against the live count rather than trusting action.Count alone.
                    int numToSend = Math.Min(action.Count, NodeData.GetMaxSendableWorkers(fromNode.NumWorkers));
                    if (numToSend <= 0) return;
                    SpawnWorkerGroup(player, fromNode, toNode, numToSend, WorkerIntent.Reinforce, null);
                    fromNode.NumWorkers -= numToSend;
                    WorldRevision++;
                }
                break;

            case AIActionType.AttackToNode:
                {
                    var toNode = action.DestNode?.RealNode;
                    if (toNode == null) return;
                    foreach (var kvp in action.AttackFromNodes)
                    {
                        var fromNode = kvp.Key.RealNode;
                        if (fromNode == null) continue;
                        if (fromNode.OwnedBy != player) continue;
                        // Game rule: each source must retain at least 1 worker; an empty source
                        // would itself be captured. Re-clamp against live NumWorkers.
                        int numToSend = Math.Min(kvp.Value, NodeData.GetMaxSendableWorkers(fromNode.NumWorkers));
                        if (numToSend <= 0) continue;
                        SpawnWorkerGroup(player, fromNode, toNode, numToSend, WorkerIntent.Attack, null);
                        fromNode.NumWorkers -= numToSend;
                    }
                    WorldRevision++;
                }
                break;

            case AIActionType.SendMultiSourceWorkersToOwnedNode:
                {
                    var toNode = action.DestNode?.RealNode;
                    if (toNode == null || toNode.OwnedBy != player) return;
                    foreach (var kvp in action.AttackFromNodes)
                    {
                        var fromNode = kvp.Key.RealNode;
                        if (fromNode == null || fromNode.OwnedBy != player) continue;
                        // Game rule: each source must retain at least 1 worker; re-clamp
                        // against live NumWorkers since interior sources may have shifted
                        // between plan time and execute time.
                        int numToSend = Math.Min(kvp.Value, NodeData.GetMaxSendableWorkers(fromNode.NumWorkers));
                        if (numToSend <= 0) continue;
                        SpawnWorkerGroup(player, fromNode, toNode, numToSend, WorkerIntent.Reinforce, null);
                        fromNode.NumWorkers -= numToSend;
                    }
                    WorldRevision++;
                }
                break;

            case AIActionType.ConstructBuildingInEmptyNode:
                {
                    var fromNode = action.SourceNode?.RealNode;
                    var toNode = action.DestNode?.RealNode;
                    if (fromNode == null || toNode == null) return;
                    if (fromNode.OwnedBy != player) return;
                    if (toNode.OwnedBy != null) return;

                    // Game rule: source must retain at least 1 worker (NodeData.GetMaxSendableWorkers).
                    int numToSend = Math.Min(action.Count, NodeData.GetMaxSendableWorkers(fromNode.NumWorkers));
                    if (numToSend <= 0) return;

                    if (toNode.PendingCaptureBy == null)
                    {
                        toNode.PendingCaptureBy = player;
                        toNode.PendingConstructBuilding = action.BuildingToConstruct;
                    }

                    if (action.BuildingToConstruct != null)
                    {
                        foreach (var req in action.BuildingToConstruct.ConstructionRequirements)
                        {
                            int remaining = req.Amount;
                            while (remaining > 0)
                            {
                                var srcNode = getClosestNodeWithResource(player, toNode, req.Good.GoodType);
                                if (srcNode == null) break;
                                int take = Math.Min(remaining, srcNode.Inventory[req.Good.GoodType]);
                                srcNode.Inventory[req.Good.GoodType] -= take;
                                remaining -= take;
                            }
                        }
                    }

                    SpawnWorkerGroup(player, fromNode, toNode, numToSend, WorkerIntent.CaptureAndConstruct, action.BuildingToConstruct);
                    fromNode.NumWorkers -= numToSend;
                    WorldRevision++;
                }
                break;

            case AIActionType.CaptureNeutralResourceNode:
                {
                    var fromNode = action.SourceNode?.RealNode;
                    var toNode = action.DestNode?.RealNode;
                    if (fromNode == null || toNode == null) return;
                    if (fromNode.OwnedBy != player) return;
                    if (toNode.OwnedBy != null) return;
                    if (toNode.PendingCaptureBy != null) return;
                    if (toNode.Building == null || !toNode.Building.Defn.CanBeGatheredFrom) return;

                    // Game rule: source must retain at least 1 worker (NodeData.GetMaxSendableWorkers).
                    int numToSend = Math.Min(action.Count, NodeData.GetMaxSendableWorkers(fromNode.NumWorkers));
                    if (numToSend <= 0) return;

                    toNode.PendingCaptureBy = player;
                    SpawnWorkerGroup(player, fromNode, toNode, numToSend, WorkerIntent.Reinforce, null);
                    fromNode.NumWorkers -= numToSend;
                    WorldRevision++;
                }
                break;

            case AIActionType.CaptureNeutralNode:
                {
                    var toNode = action.DestNode?.RealNode;
                    if (toNode == null || toNode.OwnedBy != null) return;
                    if (toNode.PendingCaptureBy != null) return;
                    if (action.BuildingToConstruct == null) return;

                    toNode.PendingCaptureBy = player;
                    toNode.PendingConstructBuilding = action.BuildingToConstruct;

                    foreach (var req in action.BuildingToConstruct.ConstructionRequirements)
                    {
                        int remaining = req.Amount;
                        while (remaining > 0)
                        {
                            var srcNode = getClosestNodeWithResource(player, toNode, req.Good.GoodType);
                            if (srcNode == null) break;
                            int take = Math.Min(remaining, srcNode.Inventory[req.Good.GoodType]);
                            srcNode.Inventory[req.Good.GoodType] -= take;
                            remaining -= take;
                        }
                    }

                    foreach (var kvp in action.AttackFromNodes)
                    {
                        var fromNode = kvp.Key.RealNode;
                        if (fromNode == null || fromNode.OwnedBy != player) continue;
                        // Game rule: each source must retain at least 1 worker (NodeData.GetMaxSendableWorkers).
                        int numToSend = Math.Min(kvp.Value, NodeData.GetMaxSendableWorkers(fromNode.NumWorkers));
                        if (numToSend <= 0) continue;
                        SpawnWorkerGroup(player, fromNode, toNode, numToSend, WorkerIntent.CaptureAndConstruct, action.BuildingToConstruct);
                        fromNode.NumWorkers -= numToSend;
                    }
                    WorldRevision++;
                }
                break;
        }

        // After dispatching, keep LastActionToTake for the map arrow and zero the live plan.
        player.AI.RememberLastAction(action);
        player.AI.RecordExecutedAction(action, WorldTime);
        player.AI.BestNextActionToTake.SetToNothing();
    }

    void SpawnWorkerGroup(PlayerData player, NodeData fromNode, NodeData toNode, int count, WorkerIntent intent, BuildingDefn buildingIntent)
    {
        var defn = player.WorkerDefn ?? DefaultWorkerDefn;

        // Prefer a path whose INTERMEDIATE nodes are all owned by `player`. Only the final
        // destination may be hostile/neutral. Without this, a Reinforce or capture group can
        // walk through a parallel-but-shorter enemy node and get intercepted there before
        // reaching the planned destination -- the AI's planning logic only considers
        // friendly-chain neighbors when picking sources, so the realtime walker should
        // honor the same constraint. Falls back to unrestricted BFS so disconnected /
        // pinch-out cases still produce a valid path rather than null.
        var path = FindPath(fromNode, toNode, player);
        if (path == null || path.Count < 2)
            path = new List<NodeData> { fromNode, toNode };

        for (int i = 0; i < count; i++)
        {
            float spawnAt = WorldTime + i * WorkerSpawnStaggerSeconds;
            var w = new WorkerData(player, defn, path, intent, buildingIntent, spawnAt);
            WorkersInFlight.Add(w);
        }
    }

    // BFS over NodeConnections returning the list of nodes from 'from' to 'to', inclusive.
    // Connections in this graph are stored uni-directionally (TownData ctor inserts both
    // directions for bidirectional defs), so a forward iteration of NodeConnections is
    // sufficient. Returns null if no path exists.
    public static List<NodeData> FindPath(NodeData from, NodeData to)
    {
        return FindPath(from, to, null);
    }

    // Owner-aware variant: when preferredOwner != null, the BFS first tries to find a path
    // where every INTERMEDIATE node (everything except `from` and `to`) is owned by that
    // player. This matches the AI's planning assumption that workers walk their own
    // territory and only meet hostile ground at the final destination. If no such path
    // exists, we fall back to the unrestricted BFS so behavior never gets worse than the
    // owner-blind version.
    public static List<NodeData> FindPath(NodeData from, NodeData to, PlayerData preferredOwner)
    {
        if (from == null || to == null) return null;
        if (from == to) return new List<NodeData> { from };

        if (preferredOwner != null)
        {
            var preferred = BFSFindPath(from, to, preferredOwner);
            if (preferred != null) return preferred;
        }
        return BFSFindPath(from, to, null);
    }

    static List<NodeData> BFSFindPath(NodeData from, NodeData to, PlayerData requireIntermediateOwner)
    {
        var prev = new Dictionary<NodeData, NodeData>();
        var queue = new Queue<NodeData>();
        prev[from] = null;
        queue.Enqueue(from);

        bool found = false;
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (cur == to) { found = true; break; }
            var conns = cur.NodeConnections;
            for (int i = 0; i < conns.Count; i++)
            {
                var next = conns[i].End;
                if (next == null) continue;
                if (prev.ContainsKey(next)) continue;
                // When restricting intermediates: a candidate next-hop is only acceptable
                // if it's the final destination (where we're allowed to land regardless of
                // ownership) or it's owned by the required player (so it'll pass through
                // without being intercepted). `from` was seeded into prev above so we never
                // re-evaluate it here.
                if (requireIntermediateOwner != null && next != to && next.OwnedBy != requireIntermediateOwner)
                    continue;
                prev[next] = cur;
                queue.Enqueue(next);
            }
        }

        if (!found) return null;

        var path = new List<NodeData>();
        var n = to;
        while (n != null)
        {
            path.Add(n);
            prev.TryGetValue(n, out n);
        }
        path.Reverse();
        return path;
    }

    private NodeData getClosestNodeWithResource(PlayerData player, NodeData startNode, GoodType goodType)
    {
        // for now, just find any node that the player owns and has > 0 of the resource
        foreach (var node in Nodes)
            if (node.OwnedBy == player && node.Inventory[goodType] > 0)
                return node;
        return null;
    }

    // ============================================================================
    // Human-player input surface. The mouse-drag layer in HumanPlayerInput funnels
    // every release-on-valid-dest through these two entry points. They mirror the
    // corresponding ExecuteRealtimeAction switch cases but take NodeData directly so the
    // input layer never needs to fabricate AI_NodeState mirrors.
    // ============================================================================

    public PlayerData GetHumanPlayer()
    {
        for (int i = 0; i < Players.Count; i++)
            if (Players[i] != null && Players[i].IsHuman) return Players[i];
        return null;
    }

    /// Strict owned-intermediate path test for human drag-to-send. Every node BETWEEN
    /// `from` and `to` (exclusive) must be owned by `player`; the destination itself can
    /// be anything (owned, enemy, neutral). Returns false if the only reachable path
    /// would have to cross hostile/neutral ground -- that route would let workers get
    /// intercepted on the way and is therefore not a legal human dispatch.
    public bool HasValidOwnedPath(PlayerData player, NodeData from, NodeData to)
    {
        if (player == null || from == null || to == null || from == to) return false;
        return BFSFindPath(from, to, player) != null;
    }

    /// Send half of `from`'s workers toward `to` as a single Galcon-style group. Intent
    /// is chosen from destination ownership:
    ///   - dest owned by player           -> Reinforce
    ///   - dest owned by an enemy         -> Attack
    ///   - dest neutral, has a building   -> Reinforce (capture-style; sets PendingCaptureBy)
    /// Returns false if no workers were dispatched (path missing, source empty, etc.).
    public bool HumanSendHalfWorkers(PlayerData player, NodeData from, NodeData to)
    {
        if (player == null || from == null || to == null) return false;
        if (from.OwnedBy != player) return false;
        if (!HasValidOwnedPath(player, from, to)) return false;

        int toSend = NodeData.GetHalfSendableWorkers(from.NumWorkers);
        if (toSend <= 0) return false;

        WorkerIntent intent;
        if (to.OwnedBy == player)
        {
            intent = WorkerIntent.Reinforce;
        }
        else if (to.OwnedBy != null)
        {
            intent = WorkerIntent.Attack;
        }
        else
        {
            // Empty neutral here would normally go through HumanConstructBuilding; callers
            // should only route gatherable resource neutrals through this method.
            if (to.Building == null || !to.Building.Defn.CanBeGatheredFrom) return false;
            if (to.PendingCaptureBy != null && to.PendingCaptureBy != player) return false;
            to.PendingCaptureBy = player;
            intent = WorkerIntent.Reinforce;
        }

        SpawnWorkerGroup(player, from, to, toSend, intent, null);
        from.NumWorkers -= toSend;
        WorldRevision++;
        return true;
    }

    /// True iff `player` could legally upgrade the building currently on `node`. Mirrors
    /// the preconditions in HumanUpgradeBuilding so the click-menu can gray the option out
    /// when it would no-op.
    public bool CanHumanUpgrade(PlayerData player, NodeData node)
    {
        if (player == null || node == null) return false;
        if (node.OwnedBy != player) return false;
        if (node.Building == null) return false;
        if (!node.Building.Defn.CanBeUpgraded) return false;
        // Upgrade halves NumWorkers; require at least 2 so the post-halve count is >= 1
        // (matches the AI's UpgradeBuilding precondition of "node has workers to spare").
        if (node.NumWorkers < 2) return false;
        return true;
    }

    /// Upgrade the building on `node` in-place. Mirrors AIActionType.UpgradeBuilding in
    /// ExecuteRealtimeAction -- bumps the building level and halves the standing garrison.
    /// Returns false if preconditions fail.
    public bool HumanUpgradeBuilding(PlayerData player, NodeData node)
    {
        if (!CanHumanUpgrade(player, node)) return false;
        node.Building.Upgrade();
        node.NumWorkers /= 2;
        WorldRevision++;
        return true;
    }

    /// Construct `buildingDefn` on an empty neutral `to` by sending half of `from`'s
    /// workers as a CaptureAndConstruct group. Mirrors AIActionType.ConstructBuildingInEmptyNode.
    ///
    /// Re-dispatch behavior: while a previous wave from the same `player` is still in
    /// flight to `to`, additional calls reuse the existing PendingConstructBuilding (the
    /// player's stored intent for that node) -- they do NOT re-pay the construction cost
    /// and they do NOT re-prompt the building picker. Resources were already drained on
    /// the first commit; if every wave dies before arrival, CleanupResolvedCaptureIntents
    /// clears PendingCaptureBy and the next drag is treated as a fresh commit.
    ///
    /// Returns false if any precondition fails (e.g. someone else owns the capture intent,
    /// the node has already been captured/built on, or there aren't enough workers to send).
    public bool HumanConstructBuilding(PlayerData player, NodeData from, NodeData to, BuildingDefn buildingDefn)
    {
        if (player == null || from == null || to == null || buildingDefn == null) return false;
        if (from.OwnedBy != player) return false;
        if (to.OwnedBy != null) return false;
        if (to.Building != null) return false;
        // Another player committed first; "first one wins" -- we can't ride along.
        if (to.PendingCaptureBy != null && to.PendingCaptureBy != player) return false;
        if (!HasValidOwnedPath(player, from, to)) return false;

        bool firstCommit = to.PendingCaptureBy == null;

        // Affordability and cost are only checked / charged on the first commit. Follow-up
        // waves to my own pending capture have already paid up front; charging again would
        // double-bill the player. If the picker passed a different defn for a follow-up
        // wave, we still ship workers with the originally-committed building so the in-
        // flight intent stays coherent (the second drag won't normally re-ask the picker;
        // see HumanPlayerInput.CompleteDrag for that bypass).
        if (firstCommit)
        {
            if (!PlayerEconomy.CanAfford(player, this, buildingDefn)) return false;
        }

        int toSend = NodeData.GetHalfSendableWorkers(from.NumWorkers);
        if (toSend <= 0) return false;

        if (firstCommit)
        {
            to.PendingCaptureBy = player;
            to.PendingConstructBuilding = buildingDefn;

            // Pay for the building up front by draining the player's inventory in the same
            // order AI construction does (closest-owned-node-first). Without this, free or
            // partially-paid buildings would slip through when affordability changes mid-walk.
            foreach (var req in buildingDefn.ConstructionRequirements)
            {
                int remaining = req.Amount;
                while (remaining > 0)
                {
                    var srcNode = getClosestNodeWithResource(player, to, req.Good.GoodType);
                    if (srcNode == null) break;
                    int take = Math.Min(remaining, srcNode.Inventory[req.Good.GoodType]);
                    srcNode.Inventory[req.Good.GoodType] -= take;
                    remaining -= take;
                }
            }
        }

        // Use the stored intent if this is a follow-up wave; otherwise the freshly-set defn.
        var buildIntent = to.PendingConstructBuilding ?? buildingDefn;

        SpawnWorkerGroup(player, from, to, toSend, WorkerIntent.CaptureAndConstruct, buildIntent);
        from.NumWorkers -= toSend;
        WorldRevision++;
        return true;
    }
}
