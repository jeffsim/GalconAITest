using System;

public partial class PlayerAI
{
    // Determine the best action that can be taken given the current aiTownState and return that action, ensuring
    // that aiTownState is fully restored to its original state before returning.
    public AIAction DetermineBestActionToPerform(int curDepth, AIDebuggerEntryData parentDebuggerEntry)
    {
        // we'll return the best action from all possible actions at this 'recursive step/turn'
        var bestAction = GetAIAction();

        // Snapshot the pre-action EvaluateScore so peer tasks at this depth can use it as an
        // optimistic upper bound for branch-and-bound pruning. Saved/restored so deeper recursion
        // levels can each maintain their own baseline.
        float prevBaseline = currentDepthBaselineScore;
        currentDepthBaselineScore = aiTownState.EvaluateScore(curDepth, maxDepth, out _);

        // const int numResources = 3;
        // Update townstate at the start of this 'recursive step'; e.g. woodcutters get +wood...
        // for (int i = 0; i < aiTownState.Nodes.Length; i++)
        // {
        //     var node = aiTownState.Nodes[i];
        //     if (node.CanGoGatherResources && node.OwnedBy == player)
        //         aiTownState.PlayerTownInventory[node.ResourceThisNodeCanGoGather] += numResources;
        //     node.aiOrigNumWorkers = node.NumWorkers;
        //     if (node.CanGenerateWorkers)
        //     {
        //         if (node.NumWorkers < node.MaxWorkers)
        //             node.NumWorkers = Math.Min(node.MaxWorkers, node.NumWorkers + node.WorkersGeneratedPerTurn);
        //         else if (node.NumWorkers > node.MaxWorkers)
        //             node.NumWorkers--;
        //     }
        // }

        // bestAction is currently set to 'do nothing' -- see if taking any of our available actions results in a better score
        for (int i = 0; i < aiTownState.Nodes.Length; i++)
        {
            var node = aiTownState.Nodes[i];
            if (node.IsVisited) continue; // don't revisit nodes we visited earlier in the recursion; avoid ping-ponging between nodes
            node.IsVisited = true;
            for (int t = 0; t < Tasks.Count; t++)
            {
                var task = Tasks[t];

                bool validTask = task.TryTask(node, curDepth, debugOutput_ActionsTried, parentDebuggerEntry, bestAction.Score, out AIAction action);
                if (validTask && action.Score > bestAction.Score)
                {
                    bestAction = action;
                    if (parentDebuggerEntry != null)
                        parentDebuggerEntry.BestNextAction = bestAction.AIDebuggerEntry;
                }
            }
            node.IsVisited = false;
        }

        // Restore town state. TODO: More?
        // for (int i = 0; i < aiTownState.Nodes.Length; i++)
        // {
        //     var node = aiTownState.Nodes[i];
        //     if (node.CanGoGatherResources && node.OwnedBy == player)
        //         aiTownState.PlayerTownInventory[node.ResourceThisNodeCanGoGather] -= numResources;
        //     node.NumWorkers = node.aiOrigNumWorkers;
        // }
        currentDepthBaselineScore = prevBaseline;
        return bestAction.Type == AIActionType.DoNothing ? null : bestAction;
    }
}
