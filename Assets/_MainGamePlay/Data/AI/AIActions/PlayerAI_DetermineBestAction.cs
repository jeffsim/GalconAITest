using System;
using UnityEngine.Pool;

public partial class PlayerAI
{
    // Determine the best action that can be taken given the current aiTownState and return that action, ensuring
    // that aiTownState is fully restored to its original state before returning.
    public AIAction DetermineBestActionToPerform(int curDepth, AIDebuggerEntryData parentDebuggerEntry)
    {
        // we'll return the best action from all possible actions at this 'recursive step/turn'
        AIAction bestAction = GetAIAction();

        // Update townstate at the start of this 'recursive step'; e.g. woodcutters get +1 wood...
        // TODO: Combine this with TownData.Debug_WorldTurn somehow
        for (int i = 0; i < aiTownState.Nodes.Length; i++)
        {
            var node = aiTownState.Nodes[i];
            if (node.CanGoGatherResources && node.OwnedBy == player)
                aiTownState.PlayerTownInventory[node.ResourceThisNodeCanGoGather] += 3; // simple for now
            node.aiOrigNumWorkers = node.NumWorkers;
            if (node.CanGenerateWorkers)
            {
                if (node.NumWorkers < node.MaxWorkers)
                    node.NumWorkers = Math.Min(node.MaxWorkers, node.NumWorkers + node.WorkersGeneratedPerTurn);
                else if (node.NumWorkers > node.MaxWorkers)
                    node.NumWorkers--;
            }
        }

        // bestAction is currently set to 'do nothing' -- see if taking any of our available actions results in a better score
        for (int i = 0; i < aiTownState.Nodes.Length; i++)
        {
            var node = aiTownState.Nodes[i];
            for (int t = 0; t < Tasks.Count; t++)
            {
                var task = Tasks[t];
            
                bool validTask = task.TryTask(node, curDepth, debugOutput_ActionsTried++, parentDebuggerEntry, bestAction.Score, out AIAction action);
                if (validTask && action.Score > bestAction.Score)
                {
                    bestAction = action;
                    parentDebuggerEntry.BestNextAction = bestAction.AIDebuggerEntry;
                }
            }
        }

        // Restore town state. TODO: More?
        for (int i = 0; i < aiTownState.Nodes.Length; i++)
        {
            var node = aiTownState.Nodes[i];
            if (node.CanGoGatherResources && node.OwnedBy == player)
                aiTownState.PlayerTownInventory[node.ResourceThisNodeCanGoGather] -= 3; // simple for now
            node.NumWorkers = node.aiOrigNumWorkers;
        }
        return bestAction.Type == AIActionType.DoNothing ? null : bestAction;
    }
}
