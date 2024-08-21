// using System.Collections.Generic;
// using System.Linq;

// public class Building
// {
//     public Dictionary<ResourceType, int> ConstructionCost { get; set; }
//     public Dictionary<ResourceType, int> ProductionRate { get; set; }
//     public int WorkerCapacity { get; set; }
// }

// public class EconomicChain
// {
//     public List<ResourceType> ProductionPath { get; set; }
// }

// public class ResourceExtractionGoal : Goal
// {
//     private Dictionary<ResourceType, Building> buildingRequirements;
//     private List<EconomicChain> economicChains;

//     public ResourceExtractionGoal(Dictionary<ResourceType, Building> buildingRequirements, List<EconomicChain> economicChains)
//     {
//         this.buildingRequirements = buildingRequirements;
//         this.economicChains = economicChains;
//     }

//     public override float EstimateCost(AIMap_State mapState, int playerId)
//     {
//         return .5f;
//     }

//     public override float CalculateUtility(AIMap_State mapState, int playerId)
//     {
//         var playerNodes = GetPlayerNodes(mapState, playerId);
//         if (playerNodes.Count == 0) return 0;

//         float totalUtility = 0;
//         foreach (var node in playerNodes)
//         {
//             foreach (var building in buildingRequirements)
//             {
//                 // Assess the need for resources based on building requirements
//                 if (ShouldBuild(building.Key, node))
//                 {
//                     totalUtility += CalculateBuildingUtility(building.Value, node);
//                 }
//             }

//             // Consider the economic chains
//             foreach (var chain in economicChains)
//             {
//                 totalUtility += EvaluateEconomicChain(chain, node);
//             }
//         }

//         return totalUtility;
//     }


//     private float CalculateBuildingUtility(Building building, AINode_State node)
//     {
//         float utility = 0;

//         // Resource Production Efficiency
//         int totalCost = building.ConstructionCost.Sum(cost => cost.Value);
//         int totalProduction = building.ProductionRate.Sum(rate => rate.Value);
//         if (totalCost > 0) // Avoid division by zero
//         {
//             utility += (float)totalProduction / totalCost;
//         }

//         // Strategic Value
//         utility += DetermineStrategicValue(building, node);

//         // Urgency
//         utility += CalculateUrgency(building, node);

//         return utility;
//     }

//     private float CalculateUrgency(Building building, AINode_State node)
//     {
//         float urgency = 0;

//         // Example: Urgency for resource production if below critical levels
//         foreach (var resource in building.ProductionRate)
//         {
//             if (node.Resources[resource.Key] < CriticalResourceThreshold(resource.Key))
//             {
//                 urgency += 5; // High urgency for critical resources
//             }
//         }

//         // Increased urgency in response to imminent threats
//         if (node.IsUnderThreat && building.Type == BuildingType.DefenseTower)
//         {
//             urgency += 10;  // Very urgent if the node is under immediate threat
//         }

//         // Example: Urgency to build trade or diplomatic buildings during peace times
//         if (!node.IsUnderThreat && building.Type == BuildingType.Embassy)
//         {
//             urgency += 3;  // Moderate urgency to improve diplomacy in peace times
//         }

//         return urgency;
//     }

//     private int CriticalResourceThreshold(ResourceType resource)
//     {
//         // Placeholder for critical resource threshold values
//         return 50; // Example threshold
//     }

//     private float CalculateFutureValue(EconomicChain chain, AINode_State node)
//     {
//         float futureValue = 0;

//         // Evaluate the potential long-term benefits of each step in the chain
//         foreach (var step in chain.ProductionPath)
//         {
//             if (step == ResourceType.Planks && !node.Buildings.Any(b => b.Type == BuildingType.Sawmill))
//             {
//                 futureValue += 5; // High future value for enabling plank production
//             }

//             if (step == ResourceType.Millstone && !node.Buildings.Any(b => b.Type == BuildingType.Mill))
//             {
//                 futureValue += 4; // Moderate future value for enabling millstone production
//             }
//         }

//         return futureValue;
//     }

//     private float IdentifyBottlenecks(EconomicChain chain, AINode_State node)
//     {
//         float bottleneckValue = 0;

//         // Identify and value removing bottlenecks in the production chain
//         foreach (var step in chain.ProductionPath)
//         {
//             if (step == ResourceType.Wood && IsBottleneck(ResourceType.Wood, node))
//             {
//                 bottleneckValue += 5;  // High value for alleviating wood bottleneck
//             }

//             if (step == ResourceType.Stone && IsBottleneck(ResourceType.Stone, node))
//             {
//                 bottleneckValue += 3;  // Moderate value for alleviating stone bottleneck
//             }
//         }

//         return bottleneckValue;
//     }

//     private bool IsBottleneck(ResourceType resource, AINode_State node)
//     {
//         // Placeholder logic to determine if a resource is a bottleneck
//         // For example, wood might be a bottleneck if it's necessary for many buildings and in short supply
//         return node.Resources[resource] < DesiredLevel(resource); // Example logic
//     }

//     private int DesiredLevel(ResourceType resource)
//     {
//         // Placeholder for desired level of a resource
//         return 100; // Example desired level
//     }

//     private bool ShouldBuild(ResourceType buildingType, AINode_State node)
//     {
//         if (!buildingRequirements.TryGetValue(buildingType, out Building buildingInfo))
//             return false;

//         // Check resource availability for construction
//         foreach (var cost in buildingInfo.ConstructionCost)
//         {
//             if (node.Resources[cost.Key] < cost.Value)
//                 return false;  // Not enough resources to build
//         }

//         // Assess strategic importance based on game state or phase
//         if (!IsStrategicallyImportant(buildingType, node))
//             return false;

//         // Evaluate if the building meets current and future resource production needs
//         if (!MeetsResourceProductionNeeds(buildingType, node))
//             return false;

//         // Ensure the building is part of a needed economic chain
//         if (!IsPartOfNeededEconomicChain(buildingType, node))
//             return false;

//         // Check existing infrastructure to avoid redundancy
//         if (ExistingInfrastructureSuffices(buildingType, node))
//             return false;

//         return true;
//     }

//     private bool IsStrategicallyImportant(ResourceType buildingType, AINode_State node)
//     {
//         // Determine strategic importance, possibly varying by game stage or threats
//         // Example: Early game, prioritize resource production; later, focus on defense or advanced production
//         switch (buildingType)
//         {
//             case ResourceType.WoodcutterHut:
//                 return node.Resources[ResourceType.Wood] < DesiredWoodLevel();
//             case ResourceType.Bakery:
//                 return node.Resources[ResourceType.Food] < DesiredFoodLevel();
//                 // Add other cases as necessary
//         }

//         return true;
//     }

//     private bool MeetsResourceProductionNeeds(ResourceType buildingType, AINode_State node)
//     {
//         // Check if additional production of the resource is needed
//         // Example: Only build a new woodcutter's hut if more wood is actually needed
//         Building buildingInfo = buildingRequirements[buildingType];
//         foreach (var production in buildingInfo.ProductionRate)
//         {
//             if (FutureResourceNeeds(production.Key) > CurrentResourceProduction(production.Key, node))
//                 return true;
//         }
//         return false;
//     }

//     private bool IsPartOfNeededEconomicChain(ResourceType buildingType, AINode_State node)
//     {
//         // Determine if the building is part of a required economic chain
//         // Example: A sawmill might be needed to process wood into planks for other constructions
//         return economicChains.Any(chain => chain.ProductionPath.Contains(buildingType));
//     }

//     private bool ExistingInfrastructureSuffices(ResourceType buildingType, AINode_State node)
//     {
//         // Check if there are already enough buildings of this type
//         // Example: If there’s already a sufficient number of woodcutter's huts, don't build another
//         int existingCount = node.Buildings.Count(b => b.Type == buildingType);
//         int neededCount = CalculateNeededBuildings(buildingType, node);
//         return existingCount >= neededCount;
//     }

//     private float EvaluateEconomicChain(EconomicChain chain, AINode_State node)
//     {
//         float utility = 0;

//         // Chain Completeness
//         int stepsCompleted = chain.ProductionPath.Count(step => node.Buildings.Any(b => b.Type == step));
//         utility += (float)stepsCompleted / chain.ProductionPath.Count;

//         // Future Value
//         utility += CalculateFutureValue(chain, node);

//         // Bottlenecks
//         utility += IdentifyBottlenecks(chain, node);

//         return utility;
//     }
//     private float DetermineStrategicValue(Building building, AINode_State node)
//     {
//         float value = 0;

//         // Example: Higher value for defensive buildings if under threat
//         if (node.IsUnderThreat && building.Type == BuildingType.Fortress)
//         {
//             value += 10;  // High strategic value for defense
//         }

//         // Example: Value resource production buildings based on current shortages
//         foreach (var rate in building.ProductionRate)
//         {
//             if (IsResourceInShortSupply(rate.Key))
//             {
//                 value += 5;  // Higher value for buildings producing needed resources
//             }
//         }

//         // Long-term strategic value, e.g., buildings enabling technology upgrades
//         if (building.EnablesTechnology)
//         {
//             value += 8;  // High value for buildings enabling technological advancements
//         }

//         return value;
//     }

//     private bool IsResourceInShortSupply(ResourceType resource)
//     {
//         // Placeholder for checking if a resource is in short supply
//         return true; // Example assumption
//     }

//     // Additional helper methods to calculate desired resource levels, future needs, etc.
// }