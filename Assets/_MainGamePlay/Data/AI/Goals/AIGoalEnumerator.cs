using System;
using System.Collections.Generic;

// Walks the map for one player and proposes the strategic goals that player would care
// about RIGHT NOW. Cheap, declarative, no recursion -- this runs once per real-game Update
// and feeds the resource demand vector and (eventually) the action search.
//
// Personality weights from PlayerAIDefn are baked into goal Value here, so an aggressive
// AI sees a richer set of CaptureNode goals (each more valuable) and ends up pulling more
// resource demand toward attack-enabling buildings.
public static class AIGoalEnumerator
{
    // Drop goals that don't clear this floor; keeps the list short and avoids polluting
    // demand with a long tail of negligible contributions.
    const float MinGoalValue = 1f;

    // Tunable goal scoring. Kept as constants here so it's the only place you change to
    // shift strategic priorities; personality multipliers refine it per player.
    const float captureBaseValue = 5f;
    const float captureResourceNodeBonus = 8f;
    const float captureWorkerNodeBonus = 6f;
    const float captureEnemyOwnedBonus = 3f;

    const float defendValuePerWorker = 1f;

    const float economicBaseValue = 5f;
    const float stockpileValuePerMissingUnit = 0.5f;

    public static void EnumerateGoals(
        PlayerData player,
        AI_TownState aiTownState,
        BuildingDefn[] buildableBuildingDefns,
        int numBuildableBuildingDefns,
        List<AIGoal> goalsOut,
        Stack<AIGoal> goalPool)
    {
        // Recycle existing goals back into the pool. Goals are cheap (POCO) but pooling
        // keeps GC steady across the per-Update enumeration.
        for (int i = 0; i < goalsOut.Count; i++)
        {
            goalsOut[i].Reset();
            goalPool.Push(goalsOut[i]);
        }
        goalsOut.Clear();

        var aiDefn = player.AIDefn;
        float aggression = aiDefn != null ? aiDefn.AggressivenessWeight : 1f;
        float defense = aiDefn != null ? aiDefn.DefenseWeight : 1f;
        float territoryExpansion = aiDefn != null ? aiDefn.TerritoryExpansionWeight : 1f;
        float economicExpansion = aiDefn != null ? aiDefn.EconomicExpansionWeight : 1f;

        var nodes = aiTownState.Nodes;
        int numNodes = aiTownState.NumNodes;

        EnumerateCaptureGoals(player, aiTownState, nodes, numNodes, aggression, territoryExpansion, goalsOut, goalPool);
        EnumerateDefendGoals(player, nodes, numNodes, defense, goalsOut, goalPool);
        EnumerateStockpileGoals(player, aiTownState, economicExpansion, goalsOut, goalPool);
        EnumerateEconomicTierGoals(player, aiTownState, buildableBuildingDefns, numBuildableBuildingDefns, aggression, defense, economicExpansion, goalsOut, goalPool);
    }

    static void EnumerateCaptureGoals(
        PlayerData player,
        AI_TownState aiTownState,
        AI_NodeState[] nodes,
        int numNodes,
        float aggression,
        float territoryExpansion,
        List<AIGoal> goalsOut,
        Stack<AIGoal> goalPool)
    {
        for (int i = 0; i < numNodes; i++)
        {
            var node = nodes[i];
            if (node.OwnedBy == player) continue;

            // Only propose captures we could plausibly reach (adjacent to our territory).
            // Anything further out becomes reachable only after we capture intermediate nodes,
            // at which point a fresh goal will be enumerated.
            bool adjacent = false;
            var neighbors = node.NeighborNodes;
            for (int n = 0; n < neighbors.Count; n++)
            {
                if (neighbors[n].OwnedBy == player)
                {
                    adjacent = true;
                    break;
                }
            }
            if (!adjacent) continue;

            float typeBonus = 0f;
            string reason = "neutral expansion";
            if (node.IsResourceNode)
            {
                typeBonus += captureResourceNodeBonus;
                reason = "resource node (" + node.ResourceGatheredFromThisNode + ")";
            }
            if (node.CanGenerateWorkers)
            {
                typeBonus += captureWorkerNodeBonus;
                reason = "worker generator";
            }
            if (node.OwnedBy != null)
            {
                typeBonus += captureEnemyOwnedBonus;
                reason = "enemy " + reason;
            }

            // Half the personality mix comes from aggression (taking from a known enemy) and
            // half from territory expansion (gaining new ground). For neutral nodes, only
            // territory expansion applies -- aggression has no enemy to act on.
            float personalityMix = node.OwnedBy != null
                ? (aggression * 0.5f + territoryExpansion * 0.5f)
                : territoryExpansion;

            float value = (captureBaseValue + typeBonus) * personalityMix;
            if (value < MinGoalValue) continue;

            int horizon = EstimateCaptureHorizon(node);

            var goal = goalPool.Count > 0 ? goalPool.Pop() : new AIGoal();
            goal.Reset();
            goal.Type = AIGoalType.CaptureNode;
            goal.TargetNode = node;
            goal.Value = value;
            goal.HorizonTurns = horizon;
            goal.DebugReason = reason;
            goalsOut.Add(goal);
        }
    }

    static void EnumerateDefendGoals(
        PlayerData player,
        AI_NodeState[] nodes,
        int numNodes,
        float defense,
        List<AIGoal> goalsOut,
        Stack<AIGoal> goalPool)
    {
        for (int i = 0; i < numNodes; i++)
        {
            var node = nodes[i];
            if (node.OwnedBy != player) continue;

            int enemyForce = 0;
            var neighbors = node.NeighborNodes;
            for (int n = 0; n < neighbors.Count; n++)
            {
                var nb = neighbors[n];
                if (nb.OwnedBy != null && nb.OwnedBy != player)
                    enemyForce += nb.NumWorkers;
            }
            if (enemyForce <= 0) continue;

            // Value scales with deficit (enemy force vs ours). At parity or surplus the goal
            // still has weight, just not much; the recursive search will then naturally
            // direct attention elsewhere.
            float deficit = enemyForce - node.NumWorkers;
            if (deficit < 0f) deficit = 0f;
            float value = (1f + deficit) * defendValuePerWorker * defense;
            if (value < MinGoalValue) continue;

            var goal = goalPool.Count > 0 ? goalPool.Pop() : new AIGoal();
            goal.Reset();
            goal.Type = AIGoalType.DefendFrontier;
            goal.TargetNode = node;
            goal.Value = value;
            goal.HorizonTurns = 1; // defense is always immediate
            goal.DebugReason = "deficit " + ((int)deficit) + " vs enemy force " + enemyForce;
            goalsOut.Add(goal);
        }
    }

    static void EnumerateStockpileGoals(
        PlayerData player,
        AI_TownState aiTownState,
        float economicExpansion,
        List<AIGoal> goalsOut,
        Stack<AIGoal> goalPool)
    {
        var aiDefn = player.AIDefn;
        float stockpileWeight = aiDefn != null ? aiDefn.ResourceStockpileWeight : 1f;
        float weight = economicExpansion * stockpileWeight;
        if (weight <= 0f) return;

        TryAddStockpileGoal(player, aiTownState, GoodType.Wood, weight, goalsOut, goalPool);
        TryAddStockpileGoal(player, aiTownState, GoodType.Stone, weight, goalsOut, goalPool);
    }

    static void TryAddStockpileGoal(
        PlayerData player,
        AI_TownState aiTownState,
        GoodType goodType,
        float weight,
        List<AIGoal> goalsOut,
        Stack<AIGoal> goalPool)
    {
        int target = player.AIDefn != null ? player.AIDefn.GetTargetStockpile(goodType) : 0;
        if (target <= 0) return;

        int have = aiTownState.PlayerTownInventory.TryGetValue(goodType, out int v) ? v : 0;
        int deficit = target - have;
        if (deficit <= 0) return;

        float value = deficit * stockpileValuePerMissingUnit * weight;
        if (value < MinGoalValue) return;

        var goal = goalPool.Count > 0 ? goalPool.Pop() : new AIGoal();
        goal.Reset();
        goal.Type = AIGoalType.MaintainStockpile;
        goal.TargetGoodType = goodType;
        goal.Value = value;
        goal.HorizonTurns = Math.Max(1, deficit);
        goal.DebugReason = $"stockpile {goodType}: have {have}, want {target}";
        goalsOut.Add(goal);
    }

    static void EnumerateEconomicTierGoals(
        PlayerData player,
        AI_TownState aiTownState,
        BuildingDefn[] buildableBuildingDefns,
        int numBuildableBuildingDefns,
        float aggression,
        float defense,
        float economicExpansion,
        List<AIGoal> goalsOut,
        Stack<AIGoal> goalPool)
    {
        for (int b = 0; b < numBuildableBuildingDefns; b++)
        {
            var bd = buildableBuildingDefns[b];

            // Raw resource gatherers are derived demand from other goals (a Barracks goal
            // implies wood demand, which makes Woodcutters appealing via the build heuristic).
            // Proposing them as standalone economic goals would double-count.
            if (bd.CanGatherResources) continue;

            // Already own one? Skip; v1 doesn't model "more of the same type". v2 could
            // add scaling based on # owned vs # desired.
            if (PlayerOwnsBuildingType(player, aiTownState, bd)) continue;

            // Building category -> personality alignment.
            float weight;
            string reason;
            if (bd.IsDefensive)
            {
                weight = defense;
                reason = "defensive structure";
            }
            else if (bd.BuildingType == BuildingType.Barracks)
            {
                weight = aggression;
                reason = "unlocks attack power";
            }
            else
            {
                weight = economicExpansion;
                reason = "economic tier";
            }

            float value = economicBaseValue * weight;
            if (value < MinGoalValue) continue;

            int horizon = EstimateBuildHorizon(aiTownState, bd);

            var goal = goalPool.Count > 0 ? goalPool.Pop() : new AIGoal();
            goal.Reset();
            goal.Type = AIGoalType.EconomicTier;
            goal.TargetBuilding = bd;
            goal.Value = value;
            goal.HorizonTurns = horizon;
            goal.DebugReason = reason;
            goalsOut.Add(goal);
        }
    }

    // Rough estimate of turns needed to overcome the defenders. Worker generation isn't
    // modeled here -- the recursive search reasons about that exactly. This horizon just
    // shapes urgency: a heavily defended target should produce demand more slowly than a
    // lightly defended one.
    static int EstimateCaptureHorizon(AI_NodeState target)
    {
        int defenders = target.NumWorkers;
        if (defenders < 1) defenders = 1;
        return defenders / 2 + 1;
    }

    // Rough estimate of turns to gather missing construction resources at ~1 unit/turn.
    static int EstimateBuildHorizon(AI_TownState town, BuildingDefn bd)
    {
        int turns = 1;
        if (bd.ConstructionRequirements == null) return turns;
        for (int r = 0; r < bd.ConstructionRequirements.Count; r++)
        {
            var req = bd.ConstructionRequirements[r];
            int have = town.PlayerTownInventory.TryGetValue(req.Good.GoodType, out int v) ? v : 0;
            int need = req.Amount - have;
            if (need > 0) turns += need;
        }
        return turns;
    }

    static bool PlayerOwnsBuildingType(PlayerData player, AI_TownState aiTownState, BuildingDefn bd)
    {
        for (int i = 0; i < aiTownState.NumNodes; i++)
        {
            var node = aiTownState.Nodes[i];
            if (node.OwnedBy != player) continue;
            if (!node.HasBuilding) continue;
            if (node.BuildingDefn == bd) return true;
        }
        return false;
    }
}
