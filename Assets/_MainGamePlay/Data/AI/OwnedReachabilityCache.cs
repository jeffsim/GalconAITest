using System.Collections.Generic;

/// <summary>
/// Per-player flood-fill of the OWNED-ONLY subgraph: assigns every node owned by the
/// viewer player a component id, with all members of that component grouped together
/// so generators can look up "every node connected to me via friendly territory" in
/// O(1) instead of running a fresh BFS per (target, generator) combo each tick.
///
/// Invalidation: the cache is keyed on the player's ownership signature -- a cheap hash
/// over which node indices the player currently owns. Each call to <see cref="Refresh"/>
/// recomputes that signature; if it matches the previous one, the cached components are
/// reused verbatim (which is the common case -- ownership only changes when a capture
/// or attack actually flips a node).
///
/// Phase 5 of the map-preprocessing plan. See <see cref="MapTopologyAnalysis"/> for the
/// static-topology siblings (resource mask, distance matrix, articulation, spawn-dist).
/// </summary>
public class OwnedReachabilityCache
{
    /// <summary>
    /// Per-node component id (indexed by <see cref="AI_NodeState.Index"/>). -1 means the
    /// node is not owned by the viewer player at the moment of the last refresh.
    /// </summary>
    int[] componentId;

    /// <summary>
    /// Component id -> list of nodes in that component. Reused across refreshes; cleared
    /// at the start of every recomputation. Index space is dense [0, NumComponents).
    /// </summary>
    readonly List<List<AI_NodeState>> componentMembers = new();
    int numComponents;

    long ownershipSignature = unchecked((long)0xBADC0FFEE0DDF00DL);

    /// <summary>
    /// Recompute components only if the player's ownership has changed since the previous
    /// call. The signature comparison is O(NumOwned) — a small fraction of the BFS work
    /// that recomputation would do, so amortising across unchanged ticks is a clear win.
    /// </summary>
    public void Refresh(AIWorldView view)
    {
        long sig = ComputeOwnershipSignature(view);
        if (sig == ownershipSignature && componentId != null && componentId.Length == view.NumNodes)
            return;
        ownershipSignature = sig;
        Recompute(view);
    }

    /// <summary>
    /// Component id for <paramref name="node"/>, or -1 if it's not currently owned by the
    /// viewer player (and therefore not flood-filled into any owned component).
    /// </summary>
    public int GetComponent(AI_NodeState node)
    {
        if (node == null || componentId == null) return -1;
        int idx = node.Index;
        if (idx < 0 || idx >= componentId.Length) return -1;
        return componentId[idx];
    }

    /// <summary>
    /// Members of <paramref name="compId"/>'s component. Returns null on bogus input;
    /// the returned list is owned by the cache -- callers must not mutate it.
    /// </summary>
    public IReadOnlyList<AI_NodeState> NodesInComponent(int compId)
    {
        if (compId < 0 || compId >= numComponents) return null;
        return componentMembers[compId];
    }

    public int NumComponents => numComponents;

    static long ComputeOwnershipSignature(AIWorldView view)
    {
        // FNV-1a over (index, ownerHash) for nodes owned by the viewer player. ownerHash
        // is reduced to "1" for owned / 0 otherwise here because we ONLY care whether the
        // owned-component flood would change. (If we ever shared the cache across players
        // we'd hash the actual ownerId; today it's per-player so this is enough.)
        const long FnvPrime = 1099511628211L;
        long h = unchecked((long)14695981039346656037L);
        for (int i = 0; i < view.NumNodes; i++)
        {
            if (view.Nodes[i].OwnedBy != view.Player) continue;
            h ^= i;
            h *= FnvPrime;
        }
        return h;
    }

    void Recompute(AIWorldView view)
    {
        int n = view.NumNodes;
        if (componentId == null || componentId.Length != n)
            componentId = new int[n];
        for (int i = 0; i < n; i++) componentId[i] = -1;
        for (int i = 0; i < componentMembers.Count; i++) componentMembers[i].Clear();
        numComponents = 0;

        var queue = new Queue<AI_NodeState>();
        for (int i = 0; i < n; i++)
        {
            if (view.Nodes[i].OwnedBy != view.Player) continue;
            if (componentId[i] != -1) continue;

            int compId = numComponents++;
            var members = AcquireComponentList(compId);

            componentId[i] = compId;
            members.Add(view.Nodes[i]);
            queue.Clear();
            queue.Enqueue(view.Nodes[i]);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                var neighbors = cur.NeighborNodes;
                for (int k = 0; k < neighbors.Count; k++)
                {
                    var nb = neighbors[k];
                    if (nb.OwnedBy != view.Player) continue;
                    int nbIdx = nb.Index;
                    if (componentId[nbIdx] != -1) continue;
                    componentId[nbIdx] = compId;
                    members.Add(nb);
                    queue.Enqueue(nb);
                }
            }
        }
    }

    List<AI_NodeState> AcquireComponentList(int compId)
    {
        while (componentMembers.Count <= compId)
            componentMembers.Add(new List<AI_NodeState>());
        return componentMembers[compId];
    }
}
