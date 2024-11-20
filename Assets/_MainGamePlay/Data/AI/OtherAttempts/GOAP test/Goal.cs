// using System.Collections.Generic;
// using System.Linq;

// public enum GoalType
// {
//     SpecificResourceCollectionGoal,
//     ConstructBuildingGoal
// }

// public abstract class Goal
// {
//     public GoalType GoalType;

//     public abstract float CalculateUtility(AIMap_State mapState, int playerId, List<AINode_State> playerNodes);
//     public abstract float EstimateCost(AIMap_State mapState, int playerId);

//     public float CalculateScore(AIMap_State mapState, int playerId, List<AINode_State> playerNodes)
//     {
//         float utility = CalculateUtility(mapState, playerId, playerNodes);
//         float cost = EstimateCost(mapState, playerId);
//         return utility - cost; // Adjust this formula based on game specifics
//     }
// }
