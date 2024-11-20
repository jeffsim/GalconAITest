using System.Collections.Generic;
using System.Linq;

/*
    Purpose: The ResourceExtractionGoal generally focuses on optimizing the extraction and use of resources across all available nodes.
    The SpecificResourceCollectionGoal might encompass a broad strategy for maximizing the overall resource output or efficiency from all controlled territories.

    Utility Calculation: This goal might calculate utility based on how efficiently resources are being gathered, considering factors
    like resource abundance, node productivity, and overall balance of resource types in relation to the game's economic demands.

    Examples:
    * Increasing the efficiency of resource nodes by upgrading extraction facilities.
    * Focusing on resources that are abundant but underutilized.
    * Balancing the resource output to meet the requirements of various other production or building goals.
*/

public class SpecificResourceCollectionGoal : Goal
{
    private GoodType targetedResource;
    private int playerId;
    private int currentTurn;
    private List<BuildingType> playerConstructedBuildings;
    private int totalBuildingTypes;
    private int moderateThreatThreshold;
    private double currentEconomicGrowthRate;
    private double targetEconomicGrowthRate;

    public float HighestUtilityValue;
    public AINode_State HighestUtilityNode;

    public SpecificResourceCollectionGoal(GoodType resource)
    {
        GoalType = GoalType.SpecificResourceCollectionGoal;
        targetedResource = resource;
    }

    public override float CalculateUtility(AIMap_State mapState, int playerId, List<AINode_State> playerNodes)
    {
        this.playerId = playerId;
        if (playerNodes.Count == 0) return 0;

        // Determine which node has the highest utility (need) for our resource
        HighestUtilityValue = -1;
        foreach (var node in playerNodes)
        {
            var utility = CalculateResourceUtility(node, targetedResource);
            if (utility > HighestUtilityValue)
            {
                HighestUtilityValue = utility;
                HighestUtilityNode = node;
            }
        }

        return HighestUtilityValue;
    }

    private float CalculateResourceUtility(AINode_State node, GoodType resource)
    {
        float utility = 0;

        // Resource Shortage
        int currentAmount = node.Resources.ContainsKey(resource) ? node.Resources[resource] : 0;
        int desiredAmount = CalculateDesiredAmount(resource);
        utility += (float)(desiredAmount - currentAmount) / desiredAmount;

        // Strategic Importance
        utility += IsStrategicallyImportant(node, resource) ? 2.0f : 0;

        // Economic Chain Role
        utility += EvaluateEconomicChainImpact(resource, node);

        return utility;
    }

    private bool IsStrategicallyImportant(AINode_State node, GoodType resourceType)
    {
        // Determine strategic importance, possibly varying by game stage or threats
        // Example: Early game, prioritize resource production; later, focus on defense or advanced production
        switch (resourceType)
        {
            // case BuildingType.Woodcutter:
            //     return node.Resources[resourceType] < DesiredWoodLevel();
            // case ResourceType.Bakery:
            //     return node.Resources[resourceType] < DesiredFoodLevel();
            //     // Add other cases as necessary
        }

        return true;
    }
    private int CalculateDesiredAmount(GoodType resource)
    {
        int baseAmount = 100;

        switch (resource)
        {
            case GoodType.Wood:
                if (IsEarlyGame())
                    return baseAmount + 150;
                if (IsLateGame())
                    return baseAmount + 50;
                break;

            case GoodType.Stone:
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

    private float EvaluateEconomicChainImpact(GoodType resource, AINode_State node)
    {
        float impact = 0;
        // foreach (var chain in mapState.EconomicChains)
        // {
        //     if (chain.ProductionPath.Contains(resource))
        //     {
        //         impact += 1.5f;
        //     }
        // }

        // if (IsBottleneckInAnyChain(resource, node))
        // {
        //     impact += 2.0f;
        // }

        return impact;
    }

    private bool IsBottleneckInAnyChain(GoodType resource, AINode_State node)
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


    // Cost


    public override float EstimateCost(AIMap_State mapState, int playerId)
    {
        // Start with a base cost factor, which could be adjusted based on game specifics
        float cost = 0;

        // // Find the closest node that has the required resource
        // var closestResourceNode = FindClosestResourceNode(mapState, playerId, resourceType);
        // if (closestResourceNode == null)
        // {
        //     // Extremely high cost if no resource nodes are available
        //     return float.MaxValue;
        // }

        // // Calculate distance to the nearest resource node
        // var path = mapState.FindPathToResource(playerId, closestResourceNode);
        // if (path == null)
        // {
        //     // No path found, set extremely high cost
        //     return float.MaxValue;
        // }

        // cost += path.Distance; // Adding distance to cost

        // // Add cost based on enemy control and strength along the path
        // cost += CalculateEnemyControlledPathCost(path);

        // // Consider the defense of the resource node
        // cost += CalculateDefenseCost(closestResourceNode);

        return cost;
    }

    private AINode_State FindClosestResourceNode(AIMap_State mapState, int playerId, GoodType resourceType)
    {
        // Placeholder for finding the closest resource node that contains the required resource
        return mapState.AllNodes.FirstOrDefault(node => node.Resources.ContainsKey(resourceType) && node.OwnerId != playerId);
    }

    private PathResult FindPathToResource(int playerId, AINode_State resourceNode)
    {
        // Placeholder for pathfinding algorithm to find the shortest or safest path to the resource
        return new PathResult { Distance = 10 }; // Example path result
    }

    private float CalculateEnemyControlledPathCost(PathResult path)
    {
        // Add cost based on enemy presence and strength
        float enemyCost = 0;
        foreach (var node in path.Nodes)
        {
            if (node.IsEnemyControlled(playerId))
            {
                enemyCost += node.MilitaryStrength * 1.5f; // Example calculation
            }
        }
        return enemyCost;
    }

    private float CalculateDefenseCost(AINode_State resourceNode)
    {
        // Higher cost if the resource node is well defended
        return resourceNode.IsUnderThreat ? resourceNode.MilitaryStrength : 0;
    }
}

// Supporting classes and methods
public class PathResult
{
    public float Distance { get; set; }
    public List<AINode_State> Nodes { get; set; } = new List<AINode_State>();
}

public static class ExtensionMethods
{
    public static bool IsEnemyControlled(this AINode_State node, int playerId)
    {
        return node.OwnerId != playerId && node.OwnerId != 0; // Assuming 0 means uncontrolled
    }
}