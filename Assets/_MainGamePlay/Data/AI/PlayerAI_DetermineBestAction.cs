using UnityEngine;

public partial class PlayerAI
{
    // Determine the best action that can be taken given the current aiTownState and return that action, ensuring
    // that aiTownState is fully restored to its original state before returning.
    // Actions a player-owned Node can take:
    // 1. Send 50% of workers to a node that neighbors the node
    // 2. Construct a building in a node we own. 
    AIAction RecursivelyDetermineBestAction(int curDepth, float scoreOnEntry)
    {
#if DEBUG
        var recurseCount = ++debugOutput_callsToRecursivelyDetermineBestAction;
#endif
        Debug.Assert(debugOutput_ActionsTried < maxPoolSize, "stuck in loop in RecursivelyDetermineBestAction");
        AIAction bestAction = actionPool[actionPoolIndex++];
        bestAction.ScoreBeforeSubActions = scoreOnEntry;
        if (curDepth == maxDepth || aiTownState.IsGameOver())
        {
            bestAction.Type = curDepth == maxDepth ? AIActionType.NoAction_MaxDepth : AIActionType.NoAction_GameOver;
            bestAction.Score = scoreOnEntry;
            return bestAction;
        }

        bestAction.Score = 0;
        for (int i = 0; i < aiTownState.NumNodes; i++)
        {
            var node = aiTownState.Nodes[i];
            if (node.OwnedBy != player) continue; // only process actions from/in nodes that we own

            TrySendWorkersToEmptyNode(node, ref bestAction, curDepth, recurseCount, ++debugOutput_ActionsTried);
            TryConstructBuildingInNode(node, ref bestAction, curDepth, recurseCount, ++debugOutput_ActionsTried);
            //      TryAttackFromNode(node, ref bestAction, curDepth, recurseCount, ++debugOutput_ActionsTried);
        }

        if (bestAction.Score <= scoreOnEntry)
        {
            // couldn't find an action that resulted in a better state; do nothing
            bestAction.Type = AIActionType.DoNothing; // ???
            bestAction.DebugOutput_NextAction = null;
            bestAction.Score = scoreOnEntry;
        }

        return bestAction;
    }

    private void TrySendWorkersToEmptyNode(AI_NodeState fromNode, ref AIAction bestAction, int curDepth, int recurseCount, int thisActionNum)
    {
        if (fromNode.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut)
            return; // not enough workers in node to send any out

        if (!fromNode.HasBuilding)
            return; // Must have a building in a node to send workers from it 

        var count = fromNode.NumNeighbors;
        for (int i = 0; i < count; i++)
        {
            var toNode = fromNode.NeighborNodes[i];

            // Verify we can perform the action
            if (toNode.OwnedBy != null) continue; // This task can't send workers to nodes owned by anyone (including this player).  Those are handled in other actions
            if (toNode.IsResourceNode) continue; // Can't send to resource nodes

            // Perform the action and get the score of the state after the action is performed
            aiTownState.SendWorkersToEmptyNode(fromNode, toNode, .5f, out int numSent);
            var debugOutput_actionScoreReasons = aiTownState.EvaluateScore(out float scoreAfterActionAndBeforeSubActions);

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
                if (GameMgr.Instance.DebugOutputStrategy)
                    bestAction.TrackStrategyDebugInfoInAction(actionScore, debugOutput_actionScoreReasons, thisActionNum, recurseCount, curDepth);
#endif
            }

            // Undo the action
            aiTownState.Undo_SendWorkersToEmptyNode(fromNode, toNode, numSent);
        }
    }

    private void TryConstructBuildingInNode(AI_NodeState node, ref AIAction bestAction, int curDepth, int recurseCount, int thisActionNum)
    {
        if (node.HasBuilding)
            return; // Node already has a building

        // TODO: Only attempt to construct buildings that we have resources within 'reach' to build.
        for (int i = 0; i < numBuildingDefns; i++)
        {
            var buildingDefn = buildableBuildingDefns[i];

            // Verify we can perform the action
            if (!aiTownState.ConstructionResourcesCanBeReachedFromNode(node, buildingDefn.ConstructionRequirements)) continue;

            // Perform the action and get the score of the state after the action is performed
            aiTownState.BuildBuilding(node, buildingDefn, out GoodType res1Id, out int resource1Amount, out GoodType res2Id, out int resource2Amount);
            var debugOutput_actionScoreReasons = aiTownState.EvaluateScore(out float scoreAfterActionAndBeforeSubActions);

            // Recursively determine what the best action is after this action is performed
            var actionScore = RecursivelyDetermineBestAction(curDepth + 1, scoreAfterActionAndBeforeSubActions);
            if (actionScore.Score > bestAction.Score)
            {
                // This is the best action so far; save the action so we can return it
                bestAction.Score = actionScore.ScoreBeforeSubActions;
                bestAction.Type = AIActionType.ConstructBuildingInOwnedNode;
                bestAction.SourceNode = node;
                bestAction.BuildingToConstruct = buildingDefn;
#if DEBUG
                if (GameMgr.Instance.DebugOutputStrategy)
                    bestAction.TrackStrategyDebugInfoInAction(actionScore, debugOutput_actionScoreReasons, thisActionNum, recurseCount, curDepth);
#endif
            }

            // Undo the action
            aiTownState.Undo_BuildBuilding(node, res1Id, resource1Amount, res2Id, resource2Amount);
        }
    }

    private void TryAttackFromNode(AI_NodeState fromNode, ref AIAction bestAction, int curDepth, int recurseCount, int thisActionNum)
    {
        // Attack from nodes that have at least 1 worker and a building, and at least 1 neighbor that is owned by another player
        // TODO: Attack farther away nodes too
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
            // aiTownState.AttackFromNode(fromNode, toNode, out int numSent);
            var debugOutput_actionScoreReasons = aiTownState.EvaluateScore(out float scoreAfterActionAndBeforeSubActions);

            // Recursively determine what the best action is after this action is performed
            var actionScore = RecursivelyDetermineBestAction(curDepth + 1, scoreAfterActionAndBeforeSubActions);
            if (actionScore.Score > bestAction.Score)
            {
                // This is the best action so far; save the action so we can return it
                bestAction.Score = actionScore.ScoreBeforeSubActions;
                bestAction.Type = AIActionType.AttackFromNode;
                // bestAction.Count = numSent;
                bestAction.SourceNode = fromNode;
                bestAction.DestNode = toNode;
#if DEBUG
                if (GameMgr.Instance.DebugOutputStrategy)
                    bestAction.TrackStrategyDebugInfoInAction(actionScore, debugOutput_actionScoreReasons, thisActionNum, recurseCount, curDepth);
#endif
            }

            // Undo the action
            // aiTownState.Undo_AttackFromNode(fromNode, toNode, numSent);
        }
    }
}
