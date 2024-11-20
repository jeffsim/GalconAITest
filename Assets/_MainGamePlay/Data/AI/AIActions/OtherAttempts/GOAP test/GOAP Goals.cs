using System;

// public class OffensiveFleetConstructionGoal : Goal
// {
//     public override float CalculateUtility(AIMap_State mapState, int playerId)
//     {
//         var playerNodes = GetPlayerNodes(mapState, playerId);
//         if (playerNodes.Count == 0) return 0;

//         float totalStrengthDeficit = 0;
//         foreach (var node in playerNodes)
//         {
//             int desiredStrength = 50; // Example
//             totalStrengthDeficit += Math.Max(0, desiredStrength - node.MilitaryStrength);
//         }
//         return totalStrengthDeficit / (playerNodes.Count * 50.0f);
//     }
//     public override float EstimateCost(AIMap_State mapState, int playerId)
//     {
//         return .5f;
//     }
// }

// public class ResourceGatheringGoal : Goal
// {
//     public override float CalculateUtility(AIMap_State mapState, int playerId)
//     {
//         var playerNodes = GetPlayerNodes(mapState, playerId);
//         if (playerNodes.Count == 0) return 0;

//         float totalDeficit = 0;
//         int totalResourceTypes = 0;
//         foreach (var node in playerNodes)
//         {
//             foreach (var resource in node.Resources)
//             {
//                 int optimalLevel = 100; // Assuming 100 is the target level for each resource type
//                 totalDeficit += Math.Max(0, optimalLevel - resource.Value);
//                 totalResourceTypes++;
//             }
//         }

//         if (totalResourceTypes == 0) return 0; // Avoid division by zero

//         return -totalDeficit / (totalResourceTypes * 100.0f); // Normalized and inversed deficit
//     }
//     public override float EstimateCost(AIMap_State mapState, int playerId)
//     {
//         return .5f;
//     }
// }

// public class StrategicExpansionGoal : Goal
// {
//     public override float CalculateUtility(AIMap_State mapState, int playerId)
//     {
//         var playerNodes = GetPlayerNodes(mapState, playerId);
//         if (playerNodes.Count == 0) return 0;

//         float utility = 0;
//         foreach (var node in playerNodes)
//         {
//             foreach (var neighbor in node.Neighbors)
//             {
//                 if (neighbor.OwnerId != playerId)
//                 {
//                     // Increase utility based on strategic value of the neighbor
//                     utility += CalculateStrategicValue(neighbor);
//                 }
//             }
//         }

//         return utility / playerNodes.Count;
//     }

//     private float CalculateStrategicValue(AINode_State node)
//     {
//         // Placeholder for strategic value calculation
//         return node.Resources.Count * 1.0f + (node.IsUnderThreat ? 2.0f : 0);
//     }
//     public override float EstimateCost(AIMap_State mapState, int playerId)
//     {
//         return .5f;
//     }

// }

// public class TacticalExpansionGoal : Goal
// {
//     public override float CalculateUtility(AIMap_State mapState, int playerId)
//     {
//         var playerNodes = GetPlayerNodes(mapState, playerId);
//         if (playerNodes.Count == 0) return 0;

//         float utility = 0;
//         foreach (var node in playerNodes)
//         {
//             foreach (var neighbor in node.Neighbors)
//             {
//                 if (neighbor.OwnerId != playerId && neighbor.IsUnderThreat)
//                 {
//                     // Increased utility for vulnerable neighbors
//                     utility += 1.0f;
//                 }
//                 else if (neighbor.OwnerId != playerId)
//                 {
//                     // Lower utility for stable neighbors, but still considered for expansion
//                     utility += 0.5f;
//                 }
//             }
//         }

//         // Normalize by the number of player nodes to prevent bias towards players with more nodes
//         return utility / playerNodes.Count;
//     }
//     public override float EstimateCost(AIMap_State mapState, int playerId)
//     {
//         return .5f;
//     }

// }