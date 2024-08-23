public partial class PlayerAI
{
    private AIAction TryButtressOwnedNode(AI_NodeState fromNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions)
    {
        var bestAction = new AIAction() { Type = AIActionType.DoNothing };

        if (fromNode.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut)
            return bestAction; // not enough workers in node to send any out

        // TODO: Support > 1 node away
        foreach (var toNode in fromNode.NeighborNodes)
        {
            // ==== Verify we can perform the action
            if (toNode.OwnedBy != player) continue;

            // ==== Perform the action and update the aiTownState to reflect the action
            aiTownState.SendWorkersToOwnedNode(fromNode, toNode, .5f, out int numSent); // TODO: Try different #s?
            var debuggerEntry = aiDebuggerParentEntry.AddEntry_SendWorkersToOwnedNode(fromNode, toNode, numSent, 0, debugOutput_ActionsTried++, curDepth);
            // debuggerEntry.Debug_ActionScoreBeforeSubactions = aiTownState.EvaluateScore(curDepth, maxDepth, out _);

            // ==== Determine the score of the action we just performed; recurse down into subsequent actions if we're not at the max depth
            float actionScore;
            AIAction bestNextAction = curDepth < maxDepth ? DetermineBestActionToPerform(curDepth + 1, debuggerEntry) : null;
            if (bestNextAction != null)
                actionScore = bestNextAction.Score; // Score of the best action after this action
            else
                actionScore = aiTownState.EvaluateScore(curDepth, maxDepth, out _); // Evaluate score of the current state after this action
            debuggerEntry.FinalActionScore = actionScore;

            // ==== If this action is the best so far amongst our peers (in our parent node) then track it as the best action
            if (actionScore > bestAction.Score)
                bestAction.SetTo_SendWorkersToOwnedNode(fromNode, toNode, numSent, actionScore, debuggerEntry);

            // ==== Undo the action to reset the townstate to its original state
            aiTownState.Undo_SendWorkersToOwnedNode(fromNode, toNode, numSent);
        }
        return bestAction;
    }
}
