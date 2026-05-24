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
    // by RealtimeTick. Empty in step mode.
    [HideInInspector] public List<WorkerData> WorkersInFlight = new();

    // Default WorkerDefn used when an AI dispatches workers in realtime mode. The scene seeds
    // this from the field set on AITestScene; PlayerData also keeps a per-player one which we
    // use first if present.
    [HideInInspector] public WorkerDefn DefaultWorkerDefn;

    public Action<int> OnAIDebuggerUpdate { get; internal set; }
    public Action<WorkerData> OnWorkerSpawned;
    public Action<WorkerData> OnWorkerArrived;
    public int TestOnePlayerId = 0;

    // Bumped any time the world state changes in a way the AI cares about (currently: each
    // Debug_WorldTurn). PlayerAI caches its decision against this and skips the full depth-7
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
        if (TestOnePlayerId == 0)
        {
            //hack: process current player last so that it RootEntry is valid for debuggerpanel
            var debugPlayer = AITestScene.Instance.DebugPlayerToViewDetailsOn;
            foreach (var player in Players)
                if (player != debugPlayer)
                    player?.Update(this);
            debugPlayer?.Update(this);
        }
        else
        {
            // or test just one player:
            Players[TestOnePlayerId].Update(this);
        }
    }

    // Realtime mode driver. Called from AITestScene.Update with deltaSeconds = Time.deltaTime
    // * GameSpeed. Step mode never calls this; that path stays exclusively driven by
    // OnStepClicked -> Debug_WorldTurn.
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

        TickBuildingProduction(deltaSeconds);
        AdvanceInFlightWorkers(deltaSeconds, gameSpeed);
        DriveRealtimeAI();
    }

    void TickBuildingProduction(float deltaSeconds)
    {
        bool somethingChanged = false;
        for (int i = 0; i < Nodes.Count; i++)
        {
            var node = Nodes[i];
            var building = node.Building;
            if (building == null) continue;
            var defn = building.Defn;

            // Resources (Forest -> Wood, etc). Threshold-driven so each unit produced is a
            // discrete inventory bump that the AI evaluator can react to. Using SecondsPerX
            // with carry-over preserves the configured rate even at variable framerates.
            if (defn.CanGatherResources && defn.SecondsPerResourceProduced > 0f)
            {
                building.ResourceProductionAccum += deltaSeconds;
                while (building.ResourceProductionAccum >= defn.SecondsPerResourceProduced)
                {
                    building.ResourceProductionAccum -= defn.SecondsPerResourceProduced;
                    var goodType = defn.ResourceThisNodeCanGoGather.GoodType;
                    if (!node.Inventory.ContainsKey(goodType))
                        node.Inventory[goodType] = 0;
                    node.Inventory[goodType] += 1;
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

        // Neutral / unowned destination: first arriving worker captures it. If the worker came
        // with a CaptureAndConstruct intent AND this is the originally-targeted destination,
        // place the building now.
        if (dest.OwnedBy == null)
        {
            dest.OwnedBy = arrivingPlayer;
            dest.NumWorkers = 1;
            if (reachedFinal
                && worker.Intent == WorkerIntent.CaptureAndConstruct
                && worker.ConstructBuildingIntent != null
                && dest.Building == null)
            {
                var building = new BuildingData(worker.ConstructBuildingIntent);
                dest.ConstructBuilding(building);
            }
            return;
        }

        // Enemy destination: 1:1 trade. If a defender exists we kill one and the attacker dies.
        // If defenders are zero, ownership flips to the arriving worker.
        if (dest.NumWorkers > 0)
        {
            dest.NumWorkers--;
            return;
        }

        dest.OwnedBy = arrivingPlayer;
        dest.NumWorkers = 1;
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
        if (TestOnePlayerId != 0)
        {
            // Test mode: just one player; ignore scheduling.
            var p = Players[TestOnePlayerId];
            if (p?.AI != null && WorldTime >= p.AI.NextRealtimeDecisionTime)
            {
                RunRealtimeDecisionFor(p);
                p.AI.ScheduleNextRealtimeDecision(WorldTime);
            }
            return;
        }

        // Process all players, debug-viewed player last so its DebugRootEntry is the most
        // recent one for the panel (mirrors the Update() ordering).
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
    // their destinations later) instead of resolving instantly the way Debug_WorldTurn does.
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
                    int numToSend = Math.Min(action.Count, fromNode.NumWorkers);
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
                        int numToSend = Math.Min(kvp.Value, fromNode.NumWorkers);
                        if (numToSend <= 0) continue;
                        SpawnWorkerGroup(player, fromNode, toNode, numToSend, WorkerIntent.Attack, null);
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

                    int numToSend = Math.Min(action.Count, fromNode.NumWorkers);
                    if (numToSend <= 0) return;

                    // Mark the target as "I'm capturing this neutral with a building intent" so
                    // (a) other AIs see it as taken via AI mirror's PendingCaptureBy hook, and
                    // (b) WE don't immediately re-issue a Construct on the same node next tick.
                    if (toNode.PendingCaptureBy == null)
                    {
                        toNode.PendingCaptureBy = player;
                        toNode.PendingConstructBuilding = action.BuildingToConstruct;
                    }

                    // Pre-consume the construction resources from the player's town inventory
                    // (greedy, like the step-mode path). For now we deliberately don't gate on
                    // transport -- the prompt explicitly excludes that.
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
        }

        // After dispatching, keep LastActionToTake for the map arrow and zero the live plan.
        player.AI.RememberLastAction(action);
        player.AI.BestNextActionToTake.SetToNothing();
    }

    void SpawnWorkerGroup(PlayerData player, NodeData fromNode, NodeData toNode, int count, WorkerIntent intent, BuildingDefn buildingIntent)
    {
        var defn = player.WorkerDefn ?? DefaultWorkerDefn;

        // Compute the multi-hop path along NodeConnections once; every worker in the group
        // shares the same List reference so they all walk the same route and we don't pay
        // BFS-per-worker. If there is no graph path (disconnected), fall back to a direct
        // two-node "path" so behavior degrades to as-the-crow-flies rather than crashing.
        var path = FindPath(fromNode, toNode);
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
        if (from == null || to == null) return null;
        if (from == to) return new List<NodeData> { from };

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

    internal void Debug_WorldTurn()
    {
        // Update resource gathering nodes
        foreach (var node in Nodes)
        {
            if (node.Building == null) continue;

            if (node.Building.Defn.CanGatherResources)
            {
                // TODO: assume a resource node is nearby and not depleted
                if (node.Inventory.ContainsKey(node.Building.Defn.ResourceThisNodeCanGoGather.GoodType))
                    node.Inventory[node.Building.Defn.ResourceThisNodeCanGoGather.GoodType] += node.Building.Defn.ResourceProducedPerTurn;
            }
            if (node.Building.Defn.CanGenerateWorkers)
            {
                if (node.NumWorkers < node.Building.MaxWorkers)
                    node.NumWorkers = Math.Min(node.Building.MaxWorkers, node.NumWorkers + node.Building.WorkersGeneratedPerTurn);
                else if (node.NumWorkers > node.Building.MaxWorkers)
                    node.NumWorkers--;
            }
        }

        // not how this will normally be done, but fine for testing purposes
        foreach (var player in Players)
        {
            if (player == null) continue;
            var moveToMake = player.AI.BestNextActionToTake;
            if (moveToMake == null || moveToMake.Type == AIActionType.DoNothing) continue; // wasn't updated

            player.AI.RememberLastAction(moveToMake);

            // Convert from ai node data to real node data
            var fromNode = moveToMake.SourceNode?.RealNode;
            var toNode = moveToMake.DestNode?.RealNode;
            switch (moveToMake.Type)
            {
                case AIActionType.UpgradeBuilding:
                    fromNode.Building.Upgrade();
                    fromNode.NumWorkers /= 2;
                    break;

                case AIActionType.AttackToNode:
                    {
                        var attackFromNodes = moveToMake.AttackFromNodes;

                        // List of attack results for each attack
                        var attackResults = moveToMake.AttackResults;

                        foreach (var attackFromNode in attackFromNodes.Keys)
                        {
                            var sourceNode = attackFromNode.RealNode;
                            var numSent = attackFromNodes[attackFromNode];
                            // var attackResult = attackResults[i++];

                            // // Subtract units sent from the source node
                            // sourceNode.NumWorkers -= numSent;

                            // // Perform the attack on the destination node based on the attack result
                            // switch (attackResult)
                            // {
                            //     case AttackResult.AttackerWon:
                            //         // If the attacker won, the destination node becomes owned by the attacker
                            //         toNode.OwnedBy = sourceNode.OwnedBy;
                            //         // The remaining workers are the attackers that survived
                            //         toNode.NumWorkers = numSent - Math.Max(0, toNode.NumWorkers);
                            //         break;

                            //     case AttackResult.DefenderWon:
                            //         // If the defender won, reduce the destination node's workers by the number of attackers
                            //         toNode.NumWorkers -= numSent;
                            //         break;

                            //     case AttackResult.BothSidesDied:
                            //         // If both sides died, the destination node becomes neutral
                            //         toNode.OwnedBy = null;
                            //         toNode.NumWorkers = 0;
                            //         break;
                            // }

                            sourceNode.NumWorkers -= numSent;
                            toNode.NumWorkers -= numSent;

                            // Only enemy-owned nodes can be taken by force. Neutral territory
                            // requires constructing a building (ConstructBuildingInEmptyNode).
                            if (toNode.NumWorkers <= 0 && toNode.OwnedBy != null)
                            {
                                toNode.OwnedBy = player;
                                toNode.NumWorkers = -toNode.NumWorkers;
                            }
                        }
                    }
                    break;
                case AIActionType.ConstructBuildingInEmptyNode:
                    // First verify that the action is still valid; e.g. another player hasn't captured the target node, the source node still has workers and is owned by player, etc

                    // Can player still send enough workers from source node?
                    if (fromNode.NumWorkers < moveToMake.Count || fromNode.OwnedBy != player) continue;

                    // Is target node still capturable?
                    if (toNode.OwnedBy != null) continue;

                    // Does player still have the necessary resources to build the building?
                    // TODO: Assume so for now

                    // Construct the building, move workers, etc
                    fromNode.NumWorkers -= moveToMake.Count;
                    toNode.OwnedBy = player;
                    toNode.NumWorkers = moveToMake.Count;

                    var building = new BuildingData(moveToMake.BuildingToConstruct);
                    toNode.ConstructBuilding(building);

                    // consume resources needed to construct the building
                    foreach (var req in moveToMake.BuildingToConstruct.ConstructionRequirements)
                    {
                        // hack
                        var remainingNeeded = req.Amount;
                        while (remainingNeeded > 0)
                        {
                            var node = getClosestNodeWithResource(player, toNode, req.Good.GoodType);
                            if (node == null)
                            {
                                // shouldn't get here; should get caught by necessary-resource validationa bove
                                Debug.LogError("Error: couldn't find node with resource " + req.Good.GoodType);
                                break;
                            }
                            var amountToTake = Math.Min(remainingNeeded, node.Inventory[req.Good.GoodType]);
                            node.Inventory[req.Good.GoodType] -= amountToTake;
                            remainingNeeded -= amountToTake;
                        }
                    }

                    break;

                case AIActionType.SendWorkersToOwnedNode:

                    // Can player still send enough workers from source node?
                    if (fromNode.NumWorkers < moveToMake.Count || fromNode.OwnedBy != player) continue;

                    fromNode.NumWorkers -= moveToMake.Count;
                    toNode.NumWorkers += moveToMake.Count;
                    break;
            }
        }

        // World mutated; invalidate per-player AI decision caches so the next Update re-searches.
        WorldRevision++;
    }

    private NodeData getClosestNodeWithResource(PlayerData player, NodeData startNode, GoodType goodType)
    {
        // for now, just find any node that the player owns and has > 0 of the resource
        foreach (var node in Nodes)
            if (node.OwnedBy == player && node.Inventory[goodType] > 0)
                return node;
        return null;
    }
}
