using UnityEngine;

public partial class PlayerAI
{
    // Determine the best action that can be taken given the current aiTownState and return that action, ensuring
    // that aiTownState is fully restored to its original state before returning.
    AIAction RecursivelyDetermineBestAction(int curDepth, float scoreOnEntry)
    {
        var recurseCount = ++debugOutput_callsToRecursivelyDetermineBestAction;

        if (actionPoolIndex >= maxPoolSize)
        {
            // Grow the pool by maxPoolSize
            maxPoolSize *= 2;
            var newPool = new AIAction[maxPoolSize];
            for (int i = 0; i < maxPoolSize; i++)
                newPool[i] = new AIAction();
            actionPool = newPool;
        }
        AIAction bestAction = actionPool[actionPoolIndex++];
        bestAction.ScoreBeforeSubActions = scoreOnEntry;
        if (curDepth == maxDepth || aiTownState.IsGameOver())
        {
            bestAction.Type = curDepth == maxDepth ? AIActionType.NoAction_MaxDepth : AIActionType.NoAction_GameOver;
            bestAction.Score = scoreOnEntry;
            return bestAction;
        }

        // Update inventory counts
        int prevWood = aiTownState.PlayerTownInventory_Wood;
        int prevStone = aiTownState.PlayerTownInventory_Stone;
        int prevStoneWoodPlank = aiTownState.PlayerTownInventory_StoneWoodPlank;
        foreach (var node in aiTownState.Nodes)
        {
            if (node.CanGoGatherResources && node.OwnedBy == player)
            {
                if (node.ResourceThisNodeCanGoGather == GoodType.Wood)
                    aiTownState.PlayerTownInventory_Wood++; // simple for now
                if (node.ResourceThisNodeCanGoGather == GoodType.Stone)
                    aiTownState.PlayerTownInventory_Stone++; // simple for now
            }
        }

        bestAction.Score = 0;
        for (int i = 0; i < aiTownState.NumNodes; i++)
        {
            var node = aiTownState.Nodes[i];
            if (node.OwnedBy != player) continue; // only process actions from/in nodes that we own

            TrySendWorkersToConstructBuildingInEmptyNeighboringNode(node, ref bestAction, curDepth, recurseCount, ++debugOutput_ActionsTried);
            TryAttackFromNode(node, ref bestAction, curDepth, recurseCount, ++debugOutput_ActionsTried);
            //      TrySendWorkersToOwnedNode(node, ref bestAction, curDepth, recurseCount, ++debugOutput_ActionsTried);
        }

        if (bestAction.Score <= scoreOnEntry)
        {
            // couldn't find an action that resulted in a better state; do nothing for a turn and see if e.g. resources accumulate to allow a better action next turn
            bestAction.Type = AIActionType.DoNothing; // ???
            bestAction.DebugOutput_NextAction = null;
            bestAction.Score = scoreOnEntry;

            aiTownState.EvaluateScore(curDepth, maxDepth, out float scoreAfterActionAndBeforeSubActions, out DebugAIStateReasons debugOutput_actionScoreReasons);
            bestAction.Score = scoreAfterActionAndBeforeSubActions;

            RecursivelyDetermineBestAction(curDepth + 1, scoreOnEntry);
        }

        // Restore inventory counts
        aiTownState.PlayerTownInventory_Wood = prevWood;
        aiTownState.PlayerTownInventory_Stone = prevStone;
        aiTownState.PlayerTownInventory_StoneWoodPlank = prevStoneWoodPlank;

        return bestAction;
    }
}
