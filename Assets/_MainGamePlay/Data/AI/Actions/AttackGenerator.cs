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
    /// BFS source-collection cap. Beyond this many hops the wave would arrive too far
    /// apart in time to be considered coordinated.
    const int MaxHops = 2;

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
            // Need at least one of our nodes adjacent (or within MaxHops) to even consider.
            if (!HasOwnedNeighborWithinHops(target, view.Player, MaxHops)) continue;

            var sources = CollectOwnedSources(target, view, analysis, out int minSourceHops);
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

    static bool HasOwnedNeighborWithinHops(AI_NodeState target, PlayerData player, int hops)
    {
        // The viable-attack precondition is "is there an owned node we can route a wave from
        // through friendly territory?". A wave can only RELAY through owned nodes -- workers
        // passing through enemy / neutral intermediates get intercepted (see TownData.FindPath
        // owner-aware variant and ResolveWorkerArrival). So the answer is simply: does the
        // enemy target have at least one direct neighbor owned by `player`?
        //
        // The `hops` parameter is retained for compatibility but is implicit: a 2-hop owned
        // source is reachable iff the depth-1 relay is owned, and we check that here.
        for (int k = 0; k < target.NumNeighbors; k++)
            if (target.NeighborNodes[k].OwnedBy == player) return true;
        return false;
    }

    static List<AI_NodeState> CollectOwnedSources(
        AI_NodeState target,
        AIWorldView view,
        StrategicAnalysis analysis,
        out int minSourceHops)
    {
        // BFS outward from the target, but ONLY relay through nodes owned by view.Player.
        // The target itself is enemy-owned (that's the whole point); its direct neighbors are
        // candidate first-hop owned relays/sources; from each owned relay we can step further
        // out through OWNED chain only. Stopping the BFS at non-owned nodes is what makes
        // "owned path required" actually true -- without it the generator happily proposes
        // a 2-hop wave that physically has to cross enemy territory and would get killed.
        var sources = new List<AI_NodeState>();
        var visited = new HashSet<int> { target.NodeId };
        var frontier = new Queue<(AI_NodeState node, int depth)>();
        frontier.Enqueue((target, 0));
        minSourceHops = int.MaxValue;
        while (frontier.Count > 0)
        {
            var (n, d) = frontier.Dequeue();
            if (d >= MaxHops) continue;
            foreach (var nb in n.NeighborNodes)
            {
                if (!visited.Add(nb.NodeId)) continue;
                if (nb.OwnedBy != view.Player) continue; // can't relay or source from enemy/neutral
                int hopsFromTarget = d + 1;
                if (analysis.SafeToSendFrom[nb.Index] > 0)
                {
                    sources.Add(nb);
                    if (hopsFromTarget < minSourceHops) minSourceHops = hopsFromTarget;
                }
                frontier.Enqueue((nb, hopsFromTarget));
            }
        }
        if (minSourceHops == int.MaxValue) minSourceHops = 1; // no source found; harmless fallback
        return sources;
    }
}
