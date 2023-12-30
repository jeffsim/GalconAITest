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
        if (debugOutput_ActionsTried >= maxPoolSize)
        {
            Debug.Assert(debugOutput_ActionsTried < maxPoolSize, "stuck in loop in RecursivelyDetermineBestAction ");
            return new AIAction() { Type = AIActionType.ERROR_StuckInLoop, Score = scoreOnEntry };
        }
#endif

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
            TryAttackFromNode(node, ref bestAction, curDepth, recurseCount, ++debugOutput_ActionsTried);
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
}
