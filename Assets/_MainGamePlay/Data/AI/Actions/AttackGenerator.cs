using System.Collections.Generic;
using UnityEngine;

/// Generates attack candidates against enemy-owned nodes. Single- and multi-source uniformly
/// -- if one adjacent owned node can field enough force, we emit a one-source attack;
/// otherwise we accumulate force from owned BFS neighbors within MaxHops and emit a
/// multi-source attack. Neutrals are out of scope (CaptureGenerator handles them).
///
/// Sizing rules (the part we keep getting wrong if we're not careful):
///   - To CAPTURE an enemy node with N defenders, we must land N+1 attackers alive
///     (combat is 1:1 trade per TownData.ResolveWorkerArrival; after defenders hit 0 the
///     next arrival flips ownership). Sending exactly N reduces them to 0 but does NOT
///     capture -- the last defender-killer is the last attacker too.
///   - Worker-generating defenders REGENERATE during travel. A Barracks at 1/2s regen
///     will replace several defenders by the time a 2-hop wave arrives, so we add an
///     expected-regen term scaled by hop count.
///   - Existing in-flight attackers count toward the requirement (they will trade 1:1
///     when they land), but do NOT subtract them from the threshold itself -- subtract
///     them from the FRESH wave we're sizing.
///   - Anti-dribble: if we cannot muster the full fresh wave, we emit nothing rather
///     than send a tiny follow-up that gets eaten by regen. The previous bug -- sending
///     2 attackers every 4s at a Barracks regenerating 2 workers every 4s -- was a
///     direct consequence of dribbling: each tiny wave was sized against
///     "remaining defenders after pretending the previous wave succeeded", which it
///     hadn't.
public class AttackGenerator : IActionGenerator
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
            if (target.OwnedBy == null || target.OwnedBy == view.Player) continue;
            if (target.AttackAlreadySufficient) continue;
            // Need at least one direct owned neighbor (the entry point into an owned
            // component) to even consider attacking this target.
            if (!HasDirectOwnedNeighbor(target, view.Player)) continue;

            var sources = CollectOwnedSources(target, view, analysis, ai.OwnedReachability, out int minSourceHops);
            if (sources.Count == 0) continue;

            // Total attackers we want to LAND alive at the target (capture-flip + regen,
            // scaled by overkill). The closest source's hop count drives the regen estimate
            // since the wave arrives over a window starting from that nearest source.
            int requiredTotal = AttackSizing.RequiredAttackersToCapture(target, p, minSourceHops);

            // Existing in-flight attackers will trade 1:1 alongside this fresh wave, so we
            // only need to scrape together the SHORTFALL.
            int requiredFresh = Mathf.Max(0, requiredTotal - target.IncomingMyAttackers);
            if (requiredFresh <= 0) continue; // already enough committed; nothing more to do.

            // Sort by SafeToSendFrom descending so we drain the most-available sources first.
            sources.Sort((a, b) => analysis.SafeToSendFrom[b.Index].CompareTo(analysis.SafeToSendFrom[a.Index]));

            var c = ai.AcquireCandidate();
            int totalSent = 0;
            foreach (var src in sources)
            {
                if (totalSent >= requiredFresh) break;
                int avail = analysis.SafeToSendFrom[src.Index];
                if (avail <= 0) continue;
                int take = Mathf.Min(avail, requiredFresh - totalSent);
                c.Sources[src] = take;
                totalSent += take;
            }

            // Anti-dribble: if we couldn't muster the full fresh wave, emit nothing. A
            // partial wave gets eaten by regen and the next tick will repeat the same
            // useless dispatch -- the exact pattern that produced the "Attack 2" loop on
            // an 8-defender Barracks. ScoreAttack will also veto, but bailing here keeps
            // the candidate pool clean.
            if (totalSent < requiredFresh)
            {
                ai.ReleaseCandidate(c);
                continue;
            }

            c.Type = AIActionType.AttackToNode;
            c.DestNode = target;
            ActionUtility.ScoreAttack(c, view, analysis, p);
            if (c.Score > 0f) sink.Add(c);
            else ai.ReleaseCandidate(c);
        }
    }

    static bool HasDirectOwnedNeighbor(AI_NodeState target, PlayerData player)
    {
        // A wave can only relay through owned nodes -- workers crossing an enemy / neutral
        // intermediate get intercepted (see TownData.FindPath owner-aware variant and
        // ResolveWorkerArrival). So a viable attack requires at least one DIRECT owned
        // neighbor as the entry point into an owned component.
        for (int k = 0; k < target.NumNeighbors; k++)
            if (target.NeighborNodes[k].OwnedBy == player) return true;
        return false;
    }

    /// <summary>
    /// Collect every node in the same owned component(s) as one of <paramref name="target"/>'s
    /// direct owned neighbors that currently has spare workers (SafeToSendFrom > 0). The
    /// per-tick <see cref="OwnedReachabilityCache"/> already flood-filled the owned
    /// subgraph; we just iterate the relevant component members. <paramref name="minSourceHops"/>
    /// is the closest contributing source's full-graph hop distance to the target (used by
    /// <see cref="AttackSizing"/> to estimate defender regen during travel).
    /// </summary>
    static List<AI_NodeState> CollectOwnedSources(
        AI_NodeState target,
        AIWorldView view,
        StrategicAnalysis analysis,
        OwnedReachabilityCache cache,
        out int minSourceHops)
    {
        var sources = new List<AI_NodeState>();
        var seenComponents = new HashSet<int>();
        minSourceHops = int.MaxValue;

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
                if (analysis.SafeToSendFrom[src.Index] <= 0) continue;
                sources.Add(src);
                int hops = HopsTargetToSource(target, src);
                if (hops > 0 && hops < minSourceHops) minSourceHops = hops;
            }
        }
        if (minSourceHops == int.MaxValue) minSourceHops = 1;
        return sources;
    }

    static int HopsTargetToSource(AI_NodeState target, AI_NodeState src)
    {
        // DistanceTo is the static all-pairs (full-graph) shortest path matrix populated
        // by MapTopologyAnalysis. For sources inside an owned component the true wave
        // travel time follows the owned chain, which is >= the full-graph distance; using
        // the static distance gives AttackSizing a tight lower bound on travel time, which
        // is exactly what its regen estimate wants.
        if (target.DistanceTo == null || src.Index < 0 || src.Index >= target.DistanceTo.Length)
            return 1;
        int d = target.DistanceTo[src.Index];
        return d == int.MaxValue ? 1 : d;
    }
}
