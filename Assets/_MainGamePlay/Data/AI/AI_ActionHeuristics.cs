using System.Collections.Generic;
using System;
using UnityEngine;

public enum AIHeuristicActionType
{
    Upgrade,
    Buttress,
    Build,
    Capture,
    Attack,
}

/// <summary>
/// Heuristic scoring and territory analysis used by the recursive search.
/// Returns bonuses scaled to be meaningful alongside EvaluateScore() results (~0-3 range).
/// </summary>
public static class AI_ActionHeuristics
{
    const float HeuristicScoreScale = 3f;

    const float nearbyEnemiesScalingFactor = 1f;
    const float buildingResourceScalingFactor = 5f;
    const float buildingStrategicScalingFactor = 8f;
    const float territoryEdgeScalingFactor = 10f;
    const float insufficientWorkersScalingFactor = 10f;

    const float upgradeNodeMinScore = 10f;
    const float upgradeNodeMaxScore = 40f;
    // buildBuildingMinScore was 20 back when GetBuildHeuristic's normalization used the buggy
    // (clamped - 10) / 30 form, which produced a floor of ~1.0 for any raw <= 20. Now that the
    // normalization uses the true min/max, 20 was clamping out every "ordinary" build candidate
    // (Camp/Outpost rawValue 8, defensive interior 8, defensive edge 12) to heuristic 0 -- so
    // the AI literally stopped building anything except high-shortage gatherers. 5 puts the
    // typical base values comfortably above the floor without saturating high-shortage cases.
    const float buildBuildingMinScore = 5f;
    const float buildBuildingMaxScore = 40f;
    const float buttressNodeMinScore = 20f;
    const float buttressNodeMaxScore = 40f;
    const float attackNodeMinScore = 20f;
    const float attackNodeMaxScore = 40f;

    // Fallback "I always want some" floor when ResourceDemand has no entry for a GoodType
    // (e.g. early game when no building requires that resource). Keeps gatherers from going
    // to zero appeal in pathological maps.
    const int resourceDemandFloor = 5;

    // Each unit of resource shortage on an enemy node we can capture adds this much raw
    // attack score. With min/max attack score = 20/40, a shortage of ~2 already maxes out
    // the bonus, so this is intentionally aggressive on critical-resource captures.
    const float attackResourceShortageScalingFactor = 5f;

    public static float GetPersonalityMultiplier(PlayerData player, AIHeuristicActionType actionType)
    {
        var aiDefn = player.AIDefn;
        if (aiDefn == null) return 1f;

        return actionType switch
        {
            AIHeuristicActionType.Upgrade => aiDefn.EconomicExpansionWeight,
            AIHeuristicActionType.Build => aiDefn.EconomicExpansionWeight,
            AIHeuristicActionType.Capture => aiDefn.TerritoryExpansionWeight,
            AIHeuristicActionType.Buttress => aiDefn.DefenseWeight,
            AIHeuristicActionType.Attack => aiDefn.AggressivenessWeight,
            _ => 1f,
        };
    }

    // How willing this AI is to upgrade a threatened node without first overloading it.
    // 1.0 = aggressive (upgrades immediately, accepts halved garrison under fire)
    // 0.0 = cautious (demands full overload buffer before upgrading under threat)
    // Derived from the ratio of AggressivenessWeight to DefenseWeight.
    public static float GetUpgradeRiskTolerance(PlayerData player)
    {
        var aiDefn = player.AIDefn;
        if (aiDefn == null) return 0.5f;
        float sum = aiDefn.AggressivenessWeight + aiDefn.DefenseWeight;
        if (sum <= 0f) return 0.5f;
        return Mathf.Clamp01(aiDefn.AggressivenessWeight / sum);
    }

    public static float ApplyHeuristicAndPersonality(float simulationScore, float heuristicBonus, PlayerData player, AIHeuristicActionType actionType)
    {
        // Personality now scales the full final score, not just the heuristic bonus. With the
        // earlier "bonus only" formula, a sufficiently good simulationScore (e.g. capturing a
        // resource-rich enemy node) would still dominate even when AggressivenessWeight = 0,
        // because simulationScore was unaffected by personality. Applying it to the full score
        // makes weights of 0/2 actually disable/double the action's appeal as the test harness
        // expects.
        float personality = GetPersonalityMultiplier(player, actionType);
        return (simulationScore + heuristicBonus) * personality;
    }

    // Goal-driven resource demand: every active strategic goal expands into the buildings
    // that would help fulfill it; each such building's construction requirements contribute
    // to demand, scaled by the goal's urgency (Value / HorizonTurns).
    //
    // This replaces the previous "sum every buildable building's reqs unconditionally"
    // formulation, which couldn't differentiate between an aggressive AI (wants Barracks ->
    // wants wood) and a peaceful one (doesn't care about Barracks). Now the same map can
    // produce different demand vectors for different personalities because the goal list
    // they generate is different.
    //
    // Capture goals collectively share a single Barracks contribution (one Barracks unlocks
    // all of them) rather than multiplying demand by goal count. EconomicTier goals each
    // contribute their own building's reqs, since each building is a distinct outcome.
    public static void UpdateResourceDemand(
        AI_TownState town,
        BuildingDefn[] buildableBuildingDefns,
        int numBuildableBuildingDefns,
        List<AIGoal> activeGoals)
    {
        var demand = town.ResourceDemand;
        demand.Clear();

        float totalCaptureUrgency = 0f;
        for (int i = 0; i < activeGoals.Count; i++)
        {
            var goal = activeGoals[i];
            float urgency = goal.Value / System.Math.Max(1, goal.HorizonTurns);

            switch (goal.Type)
            {
                case AIGoalType.EconomicTier:
                    AddBuildingReqsToDemand(demand, goal.TargetBuilding, urgency);
                    break;

                case AIGoalType.CaptureNode:
                    // Aggregate; folded into a single Barracks contribution below.
                    totalCaptureUrgency += urgency;
                    break;

                case AIGoalType.DefendFrontier:
                    // v1: defense doesn't drive resource demand directly. Buttress (the
                    // recursive task) handles it via worker shuffling. v2 could add demand
                    // for defensive buildings.
                    break;

                case AIGoalType.MaintainStockpile:
                    AddStockpileToDemand(demand, town, goal);
                    break;
            }
        }

        if (totalCaptureUrgency > 0f)
        {
            var barracksDefn = FindBuildingDefnOfType(buildableBuildingDefns, numBuildableBuildingDefns, BuildingType.Barracks);
            if (barracksDefn != null)
                AddBuildingReqsToDemand(demand, barracksDefn, totalCaptureUrgency);
        }
    }

    static void AddStockpileToDemand(Dictionary<GoodType, int> demand, AI_TownState town, AIGoal goal)
    {
        if (goal.TargetGoodType == GoodType.Unset) return;

        float urgency = goal.Value / System.Math.Max(1, goal.HorizonTurns);
        int target = town.player.AIDefn != null ? town.player.AIDefn.GetTargetStockpile(goal.TargetGoodType) : 0;
        int have = town.PlayerTownInventory.TryGetValue(goal.TargetGoodType, out int v) ? v : 0;
        int deficit = target - have;
        if (deficit <= 0) return;

        // Demand at least the stockpile target, scaled up by urgency so critical shortfalls
        // push harder toward gatherers and worker staffing.
        int contribution = Math.Max(deficit, (int)(target * urgency * 0.1f));
        demand.TryGetValue(goal.TargetGoodType, out int prev);
        demand[goal.TargetGoodType] = prev + contribution;
    }

    static void AddBuildingReqsToDemand(Dictionary<GoodType, int> demand, BuildingDefn bd, float urgency)
    {
        if (bd == null || bd.ConstructionRequirements == null) return;

        for (int r = 0; r < bd.ConstructionRequirements.Count; r++)
        {
            var req = bd.ConstructionRequirements[r];
            var goodType = req.Good.GoodType;

            // Always at least the literal cost (so even minimum-urgency goals still register
            // their building's construction footprint), scaled up linearly with urgency.
            // The 0.1 factor keeps urgency~10 producing roughly 1x req.Amount of extra demand.
            int contribution = (int)(req.Amount * urgency * 0.1f);
            if (contribution < req.Amount) contribution = req.Amount;

            demand.TryGetValue(goodType, out int prev);
            demand[goodType] = prev + contribution;
        }
    }

    static BuildingDefn FindBuildingDefnOfType(BuildingDefn[] defns, int count, BuildingType type)
    {
        for (int i = 0; i < count; i++)
            if (defns[i].BuildingType == type) return defns[i];
        return null;
    }

    // Shortage of a resource = max(0, demand - currentlyOwned). Falls back to a small floor
    // when nothing in the buildable catalog needs the resource so gatherers still register
    // some appeal.
    public static int GetResourceShortage(AI_TownState town, GoodType goodType)
    {
        if (goodType == GoodType.Unset) return 0;
        int wanted = town.ResourceDemand.TryGetValue(goodType, out int d) ? d : resourceDemandFloor;
        if (wanted < resourceDemandFloor) wanted = resourceDemandFloor;

        // Also treat PlayerAIDefn stockpile targets as implicit demand so the AI keeps
        // producing even when no construction goals are active.
        int stockpileTarget = town.player.AIDefn != null ? town.player.AIDefn.GetTargetStockpile(goodType) : 0;
        if (stockpileTarget > wanted) wanted = stockpileTarget;

        int owned = town.PlayerTownInventory.TryGetValue(goodType, out int o) ? o : 0;
        int shortage = wanted - owned;
        return shortage < 0 ? 0 : shortage;
    }

    // How many workers this node should have to meet current resource demand. Scales up
    // with shortage so 1/10 workers in a forest is not "good enough" when wood is needed.
    public static int GetDesiredWorkersForResourceNode(AI_TownState town, AI_NodeState node)
    {
        if (!node.CanBeGatheredFrom && !node.CanGoGatherResources) return 0;
        if (node.MaxWorkers <= 0) return 0;

        GoodType resource = node.CanBeGatheredFrom
            ? node.ResourceGatheredFromThisNode
            : node.ResourceThisNodeCanGoGather;
        int shortage = GetResourceShortage(town, resource);
        if (shortage <= 0) return Math.Min(1, node.NumWorkers);

        int wanted = town.ResourceDemand.TryGetValue(resource, out int d) ? d : resourceDemandFloor;
        float fillRatio = Mathf.Clamp01((float)shortage / Mathf.Max(1, wanted));
        int desired = Math.Max(1, (int)Math.Ceiling(node.MaxWorkers * fillRatio));
        return Math.Min(desired, node.MaxWorkers);
    }

    public static bool NodeProducesResource(AI_NodeState node, GoodType goodType)
    {
        if (goodType == GoodType.Unset) return false;
        if (node.CanBeGatheredFrom && node.ResourceGatheredFromThisNode == goodType) return true;
        if (node.CanGoGatherResources && node.ResourceThisNodeCanGoGather == goodType) return true;
        return false;
    }

    public static bool NeutralNeighborTouchesEnemy(AI_NodeState neutral, PlayerData player)
    {
        if (neutral.OwnedBy != null) return false;
        var neighbors = neutral.NeighborNodes;
        for (int i = 0; i < neighbors.Count; i++)
        {
            var nb = neighbors[i];
            if (nb.OwnedBy != null && nb.OwnedBy != player)
                return true;
        }
        return false;
    }

    public static int GetFrontierPressure(AI_NodeState node) =>
        node.NumEnemiesInNeighborNodes + node.NumContestedNeutralWorkersNearby;

    // How many "virtual enemies" each unit of AttackHeat translates to. The buttress and
    // frontier heuristics treat the result as if it were live enemy force adjacent to the
    // node, so this controls how aggressively past attacks bias the AI toward reinforcing
    // a chokepoint when the current snapshot of enemy neighbors looks calm.
    const float attackHeatToPressureMultiplier = 1.5f;

    // AttackHeat level above which a defender node is considered "in emergency" by
    // GetButtressSourceNode even if effective pressure is otherwise low. Tuned so a single
    // recent attacker doesn't immediately trip emergency, but a couple in quick succession
    // does. Heat decays at AttackHeatDecayPerSecond (TownData) so the threshold falls off
    // naturally when attacks stop.
    public const float AttackHeatEmergencyThreshold = 2f;

    // AttackHeat level above which an owned node is treated as a chokepoint that should
    // retain workers for defense (used in offensive willing-to-send guard).
    public const float AttackHeatChokepointThreshold = 1f;

    // Effective frontier pressure: live snapshot pressure plus a heat-driven inflation so
    // chokepoint nodes that have been attacked repeatedly (but currently look "fine" because
    // the previous wave has been killed and the next wave is in transit) still register as
    // needing defense. Also adds 1:1 the count of in-flight hostile workers targeting this
    // node so the AI can prepare for telegraphed waves BEFORE they begin landing -- AttackHeat
    // alone is post-hoc and only fires after the first attacker resolves.
    public static int GetEffectiveFrontierPressure(AI_NodeState node)
    {
        int snapshot = GetFrontierPressure(node);
        int heatBonus = node.AttackHeat > 0f
            ? (int)System.Math.Ceiling(node.AttackHeat * attackHeatToPressureMultiplier)
            : 0;
        return snapshot + heatBonus + node.IncomingHostileWorkers;
    }

    public static void UpdateTerritoryDetails(AI_TownState town, PlayerData player)
    {
        var nodes = town.Nodes;
        int numNodes = nodes.Length;
        for (int i = 0; i < numNodes; i++)
        {
            var node = nodes[i];
            node.IsOnTerritoryEdge = false;
            node.NumEnemiesInNeighborNodes = 0;
            node.NumContestedNeutralWorkersNearby = 0;
        }

        for (int i = 0; i < numNodes; i++)
        {
            var node = nodes[i];
            if (node.OwnedBy != player) continue;

            var neighbors = node.NeighborNodes;
            for (int n = 0; n < neighbors.Count; n++)
            {
                var nn = neighbors[n];
                if (nn.OwnedBy != player)
                {
                    if (nn.OwnedBy != null)
                        node.NumEnemiesInNeighborNodes += nn.NumWorkers;
                    else if (NeutralNeighborTouchesEnemy(nn, player))
                        node.NumContestedNeutralWorkersNearby += nn.NumWorkers;
                    node.IsOnTerritoryEdge = true;
                }
            }
        }

        // Neutral nodes adjacent to our territory are expansion-frontier targets for build heuristics
        for (int i = 0; i < numNodes; i++)
        {
            var node = nodes[i];
            if (node.OwnedBy != null) continue;

            var neighbors = node.NeighborNodes;
            for (int n = 0; n < neighbors.Count; n++)
            {
                if (neighbors[n].OwnedBy == player)
                {
                    node.IsOnTerritoryEdge = true;
                    break;
                }
            }
        }
    }

    public static int GetWorkersWillingToSend(AI_NodeState node, int minWorkersInNodeBeforeConsideringSendingAnyOut)
    {
        if (node.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut) return 0;
        if (node.NumWorkers < node.MaxWorkers * 3f / 4f) return 0;
        // Chokepoint drain guard: a node that's been getting attacked is a chokepoint -- don't
        // drain it to launch attacks/captures elsewhere. Solves the observed pathology where
        // Green's chokepoint #3 kept losing workers to offensive sends despite being under
        // sustained attack itself.
        if (node.AttackHeat >= AttackHeatChokepointThreshold) return 0;
        return node.NumWorkers / 2;
    }

    // When a frontier node is under real pressure, allow sources below 75% capacity to pitch in.
    // The destAttackHeat parameter lets the caller signal "destination is a chokepoint that's
    // been getting attacked even if the current snapshot looks quiet". In that case we drop
    // the source-capacity threshold from 75% to 50% so a mid-staffed interior node can still
    // reinforce a hot frontier. Emergency cases (effective pressure exceeds the destination's
    // garrison) remain fully relaxed: source can give down to its minimum garrison.
    public static int GetWorkersWillingToSendForDefense(AI_NodeState node, int minWorkersInNodeBeforeConsideringSendingAnyOut, bool emergency, float destAttackHeat = 0f)
    {
        if (node.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut) return 0;
        if (emergency)
            return Math.Max(1, node.NumWorkers - minWorkersInNodeBeforeConsideringSendingAnyOut + 1);

        // Destination has chokepoint-level heat but isn't in emergency: relax source threshold
        // to 50% so mid-staffed neighbors can pitch in without waiting to hit 75%.
        float capacityThreshold = destAttackHeat >= AttackHeatChokepointThreshold ? 0.5f : 0.75f;
        if (node.NumWorkers < node.MaxWorkers * capacityThreshold) return 0;
        return node.NumWorkers / 2;
    }

    public static int GetTargetForceWithOverkill(int threat, float overkillMultiplier)
    {
        if (threat <= 0) return 1;
        return Math.Max(1, (int)Math.Ceiling(threat * overkillMultiplier));
    }

    // Minimum workers to commit in one capture dispatch. Prevents drip-feeding 1 worker per
    // AI decision tick on safe neutrals; capped by willing so a single source still sends
    // the largest batch it can (e.g. 5) rather than failing until multi-source can muster 10.
    public const int MinCaptureWaveSize = 10;

    // Realtime: a neutral already has an in-flight capture wave from this player.
    public static bool IsCaptureAlreadyCommitted(AI_NodeState toNode, PlayerData player) =>
        toNode?.RealNode != null
        && toNode.RealNode.PendingCaptureBy == player
        && toNode.RealNode.OwnedBy == null;

    public static int GetCaptureDispatchSize(int threat, float overkillMultiplier, int willing)
    {
        int threatBased = GetTargetForceWithOverkill(threat, overkillMultiplier);
        return Math.Max(threatBased, Math.Min(MinCaptureWaveSize, willing));
    }

    // Multi-source flanking on a shared gateway neutral: garrison-only sizing plus a small
    // overkill buffer is enough; full adjacent-enemy stacking made capture unplanable.
    public static int GetGatewayCaptureTargetForce(AI_NodeState target, PlayerData player, float overkillMultiplier, int numSources)
    {
        int threat = GetGatewayCaptureThreat(target, player);
        int targetForce = GetTargetForceWithOverkill(threat, overkillMultiplier);
        if (numSources >= 2 && NeutralNeighborTouchesEnemy(target, player))
        {
            int relaxed = threat + Math.Max(1, (int)Math.Ceiling(overkillMultiplier));
            targetForce = Math.Min(targetForce, relaxed);
        }
        return Math.Max(targetForce, MinCaptureWaveSize);
    }

    public static int GetAdjacentEnemyThreat(AI_NodeState node, PlayerData player)
    {
        int threat = 0;
        var neighbors = node.NeighborNodes;
        for (int n = 0; n < neighbors.Count; n++)
        {
            var neighbor = neighbors[n];
            if (neighbor.OwnedBy != null && neighbor.OwnedBy != player)
                threat += neighbor.NumWorkers;
        }
        return threat;
    }

    public static int GetNeutralCaptureThreat(AI_NodeState toNode, PlayerData player)
    {
        int threat = toNode.OwnedBy == null ? toNode.NumWorkers : 0;
        return threat + GetAdjacentEnemyThreat(toNode, player);
    }

    // For a neutral gateway we share with an enemy (e.g. #11 between Red and Blue): sizing a
    // capture only needs to beat the garrison ON the node, not every enemy worker one hop away.
    // Counting adjacent enemies made Red need 29 workers when it only had 14 and gave up on
    // capture while buttress-shuffling forever.
    public static int GetGatewayCaptureThreat(AI_NodeState toNode, PlayerData player)
    {
        if (toNode.OwnedBy != null) return GetNeutralCaptureThreat(toNode, player);
        if (!NeutralNeighborTouchesEnemy(toNode, player)) return GetNeutralCaptureThreat(toNode, player);
        foreach (var nb in toNode.NeighborNodes)
            if (nb.OwnedBy == player) return Math.Max(0, toNode.NumWorkers);
        return GetNeutralCaptureThreat(toNode, player);
    }

    // Workers this owned node should hold given current frontier pressure. Uses effective
    // pressure (snapshot + heat) so chokepoints under sustained attack are sized for what
    // they've BEEN absorbing, not just what's visible at the current instant.
    public static int GetDesiredFrontierWorkers(AI_NodeState node)
    {
        int pressure = GetEffectiveFrontierPressure(node);
        if (pressure <= 0) return 0;
        return Math.Min(node.MaxWorkers, Math.Max(1, pressure));
    }

    public static int GetFrontierWorkerDeficit(AI_NodeState node) =>
        Math.Max(0, GetDesiredFrontierWorkers(node) - node.NumWorkers);

    public static bool NeedsFrontierButtress(AI_NodeState node)
    {
        const int minDeficit = 2;
        return GetFrontierWorkerDeficit(node) >= minDeficit;
    }

    public static bool IsUnderstaffedFrontier(AI_NodeState node) =>
        node.IsOnTerritoryEdge
        && node.MaxWorkers > 0
        && node.NumWorkers < node.MaxWorkers * understaffedFrontierThreshold;

    // Size a neutral capture: 1 worker on safe frontiers; enough to clear unowned garrison on
    // the target and hold against adjacent enemy garrisons on contested ones.
    public static bool TryGetCaptureWorkersToSend(AI_NodeState fromNode, AI_NodeState toNode, PlayerData player, int minWorkersInNodeBeforeConsideringSendingAnyOut, float overkillMultiplier, out int numToSend)
    {
        numToSend = 0;
        if (fromNode.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut) return false;

        int willing = GetWorkersWillingToSend(fromNode, minWorkersInNodeBeforeConsideringSendingAnyOut);
        if (willing <= 0) return false;

        int threat = GetNeutralCaptureThreat(toNode, player);
        int target = GetCaptureDispatchSize(threat, overkillMultiplier, willing);
        if (willing < target) return false;

        numToSend = target;
        return true;
    }

    // Maximum raw bonus overcrowding alone can contribute. Sized so a node at 200% capacity
    // lands roughly in the middle of the heuristic band (raw ~26, h ~1.6) -- enough signal
    // to rank upgrade as an attractive option without saturating to 3.0 and thereby pruning
    // away every Construct/Build alternative via branch-and-bound. Only a genuine defensive
    // imperative below (overcrowded AND threatened) should drive the heuristic to max.
    const float overcrowdingMaxRawBonus = 15f;

    // Raw bonus per neighboring target that becomes plausibly attackable after upgrading.
    // Resource/worker-generating nodes are worth more; ordinary territory captures less.
    const float upgradeAttackPotentialBonusPerTarget = 6f;
    const float upgradeAttackPotentialResourceBonus = 4f;

    // Overkill multiplier used when judging "could I beat this target with the post-upgrade
    // accumulated workforce?". Conservative (1.5x defender) so the AI doesn't upgrade based
    // on barely-marginal plans.
    const float upgradeAttackPotentialOverkillMultiplier = 1.5f;

    public static float GetUpgradeHeuristic(AI_NodeState node, float riskTolerance = 0.5f)
    {
        if (node.NumWorkers < node.MaxWorkers) return 0f;

        float rawValue = upgradeNodeMinScore * 1.1f;

        // Overcrowding signal: quadratic in PERCENTAGE excess (not absolute count) so the
        // heuristic doesn't depend on a building's MaxWorkers scale. The previous formulation
        // added Pow(numExcessiveWorkers, 2) on top, which let a 10-cap Camp with 10 excess
        // workers saturate the heuristic at 3.0 just from being overcrowded -- which is why
        // Green's first move was always Upgrade #9: peer Construct/Build alternatives were
        // then pruned by ShouldPruneByHeuristic before they could even simulate.
        int numExcessiveWorkers = node.NumWorkers - node.MaxWorkers;
        if (numExcessiveWorkers > 0)
        {
            float percentExcessive = (float)numExcessiveWorkers / node.MaxWorkers;
            rawValue += Mathf.Pow(percentExcessive, 2) * overcrowdingMaxRawBonus;
        }

        // Defensive imperative: a frontier node at/above capacity under hostile pressure NEEDS
        // a capacity bump. Uses effective pressure so chokepoints under sustained attack still
        // fire even when the current snapshot looks light. No upper-cap on worker count — an
        // overloaded node preparing for upgrade should still benefit from this signal.
        bool defensiveImperative = node.IsOnTerritoryEdge
            && GetEffectiveFrontierPressure(node) > 0;

        // Outnumbered penalty scaled by personality: a cautious AI (riskTolerance~0) gets the
        // full penalty when NOT under defensive imperative and a reduced penalty even when IS
        // under imperative (it wants to wait for overload). A risky AI (riskTolerance~1) gets
        // no penalty under imperative and reduced penalty otherwise — it's willing to upgrade
        // immediately and accept the halved garrison.
        int outnumberedBy = node.NumEnemiesInNeighborNodes - node.NumWorkers;
        if (outnumberedBy > 0)
        {
            float penaltyScale = defensiveImperative ? (1f - riskTolerance) : 1f;
            rawValue -= outnumberedBy * outnumberedBy * nearbyEnemiesScalingFactor * penaltyScale;
        }

        if (defensiveImperative)
            rawValue += 35;

        // Forward-looking "attack potential unlocked" signal: if the current cap can't muster
        // enough force to attack a nearby enemy/neutral target but the post-upgrade cap could,
        // that target becomes the strategic reason to upgrade. Without this signal the
        // recursive search rarely discovers upgrade-then-attack chains -- the search would
        // have to spend depth on upgrade (immediately halving workers) and then several more
        // plies accumulating before the payoff is visible to EvaluateScore.
        rawValue += GetUpgradeAttackPotentialBonus(node);

        float clampedRawValue = Mathf.Clamp(rawValue, upgradeNodeMinScore, upgradeNodeMaxScore);
        float normalizedValue = (clampedRawValue - upgradeNodeMinScore) / (upgradeNodeMaxScore - upgradeNodeMinScore);
        return normalizedValue * HeuristicScoreScale;
    }

    static float GetUpgradeAttackPotentialBonus(AI_NodeState node)
    {
        int currentCap = node.MaxWorkers;
        if (currentCap <= 0) return 0f;
        int postUpgradeCap = currentCap * 2;

        float total = 0f;
        var neighbors = node.NeighborNodes;
        for (int i = 0; i < neighbors.Count; i++)
        {
            var nb = neighbors[i];
            if (nb.OwnedBy == node.OwnedBy) continue;
            int defenders = nb.NumWorkers;
            if (defenders <= 0) continue;

            // Plausible attack force = ceil(half the cap), the same heuristic used by
            // GetWorkersWillingToSend (NumWorkers / 2 once at full cap).
            int forceNow = currentCap / 2;
            int forcePostUpgrade = postUpgradeCap / 2;
            int needed = (int)Math.Ceiling(defenders * upgradeAttackPotentialOverkillMultiplier);

            // Already attackable now -> upgrade is not the unlocker for this target.
            if (forceNow >= needed) continue;
            // Even post-upgrade can't beat it -> upgrade alone wouldn't unlock it.
            if (forcePostUpgrade < needed) continue;

            float perTarget = upgradeAttackPotentialBonusPerTarget;
            if (nb.IsResourceNode || nb.CanGenerateWorkers)
                perTarget += upgradeAttackPotentialResourceBonus;
            total += perTarget;
        }
        return total;
    }

    const float resourceStaffingScalingFactor = 8f;

    // A frontier node below this fill ratio is considered dangerously understaffed
    // regardless of whether adjacent enemies are currently visible (they may have
    // already departed as in-flight attackers).
    const float understaffedFrontierThreshold = 0.25f;

    public static float GetButtressHeuristic(AI_TownState town, AI_NodeState toNode)
    {
        float rawValue = 0f;
        float riskTolerance = GetUpgradeRiskTolerance(town.player);

        int frontierDeficit = GetFrontierWorkerDeficit(toNode);
        bool understaffedFrontier = toNode.IsOnTerritoryEdge
            && toNode.MaxWorkers > 0
            && toNode.NumWorkers < toNode.MaxWorkers * understaffedFrontierThreshold;
        bool needsUpgradeOverload = NeedsUpgradeOverloadButtress(toNode, riskTolerance);

        if (frontierDeficit <= 0 && !understaffedFrontier && !NeedsResourceStaffingButtress(town, toNode) && !needsUpgradeOverload)
            return 0f;

        if (frontierDeficit > 0)
            rawValue += frontierDeficit * frontierDeficit * nearbyEnemiesScalingFactor;

        if (toNode.IsOnTerritoryEdge && frontierDeficit > 0)
            rawValue += territoryEdgeScalingFactor;

        if (understaffedFrontier)
        {
            float capacityDeficit = toNode.MaxWorkers - toNode.NumWorkers;
            rawValue += capacityDeficit * insufficientWorkersScalingFactor;
        }

        // Overload for upgrade: a frontier node at max capacity under pressure needs workers
        // beyond its current cap so that after upgrade (halves workers) it's still defensible.
        // This is a high-priority defensive action — weighted similarly to understaffed frontier.
        if (needsUpgradeOverload)
        {
            int desiredOverload = GetDesiredOverloadForUpgrade(toNode, riskTolerance);
            int overloadDeficit = desiredOverload - toNode.NumWorkers;
            rawValue += overloadDeficit * insufficientWorkersScalingFactor;
            rawValue += territoryEdgeScalingFactor;
        }

        // Staff resource-producing nodes when we need more output.
        if (NeedsResourceStaffingButtress(town, toNode))
        {
            int desired = GetDesiredWorkersForResourceNode(town, toNode);
            float workerDeficit = desired - toNode.NumWorkers;
            GoodType resource = toNode.CanBeGatheredFrom
                ? toNode.ResourceGatheredFromThisNode
                : toNode.ResourceThisNodeCanGoGather;
            int shortage = GetResourceShortage(town, resource);
            rawValue += 12f;
            rawValue += workerDeficit * workerDeficit * resourceStaffingScalingFactor * Mathf.Max(1f, shortage * 0.1f);
        }

        if (rawValue < buttressNodeMinScore) return 0f;

        float clampedRawValue = Mathf.Clamp(rawValue, buttressNodeMinScore, buttressNodeMaxScore);
        float normalizedValue = (clampedRawValue - buttressNodeMinScore) / (buttressNodeMaxScore - buttressNodeMinScore);
        return normalizedValue * HeuristicScoreScale;
    }

    // A frontier node at capacity with an upgradeable building under pressure needs workers
    // ABOVE max so that after upgrade (which halves workers) it remains defensible. Returns
    // the desired pre-upgrade worker count, or 0 if overloading isn't needed.
    //
    // riskTolerance (0-1): a cautious AI (0) demands full overload so post-upgrade garrison
    // matches the pressure. A risky AI (1) is fine upgrading at capacity — no overload needed.
    public static int GetDesiredOverloadForUpgrade(AI_NodeState node, float riskTolerance)
    {
        if (node.NumWorkers < node.MaxWorkers) return 0;
        if (node.BuildingDefn == null || !node.BuildingDefn.CanBeUpgraded) return 0;
        if (!node.IsOnTerritoryEdge) return 0;
        int pressure = GetEffectiveFrontierPressure(node);
        if (pressure <= 0) return 0;

        // A fully risky AI doesn't need any overload — it upgrades at capacity immediately.
        if (riskTolerance >= 1f) return 0;

        // After upgrade: maxWorkers doubles, workers halve. We want post-upgrade workers to
        // be at least enough to survive the pressure. Target: have enough pre-upgrade so that
        // (preUpgradeWorkers / 2) >= min(pressure, newMax/2). Effectively: overload to at
        // least 2 * min(pressure, MaxWorkers) capped at 2x current max (the post-upgrade cap).
        int postUpgradeMax = node.MaxWorkers * 2;
        int desiredPostUpgrade = Math.Min(pressure, postUpgradeMax / 2);
        int fullDesiredPreUpgrade = Math.Max(node.MaxWorkers + 1, desiredPostUpgrade * 2);
        fullDesiredPreUpgrade = Math.Min(fullDesiredPreUpgrade, postUpgradeMax);

        // Interpolate between "just at capacity" (risky) and full overload (cautious).
        // Ensure at least MaxWorkers+2 for partial-risk AIs so post-upgrade has > 1 worker.
        int minPreUpgrade = node.MaxWorkers + 2;
        int desiredPreUpgrade = (int)Mathf.Lerp(minPreUpgrade, fullDesiredPreUpgrade, 1f - riskTolerance);
        return Math.Max(minPreUpgrade, desiredPreUpgrade);
    }

    public static bool NeedsUpgradeOverloadButtress(AI_NodeState node, float riskTolerance)
    {
        int desired = GetDesiredOverloadForUpgrade(node, riskTolerance);
        return desired > 0 && node.NumWorkers < desired;
    }

    public static bool NeedsResourceStaffingButtress(AI_TownState town, AI_NodeState toNode)
    {
        if (!toNode.CanBeGatheredFrom && !toNode.CanGoGatherResources) return false;
        int desired = GetDesiredWorkersForResourceNode(town, toNode);
        if (toNode.NumWorkers >= desired) return false;
        GoodType resource = toNode.CanBeGatheredFrom
            ? toNode.ResourceGatheredFromThisNode
            : toNode.ResourceThisNodeCanGoGather;
        return GetResourceShortage(town, resource) > 0;
    }

    // Small post-normalization boost given to builds on empty neutral targets. Applied AFTER
    // clamping so it cannot saturate the heuristic the way the earlier in-rawValue +15 did.
    // This is the "constructing on a neutral literally claims a node" signal -- separate from
    // the building-type quality signal that the rawValue path already captures.
    const float neutralTargetBuildBonus = 0.5f;

    public static float GetBuildHeuristic(AI_TownState town, BuildingDefn buildingDefn, AI_NodeState toNode)
    {
        float rawValue = 0f;

        if (buildingDefn.IsDefensive)
        {
            if (toNode.IsOnTerritoryEdge)
                rawValue += buildingStrategicScalingFactor * 1.5f;
            else
                rawValue += buildingStrategicScalingFactor;
        }
        else if (buildingDefn.CanGatherResources)
        {
            // Goal-driven: appeal of a gatherer is proportional to how short we are on its
            // produced resource relative to what the buildable catalog demands. Replaces the
            // previous constant target of 20-of-everything which had no relationship to what
            // the player was actually trying to build.
            var gatherableResource = buildingDefn.ResourceThisNodeCanGoGather.GoodType;
            int shortage = GetResourceShortage(town, gatherableResource);
            rawValue += buildingResourceScalingFactor * shortage;
        }
        else
        {
            rawValue += buildingStrategicScalingFactor;
        }

        float clampedRawValue = Mathf.Clamp(rawValue, buildBuildingMinScore, buildBuildingMaxScore);
        float normalizedValue = (clampedRawValue - buildBuildingMinScore) / (buildBuildingMaxScore - buildBuildingMinScore);
        float result = normalizedValue * HeuristicScoreScale;

        // Territory-gain bonus for empty neutral targets. Adding here (after normalization)
        // means it bumps the final value by a fixed amount without saturating -- earlier I
        // added it to rawValue before clamp and every neutral candidate maxed out at the same
        // score, defeating top-K ordering and branch-and-bound pruning.
        if (toNode.OwnedBy == null)
            result += neutralTargetBuildBonus;

        return result;
    }

    public static float GetCaptureResourceNodeHeuristic(AI_TownState town, AI_NodeState targetNode, int workersToSend)
    {
        return GetAttackHeuristic(town, targetNode, workersToSend);
    }

    public static float GetCaptureNeutralNodeHeuristic(AI_TownState town, AI_NodeState targetNode, int workersToSend)
    {
        float h = GetAttackHeuristic(town, targetNode, workersToSend);
        if (NeutralNeighborTouchesEnemy(targetNode, town.player))
            h += 1.5f;
        return h;
    }

    public static float GetAttackHeuristic(AI_TownState town, AI_NodeState targetNode, int totalWorkersWillingToSend)
    {
        float rawValue = attackNodeMaxScore / 2f;

        // Prefer weaker targets and attacks where we have comfortable force advantage
        float forceRatio = totalWorkersWillingToSend / (float)Mathf.Max(1, targetNode.NumWorkers);
        if (forceRatio >= 1.5f)
            rawValue += 10f;
        else if (forceRatio >= 1f)
            rawValue += 5f;

        if (targetNode.HasBuilding)
            rawValue += 5f;

        // Resource-aware bonus: if the target node either gathers a resource (e.g. enemy
        // Woodcutter) or *is* a resource source (e.g. neutral/enemy Forest), and we are
        // short on that resource relative to what the buildable catalog demands, score the
        // capture much more highly. This is what turns "capture the forest because I need
        // wood for a Barracks" from accidental into intentional.
        GoodType targetResource = GoodType.Unset;
        if (targetNode.CanGoGatherResources)
            targetResource = targetNode.ResourceThisNodeCanGoGather;
        else if (targetNode.CanBeGatheredFrom)
            targetResource = targetNode.ResourceGatheredFromThisNode;

        if (targetResource != GoodType.Unset)
        {
            int shortage = GetResourceShortage(town, targetResource);
            rawValue += shortage * attackResourceShortageScalingFactor;
        }

        float clampedRawValue = Mathf.Clamp(rawValue, attackNodeMinScore, attackNodeMaxScore);
        float normalizedValue = (clampedRawValue - attackNodeMinScore) / (attackNodeMaxScore - attackNodeMinScore);
        return normalizedValue * HeuristicScoreScale;
    }

    public static AI_NodeState GetButtressSourceNode(AI_NodeState toNode, PlayerData player, int minWorkersInNodeBeforeConsideringSendingAnyOut)
    {
        // Use effective pressure for the emergency check so a chokepoint with high AttackHeat
        // (recently hammered, but currently quiet) is treated as an emergency and pulls workers
        // from sources below the normal 75% capacity threshold.
        bool emergency = GetEffectiveFrontierPressure(toNode) > toNode.NumWorkers
                         || toNode.NumContestedNeutralWorkersNearby > toNode.NumWorkers / 2
                         || toNode.AttackHeat >= AttackHeatEmergencyThreshold;

        AI_NodeState best = null;
        int bestWilling = 0;
        Queue<AI_NodeState> queue = new();
        HashSet<AI_NodeState> visited = new();
        queue.Enqueue(toNode);
        visited.Add(toNode);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node.OwnedBy == player && node != toNode)
            {
                // Don't drain a hotter node to help a cooler one. A node that's seen more
                // recent attacks than the destination is itself the more urgent defender.
                bool sourceHotterThanDest = node.AttackHeat >= AttackHeatChokepointThreshold
                                            && node.AttackHeat > toNode.AttackHeat;
                if (!sourceHotterThanDest)
                {
                    int keepAtSource = Math.Max(minWorkersInNodeBeforeConsideringSendingAnyOut, GetDesiredFrontierWorkers(node));
                    int excess = node.NumWorkers - keepAtSource;
                    if (excess > 0)
                    {
                        int willing = GetWorkersWillingToSendForDefense(node, minWorkersInNodeBeforeConsideringSendingAnyOut, emergency, toNode.AttackHeat);
                        willing = Math.Min(willing, excess);
                        if (willing > bestWilling)
                        {
                            bestWilling = willing;
                            best = node;
                        }
                    }
                }
            }
            foreach (var neighbor in node.NeighborNodes)
            {
                if (!visited.Contains(neighbor))
                {
                    visited.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }
        }

        return best;
    }

    public static bool CanButtressFromAnySource(AI_NodeState toNode, PlayerData player, int minWorkersInNodeBeforeConsideringSendingAnyOut)
    {
        return GetButtressSourceNode(toNode, player, minWorkersInNodeBeforeConsideringSendingAnyOut) != null;
    }

    public static AI_NodeState GetFriendlyNodeWithMostWorkers(AI_NodeState toNode, PlayerData player)
    {
        AI_NodeState maxNode = null;
        int maxWorkers = int.MinValue;
        Queue<AI_NodeState> queue = new();
        HashSet<AI_NodeState> visited = new();
        queue.Enqueue(toNode);
        visited.Add(toNode);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node.OwnedBy == player && node.NumWorkers > maxWorkers && !node.IsOnTerritoryEdge)
            {
                maxNode = node;
                maxWorkers = node.NumWorkers;
            }
            foreach (var neighbor in node.NeighborNodes)
            {
                if (!visited.Contains(neighbor))
                {
                    queue.Enqueue(neighbor);
                    visited.Add(neighbor);
                }
            }
        }

        return maxNode;
    }

    public static bool CanBuildBuilding(AI_TownState town, BuildingDefn buildingDefn, AI_NodeState toNode)
    {
        if (toNode.CanBeGatheredFrom)
        {
            if (!buildingDefn.CanGatherResources)
                return false;
            if (toNode.ResourceGatheredFromThisNode != buildingDefn.ResourceThisNodeCanGoGather.GoodType)
                return false;
        }
        else if (buildingDefn.CanGatherResources)
            return false;

        if (!town.ConstructionResourcesCanBeReachedFromNode(toNode, buildingDefn.ConstructionRequirements))
            return false;

        return true;
    }

    public static bool PlayerHasExcessWorkers(AI_TownState town, PlayerData player)
    {
        for (int i = 0; i < town.NumNodes; i++)
        {
            var node = town.Nodes[i];
            if (node.OwnedBy == player && (node.NumWorkers > node.MaxWorkers || node.NumWorkers > 15))
                return true;
        }
        return false;
    }
}
