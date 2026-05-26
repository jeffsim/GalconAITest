using System.Collections.Generic;
using UnityEngine;

/// Generates capture candidates against neutral nodes. Two flavors emerge naturally
/// depending on what the target looks like:
///
///   Target has a Forest/StoneMine building (CanBeGatheredFrom):
///     Emit AIActionType.CaptureNeutralResourceNode (single-source).
///
///   Target is an empty neutral (HasBuilding == false):
///     Emit AIActionType.CaptureNeutralNode with the highest-scoring building this
///     player can afford and build, and however many sources are needed to overcome any
///     existing neutral garrison.
///
/// We never emit a candidate for a HasBuilding-but-not-gatherable neutral; the game has
/// no such case today (every neutral building is either Forest or StoneMine).
public class CaptureGenerator : IActionGenerator
{
    const int MaxHops = 2;
    /// Per-target, only consider the top N candidate buildings (avoid emitting one
    /// candidate per buildable type per neutral neighbor).
    const int MaxBuildingsPerTarget = 2;

    public void Generate(
        AIWorldView view,
        StrategicAnalysis analysis,
        PersonalityWeights p,
        PlayerAI ai,
        List<AICandidate> sink)
    {
        for (int i = 0; i < view.NumNodes; i++)
        {
            var target = view.Nodes[i];
            if (target.OwnedBy != null) continue;
            // PendingMyCapture: a capture wave from this player is already in flight at this
            // node; emitting another would double-dispatch (and the second one's intent might
            // not be CaptureAndConstruct so its workers would die on arrival).
            if (target.PendingMyCapture) continue;
            if (!HasOwnedNeighborWithinHops(target, view.Player, MaxHops)) continue;

            if (target.HasBuilding && target.CanBeGatheredFrom)
                GenerateResourceCapture(target, view, analysis, p, ai, sink);
            else if (!target.HasBuilding)
                GenerateEmptyCapture(target, view, analysis, p, ai, sink);
            // else: HasBuilding && !CanBeGatheredFrom -- no game case today, skip silently.
        }
    }

    void GenerateResourceCapture(
        AI_NodeState target,
        AIWorldView view,
        StrategicAnalysis analysis,
        PersonalityWeights p,
        PlayerAI ai,
        List<AICandidate> sink)
    {
        // Resource captures are simple: pick the adjacent owned node with the most safe-to-send,
        // and send Min(safe, defender + overkill) workers. We don't multi-source these because
        // resource nodes are tiny -- one wave from one node is enough.
        AI_NodeState bestSource = null;
        int bestSafe = 0;
        for (int k = 0; k < target.NumNeighbors; k++)
        {
            var src = target.NeighborNodes[k];
            if (src.OwnedBy != view.Player) continue;
            int safe = analysis.SafeToSendFrom[src.Index];
            if (safe > bestSafe)
            {
                bestSafe = safe;
                bestSource = src;
            }
        }
        if (bestSource == null || bestSafe < StrategicAnalysis.MinCaptureWave) return;

        int defenderGarrison = target.NumWorkers;
        int required = Mathf.CeilToInt(Mathf.Max(1, defenderGarrison) * p.AttackOverkill);
        int send = Mathf.Clamp(required, StrategicAnalysis.MinCaptureWave, bestSafe);

        var c = ai.AcquireCandidate();
        c.Type = AIActionType.CaptureNeutralResourceNode;
        c.SourceNode = bestSource;
        c.DestNode = target;
        c.Count = send;
        ActionUtility.ScoreCapture(c, view, analysis, p);
        if (c.Score > 0f) sink.Add(c); else ai.ReleaseCandidate(c);
    }

    void GenerateEmptyCapture(
        AI_NodeState target,
        AIWorldView view,
        StrategicAnalysis analysis,
        PersonalityWeights p,
        PlayerAI ai,
        List<AICandidate> sink)
    {
        // Pick the top-N buildings we can afford AND that make sense to construct here.
        // The terrain-adjacency check (gatherer building needs a matching resource neighbor)
        // is applied PER target, so we run SelectTopBuildings with a per-target filter.
        var topBuildings = SelectTopBuildings(view, analysis, target);
        if (topBuildings.Count == 0) return;

        // Determine force we can muster from BFS sources.
        var sources = CollectOwnedSources(target, view, analysis);
        if (sources.Count == 0) return;
        sources.Sort((a, b) => analysis.SafeToSendFrom[b.Index].CompareTo(analysis.SafeToSendFrom[a.Index]));

        int defenderGarrison = target.NumWorkers; // usually 0 for an empty neutral, but neutrals can have wild garrisons.
        int required = Mathf.CeilToInt(Mathf.Max(1, defenderGarrison) * p.AttackOverkill);
        if (required < StrategicAnalysis.MinCaptureWave) required = StrategicAnalysis.MinCaptureWave;

        // Reusable allocation: prefer single-source when possible; otherwise multi.
        var allocation = AllocateForce(sources, analysis, required);
        if (allocation.totalSent < required) return;

        // Emit one candidate per top building. Each is scored independently.
        foreach (var building in topBuildings)
        {
            if (!CanAfford(building, view)) continue;
            var c = ai.AcquireCandidate();
            c.Type = AIActionType.CaptureNeutralNode;
            c.DestNode = target;
            c.BuildingToConstruct = building;
            foreach (var kv in allocation.alloc)
                c.Sources[kv.Key] = kv.Value;
            ActionUtility.ScoreCapture(c, view, analysis, p);
            if (c.Score > 0f) sink.Add(c); else ai.ReleaseCandidate(c);
        }
    }

    static (Dictionary<AI_NodeState, int> alloc, int totalSent) AllocateForce(
        List<AI_NodeState> sources,
        StrategicAnalysis analysis,
        int required)
    {
        var alloc = new Dictionary<AI_NodeState, int>();
        int total = 0;
        foreach (var src in sources)
        {
            if (total >= required) break;
            int avail = analysis.SafeToSendFrom[src.Index];
            if (avail <= 0) continue;
            int take = Mathf.Min(avail, required - total);
            alloc[src] = take;
            total += take;
        }
        return (alloc, total);
    }

    static bool CanAfford(BuildingDefn defn, AIWorldView view)
    {
        if (defn.ConstructionRequirements == null) return true;
        foreach (var req in defn.ConstructionRequirements)
            if (view.GetInventory(req.Good.GoodType) < req.Amount) return false;
        return true;
    }

    static List<BuildingDefn> SelectTopBuildings(AIWorldView view, StrategicAnalysis analysis, AI_NodeState target)
    {
        // Score each buildable defn by a rough capture-context heuristic; pick top N.
        // Gatherer buildings are filtered out if `target` has no matching adjacent resource.
        var result = new List<BuildingDefn>();
        if (GameDefns.Instance == null) return result;

        var scores = new List<(BuildingDefn defn, float score)>();
        foreach (var bd in GameDefns.Instance.BuildingDefns.Values)
        {
            if (!bd.CanBeBuiltByPlayer) continue;
            if (!CanAfford(bd, view)) continue;
            if (!HasMatchingAdjacentResource(target, bd)) continue;
            float s = 1f;
            if (analysis.IsBuildingTypeMissing(bd.BuildingType)) s += 3f;
            if (bd.CanGenerateWorkers) s += 2f;
            if (bd.CanGatherResources) s += 1f;
            scores.Add((bd, s));
        }
        scores.Sort((a, b) => b.score.CompareTo(a.score));
        for (int i = 0; i < scores.Count && i < MaxBuildingsPerTarget; i++)
            result.Add(scores[i].defn);
        return result;
    }

    static bool HasOwnedNeighborWithinHops(AI_NodeState target, PlayerData player, int hops)
    {
        // See AttackGenerator.HasOwnedNeighborWithinHops for the rationale: with owned-only
        // relays required, the answer is simply "does this neutral have a direct owned
        // neighbor?". The `hops` parameter is retained for symmetry only.
        for (int k = 0; k < target.NumNeighbors; k++)
            if (target.NeighborNodes[k].OwnedBy == player) return true;
        return false;
    }

    static List<AI_NodeState> CollectOwnedSources(AI_NodeState target, AIWorldView view, StrategicAnalysis analysis)
    {
        // Owned-only BFS outward from the neutral target. The wave physically routes from
        // each source -> friendly chain -> neutral landing site, so any non-owned node mid-
        // path would intercept the wave (workers die at the wrong neutral, see
        // ResolveWorkerArrival). Relaying through owned only matches the executor's actual
        // pathfinding (TownData.FindPath with preferredOwner = view.Player).
        var sources = new List<AI_NodeState>();
        var visited = new HashSet<int> { target.NodeId };
        var frontier = new Queue<(AI_NodeState n, int d)>();
        frontier.Enqueue((target, 0));
        while (frontier.Count > 0)
        {
            var (n, d) = frontier.Dequeue();
            if (d >= MaxHops) continue;
            foreach (var nb in n.NeighborNodes)
            {
                if (!visited.Add(nb.NodeId)) continue;
                if (nb.OwnedBy != view.Player) continue;
                if (analysis.SafeToSendFrom[nb.Index] > 0)
                    sources.Add(nb);
                frontier.Enqueue((nb, d + 1));
            }
        }
        return sources;
    }

    /// True iff this gatherer building would have at least one adjacent resource node that
    /// produces its required good. A StoneMiner with no adjacent Stone deposit produces
    /// nothing and is purely an economy waste, so we refuse to emit such proposals.
    static bool HasMatchingAdjacentResource(AI_NodeState target, BuildingDefn bd)
    {
        if (!bd.CanGatherResources || bd.ResourceThisNodeCanGoGather == null) return true;
        var needed = bd.ResourceThisNodeCanGoGather.GoodType;
        for (int k = 0; k < target.NumNeighbors; k++)
        {
            var nb = target.NeighborNodes[k];
            if (nb.CanBeGatheredFrom && nb.ResourceGatheredFromThisNode == needed)
                return true;
        }
        return false;
    }
}
