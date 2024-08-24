public partial class PlayerAI
{
    // Determine the best action that can be taken given the current aiTownState and return that action, ensuring
    // that aiTownState is fully restored to its original state before returning.
    public AIAction DetermineBestActionToPerform(int curDepth, AIDebuggerEntryData parentDebuggerEntry)
    {
        // we'll return the best action from all possible actions at this 'recursive step/turn'
        AIAction bestAction = new();
        // if (curDepth == maxDepth || aiTownState.IsGameOver())
        // {
        //     bestAction.Type = curDepth == maxDepth ? AIActionType.NoAction_MaxDepth : AIActionType.NoAction_GameOver;
        //     bestAction.Score = 0;
        //     return bestAction;
        // }

        // Update townstate at the start of this 'recursive step'; e.g. woodcutters get +1 wood...
        // TODO: Combine this with TownData.Debug_WorldTurn somehow
        foreach (var node in aiTownState.Nodes)
        {
            if (node.CanGoGatherResources && node.OwnedBy == player)
                aiTownState.PlayerTownInventory[node.ResourceThisNodeCanGoGather] += 3; // simple for now
            node.aiOrigNumWorkers = node.NumWorkers;
            if (node.CanGenerateWorkers && node.NumWorkers < node.MaxWorkers)
            {
                node.NumWorkers += node.WorkersGeneratedPerTurn; // TODO: node.Building.Defn.WorkersGeneratedPerTurn; 
            }
        }

        // bestAction is currently set to 'do nothing' -- see if taking any of our available actions results in a better score
        foreach (var node in aiTownState.Nodes)
        {
            if (node.OwnedBy != player) continue; // only process actions from/in nodes that we own

            foreach (var task in Tasks)
            {
                var action = task.TryTask(node, curDepth, debugOutput_ActionsTried++, parentDebuggerEntry, bestAction.Score);
                if (action.Score > bestAction.Score)
                {
                    bestAction = action;
                    parentDebuggerEntry.BestNextAction = bestAction.AIDebuggerEntry;
                }
            }
        }

        // Attacking AI needs to take into account abiliyt to send workers from multiple nodes to 'pincer' attack
        // a node.  The above loop starts from sourceNode so it won't work.  Need a different loop that starts from destNode
        // foreach (var node in aiTownState.Nodes)
        // {
        //     if (node.OwnedBy == player || node.OwnedBy == null) continue;
        //     var action = TryAttackToNode(node, curDepth, debugOutput_ActionsTried++, parentDebuggerEntry, bestAction.Score);
        //     if (action.Score > bestAction.Score)
        //     {
        //         bestAction = action;
        //         parentDebuggerEntry.BestNextAction = bestAction.AIDebuggerEntry;
        //     }
        // }




        // TODO: restore town state
        foreach (var node in aiTownState.Nodes)
        {
            if (node.CanGoGatherResources && node.OwnedBy == player)
                aiTownState.PlayerTownInventory[node.ResourceThisNodeCanGoGather] -= 3; // simple for now
            node.NumWorkers = node.aiOrigNumWorkers;
        }
        if (bestAction.Type == AIActionType.DoNothing)
            return null;
        return bestAction;
    }
}
