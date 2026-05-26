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
            if (!analysis.IsOwned[i]) continue;
            int deficit = analysis.DefensiveDeficit[i];
            if (deficit <= 0) continue;

            // Don't waste a reinforcement send if the wave would be smaller than the
            // minimum drip-prevention threshold.
            int neededWave = Mathf.Max(StrategicAnalysis.MinReinforceWave, deficit);

            // Try single-source first (preferred -- shorter travel, cleaner intent).
            AI_NodeState bestSingle = null;
            int bestSingleSafe = 0;
            for (int k = 0; k < target.NumNeighbors; k++)
            {
                var src = target.NeighborNodes[k];
                if (src.OwnedBy != view.Player) continue;
                int safe = analysis.SafeToSendFrom[src.Index];
                if (safe > bestSingleSafe)
                {
                    bestSingleSafe = safe;
                    bestSingle = src;
                }
            }

            if (bestSingle != null && bestSingleSafe >= neededWave)
            {
                int send = Mathf.Min(bestSingleSafe, deficit);
                if (send < StrategicAnalysis.MinReinforceWave) continue;
                var c = ai.AcquireCandidate();
                c.Type = AIActionType.SendWorkersToOwnedNode;
                c.SourceNode = bestSingle;
                c.DestNode = target;
                c.Count = send;
                ActionUtility.ScoreReinforce(c, view, analysis, p);
                if (c.Score > 0f) sink.Add(c); else ai.ReleaseCandidate(c);
                continue;
            }

            // No single source enough: accumulate from BFS.
            var sources = CollectOwnedSources(target, view, analysis);
            if (sources.Count == 0) continue;
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
                continue;
            }
            c2.Type = AIActionType.SendMultiSourceWorkersToOwnedNode;
            c2.DestNode = target;
            ActionUtility.ScoreReinforce(c2, view, analysis, p);
            if (c2.Score > 0f) sink.Add(c2); else ai.ReleaseCandidate(c2);
        }
    }

    static List<AI_NodeState> CollectOwnedSources(AI_NodeState target, AIWorldView view, StrategicAnalysis analysis)
    {
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
                if (nb.OwnedBy == view.Player && analysis.SafeToSendFrom[nb.Index] > 0)
                    sources.Add(nb);
                frontier.Enqueue((nb, d + 1));
            }
        }
        return sources;
    }
}
