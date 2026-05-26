using System.Collections.Generic;

/// <summary>
/// Coarse degree-bucket classification used by build-site heuristics and human-readable
/// debug dumps. Boundary thresholds intentionally match the plan; if a future map ever has
/// nodes with degree 5+ the Hub bucket still holds.
/// </summary>
public enum NodeRole
{
    Isolated, // degree 0 (shouldn't happen on a real map but defined so default(NodeRole) is meaningful)
    Leaf,     // degree 1 -- dead end
    Corridor, // degree 2 -- pass-through
    Junction, // degree 3 -- minor branching
    Hub,      // degree >= 4 -- launchpad-worthy
}

/// <summary>
/// Static (one-time at map load) topology preprocessing. Anything that depends only on the
/// graph + terrain layout, not on who owns what or how many workers stand where, belongs
/// here. Runs alongside <see cref="ChokepointAnalysis"/> from the <see cref="TownData"/>
/// constructor and writes its results onto each <see cref="NodeData"/>; the per-player
/// <see cref="AI_NodeState"/> mirrors then copy those fields once in
/// <see cref="AIWorldView.InitializeStatic"/>.
///
/// Phase-by-phase contents (see the map_preprocessing_roi_plan):
///
///   Phase 1 -- AdjacentResourceMask, LocalGatherableMask, building-type masks.
///   Phase 2 -- All-pairs distance matrix, degree, NodeRole classification.
///   Phase 3 -- Articulation points + bridges (Tarjan single-DFS).
///   Phase 4 -- Per-starting-player spawn-distance maps and RaceMargin.
///   Phase 6 -- Biconnected-components RegionId.
///
/// Splitting the work this way keeps the dynamic per-tick <see cref="StrategicAnalysis"/>
/// path lean: it can READ this preprocessor's static results, but never has to recompute
/// any of them.
/// </summary>
public static class MapTopologyAnalysis
{
    /// <summary>
    /// Bitmask over <see cref="GoodType"/> -- bit (1 &lt;&lt; (int)goodType) is set when the
    /// flag means "this resource is present here / adjacent here / can be gathered here".
    /// 32 bits comfortably covers the current GoodType enum (~20 entries).
    /// </summary>
    public static uint MaskFor(GoodType g) => g == GoodType.Unset ? 0u : (1u << (int)g);

    public static void Compute(TownData townData)
    {
        var nodes = townData.Nodes;
        int n = nodes.Count;

        // --- Pass 1: per-node terrain self-mask ("what does this tile yield if I gather
        // here?"). A node yields a resource iff it currently hosts a gatherable building --
        // Forest -> Wood, StoneMine -> Stone, etc.
        for (int i = 0; i < n; i++)
        {
            var node = nodes[i];
            uint local = 0;
            if (node.Building != null && node.Building.Defn != null
                && node.Building.Defn.CanBeGatheredFrom
                && node.Building.Defn.ResourceGatheredFromThisNode != null)
            {
                local |= MaskFor(node.Building.Defn.ResourceGatheredFromThisNode.GoodType);
            }
            node.LocalGatherableMask = local;
        }

        // --- Pass 2: per-node adjacent-resource mask (OR of every neighbor's local mask).
        // This is what gatherer-validity checks consult: "is at least one neighbor a Forest /
        // a StoneMine / etc?" becomes a single bit test instead of an N-way walk.
        for (int i = 0; i < n; i++)
        {
            var node = nodes[i];
            uint adjMask = 0;
            for (int k = 0; k < node.NodeConnections.Count; k++)
            {
                var other = node.NodeConnections[k].End;
                if (other == null || other == node) continue;
                adjMask |= other.LocalGatherableMask;
            }
            node.AdjacentResourceMask = adjMask;
        }

        // --- Pass 3: graph-structural facts (Phase 2 of the preprocessing plan).
        // Index lookup is identical to ChokepointAnalysis's: position within TownData.Nodes.
        // This MUST match AIWorldView.InitializeStatic, which assigns AI_NodeState.Index = i
        // in the same loop order -- otherwise downstream DistanceTo[index] lookups would
        // alias the wrong node.
        var indexOf = new Dictionary<NodeData, int>(n);
        for (int i = 0; i < n; i++) indexOf[nodes[i]] = i;

        // Degree (de-duplicated against any duplicate NodeConnection entries for the same
        // neighbor) and Role classification.
        var neighborScratch = new HashSet<NodeData>();
        for (int i = 0; i < n; i++)
        {
            var node = nodes[i];
            neighborScratch.Clear();
            foreach (var conn in node.NodeConnections)
            {
                var other = conn.Start == node ? conn.End : conn.Start;
                if (other == null || other == node) continue;
                neighborScratch.Add(other);
            }
            node.Degree = neighborScratch.Count;
            node.Role = ClassifyRole(node.Degree);
        }

        // All-pairs shortest-path matrix. Stored per-node as a single int[] of length n
        // so AI generators can do "dist = view.Nodes[a].DistanceTo[view.Nodes[b].Index]"
        // without a separate central matrix lookup. int.MaxValue means unreachable.
        for (int i = 0; i < n; i++)
        {
            var distArr = new int[n];
            for (int j = 0; j < n; j++) distArr[j] = int.MaxValue;
            BFSDistanceFrom(nodes, indexOf, i, distArr);
            nodes[i].DistanceTo = distArr;
        }

        // --- Pass 4: Tarjan single-DFS for articulation points + bridges (Phase 3).
        // Required pre-state: every nodes[i].IsArticulationPoint reset, BridgeNeighborIndices
        // cleared. Then run DFS from every unvisited vertex (handles disconnected graphs,
        // though our maps are typically one component).
        var adj = BuildUniqueNeighborIndexLists(nodes, indexOf);
        ComputeArticulationAndBridges(nodes, adj);

        // --- Pass 5: 2-edge-connected components (Phase 6). Must run AFTER bridges.
        ComputeRegions(nodes, adj);

        // --- Pass 6: spawn-distance maps + race-margin (Phase 4 of the preprocessing plan).
        ComputeSpawnDistances(townData, nodes, indexOf);
    }

    /// <summary>
    /// Per-player multi-source BFS yielding "hop distance from any of player P's starting
    /// camps to each node". Used to score uncontested expansion (we're closer than the
    /// nearest other spawn) versus contested expansion (the enemy is closer).
    /// </summary>
    static void ComputeSpawnDistances(TownData townData, List<NodeData> nodes, Dictionary<NodeData, int> indexOf)
    {
        int n = nodes.Count;
        // Players list is [null, P1, P2, ...]; allocate slot arrays sized to its length so
        // PlayerData.Id indexes cleanly without a -1 offset.
        int slots = townData.Players.Count;

        var spawnsBySlot = new List<NodeData>[slots];
        for (int i = 0; i < n; i++)
        {
            var owner = nodes[i].OwnedBy;
            if (owner == null) continue;
            int slot = owner.Id;
            if (slot < 0 || slot >= slots) continue;
            if (spawnsBySlot[slot] == null) spawnsBySlot[slot] = new List<NodeData>();
            spawnsBySlot[slot].Add(nodes[i]);
        }

        // Allocate per-node spawn-distance / race-margin arrays. Default = unreachable.
        for (int i = 0; i < n; i++)
        {
            nodes[i].PlayerSpawnDistance = new int[slots];
            nodes[i].RaceMargin = new int[slots];
            for (int s = 0; s < slots; s++)
            {
                nodes[i].PlayerSpawnDistance[s] = int.MaxValue;
                nodes[i].RaceMargin[s] = 0;
            }
            nodes[i].OwnerOfNearestSpawn = -1;
        }

        // BFS from each player's set of starting camps. A multi-source BFS gives the min
        // distance to ANY of that player's camps, which is what "spawn distance" means
        // when a player owns more than one starting node.
        for (int s = 0; s < slots; s++)
        {
            if (spawnsBySlot[s] == null) continue;
            var spawnDist = MultiSourceBFS(nodes, indexOf, spawnsBySlot[s]);
            for (int i = 0; i < n; i++)
                nodes[i].PlayerSpawnDistance[s] = spawnDist[i];
        }

        // Derive OwnerOfNearestSpawn (with -1 sentinel on a tie) and RaceMargin per slot.
        // RaceMargin uses int.MinValue when my spawn can't reach this node at all (so
        // callers can detect "I literally can't get here" vs "I'm losing the race by N").
        for (int i = 0; i < n; i++)
        {
            int minDist = int.MaxValue;
            int minOwner = -1;
            bool tied = false;
            for (int s = 1; s < slots; s++) // skip slot 0 (the "no player" sentinel)
            {
                int d = nodes[i].PlayerSpawnDistance[s];
                if (d == int.MaxValue) continue;
                if (d < minDist) { minDist = d; minOwner = s; tied = false; }
                else if (d == minDist) { tied = true; }
            }
            nodes[i].OwnerOfNearestSpawn = tied ? -1 : minOwner;

            for (int s = 0; s < slots; s++)
            {
                int my = nodes[i].PlayerSpawnDistance[s];
                if (my == int.MaxValue)
                {
                    nodes[i].RaceMargin[s] = int.MinValue;
                    continue;
                }
                int nearestOther = int.MaxValue;
                for (int q = 1; q < slots; q++)
                {
                    if (q == s) continue;
                    int dq = nodes[i].PlayerSpawnDistance[q];
                    if (dq < nearestOther) nearestOther = dq;
                }
                if (nearestOther == int.MaxValue)
                {
                    // Solo player: no opponent to race against. Margin is unbounded; cap
                    // at a large positive sentinel that ScoreCapture's clamp can absorb.
                    nodes[i].RaceMargin[s] = int.MaxValue;
                    continue;
                }
                nodes[i].RaceMargin[s] = nearestOther - my;
            }
        }
    }

    static int[] MultiSourceBFS(
        List<NodeData> nodes,
        Dictionary<NodeData, int> indexOf,
        List<NodeData> sources)
    {
        int n = nodes.Count;
        var dist = new int[n];
        for (int i = 0; i < n; i++) dist[i] = int.MaxValue;

        var queue = new Queue<NodeData>();
        foreach (var src in sources)
        {
            int idx = indexOf[src];
            if (dist[idx] != 0) { dist[idx] = 0; queue.Enqueue(src); }
        }

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            int curIdx = indexOf[cur];
            int curDist = dist[curIdx];
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
            }
        }
        return dist;
    }

    /// <summary>
    /// Build, per node, a sorted unique-by-index list of its neighbors. NodeConnections may
    /// contain duplicates (e.g. multi-edges between the same pair); Tarjan needs distinct
    /// neighbor identities, and downstream iteration is friendlier with int indices than
    /// object references.
    /// </summary>
    static List<int[]> BuildUniqueNeighborIndexLists(List<NodeData> nodes, Dictionary<NodeData, int> indexOf)
    {
        int n = nodes.Count;
        var adj = new List<int[]>(n);
        var scratch = new HashSet<int>();
        for (int i = 0; i < n; i++)
        {
            scratch.Clear();
            foreach (var conn in nodes[i].NodeConnections)
            {
                var other = conn.Start == nodes[i] ? conn.End : conn.Start;
                if (other == null || other == nodes[i]) continue;
                scratch.Add(indexOf[other]);
            }
            var arr = new int[scratch.Count];
            int k = 0;
            foreach (var v in scratch) arr[k++] = v;
            adj.Add(arr);
        }
        return adj;
    }

    /// <summary>
    /// 2-edge-connected components (Phase 6): partition the static graph into "regions"
    /// such that two nodes share a region iff there is a path between them that does NOT
    /// cross any bridge edge. Removing all bridges and flood-filling the remainder yields
    /// these regions. A single bridge between two cycles produces two regions; a tree
    /// has every node in its own region.
    ///
    /// Must run AFTER bridges have been populated by Tarjan.
    /// </summary>
    static void ComputeRegions(List<NodeData> nodes, List<int[]> adj)
    {
        int n = nodes.Count;
        for (int i = 0; i < n; i++) nodes[i].RegionId = -1;
        int nextRegionId = 0;
        var queue = new Queue<int>();
        for (int s = 0; s < n; s++)
        {
            if (nodes[s].RegionId != -1) continue;
            int rid = nextRegionId++;
            nodes[s].RegionId = rid;
            queue.Clear();
            queue.Enqueue(s);
            while (queue.Count > 0)
            {
                int u = queue.Dequeue();
                var neighbors = adj[u];
                for (int k = 0; k < neighbors.Length; k++)
                {
                    int v = neighbors[k];
                    if (nodes[v].RegionId != -1) continue;
                    // Skip bridge edges: they separate regions by definition.
                    if (nodes[u].BridgeNeighborIndices != null && nodes[u].BridgeNeighborIndices.Contains(v))
                        continue;
                    nodes[v].RegionId = rid;
                    queue.Enqueue(v);
                }
            }
        }
    }

    static void ComputeArticulationAndBridges(List<NodeData> nodes, List<int[]> adj)
    {
        int n = nodes.Count;

        for (int i = 0; i < n; i++)
        {
            nodes[i].IsArticulationPoint = false;
            if (nodes[i].BridgeNeighborIndices == null)
                nodes[i].BridgeNeighborIndices = new HashSet<int>();
            else
                nodes[i].BridgeNeighborIndices.Clear();
        }
        if (n == 0) return;

        var disc = new int[n];
        var low = new int[n];
        var parent = new int[n];
        var visited = new bool[n];
        for (int i = 0; i < n; i++)
        {
            disc[i] = -1;
            low[i] = -1;
            parent[i] = -1;
        }
        int timer = 0;

        // Iterative DFS to avoid any stack-overflow risk on large maps. Each frame tracks
        // which neighbor it is currently visiting (nextChildIdx). When we return from a
        // child, we update low[u] and check the articulation / bridge conditions for u.
        var stack = new Stack<(int u, int nextChildIdx, int childrenSoFar)>();

        for (int s = 0; s < n; s++)
        {
            if (visited[s]) continue;
            visited[s] = true;
            disc[s] = low[s] = timer++;
            stack.Push((s, 0, 0));

            while (stack.Count > 0)
            {
                var frame = stack.Pop();
                int u = frame.u;
                int idx = frame.nextChildIdx;
                int kids = frame.childrenSoFar;
                var neighbors = adj[u];

                // Advance to the next unvisited neighbor, OR a visited-non-parent neighbor
                // (for the low-link back-edge update).
                bool descended = false;
                while (idx < neighbors.Length)
                {
                    int v = neighbors[idx];
                    idx++;
                    if (!visited[v])
                    {
                        visited[v] = true;
                        parent[v] = u;
                        disc[v] = low[v] = timer++;
                        // Push the updated parent frame back (with the post-call return point),
                        // then push the child frame to be processed next.
                        stack.Push((u, idx, kids + 1));
                        stack.Push((v, 0, 0));
                        descended = true;
                        break;
                    }
                    else if (v != parent[u])
                    {
                        // Back edge: update u's low-link with v's discovery time.
                        if (disc[v] < low[u]) low[u] = disc[v];
                    }
                }

                if (descended) continue;

                // We've exhausted u's neighbors -- finalize. If u has a parent, propagate
                // low[u] back up and run the articulation / bridge checks for the edge
                // (parent[u] -> u) at u's parent.
                if (parent[u] != -1)
                {
                    int p = parent[u];
                    if (low[u] < low[p]) low[p] = low[u];

                    if (low[u] >= disc[p] && parent[p] != -1)
                        nodes[p].IsArticulationPoint = true;

                    if (low[u] > disc[p])
                    {
                        // (p, u) is a bridge. Record both directions for easy lookup.
                        nodes[p].BridgeNeighborIndices.Add(u);
                        nodes[u].BridgeNeighborIndices.Add(p);
                    }
                }
                else
                {
                    // u is the root of this DFS tree. It's an articulation point iff it
                    // produced 2 or more children.
                    if (kids >= 2)
                        nodes[u].IsArticulationPoint = true;
                }
            }
        }
    }

    /// <summary>
    /// True iff the edge between the AI mirrors <paramref name="a"/> and <paramref name="b"/>
    /// is a bridge: removing it would disconnect the graph. Returns false for non-adjacent
    /// pairs.
    /// </summary>
    public static bool IsBridge(AI_NodeState a, AI_NodeState b)
    {
        if (a == null || b == null) return false;
        var bridges = a.BridgeNeighborIndices;
        return bridges != null && bridges.Contains(b.Index);
    }

    static NodeRole ClassifyRole(int degree)
    {
        if (degree <= 0) return NodeRole.Isolated;
        if (degree == 1) return NodeRole.Leaf;
        if (degree == 2) return NodeRole.Corridor;
        if (degree == 3) return NodeRole.Junction;
        return NodeRole.Hub;
    }

    /// <summary>
    /// Plain BFS that fills <paramref name="dist"/> (indexed by NodeData's position in
    /// <see cref="TownData.Nodes"/>) with hop distance from <paramref name="sourceIdx"/>.
    /// Identical iteration shape to <see cref="ChokepointAnalysis"/>'s BFS but without
    /// the path-count accounting -- we only need distances here.
    /// </summary>
    static void BFSDistanceFrom(
        List<NodeData> nodes,
        Dictionary<NodeData, int> indexOf,
        int sourceIdx,
        int[] dist)
    {
        dist[sourceIdx] = 0;
        var queue = new Queue<NodeData>();
        queue.Enqueue(nodes[sourceIdx]);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            int curIdx = indexOf[cur];
            int curDist = dist[curIdx];
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
            }
        }
    }

    /// <summary>
    /// Lookup helper: bitmask of the resource(s) this building requires to be ADJACENT to
    /// in order to actually function. Non-gatherer buildings yield 0 (no adjacency
    /// constraint). Gatherer buildings yield the bit corresponding to their gathered good.
    /// </summary>
    public static uint RequiredAdjacentResourceMask(BuildingDefn bd)
    {
        if (bd == null) return 0u;
        if (!bd.CanGatherResources || bd.ResourceThisNodeCanGoGather == null) return 0u;
        return MaskFor(bd.ResourceThisNodeCanGoGather.GoodType);
    }

    /// <summary>
    /// True iff <paramref name="bd"/> is either not a gatherer (no constraint) OR has at
    /// least one matching resource adjacent to <paramref name="target"/>. Replaces the
    /// per-call neighbor walk previously duplicated in Build / Capture generators.
    /// </summary>
    public static bool HasMatchingAdjacentResource(AI_NodeState target, BuildingDefn bd)
    {
        uint required = RequiredAdjacentResourceMask(bd);
        if (required == 0u) return true;
        return (target.AdjacentResourceMask & required) != 0u;
    }
}
