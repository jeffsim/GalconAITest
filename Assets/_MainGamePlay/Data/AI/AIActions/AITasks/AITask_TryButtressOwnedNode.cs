public class AITask_TryButtressOwnedNode : AITask
{
    public AITask_TryButtressOwnedNode(PlayerData player, AI_TownState aiTownState, int maxDepth, int minWorkersInNodeBeforeConsideringSendingAnyOut) : base(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut) { }

    override public bool TryTask(AI_NodeState fromNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions, out AIAction bestAction)
    {
        bestAction = null;
        
        if (fromNode.OwnedBy != player) // only process actions from/in nodes that we own
            return false;

        if (fromNode.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut)
            return false; // not enough workers in node to send any out

        bestAction = player.AI.GetAIAction();

        foreach (var toNode in fromNode.NeighborNodes)
        {
            // ==== Verify we can perform the action
            if (toNode.OwnedBy != player) continue;
            if (toNode.IsVisited) continue; // don't revisit nodes we visited earlier in the recursion; avoid ping-ponging between nodes

            // ==== Perform the action and update the aiTownState to reflect the action
            aiTownState.SendWorkersToOwnedNode(fromNode, toNode, .5f, out int numSent); // TODO: Try different #s?
            var debuggerEntry = aiDebuggerParentEntry.AddEntry_SendWorkersToOwnedNode(fromNode, toNode, numSent, 0, player.AI.debugOutput_ActionsTried++, curDepth);

            // ==== Determine the score of the action we just performed (recurse down); if this is the best so far amongst our peers (in our parent node) then track it as the best action
            var actionScore = GetActionScore(curDepth, debuggerEntry);
            if (actionScore > bestAction.Score)
                bestAction.SetTo_SendWorkersToOwnedNode(fromNode, toNode, numSent, actionScore, debuggerEntry);

            // ==== Undo the action to reset the townstate to its original state
            aiTownState.Undo_SendWorkersToOwnedNode(fromNode, toNode, numSent);
        }
        return true;
    }
}
