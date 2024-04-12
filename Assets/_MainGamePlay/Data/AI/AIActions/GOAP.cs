// using UnityEngine;
// using System.Collections.Generic;

// public enum GoalType
// {
//     StrategicExpansion,
//     TacticalExpansion,
//     ResourceGathering,
//     DefensiveInfrastructure,
//     OffensiveFleetConstruction,
//     EstablishAlliances,
//     ResourceExtraction,
//     SpecificResourceCollection
// }

// public enum AIActionType
// {
//     DoNothing,
//     SendWorkersToEmptyNode,
//     SendWorkersToOwnedNode,
//     ConstructBuildingInEmptyNode,
//     ConstructBuildingInOwnedEmptyNode,
//     AttackFromNode,
//     NoAction_GameOver,
//     NoAction_MaxDepth
// }

// public class Goal
// {
//     public GoalType Type;
//     public Dictionary<GoalType, Goal> Subgoals = new();
//     public float Utility;
// }

// public class AIAction
// {
//     public AIActionType Type;
//     public float Utility;
// }

// public partial class PlayerAI
// {
//     private List<Goal> goals = new();

//     void InitializeGoals()
//     {
//         // Top-tier goals
//         Goal strategicExpansionGoal = new() { Type = GoalType.StrategicExpansion };
//         Goal tacticalExpansionSubgoal = new() { Type = GoalType.TacticalExpansion };
//         Goal resourceGatheringSubgoal = new() { Type = GoalType.ResourceGathering };
//         Goal defensiveInfrastructureGoal = new() { Type = GoalType.DefensiveInfrastructure };
//         Goal offensiveFleetConstructionGoal = new() { Type = GoalType.OffensiveFleetConstruction };
//         Goal establishAlliancesGoal = new() { Type = GoalType.EstablishAlliances };
//         Goal resourceExtractionGoal = new() { Type = GoalType.ResourceExtraction };
//         Goal specificResourceCollectionGoal = new() { Type = GoalType.SpecificResourceCollection };

//         // Subgoals for Strategic Expansion
//         strategicExpansionGoal.Subgoals.Add(GoalType.TacticalExpansion, tacticalExpansionSubgoal);
//         strategicExpansionGoal.Subgoals.Add(GoalType.ResourceGathering, resourceGatheringSubgoal);

//         // Subgoals for Tactical Expansion
//         tacticalExpansionSubgoal.Subgoals.Add(GoalType.ResourceGathering, resourceGatheringSubgoal);

//         // Add top-tier goals to the list of goals
//         goals.Add(strategicExpansionGoal);
//         goals.Add(defensiveInfrastructureGoal);
//         goals.Add(offensiveFleetConstructionGoal);
//         goals.Add(establishAlliancesGoal);
//         goals.Add(resourceExtractionGoal);
//         goals.Add(specificResourceCollectionGoal);

//         // Add subgoals to the list of goals
//         goals.Add(tacticalExpansionSubgoal);
//         goals.Add(resourceGatheringSubgoal);
//     }

//     float CalculateGoalUtility(Goal goal)
//     {
//         float totalUtility = 0.0f;

//         switch (goal.Type)
//         {
//             case GoalType.StrategicExpansion:
//                 totalUtility += CalculateStrategicExpansionUtility(goal);
//                 break;
//             case GoalType.TacticalExpansion:
//                 totalUtility += CalculateTacticalExpansionUtility(goal);
//                 break;
//             case GoalType.ResourceGathering:
//                 // Calculate utility based on resource gathering needs
//                 totalUtility += CalculateResourceGatheringUtility();
//                 break;
//             case GoalType.DefensiveInfrastructure:
//                 // Calculate utility based on defensive infrastructure needs
//                 totalUtility += CalculateDefensiveInfrastructureUtility();
//                 break;
//             case GoalType.OffensiveFleetConstruction:
//                 // Calculate utility based on offensive fleet construction needs
//                 totalUtility += CalculateOffensiveFleetConstructionUtility();
//                 break;
//             case GoalType.EstablishAlliances:
//                 // Calculate utility based on establishing alliances
//                 totalUtility += CalculateEstablishAlliancesUtility();
//                 break;
//             case GoalType.ResourceExtraction:
//                 // Calculate utility based on resource extraction needs
//                 totalUtility += CalculateResourceExtractionUtility();
//                 break;
//             case GoalType.SpecificResourceCollection:
//                 // Calculate utility based on specific resource collection needs
//                 totalUtility += CalculateSpecificResourceCollectionUtility();
//                 break;
//         }

//         // Recursively calculate utility for subgoals
//         foreach (var subgoal in goal.Subgoals.Values)
//         {
//             totalUtility += CalculateGoalUtility(subgoal);
//         }

//         // Update the goal's utility
//         goal.Utility = totalUtility;

//         return totalUtility;
//     }

//     float CalculateStrategicExpansionUtility(Goal goal)
//     {
//         float utility = 0.0f;

//         // Calculate utility based on various factors
//         utility += CalculateTerritoryExpansionUtility();
//         utility += CalculateResourceAccessibilityUtility();
//         utility += CalculateStrategicPositioningUtility();
//         utility += CalculateDefensiveAdvantageUtility();
//         utility += CalculatePotentialThreatsUtility();
//         utility += CalculateEconomicGrowthUtility();

//         return utility;
//     }

//     float CalculateTacticalExpansionUtility(Goal goal)
//     {
//         float utility = 0.0f;

//         // Calculate utility based on various tactical factors
//         utility += CalculateEnemyWeaknessExploitationUtility();
//         utility += CalculateStrategicPositioningUtility();
//         utility += CalculateResourceAccessibilityUtility();
//         utility += CalculateDisruptionOfEnemyLinesUtility();
//         utility += CalculateStrategicDiversificationUtility();

//         return utility;
//     }

//     // Utility calculation functions for specific aspects of goals
//     float CalculateResourceGatheringUtility() { /* Implementation */ }
//     float CalculateDefensiveInfrastructureUtility() { /* Implementation */ }
//     float CalculateOffensiveFleetConstructionUtility() { /* Implementation */ }
//     float CalculateEstablishAlliancesUtility() { /* Implementation */ }
//     float CalculateResourceExtractionUtility() { /* Implementation */ }
//     float CalculateSpecificResourceCollectionUtility() { /* Implementation */ }

//     // Utility calculation functions for strategic expansion factors
//     float CalculateTerritoryExpansionUtility() { /* Implementation */ }
//     float CalculateResourceAccessibilityUtility() { /* Implementation */ }
//     float CalculateStrategicPositioningUtility() { /* Implementation */ }
//     float CalculateDefensiveAdvantageUtility() { /* Implementation */ }
//     float CalculatePotentialThreatsUtility() { /* Implementation */ }
//     float CalculateEconomicGrowthUtility() { /* Implementation */ }

//     // Utility calculation functions for tactical expansion factors
//     float CalculateEnemyWeaknessExploitationUtility() { /* Implementation */ }
//     float CalculateDisruptionOfEnemyLinesUtility() { /* Implementation */ }
//     float CalculateStrategicDiversificationUtility() { /* Implementation */ }

//     // Main function to determine the best action using GOAP combined with utility-based AI
//     AIAction DetermineBestAction_GOAP()
//     {
//         InitializeGoals(); // Initialize goals and subgoals
//         CalculateGoalUtility(goals[0]); // Calculate utility for top-tier goal (e.g., Strategic Expansion)

//         // Determine the best action based on utility calculations
//         AIAction bestAction = new();
//         float highestUtility = float.MinValue;

//         foreach (var goal in goals)
//         {
//             if (goal.Utility > highestUtility)
//             {
//                 highestUtility = goal.Utility;
//                 // Set the best action based on the goal with the highest utility
//                 bestAction.Type = DetermineActionType(goal.Type);
//                 bestAction.Utility = highestUtility;
//             }
//         }

//         return bestAction;
//     }

//     // Function to map goal types to corresponding action types
//     AIActionType DetermineActionType(GoalType goalType)
//     {
//         return goalType switch
//         {
//             GoalType.StrategicExpansion => AIActionType.SendWorkersToEmptyNode,
//             GoalType.TacticalExpansion => AIActionType.AttackFromNode,
//             GoalType.ResourceGathering => AIActionType.SendWorkersToOwnedNode,
//             GoalType.DefensiveInfrastructure => AIActionType.ConstructBuildingInOwnedEmptyNode,
//             GoalType.OffensiveFleetConstruction => AIActionType.ConstructBuildingInEmptyNode,
//             GoalType.EstablishAlliances => AIActionType.DoNothing,
//             GoalType.ResourceExtraction => AIActionType.DoNothing,
//             GoalType.SpecificResourceCollection => AIActionType.SendWorkersToEmptyNode,
//             _ => AIActionType.DoNothing,
//         };
//     }
// }
