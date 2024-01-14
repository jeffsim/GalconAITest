using UnityEngine;

public partial class PlayerAI
{
    // used to e.g. buttress buildings that are near enemy nodes
    private void TrySendWorkersToOwnedNode(AI_NodeState fromNode, ref AIAction bestAction, int curDepth, int recurseCount, int thisActionNum)
    {
#if DEBUG
        AIDebugger.PushTryActionStart(thisActionNum, AIActionType.SendWorkersToNode, fromNode, curDepth, recurseCount);
#endif
        if (fromNode.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut)
            return; // not enough workers in node to send any out

        if (!fromNode.HasBuilding)
            return; // Must have a building in a node to send workers from it 

        var count = fromNode.NumNeighbors;
        for (int i = 0; i < count; i++)
        {
            var toNode = fromNode.NeighborNodes[i];

            // Verify we can perform the action
            if (toNode.OwnedBy != player) continue; // This task can only send workers to nodes owned by this player

            // Perform the action and get the score of the state after the action is performed
            aiTownState.SendWorkersToOwnedNode(fromNode, toNode, .5f, out int numSent);
            aiTownState.EvaluateScore(curDepth, maxDepth, out float scoreAfterActionAndBeforeSubActions, out DebugAIStateReasons debugOutput_actionScoreReasons);

#if DEBUG
            AIDebugger.TrackPerformAction_SendWorkersToOwnedNode(toNode, numSent, scoreAfterActionAndBeforeSubActions);
#endif
            // Recursively determine what the best action is after this action is performed
            var actionScore = RecursivelyDetermineBestAction(curDepth + 1, scoreAfterActionAndBeforeSubActions);
            if (actionScore.Score > bestAction.Score)
            {
                // This is the best action so far in this 'level' of the AI stack; save the action so we can return it
                bestAction.Score = actionScore.ScoreBeforeSubActions;
                bestAction.Type = AIActionType.SendWorkersToNode;
                bestAction.Count = numSent;
                bestAction.SourceNode = fromNode;
                bestAction.DestNode = toNode;
#if DEBUG
                bestAction.TrackStrategyDebugInfoInAction(actionScore, debugOutput_actionScoreReasons, thisActionNum, recurseCount, curDepth);
#endif
            }

            // Undo the action
            aiTownState.Undo_SendWorkersToEmptyNode(fromNode, toNode, numSent);
        }

#if DEBUG
        AIDebugger.PopTryActionStart();
#endif
    }
}
