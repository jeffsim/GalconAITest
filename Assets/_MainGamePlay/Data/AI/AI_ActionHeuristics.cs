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
            }
        }

        if (totalCaptureUrgency > 0f)
        {
            var barracksDefn = FindBuildingDefnOfType(buildableBuildingDefns, numBuildableBuildingDefns, BuildingType.Barracks);
            if (barracksDefn != null)
                AddBuildingReqsToDemand(demand, barracksDefn, totalCaptureUrgency);
        }
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
        int owned = town.PlayerTownInventory.TryGetValue(goodType, out int o) ? o : 0;
        int shortage = wanted - owned;
        return shortage < 0 ? 0 : shortage;
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
        return node.NumWorkers / 2;
    }

    public static int GetTargetForceWithOverkill(int threat, float overkillMultiplier)
    {
        if (threat <= 0) return 1;
        return Math.Max(1, (int)Math.Ceiling(threat * overkillMultiplier));
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

    // Size a neutral capture: 1 worker on safe frontiers; enough to clear unowned garrison on
    // the target and hold against adjacent enemy garrisons on contested ones.
    public static bool TryGetCaptureWorkersToSend(AI_NodeState fromNode, AI_NodeState toNode, PlayerData player, int minWorkersInNodeBeforeConsideringSendingAnyOut, float overkillMultiplier, out int numToSend)
    {
        numToSend = 0;
        if (fromNode.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut) return false;

        int willing = GetWorkersWillingToSend(fromNode, minWorkersInNodeBeforeConsideringSendingAnyOut);
        if (willing <= 0) return false;

        int threat = GetNeutralCaptureThreat(toNode, player);
        int target = GetTargetForceWithOverkill(threat, overkillMultiplier);
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

    public static float GetUpgradeHeuristic(AI_NodeState node)
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

        // Only penalize upgrade when we are ACTUALLY outnumbered (signed delta squared previously
        // discounted upgrades for over-defended nodes too, which it shouldn't).
        int outnumberedBy = node.NumEnemiesInNeighborNodes - node.NumWorkers;
        if (outnumberedBy > 0)
            rawValue -= outnumberedBy * outnumberedBy * nearbyEnemiesScalingFactor;

        // Defensive imperative: a node that's at-or-near capacity, on the territory edge, AND
        // has actual enemies adjacent NEEDS a capacity bump now. This is the only path that
        // should drive the heuristic to its max so the upgrade outranks expansion in genuinely
        // threatened situations -- not in peaceful overcrowding.
        if (node.IsOnTerritoryEdge && node.NumWorkers < node.MaxWorkers * 1.5f && node.NumEnemiesInNeighborNodes > 0)
            rawValue += 35;

        float clampedRawValue = Mathf.Clamp(rawValue, upgradeNodeMinScore, upgradeNodeMaxScore);
        float normalizedValue = (clampedRawValue - upgradeNodeMinScore) / (upgradeNodeMaxScore - upgradeNodeMinScore);
        return normalizedValue * HeuristicScoreScale;
    }

    public static float GetButtressHeuristic(AI_NodeState toNode)
    {
        float rawValue = 0f;

        // Only reward buttressing when we are ACTUALLY outnumbered. The previous formulation
        // squared the signed delta, so an over-defended node (e.g. 220 workers vs 60 enemies,
        // delta = -160) produced the same huge bonus as a genuinely besieged one (delta = +160).
        // That meant a dominant player with many over-defended frontiers generated a wall of
        // false-positive Buttress candidates that crowded all real attacks out of Phase 1's top-K
        // and the AI sat on top of an enormous economy doing nothing. Match the DefendFrontier
        // goal enumerator's clamping (deficit = max(0, enemies - ours)) here.
        int outnumberedBy = toNode.NumEnemiesInNeighborNodes - toNode.NumWorkers;
        if (outnumberedBy > 0)
            rawValue += outnumberedBy * outnumberedBy * nearbyEnemiesScalingFactor;

        if (toNode.IsOnTerritoryEdge)
            rawValue += territoryEdgeScalingFactor;

        // Reinforce understaffed nodes ONLY when there is something to defend against. An
        // interior Woodcutter at 18/40 doesn't need workers shipped to it (gathering is
        // per-tick, not worker-count-dependent). Without this gate, a dominant player ends
        // up with a pile of phantom buttress candidates against its own peaceful interior
        // and never reaches Phase 2 attack candidates.
        if (toNode.NumWorkers < toNode.MaxWorkers / 2
            && (toNode.IsOnTerritoryEdge || toNode.NumEnemiesInNeighborNodes > 0))
        {
            float workersDeficit = toNode.MaxWorkers - toNode.NumWorkers;
            rawValue += workersDeficit * workersDeficit * insufficientWorkersScalingFactor;
        }

        if (rawValue < buttressNodeMinScore) return 0f;

        float clampedRawValue = Mathf.Clamp(rawValue, buttressNodeMinScore, buttressNodeMaxScore);
        float normalizedValue = (clampedRawValue - buttressNodeMinScore) / (buttressNodeMaxScore - buttressNodeMinScore);
        return normalizedValue * HeuristicScoreScale;
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
