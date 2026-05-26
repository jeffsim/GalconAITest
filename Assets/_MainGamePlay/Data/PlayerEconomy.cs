using System.Collections.Generic;

/// Lightweight per-player economy helpers shared by the human-player input layer (drag-to-
/// build modal affordance check) and any future systems that need a town-wide inventory
/// rollup. The AI uses AIWorldView.Refresh for its own per-tick inventory mirror; we don't
/// want the human input path to reach into that mirror just to read totals.
public static class PlayerEconomy
{
    /// Sum the inventory across every node owned by `player`. Adds entries to `target` so
    /// callers can re-use a pre-allocated dictionary across frames; pass a fresh one if you
    /// don't care about reuse.
    public static void GetTotalInventory(PlayerData player, TownData town, Dictionary<GoodType, int> target)
    {
        target.Clear();
        if (player == null || town == null) return;
        for (int i = 0; i < town.Nodes.Count; i++)
        {
            var node = town.Nodes[i];
            if (node.OwnedBy != player) continue;
            foreach (var kv in node.Inventory)
            {
                target.TryGetValue(kv.Key, out int cur);
                target[kv.Key] = cur + kv.Value;
            }
        }
    }

    /// True when `player`'s aggregated inventory covers every requirement listed on `defn`.
    /// Buildings with no ConstructionRequirements (or a null list) are always affordable.
    public static bool CanAfford(PlayerData player, TownData town, BuildingDefn defn)
    {
        if (defn == null) return false;
        if (defn.ConstructionRequirements == null || defn.ConstructionRequirements.Count == 0)
            return true;
        var totals = scratchInventory;
        GetTotalInventory(player, town, totals);
        foreach (var req in defn.ConstructionRequirements)
        {
            totals.TryGetValue(req.Good.GoodType, out int have);
            if (have < req.Amount) return false;
        }
        return true;
    }

    // Scratch dictionary reused across CanAfford calls to avoid per-frame allocations when
    // the building picker checks affordability on every drag movement.
    static readonly Dictionary<GoodType, int> scratchInventory = new();
}
