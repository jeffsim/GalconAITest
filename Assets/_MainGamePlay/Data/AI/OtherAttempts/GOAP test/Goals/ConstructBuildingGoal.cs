
// using System.Collections.Generic;
// using System.Linq;

// public class ConstructBuildingGoal : Goal
// {
//     public ConstructBuildingGoal()//GoodType resource)
//     {
//         GoalType = GoalType.ConstructBuildingGoal;
//         // targetedResource = resource;
//     }

//     public override float CalculateUtility(AIMap_State mapState, int playerId, List<AINode_State> playerNodes)
//     {
//         if (playerNodes.Count == 0) return 0;
//         float utility = 0;
//         foreach (var node in playerNodes)
//         {
//             if (node.IsUnderThreat || IsStrategicallyImportant(node))
//             {
//                 utility += 1.0f;
//             }
//         }

//         return utility / playerNodes.Count;
//     }

//     private bool IsStrategicallyImportant(AINode_State node)
//     {
//         // Placeholder for strategic importance calculation
//         return node.Neighbors.Any(n => n.OwnerId != node.OwnerId);
//     }

//     public override float EstimateCost(AIMap_State mapState, int playerId)
//     {
//         return .5f;
//     }

// }
