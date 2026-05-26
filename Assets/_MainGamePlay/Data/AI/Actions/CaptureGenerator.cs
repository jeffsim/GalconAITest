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
            if (!HasDirectOwnedNeighbor(target, view.Player)) continue;

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

        // Capture-flip rule (TownData.ResolveWorkerArrival neutral branch): each attacker
        // trades 1:1 with the neutral garrison, then the NEXT arrival captures. So we need
        // (defenders + 1) attackers to LAND, scaled by AttackOverkill. Neutrals don't
        // generate workers, so no travel regen term is needed here.
        int defenderGarrison = target.NumWorkers;
        int required = Mathf.CeilToInt((defenderGarrison + 1) * p.AttackOverkill);
        int send = Mathf.Clamp(required, StrategicAnalysis.MinCaptureWave, bestSafe);
        // Refuse to dribble: if even the largest single source can't muster the full
        // capture-flip wave, skip rather than send a doomed partial attack.
        if (send < required) return;

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

        // Determine force we can muster from the owned components reachable via direct
        // owned neighbors of this neutral target.
        var sources = CollectOwnedSources(target, view, analysis, ai.OwnedReachability);
        if (sources.Count == 0) return;
        sources.Sort((a, b) => analysis.SafeToSendFrom[b.Index].CompareTo(analysis.SafeToSendFrom[a.Index]));

        // Capture-flip: (defenders + 1) attackers landed, scaled by overkill. Empty
        // neutrals usually have 0 defenders, but un-built neutrals occasionally carry a
        // wild garrison.
        int defenderGarrison = target.NumWorkers;
        int required = Mathf.CeilToInt((defenderGarrison + 1) * p.AttackOverkill);
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
            if (!MapTopologyAnalysis.HasMatchingAdjacentResource(target, bd)) continue;
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

    static bool HasDirectOwnedNeighbor(AI_NodeState target, PlayerData player)
    {
        // A viable capture requires a DIRECT owned neighbor as the entry point into one
        // of this player's owned components. Workers physically route source -> friendly
        // chain -> neutral landing site; any non-owned intermediate would intercept the
        // wave (see TownData.FindPath owner-aware variant + ResolveWorkerArrival).
        for (int k = 0; k < target.NumNeighbors; k++)
            if (target.NeighborNodes[k].OwnedBy == player) return true;
        return false;
    }

    /// <summary>
    /// Collect every owned source that has spare workers, restricted to nodes inside an
    /// owned component reachable through one of <paramref name="target"/>'s direct owned
    /// neighbors. Replaces the per-target owned-only BFS with an O(component) lookup
    /// against the per-tick <see cref="OwnedReachabilityCache"/>.
    /// </summary>
    static List<AI_NodeState> CollectOwnedSources(
        AI_NodeState target,
        AIWorldView view,
        StrategicAnalysis analysis,
        OwnedReachabilityCache cache)
    {
        var sources = new List<AI_NodeState>();
        var seenComponents = new HashSet<int>();
        for (int k = 0; k < target.NumNeighbors; k++)
        {
            var entry = target.NeighborNodes[k];
            if (entry.OwnedBy != view.Player) continue;
            int compId = cache.GetComponent(entry);
            if (compId < 0 || !seenComponents.Add(compId)) continue;
            var members = cache.NodesInComponent(compId);
            if (members == null) continue;
            for (int m = 0; m < members.Count; m++)
            {
                var src = members[m];
                if (analysis.SafeToSendFrom[src.Index] > 0)
                    sources.Add(src);
            }
        }
        return sources;
    }
}
