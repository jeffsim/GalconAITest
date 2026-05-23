using System.Collections.Generic;
using UnityEngine;

public enum AIHeuristicActionType
{
    Upgrade,
    Buttress,
    Build,
    Attack,
}

/// <summary>
/// Heuristic scoring and territory analysis ported from Strategy_NonRecursive for use in recursive search.
/// Returns bonuses scaled to be meaningful alongside EvaluateScore() results (~0-3 range).
/// </summary>
public static class AI_ActionHeuristics
{
    const float HeuristicScoreScale = 3f;

    const float excessWorkersScalingFactor = 1f;
    const float excessWorkersScalingFactor2 = 1f;
    const float nearbyEnemiesScalingFactor = 1f;
    const float buildingResourceScalingFactor = 5f;
    const float buildingStrategicScalingFactor = 8f;
    const float territoryEdgeScalingFactor = 10f;
    const float insufficientWorkersScalingFactor = 10f;

    const float upgradeNodeMinScore = 10f;
    const float upgradeNodeMaxScore = 40f;
    const float buildBuildingMinScore = 20f;
    const float buildBuildingMaxScore = 40f;
    const float buttressNodeMinScore = 20f;
    const float buttressNodeMaxScore = 40f;
    const float attackNodeMinScore = 20f;
    const float attackNodeMaxScore = 40f;

    const int numGatherableResourceDesired = 20;

    public static float GetPersonalityMultiplier(PlayerData player, AIHeuristicActionType actionType)
    {
        var aiDefn = player.AIDefn;
        if (aiDefn == null) return 1f;

        return actionType switch
        {
            AIHeuristicActionType.Upgrade => aiDefn.ExpansionWeight,
            AIHeuristicActionType.Build => aiDefn.ExpansionWeight,
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

    public static float GetUpgradeHeuristic(AI_NodeState node)
    {
        if (node.NumWorkers < node.MaxWorkers) return 0f;

        float rawValue = upgradeNodeMinScore * 1.1f;

        int numExcessiveWorkers = node.NumWorkers - node.MaxWorkers;
        if (node.NumWorkers > node.MaxWorkers * 1.5f)
            rawValue = 100;
        else if (numExcessiveWorkers > 0)
        {
            float percentExcessive = (float)numExcessiveWorkers / node.MaxWorkers;
            rawValue += Mathf.Pow(percentExcessive, 2) * excessWorkersScalingFactor;

            if (node.NumEnemiesInNeighborNodes == 0)
                rawValue += Mathf.Pow(numExcessiveWorkers, 2) * excessWorkersScalingFactor2;
        }

        if (node.NumEnemiesInNeighborNodes > 0)
        {
            float delta = node.NumEnemiesInNeighborNodes - node.NumWorkers;
            rawValue -= Mathf.Pow(delta, 2) * nearbyEnemiesScalingFactor;
        }
        if (node.IsOnTerritoryEdge && node.NumWorkers < node.MaxWorkers * 1.5f && node.NumEnemiesInNeighborNodes > 0)
            rawValue += 35;

        float clampedRawValue = Mathf.Clamp(rawValue, upgradeNodeMinScore, upgradeNodeMaxScore);
        float normalizedValue = (clampedRawValue - upgradeNodeMinScore) / (upgradeNodeMaxScore - upgradeNodeMinScore);
        return normalizedValue * HeuristicScoreScale;
    }

    public static float GetButtressHeuristic(AI_NodeState toNode)
    {
        float rawValue = 0f;

        if (toNode.NumEnemiesInNeighborNodes > 0)
        {
            float delta = toNode.NumEnemiesInNeighborNodes - toNode.NumWorkers;
            rawValue += Mathf.Pow(delta, 2) * nearbyEnemiesScalingFactor;
        }

        if (toNode.IsOnTerritoryEdge)
            rawValue += territoryEdgeScalingFactor;

        if (toNode.NumWorkers < toNode.MaxWorkers / 2)
        {
            float workersDeficit = toNode.MaxWorkers - toNode.NumWorkers;
            rawValue += Mathf.Pow(workersDeficit, 2) * insufficientWorkersScalingFactor;
        }

        if (rawValue < buttressNodeMinScore) return 0f;

        float clampedRawValue = Mathf.Clamp(rawValue, buttressNodeMinScore, buttressNodeMaxScore);
        float normalizedValue = (clampedRawValue - buttressNodeMinScore) / (buttressNodeMaxScore - buttressNodeMinScore);
        return normalizedValue * HeuristicScoreScale;
    }

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
            var gatherableResource = buildingDefn.ResourceThisNodeCanGoGather.GoodType;
            int numGatherableResourceOwned = town.PlayerTownInventory[gatherableResource];
            rawValue += buildingResourceScalingFactor * (numGatherableResourceDesired - numGatherableResourceOwned);
        }
        else
        {
            rawValue += buildingStrategicScalingFactor;
        }

        float clampedRawValue = Mathf.Clamp(rawValue, buildBuildingMinScore, buildBuildingMaxScore);
        float normalizedValue = (clampedRawValue - 10f) / 30f;
        return normalizedValue * HeuristicScoreScale;
    }

    public static float GetAttackHeuristic(AI_NodeState enemyNode, int totalWorkersWillingToSend)
    {
        float rawValue = attackNodeMaxScore / 2f;

        // Prefer weaker targets and attacks where we have comfortable force advantage
        float forceRatio = totalWorkersWillingToSend / (float)Mathf.Max(1, enemyNode.NumWorkers);
        if (forceRatio >= 1.5f)
            rawValue += 10f;
        else if (forceRatio >= 1f)
            rawValue += 5f;

        if (enemyNode.HasBuilding)
            rawValue += 5f;

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
