// using System.Collections.Generic;
// using System.Linq;

// public abstract class Goal
// {
//     public GoalType GoalType;
    
//     // Now also includes a method to estimate the cost of achieving the goal
//     public abstract float CalculateUtility(AIMap_State mapState, int playerId);
//     public abstract float EstimateCost(AIMap_State mapState, int playerId);

//     public float CalculateScore(AIMap_State mapState, int playerId)
//     {
//         float utility = CalculateUtility(mapState, playerId);
//         float cost = EstimateCost(mapState, playerId);
//         return utility - cost; // Adjust this formula based on game specifics
//     }

//     protected List<AINode_State> GetPlayerNodes(AIMap_State mapState, int playerId)
//     {
//         return mapState.AllNodes.Where(node => node.OwnerId == playerId).ToList();
//     }
// }
