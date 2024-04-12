using System.Collections.Generic;
using System.Linq;

public class SpecificResourceCollectionGoal : Goal
{
    private ResourceType targetedResource;
    private AIMap_State mapState;
    private int playerId;
    private int currentTurn;
    private List<BuildingType> playerConstructedBuildings;
    private int totalBuildingTypes;
    private int moderateThreatThreshold;
    private double currentEconomicGrowthRate;
    private double targetEconomicGrowthRate;

    public SpecificResourceCollectionGoal(ResourceType resource, AIMap_State mapState, int playerId)
    {
        targetedResource = resource;
        this.mapState = mapState;
        this.playerId = playerId;
    }

    public override float CalculateUtility(AIMap_State mapState, int playerId)
    {
        var playerNodes = GetPlayerNodes(mapState, playerId);
        if (playerNodes.Count == 0) return 0;

        float totalUtility = 0;
        foreach (var node in playerNodes)
        {
            totalUtility += CalculateResourceUtility(node, targetedResource);
        }

        return totalUtility / playerNodes.Count;
    }

    private float CalculateResourceUtility(AINode_State node, ResourceType resource)
    {
        float utility = 0;

        // Resource Shortage
        int currentAmount = node.Resources.ContainsKey(resource) ? node.Resources[resource] : 0;
        int desiredAmount = CalculateDesiredAmount(resource);
        utility += (float)(desiredAmount - currentAmount) / desiredAmount;

        // Strategic Importance
        utility += IsStrategicallyImportant(resource) ? 2.0f : 0;

        // Economic Chain Role
        utility += EvaluateEconomicChainImpact(resource, node);

        return utility;
    }

    private bool IsStrategicallyImportant(ResourceType buildingType)
    {
        // Determine strategic importance, possibly varying by game stage or threats
        // Example: Early game, prioritize resource production; later, focus on defense or advanced production
        switch (buildingType)
        {
            case ResourceType.WoodcutterHut:
                return node.Resources[ResourceType.Wood] < DesiredWoodLevel();
            case ResourceType.Bakery:
                return node.Resources[ResourceType.Food] < DesiredFoodLevel();
                // Add other cases as necessary
        }

        return true;
    }
    private int CalculateDesiredAmount(ResourceType resource)
    {
        int baseAmount = 100;

        switch (resource)
        {
            case ResourceType.Wood:
                if (IsEarlyGame())
                    return baseAmount + 150;
                if (IsLateGame())
                    return baseAmount + 50;
                break;

            case ResourceType.Stone:
                if (NeedsDefensiveStructures())
                    return baseAmount + 200;
                if (IsEconomicExpansionPhase())
                    return baseAmount + 100;
                break;
        }

        return baseAmount;
    }

    private bool IsEarlyGame()
    {
        int earlyGameTurnThreshold = 20;
        return currentTurn <= earlyGameTurnThreshold;
    }

    private bool IsLateGame()
    {
        int lateGameBuildingThreshold = (int)(totalBuildingTypes * 0.75);
        return playerConstructedBuildings.Count >= lateGameBuildingThreshold;
    }

    private bool NeedsDefensiveStructures()
    {
        int enemyThreatLevel = CalculateEnemyThreatLevel();
        return enemyThreatLevel > moderateThreatThreshold;
    }

    private bool IsEconomicExpansionPhase()
    {
        return currentEconomicGrowthRate > targetEconomicGrowthRate && !HasReachedEconomicMilestones();
    }

    private float EvaluateEconomicChainImpact(ResourceType resource, AINode_State node)
    {
        float impact = 0;
        foreach (var chain in mapState.EconomicChains)
        {
            if (chain.ProductionPath.Contains(resource))
            {
                impact += 1.5f;
            }
        }

        if (IsBottleneckInAnyChain(resource, node))
        {
            impact += 2.0f;
        }

        return impact;
    }

    private bool IsBottleneckInAnyChain(ResourceType resource, AINode_State node)
    {
        int requiredAmount = CalculateDesiredAmount(resource);
        return node.Resources[resource] < requiredAmount;
    }

    private int CalculateEnemyThreatLevel()
    {
        // Example implementation of calculating enemy threat level based on proximity and strength of enemy forces
        return 10; // Example threat level
    }

    private bool HasReachedEconomicMilestones()
    {
        // Example implementation of checking if economic milestones have been reached
        return false; // Example condition
    }
}
