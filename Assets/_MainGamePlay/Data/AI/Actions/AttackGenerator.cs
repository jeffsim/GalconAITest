using System.Collections.Generic;
using UnityEngine;

/// Generates attack candidates against enemy-owned nodes. Single- and multi-source uniformly
/// -- if one adjacent owned node can field enough force, we emit a one-source attack;
/// otherwise we accumulate force from owned BFS neighbors within MaxHops and emit a
/// multi-source attack. Neutrals are out of scope (CaptureGenerator handles them).
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

            int required = Mathf.CeilToInt(target.NumWorkers * p.AttackOverkill);
            if (required <= 0) required = 1;

            var sources = CollectOwnedSources(target, view, analysis);
            if (sources.Count == 0) continue;

            // Sort by SafeToSendFrom descending so we drain the most-available sources first.
            sources.Sort((a, b) => analysis.SafeToSendFrom[b.Index].CompareTo(analysis.SafeToSendFrom[a.Index]));

            // Greedy allocation.
            var c = ai.AcquireCandidate();
            int totalSent = 0;
            foreach (var src in sources)
            {
                if (totalSent >= required) break;
                int avail = analysis.SafeToSendFrom[src.Index];
                if (avail <= 0) continue;
                int take = Mathf.Min(avail, required - totalSent);
                c.Sources[src] = take;
                totalSent += take;
            }

            if (totalSent < required)
            {
                // Couldn't muster required force. The viability check in ActionUtility.ScoreAttack
                // will zero this out, but we still let the scorer label it for debug output.
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

    static List<AI_NodeState> CollectOwnedSources(AI_NodeState target, AIWorldView view, StrategicAnalysis analysis)
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
        while (frontier.Count > 0)
        {
            var (n, d) = frontier.Dequeue();
            if (d >= MaxHops) continue;
            foreach (var nb in n.NeighborNodes)
            {
                if (!visited.Add(nb.NodeId)) continue;
                if (nb.OwnedBy != view.Player) continue; // can't relay or source from enemy/neutral
                if (analysis.SafeToSendFrom[nb.Index] > 0)
                    sources.Add(nb);
                frontier.Enqueue((nb, d + 1));
            }
        }
        return sources;
    }
}
