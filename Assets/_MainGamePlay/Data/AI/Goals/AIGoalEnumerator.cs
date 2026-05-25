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
        EnumerateStrategicUpgradeGoals(player, nodes, numNodes, aggression, economicExpansion, goalsOut, goalPool);
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

            // Personality mix:
            //   Enemy-owned target -> 50/50 aggression+territory (taking from a known enemy
            //     and gaining new ground both apply).
            //   Neutral target     -> danger-blended terr/agg via GetCapturePersonalityMultiplier.
            //     Safe interior neutral: pure territory weight. Contested neutral surrounded by
            //     enemies / contested-neutrals (e.g. a peak chokepoint in no-man's-land):
            //     collapses to aggression weight, so a pacifist AI's CaptureNode goal for a
            //     far chokepoint drops below MinGoalValue and stops driving resource demand.
            float personalityMix = node.OwnedBy != null
                ? (aggression * 0.5f + territoryExpansion * 0.5f)
                : AI_ActionHeuristics.GetCapturePersonalityMultiplier(player, node);

            // Chokepoint amplifier: a CaptureNode goal targeting a structural chokepoint is
            // worth significantly more than capturing a leaf node. Scaled by aggressiveness
            // because grabbing a chokepoint is an offensive/force-projection move -- a
            // pacifist (agg=0) shouldn't be drawn to a distant chokepoint any more than to
            // any other neutral; an aggressor (agg=2) should be drawn double. Without this,
            // a def=2 / agg=0 AI at game start would rush its entire starting force across
            // multiple hops to grab the peak chokepoint, which is the opposite of defensive
            // behavior. Surfaces in the goal-list dump as an inflated `val=` so the user
            // can tell at a glance which captures the AI considers strategically important.
            float chokepointMult = AI_ActionHeuristics.GetChokepointMultiplier(node, AI_ActionHeuristics.ChokepointGoalScale, aggression);
            float value = (captureBaseValue + typeBonus) * personalityMix * chokepointMult;
            if (value < MinGoalValue) continue;

            int horizon = EstimateCaptureHorizon(node);

            var goal = goalPool.Count > 0 ? goalPool.Pop() : new AIGoal();
            goal.Reset();
            goal.Type = AIGoalType.CaptureNode;
            goal.TargetNode = node;
            goal.Value = value;
            goal.HorizonTurns = horizon;
            if (node.OwnedBy == null)
            {
                // Surface the danger-blend in the dump so it's obvious WHY a peaceful AI
                // declines a contested neutral (mix collapses toward AggressivenessWeight)
                // while still pursuing safe ones (mix stays near TerritoryExpansionWeight).
                int exposure = AI_ActionHeuristics.GetNeutralCaptureExposure(node, player);
                reason += $" mix={personalityMix:F2} exposure={exposure}";
            }
            if (node.ChokepointScore > 0.05f)
                reason += $" (choke={node.ChokepointScore:F2})";
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
            // Use REAL game ownership, not the mirror's PendingCaptureBy lie. The lie is
            // there to dedupe offensive sends (Capture/Construct/Attack) onto a target
            // we're already capturing; it should NOT promote that in-flight capture target
            // to "owned territory needing defense" -- otherwise an AI that already
            // committed workers to a contested neutral keeps generating high-value
            // DefendFrontier goals for the not-yet-owned node and may pile on additional
            // buttress sends, compounding a commitment the danger-blended capture
            // multiplier is specifically trying to discourage.
            if (node.RealNode == null || node.RealNode.OwnedBy != player) continue;

            int enemyForce = AI_ActionHeuristics.GetFrontierPressure(node);
            if (enemyForce <= 0) continue;

            // Value scales with the TOTAL defensive deficit (frontier + upgrade-overload).
            // Routing through GetTotalDefensiveDeficit means a low-tier choke under heavy
            // pressure -- where the strategic move is upgrade-after-overload, not raw
            // overstack -- gets a goal value proportional to how many workers the AI needs
            // to assemble for that upgrade. Previously the goal used only frontier deficit
            // (which clamps at MaxWorkers), reporting "deficit 0 vs enemy force 32" on a
            // node a defensive AI was desperately trying to reinforce, and the goal value
            // stayed at the floor (~4) while CaptureNode goals dominated demand.
            int deficit = AI_ActionHeuristics.GetTotalDefensiveDeficit(node, player);
            // Chokepoint amplifier on defense: losing a chokepoint is structurally worse than
            // losing a leaf, so the same enemy pressure on a chokepoint produces a much
            // higher-value DefendFrontier goal. Scaled by defense (symmetric with the
            // offensive choke amp on CaptureNode being scaled by aggression): a defensive AI
            // cares disproportionately about keeping its own chokes; an aggressive AI sees
            // them as just-another-frontier and would rather be attacking.
            float chokepointMult = AI_ActionHeuristics.GetChokepointMultiplier(node, AI_ActionHeuristics.ChokepointDefenseScale, defense);
            float value = (1f + deficit) * defendValuePerWorker * defense * chokepointMult;
            if (value < MinGoalValue) continue;

            var goal = goalPool.Count > 0 ? goalPool.Pop() : new AIGoal();
            goal.Reset();
            goal.Type = AIGoalType.DefendFrontier;
            goal.TargetNode = node;
            goal.Value = value;
            goal.HorizonTurns = 1; // defense is always immediate
            string reason = "deficit " + deficit + " vs enemy force " + enemyForce;
            if (node.ChokepointScore > 0.05f)
                reason += $" (choke={node.ChokepointScore:F2})";
            goal.DebugReason = reason;
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

    const float strategicUpgradeBaseValue = 4f;
    const float strategicUpgradeResourceBonus = 3f;

    static void EnumerateStrategicUpgradeGoals(
        PlayerData player,
        AI_NodeState[] nodes,
        int numNodes,
        float aggression,
        float economicExpansion,
        List<AIGoal> goalsOut,
        Stack<AIGoal> goalPool)
    {
        // Mix of aggression (the upgrade enables an attack) and economic expansion (upgrading
        // is itself an economic action). Equal blend; both must be > 0 for the goal to matter.
        float weight = (aggression + economicExpansion) * 0.5f;
        if (weight <= 0f) return;

        for (int i = 0; i < numNodes; i++)
        {
            var node = nodes[i];
            if (node.OwnedBy != player) continue;
            if (node.BuildingDefn == null || !node.BuildingDefn.CanBeUpgraded) continue;
            if (node.NumWorkers < node.MaxWorkers) continue;

            int currentCap = node.MaxWorkers;
            int postUpgradeCap = currentCap * 2;
            int forceNow = currentCap / 2;
            int forcePostUpgrade = postUpgradeCap / 2;

            AI_NodeState bestTarget = null;
            float bestTargetBonus = 0f;
            var neighbors = node.NeighborNodes;
            for (int n = 0; n < neighbors.Count; n++)
            {
                var nb = neighbors[n];
                if (nb.OwnedBy == player) continue;
                if (nb.NumWorkers <= 0) continue;
                int needed = (int)System.Math.Ceiling(nb.NumWorkers * 1.5f);
                if (forceNow >= needed) continue;
                if (forcePostUpgrade < needed) continue;
                float bonus = strategicUpgradeBaseValue;
                if (nb.IsResourceNode || nb.CanGenerateWorkers)
                    bonus += strategicUpgradeResourceBonus;
                if (bonus > bestTargetBonus)
                {
                    bestTargetBonus = bonus;
                    bestTarget = nb;
                }
            }

            if (bestTarget == null) continue;

            float value = bestTargetBonus * weight;
            if (value < MinGoalValue) continue;

            var goal = goalPool.Count > 0 ? goalPool.Pop() : new AIGoal();
            goal.Reset();
            goal.Type = AIGoalType.StrategicUpgrade;
            goal.TargetNode = node;
            goal.Value = value;
            // Upgrading is immediate (one ply) but the payoff requires accumulation; use a
            // small horizon to keep urgency comparable to capture goals.
            goal.HorizonTurns = 3;
            goal.DebugReason = $"upgrade #{node.NodeId} to threaten #{bestTarget.NodeId}";
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
