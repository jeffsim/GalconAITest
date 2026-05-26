using System.Collections.Generic;
using UnityEngine;

/// Generates reinforcement candidates for owned nodes whose defensive deficit > 0. Single-
/// and multi-source uniformly: if one neighbor can cover the deficit, we emit a single-
/// source send; otherwise we accumulate from BFS neighbors and emit a multi-source send.
///
/// "Defensive deficit" comes from StrategicAnalysis: it accounts for visible enemy
/// neighbor workers, predicted hostile in-flight workers, and decaying AttackHeat.
public class ReinforceGenerator : IActionGenerator
{
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
            if (!analysis.IsOwned[i]) continue;
            int deficit = analysis.DefensiveDeficit[i];
            if (deficit <= 0) continue;

            // Don't waste a reinforcement send if the wave would be smaller than the
            // minimum drip-prevention threshold.
            int neededWave = Mathf.Max(StrategicAnalysis.MinReinforceWave, deficit);

            // Same-region preference (Phase 6): a region is a 2-edge-connected component.
            // We'd rather drain defenders inside the SAME region than pull workers across
            // a bridge from "the other room" -- those cross-bridge defenders are the only
            // thing holding their own region. Try in-region first (both single- and multi-
            // source), and only fall back to cross-bridge when same-region is too dry.
            int targetRegion = target.RegionId;
            var emitted = TryEmitReinforce(
                target, view, analysis, p, ai, deficit, neededWave,
                inRegionOnly: true, targetRegion, sink);
            if (emitted) continue;
            TryEmitReinforce(
                target, view, analysis, p, ai, deficit, neededWave,
                inRegionOnly: false, targetRegion, sink);
        }
    }

    /// Try to emit a reinforce candidate for `target`. When `inRegionOnly` is true, only
    /// sources sharing the target's RegionId are considered; otherwise all owned sources
    /// in the owned component are eligible. Returns true if a candidate was emitted (so
    /// the caller can skip the cross-bridge fallback).
    static bool TryEmitReinforce(
        AI_NodeState target,
        AIWorldView view,
        StrategicAnalysis analysis,
        PersonalityWeights p,
        PlayerAI ai,
        int deficit,
        int neededWave,
        bool inRegionOnly,
        int targetRegion,
        List<AICandidate> sink)
    {
        // Best single source first -- cheapest, shortest travel.
        AI_NodeState bestSingle = null;
        int bestSingleSafe = 0;
        for (int k = 0; k < target.NumNeighbors; k++)
        {
            var src = target.NeighborNodes[k];
            if (src.OwnedBy != view.Player) continue;
            if (inRegionOnly && src.RegionId != targetRegion) continue;
            int safe = analysis.SafeToSendFrom[src.Index];
            if (safe > bestSingleSafe) { bestSingleSafe = safe; bestSingle = src; }
        }

        if (bestSingle != null && bestSingleSafe >= neededWave)
        {
            int send = Mathf.Min(bestSingleSafe, deficit);
            if (send < StrategicAnalysis.MinReinforceWave) return false;
            var c = ai.AcquireCandidate();
            c.Type = AIActionType.SendWorkersToOwnedNode;
            c.SourceNode = bestSingle;
            c.DestNode = target;
            c.Count = send;
            ActionUtility.ScoreReinforce(c, view, analysis, p);
            if (c.Score > 0f) { sink.Add(c); return true; }
            ai.ReleaseCandidate(c);
            return false;
        }

        // No single source enough: accumulate from the owned component, optionally
        // filtered to in-region only. Sort by safe-to-send desc so the largest
        // contributors fill the wave first.
        var sources = CollectOwnedSources(target, view, analysis, ai.OwnedReachability, inRegionOnly, targetRegion);
        if (sources.Count == 0) return false;
        sources.Sort((a, b) => analysis.SafeToSendFrom[b.Index].CompareTo(analysis.SafeToSendFrom[a.Index]));

        var c2 = ai.AcquireCandidate();
        int totalSent = 0;
        foreach (var src in sources)
        {
            if (totalSent >= deficit) break;
            int avail = analysis.SafeToSendFrom[src.Index];
            if (avail <= 0) continue;
            int take = Mathf.Min(avail, deficit - totalSent);
            c2.Sources[src] = take;
            totalSent += take;
        }
        if (totalSent < StrategicAnalysis.MinReinforceWave)
        {
            ai.ReleaseCandidate(c2);
            return false;
        }
        c2.Type = AIActionType.SendMultiSourceWorkersToOwnedNode;
        c2.DestNode = target;
        ActionUtility.ScoreReinforce(c2, view, analysis, p);
        if (c2.Score > 0f) { sink.Add(c2); return true; }
        ai.ReleaseCandidate(c2);
        return false;
    }

    /// <summary>
    /// Collect every other owned source in the target's owned component that has spare
    /// workers. Target is by definition owned, so any owned neighbor lives in the same
    /// component as target; iterating that component skips the per-target BFS that the
    /// old implementation ran. When <paramref name="inRegionOnly"/> is true, only sources
    /// whose RegionId matches <paramref name="targetRegion"/> are returned -- Phase 6's
    /// same-region preference (don't pull defenders across a bridge if same-region has
    /// the wave covered).
    /// </summary>
    static List<AI_NodeState> CollectOwnedSources(
        AI_NodeState target,
        AIWorldView view,
        StrategicAnalysis analysis,
        OwnedReachabilityCache cache,
        bool inRegionOnly,
        int targetRegion)
    {
        var sources = new List<AI_NodeState>();
        int compId = cache.GetComponent(target);
        if (compId < 0) return sources;
        var members = cache.NodesInComponent(compId);
        if (members == null) return sources;
        for (int m = 0; m < members.Count; m++)
        {
            var src = members[m];
            if (src == target) continue;
            if (inRegionOnly && src.RegionId != targetRegion) continue;
            if (analysis.SafeToSendFrom[src.Index] > 0)
                sources.Add(src);
        }
        return sources;
    }
}
