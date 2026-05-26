using System.Collections.Generic;

/// Per-player compact mirror of TownData. One AIWorldView exists per PlayerAI. Owns the
/// AI_NodeState array and the aggregated PlayerTownInventory.
///
/// Lifecycle:
///   ctor              -- bind to PlayerData (no node data yet).
///   InitializeStatic  -- run once, after TownData has wired connections + chokepoint scores.
///   Refresh(town)     -- run once per AI tick to pull the latest world state.
///
/// There is intentionally NO mutation surface for the search to use (no AttackFromNode,
/// SendWorkers*, Undo_*, ConstructBuilding, etc.). The new AI is a single-pass utility
/// evaluator and does not simulate hypothetical moves -- it scores candidates directly
/// against the refreshed mirror.
public class AIWorldView
{
    public readonly PlayerData Player;

    public AI_NodeState[] Nodes;
    public int NumNodes;

    /// Aggregated inventory across all owned nodes. Rebuilt every Refresh.
    public readonly Dictionary<GoodType, int> Inventory = new();

    public AIWorldView(PlayerData player)
    {
        Player = player;
    }

    public void InitializeStatic(TownData town)
    {
        NumNodes = town.Nodes.Count;
        Nodes = new AI_NodeState[NumNodes];
        for (int i = 0; i < NumNodes; i++)
        {
            Nodes[i] = new AI_NodeState(town.Nodes[i]);
            Nodes[i].Index = i;
        }

        // Wire the AI-side neighbor graph. NodeConnection edges in TownData are already
        // duplicated for bidirectional defs (see TownData ctor), so iterating each node's
        // NodeConnections once is sufficient -- but the destination NodeData may have been
        // added before us, so we explicitly de-dupe by AI_NodeState reference.
        for (int i = 0; i < NumNodes; i++)
        {
            var conns = town.Nodes[i].NodeConnections;
            foreach (var conn in conns)
            {
                int endIdx = town.Nodes.IndexOf(conn.End);
                if (endIdx < 0) continue;
                var endMirror = Nodes[endIdx];
                if (!Nodes[i].NeighborNodes.Contains(endMirror))
                    Nodes[i].NeighborNodes.Add(endMirror);
                if (conn.IsBidirectional && !endMirror.NeighborNodes.Contains(Nodes[i]))
                    endMirror.NeighborNodes.Add(Nodes[i]);
            }
        }

        for (int i = 0; i < NumNodes; i++)
        {
            Nodes[i].NumNeighbors = Nodes[i].NeighborNodes.Count;
            Nodes[i].SetDistanceToResources();
        }
    }

    /// Refresh dynamic state from TownData. After this returns, every AI_NodeState reflects
    /// the live world from Player's perspective (with in-flight worker projections so the
    /// AI doesn't double-send to nodes already being captured/attacked).
    public void Refresh(TownData town)
    {
        // Initialise inventory keys so callers can index without try-add gymnastics. Defns
        // is the source of truth for which goods exist.
        if (GameDefns.Instance != null)
        {
            foreach (var goodDefn in GameDefns.Instance.GoodDefns.Values)
                Inventory[goodDefn.GoodType] = 0;
        }
        // Always also seed the two that the test harness uses without GameDefns wired up.
        Inventory[GoodType.Wood] = 0;
        Inventory[GoodType.Stone] = 0;

        for (int i = 0; i < NumNodes; i++)
        {
            var node = town.Nodes[i];
            if (node.OwnedBy == Player)
            {
                foreach (var invItem in node.Inventory)
                {
                    if (!Inventory.ContainsKey(invItem.Key))
                        Inventory[invItem.Key] = 0;
                    Inventory[invItem.Key] += invItem.Value;
                }
            }
        }

        // Always project from this player's perspective. In step mode WorkersInFlight is
        // empty so the projections are no-ops and we still get a clean mirror.
        for (int i = 0; i < NumNodes; i++)
            Nodes[i].Refresh(Player);
    }

    public int GetInventory(GoodType good)
    {
        return Inventory.TryGetValue(good, out int v) ? v : 0;
    }
}
