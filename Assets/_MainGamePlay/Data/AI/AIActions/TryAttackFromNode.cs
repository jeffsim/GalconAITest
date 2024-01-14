using System;
using UnityEngine;

public enum AttackResult { AttackerWon, DefenderWon, BothSidesDied };

public partial class PlayerAI
{
    private void TryAttackFromNode(AI_NodeState fromNode, ref AIAction bestAction, int curDepth, int recurseCount, int thisActionNum)
    {
#if DEBUG
        AIDebugger.PushTryActionStart(thisActionNum, AIActionType.AttackFromNode, fromNode, curDepth, recurseCount);
#endif

        // Attack from nodes that have at least 1 worker and a building, and at least 1 neighbor that is owned by another player
        // TODO: Attack farther away nodes too (as long as we have buildings in interim nodes)
        if (!fromNode.HasBuilding || fromNode.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut)
            return;

        // are any neighbors owned by another player?
        var count = fromNode.NumNeighbors;
        for (int i = 0; i < count; i++)
        {
            var toNode = fromNode.NeighborNodes[i];

            // Verify we can perform the action
            if (toNode.OwnedBy == null || toNode.OwnedBy == player) continue;

            // Perform the action and get the score of the state after the action is performed
            aiTownState.AttackFromNode(fromNode, toNode, out AttackResult attackResult, out int origNumInSourceNode, out int origNumInDestNode, out int numSent, out PlayerData origToNodeOwner);
            aiTownState.EvaluateScore(curDepth, maxDepth, out float scoreAfterActionAndBeforeSubActions, out DebugAIStateReasons debugOutput_actionScoreReasons);

#if DEBUG
            AIDebugger.TrackPerformAction_Attack(toNode, attackResult, numSent, scoreAfterActionAndBeforeSubActions);
#endif

            // Recursively determine what the best action is after this action is performed
            var actionScore = RecursivelyDetermineBestAction(curDepth + 1, scoreAfterActionAndBeforeSubActions);
            if (actionScore.Score > bestAction.Score)
            {
                // This is the best action so far; save the action so we can return it
                bestAction.Score = actionScore.ScoreBeforeSubActions;
                bestAction.Type = AIActionType.AttackFromNode;
                bestAction.AttackResult = attackResult;
                bestAction.Count = numSent;
                bestAction.SourceNode = fromNode;
                bestAction.DestNode = toNode;
#if DEBUG
                bestAction.TrackStrategyDebugInfoInAction(actionScore, debugOutput_actionScoreReasons, thisActionNum, recurseCount, curDepth);
#endif
            }

            // Undo the action
            aiTownState.Undo_AttackFromNode(fromNode, toNode, attackResult, origNumInSourceNode, origNumInDestNode, numSent, origToNodeOwner);
        }

#if DEBUG
        AIDebugger.PopTryActionStart();
#endif
    }
}
