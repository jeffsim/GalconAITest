using System;

public partial class PlayerAI
{
    // Determine the best action that can be taken given the current aiTownState and return that action, ensuring
    // that aiTownState is fully restored to its original state before returning.
    AIAction DetermineBestActionToPerform(int curDepth, AIDebuggerEntryData parentDebuggerEntry)
    {
        // we'll return the best action from all possible actions at this 'recursive step/turn'
        AIAction bestAction = new();
        // if (curDepth == maxDepth || aiTownState.IsGameOver())
        // {
        //     bestAction.Type = curDepth == maxDepth ? AIActionType.NoAction_MaxDepth : AIActionType.NoAction_GameOver;
        //     bestAction.Score = 0;
        //     return bestAction;
        // }

        // Update inventory counts at the start of this 'recursive step'; e.g. woodcutters get +1 wood...
        foreach (var node in aiTownState.Nodes)
            if (node.CanGoGatherResources && node.OwnedBy == player)
                aiTownState.PlayerTownInventory[node.ResourceThisNodeCanGoGather] += 3; // simple for now

        // bestAction is currently set to 'do nothing' -- see if taking any of our available actions results in a better score
        for (int i = 0; i < aiTownState.NumNodes; i++)
        {
            var node = aiTownState.Nodes[i];
            if (node.OwnedBy != player) continue; // only process actions from/in nodes that we own

            var action = TrySendWorkersToConstructBuildingInEmptyNeighboringNode(node, curDepth, debugOutput_ActionsTried++, parentDebuggerEntry, bestAction.Score);
            if (action.Score > bestAction.Score)
                bestAction = action;
        }

        return bestAction;
    }
}
