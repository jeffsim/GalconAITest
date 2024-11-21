using System.Collections.Generic;

public enum AttackResult { Undefined, AttackerWon, DefenderWon, BothSidesDied };

// public class AITask_AttackFromNode : AITask
// {
//     public AITask_AttackFromNode(PlayerData player, AI_TownState aiTownState, int maxDepth, int minWorkersInNodeBeforeConsideringSendingAnyOut) : base(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut) { }

//     override public AIAction TryTask(AI_NodeState fromNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions)
//     {
//         var bestAction = new AIAction() { Type = AIActionType.DoNothing };

//         if (fromNode.OwnedBy != player) // only process actions from/in nodes that we own
//             return bestAction;

//         // Attack from nodes that have at least 1 worker and a building, and at least 1 neighbor that is owned by another player
//         // TODO: Attack farther away nodes too (as long as we have buildings in interim nodes)
//         if (!fromNode.HasBuilding || fromNode.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut)
//             return bestAction;

//         // are any neighbors owned by another player?
//         foreach (var toNode in fromNode.NeighborNodes)
//         {
//             // ==== Verify we can perform the action
//             if (toNode.OwnedBy == null || toNode.OwnedBy == player) continue;

//             // depending on AI strategy, do/don't attack if it's hopeless to win
//             if (toNode.NumWorkers > fromNode.NumWorkers)
//                 continue; // TODO: Need coordinated attacks

//             // TODO: Cull other possible attacks to reduce search space.
//             // * Don't attack if not at war?
//             // * Don't attack if we don't need the building


//             // ==== Perform the action and update the aiTownState to reflect the action
//             aiTownState.AttackFromNode(fromNode, toNode, out AttackResult attackResult, out int origNumInSourceNode, out int origNumInDestNode, out int numSent, out PlayerData origToNodeOwner);
//             var debuggerEntry = aiDebuggerParentEntry.AddEntry_AttackFromNode(fromNode, toNode, attackResult, numSent, 0, player.AI.debugOutput_ActionsTried++, curDepth);

//             // ==== Determine the score of the action we just performed (recurse down); if this is the best so far amongst our peers (in our parent node) then track it as the best action
//             var actionScore = GetActionScore(curDepth, debuggerEntry);
//             if (actionScore > bestAction.Score)
//                 bestAction.SetTo_AttackFromNode(fromNode, toNode, numSent, attackResult, actionScore, debuggerEntry);

//             // ==== Undo the action to reset the townstate to its original state
//             aiTownState.Undo_AttackFromNode(fromNode, toNode, attackResult, origNumInSourceNode, origNumInDestNode, numSent, origToNodeOwner);
//         }

//         return bestAction;
//     }
// }
