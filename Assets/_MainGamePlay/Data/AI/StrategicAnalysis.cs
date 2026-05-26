using System.Collections.Generic;
using UnityEngine;

/// One-shot per-tick pre-pass. Reads the refreshed AIWorldView and computes every derived
/// fact the generators and scorers need, exactly once, so no scorer ever has to recompute
/// "what's the frontier pressure here?" or "how many workers can I send from there?".
///
/// All per-node arrays are indexed by AI_NodeState.Index -- the [0, NumNodes) array slot
/// inside worldView.Nodes. This is intentionally NOT the same as NodeId, which is a
/// stable map-level identifier and may be sparse / non-zero-based.
///
/// This class is the new single home for every fact that used to be scattered across
/// AI_ActionHeuristics.GetFrontierPressure / GetEffectiveFrontierPressure /
/// GetReservedForImmediateDefense / GetDesiredFrontierWorkers / GetTotalDefensiveDeficit /
/// GetWorkersWillingToSend / GetWorkersWillingToSendForDefense / UpdateTerritoryDetails.
public class StrategicAnalysis
{
    PlayerData player;
    PersonalityWeights personality;
    AIWorldView view;

    // Per-node derived facts (sized to NumNodes).
    public int[] SafeToSendFrom;          // workers safe to dispatch from this node right now
    public int[] DefensiveDeficit;        // for owned nodes: pressure - garrison (>=0)
    public int[] FrontierPressure;        // enemy neighbor workers + incoming hostile + heat
    public bool[] IsOwned;                // owned by `player`
    public bool[] IsFrontier;             // owned and at least one non-friendly neighbor

    // Player-level derived facts.
    public readonly Dictionary<GoodType, int> ResourceShortage = new();
    public readonly HashSet<BuildingType> OwnedBuildingTypes = new();
    public int TotalOwnedNodes;
    public int TotalOwnedWorkers;

    /// Min wave size for a viable reinforcement send -- drip-prevention.
    public const int MinReinforceWave = 5;
    /// Min wave size for a viable capture/build send -- prevents 1-worker captures.
    public const int MinCaptureWave = 3;

    /// AttackHeat scaling into pressure (heat 1.0 contributes 2 worker-equivalents of
    /// pressure). Tuned so a node hit ~once per second feels meaningfully more pressured
    /// than a quiet one without overwhelming the actual enemy-count term.
    const float HeatToPressure = 2f;

    public void Compute(AIWorldView worldView, PlayerData currentPlayer, PersonalityWeights weights)
    {
        view = worldView;
        player = currentPlayer;
        personality = weights;

        int n = worldView.NumNodes;
        EnsureCapacity(n);

        TotalOwnedNodes = 0;
        TotalOwnedWorkers = 0;
        OwnedBuildingTypes.Clear();

        // --- Pass 1: ownership facts + frontier classification ---
        for (int i = 0; i < n; i++)
        {
            var node = worldView.Nodes[i];
            bool owned = node.OwnedBy == player;
            IsOwned[i] = owned;

            int enemyWorkers = 0;
            int contestedNeutralWorkers = 0;
            bool onEdge = false;

            for (int k = 0; k < node.NumNeighbors; k++)
            {
                var nb = node.NeighborNodes[k];
                if (nb.OwnedBy == player) continue;
                onEdge = true;
                if (nb.OwnedBy != null)
                    enemyWorkers += nb.NumWorkers;
                else
                {
                    // Neutral neighbor that itself touches a hostile player counts as
                    // contested -- captures of these get a chokepoint-style pull.
                    for (int j = 0; j < nb.NumNeighbors; j++)
                    {
                        var nbnb = nb.NeighborNodes[j];
                        if (nbnb.OwnedBy != null && nbnb.OwnedBy != player)
                        {
                            contestedNeutralWorkers += nb.NumWorkers;
                            break;
                        }
                    }
                }
            }

            node.IsOnTerritoryEdge = onEdge;
            node.NumEnemiesInNeighborNodes = enemyWorkers;
            node.NumContestedNeutralWorkersNearby = contestedNeutralWorkers;
            IsFrontier[i] = owned && onEdge;

            if (owned)
            {
                TotalOwnedNodes++;
                TotalOwnedWorkers += node.NumWorkers;
                if (node.HasBuilding && node.BuildingDefn != null)
                    OwnedBuildingTypes.Add(node.BuildingDefn.BuildingType);
            }

            // Frontier pressure: visible enemy weight on this node. AttackHeat captures
            // post-hoc attack memory; IncomingHostileWorkers captures predicted wave.
            FrontierPressure[i] = enemyWorkers + node.IncomingHostileWorkers
                                  + Mathf.RoundToInt(node.AttackHeat * HeatToPressure);
        }

        // --- Pass 2: per-owned-node defensive deficit + safe-to-send ---
        for (int i = 0; i < n; i++)
        {
            var node = worldView.Nodes[i];
            if (!IsOwned[i])
            {
                DefensiveDeficit[i] = 0;
                SafeToSendFrom[i] = 0;
                continue;
            }

            int garrison = node.EffectiveDefenseGarrison;
            int pressure = FrontierPressure[i];
            int requiredDefense = Mathf.CeilToInt(pressure * personality.AttackOverkill);
            DefensiveDeficit[i] = Mathf.Max(0, requiredDefense - garrison);

            // Workers safe to dispatch from this node = NumWorkers - 1 (game rule, source
            // must retain at least 1) - reservedForImmediateDefense (don't drain a node
            // facing an active threat below what it needs to hold).
            int reservedForDefense = Mathf.CeilToInt(pressure * 1.0f);
            int safe = node.NumWorkers - personality.MinReserveAtSource - reservedForDefense;
            if (safe < 0) safe = 0;
            // Hard cap at sendable rule.
            int maxSendable = NodeData.GetMaxSendableWorkers(node.NumWorkers);
            if (safe > maxSendable) safe = maxSendable;
            SafeToSendFrom[i] = safe;
        }

        // --- Player-level demand vector ---
        ComputeResourceShortage();
    }

    void EnsureCapacity(int n)
    {
        if (SafeToSendFrom == null || SafeToSendFrom.Length < n)
        {
            SafeToSendFrom = new int[n];
            DefensiveDeficit = new int[n];
            FrontierPressure = new int[n];
            IsOwned = new bool[n];
            IsFrontier = new bool[n];
        }
    }

    void ComputeResourceShortage()
    {
        ResourceShortage.Clear();
        var defn = player.AIDefn;

        // Stockpile demand: maintain at least DesiredStockpile of each tracked resource.
        int woodTarget = defn != null ? defn.TargetWoodStockpile : 15;
        int stoneTarget = defn != null ? defn.TargetStoneStockpile : 15;
        ResourceShortage[GoodType.Wood] = Mathf.Max(0, woodTarget - view.GetInventory(GoodType.Wood));
        ResourceShortage[GoodType.Stone] = Mathf.Max(0, stoneTarget - view.GetInventory(GoodType.Stone));

        // Construction demand: any building type we want and don't yet own contributes its
        // construction cost as additional demand. Capped at one instance per type so a
        // single missing Barracks doesn't generate N copies of Wood demand.
        if (GameDefns.Instance == null) return;
        foreach (var bd in GameDefns.Instance.BuildingDefns.Values)
        {
            if (!bd.CanBeBuiltByPlayer) continue;
            if (OwnedBuildingTypes.Contains(bd.BuildingType)) continue;
            foreach (var req in bd.ConstructionRequirements)
            {
                if (!ResourceShortage.ContainsKey(req.Good.GoodType))
                    ResourceShortage[req.Good.GoodType] = 0;
                int already = view.GetInventory(req.Good.GoodType);
                int additional = Mathf.Max(0, req.Amount - already);
                ResourceShortage[req.Good.GoodType] += additional;
            }
        }
    }

    public int GetResourceShortage(GoodType good)
    {
        return ResourceShortage.TryGetValue(good, out int v) ? v : 0;
    }

    /// True when player does not yet own any node with this building type. Used by build
    /// scoring to value "fill a missing tier" candidates more heavily than redundant builds.
    public bool IsBuildingTypeMissing(BuildingType type) => !OwnedBuildingTypes.Contains(type);
}
