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
        // BFS frontier; node set kept small with a HashSet of NodeId.
        var visited = new HashSet<int> { target.NodeId };
        var frontier = new Queue<(AI_NodeState node, int depth)>();
        frontier.Enqueue((target, 0));
        while (frontier.Count > 0)
        {
            var (n, d) = frontier.Dequeue();
            if (d >= hops) continue;
            foreach (var nb in n.NeighborNodes)
            {
                if (!visited.Add(nb.NodeId)) continue;
                if (nb.OwnedBy == player) return true;
                frontier.Enqueue((nb, d + 1));
            }
        }
        return false;
    }

    static List<AI_NodeState> CollectOwnedSources(AI_NodeState target, AIWorldView view, StrategicAnalysis analysis)
    {
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
                if (nb.OwnedBy == view.Player && analysis.SafeToSendFrom[nb.Index] > 0)
                    sources.Add(nb);
                // Continue BFS even past non-owned nodes (allows a coordinated 2-hop wave through
                // a friendly relay). We don't pathfind here -- the realtime executor handles
                // path planning. We only need force.
                frontier.Enqueue((nb, d + 1));
            }
        }
        return sources;
    }
}
