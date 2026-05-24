using System.Collections.Generic;

/// <summary>
/// Static (one-time at map load) chokepoint scoring. Identifies the nodes that lie on the
/// shortest paths between starting camps and rates each in [0, 1]. A node with score ~1
/// sits on essentially every inter-camp shortest path; a score of 0 means the node is
/// off the inter-camp routes entirely.
///
/// Why not just node degree? Degree alone confuses "highly connected" with "structurally
/// critical". A degree-4 dead-end pocket can have lots of edges and still be irrelevant
/// to who-controls-the-map. Inter-camp betweenness centrality captures the "everyone has
/// to walk through here to attack everyone else" intuition that degree misses.
///
/// Computed once in TownData ctor; mirrored onto AI_NodeState in InitializeStaticData.
/// AI heuristics multiply Capture / Attack / Buttress scores by (1 + score * scale) so
/// chokepoints become high-priority targets to take, defend, and attack.
/// </summary>
public static class ChokepointAnalysis
{
    public static void Compute(TownData townData)
    {
        var nodes = townData.Nodes;
        int n = nodes.Count;
        for (int i = 0; i < n; i++)
            nodes[i].ChokepointScore = 0f;

        // "Camps" = nodes already owned by some player at game start. These are the
        // anchors between which contested chokepoints emerge during play.
        var camps = new List<NodeData>();
        for (int i = 0; i < n; i++)
            if (nodes[i].OwnedBy != null)
                camps.Add(nodes[i]);
        if (camps.Count < 2) return;

        var indexOf = new Dictionary<NodeData, int>(n);
        for (int i = 0; i < n; i++) indexOf[nodes[i]] = i;

        // BFS once from each camp; reused across all pair iterations below.
        int numCamps = camps.Count;
        var dist = new int[numCamps][];
        var sigma = new long[numCamps][];
        for (int c = 0; c < numCamps; c++)
            BFSFromSource(nodes, indexOf, camps[c], out dist[c], out sigma[c]);

        // For each unordered camp pair (a, b), accumulate fractional path-counts through
        // every intermediate node v on a shortest s..t path. Brandes's identity:
        //   numShortestPathsThroughV(s,t) = sigma_s[v] * sigma_t[v]
        // when dist_s[v] + dist_t[v] == dist_s[t]; zero otherwise. Dividing by sigma_s[t]
        // yields v's fractional contribution to the (s,t) pair. Summing across all camp
        // pairs gives the standard endpoint-restricted betweenness centrality.
        var contrib = new double[n];
        for (int a = 0; a < numCamps; a++)
        {
            for (int b = a + 1; b < numCamps; b++)
            {
                int targetIdx = indexOf[camps[b]];
                int dst = dist[a][targetIdx];
                long total = sigma[a][targetIdx];
                if (total <= 0 || dst <= 0) continue;

                for (int v = 0; v < n; v++)
                {
                    if (nodes[v] == camps[a] || nodes[v] == camps[b]) continue;
                    int da = dist[a][v];
                    int db = dist[b][v];
                    if (da == int.MaxValue || db == int.MaxValue) continue;
                    if (da + db != dst) continue;

                    long sa = sigma[a][v];
                    long sb = sigma[b][v];
                    if (sa <= 0 || sb <= 0) continue;
                    contrib[v] += (double)(sa * sb) / total;
                }
            }
        }

        // Strip out pure corridors: a degree-2 node has no branching — anyone walking through
        // is forced to enter via one neighbor and leave via the other, so capturing the
        // corridor doesn't gate any decision its two neighbors don't already gate. This keeps
        // single-step gateways like Blue's #11 -> #1 -> #2 from being flagged as a chokepoint
        // (the chokepoint there is #2, where multiple inter-camp routes actually converge).
        // Leaves (degree 1) are off all inter-camp paths and already score 0 from betweenness.
        for (int v = 0; v < n; v++)
        {
            if (nodes[v].NodeConnections.Count <= 2)
                contrib[v] = 0.0;
        }

        // Normalize so the strongest chokepoint maps to 1.0; everything else scales linearly.
        // Without this the absolute scale depends on camp count which makes downstream
        // multiplier tuning awkward (3-player maps would naturally score higher than 2-player).
        double maxC = 0.0;
        for (int v = 0; v < n; v++) if (contrib[v] > maxC) maxC = contrib[v];
        if (maxC <= 0.0) return;

        for (int v = 0; v < n; v++)
            nodes[v].ChokepointScore = (float)(contrib[v] / maxC);
    }

    // Standard Brandes BFS: dist[v] = hop distance from source, sigma[v] = number of distinct
    // shortest paths from source to v. Edges on this graph are stored uni-directionally per
    // node (TownData ctor adds both directions for bidirectional defs), so iterating
    // cur.NodeConnections is sufficient.
    static void BFSFromSource(
        List<NodeData> nodes,
        Dictionary<NodeData, int> indexOf,
        NodeData source,
        out int[] dist,
        out long[] sigma)
    {
        int n = nodes.Count;
        dist = new int[n];
        sigma = new long[n];
        for (int i = 0; i < n; i++) dist[i] = int.MaxValue;

        int srcIdx = indexOf[source];
        dist[srcIdx] = 0;
        sigma[srcIdx] = 1;

        var queue = new Queue<NodeData>();
        queue.Enqueue(source);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            int curIdx = indexOf[cur];
            int curDist = dist[curIdx];
            long curSigma = sigma[curIdx];
            var conns = cur.NodeConnections;
            for (int i = 0; i < conns.Count; i++)
            {
                var nb = conns[i].End;
                if (nb == null) continue;
                int nbIdx = indexOf[nb];
                if (dist[nbIdx] == int.MaxValue)
                {
                    dist[nbIdx] = curDist + 1;
                    queue.Enqueue(nb);
                }
                if (dist[nbIdx] == curDist + 1)
                    sigma[nbIdx] += curSigma;
            }
        }
    }
}
