public class AITask_TryButtressOwnedNode : AITask
{
    public AITask_TryButtressOwnedNode(PlayerData player, AI_TownState aiTownState, int maxDepth, int minWorkersInNodeBeforeConsideringSendingAnyOut) : base(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut) { }

    override public AIAction TryTask(AI_NodeState fromNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions)
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
            var debuggerEntry = aiDebuggerParentEntry.AddEntry_SendWorkersToOwnedNode(fromNode, toNode, numSent, 0, player.AI.debugOutput_ActionsTried++, curDepth);

            // ==== Determine the score of the action we just performed (recurse down); if this is the best so far amongst our peers (in our parent node) then track it as the best action
            var actionScore = GetActionScore(curDepth, debuggerEntry);
            if (actionScore > bestAction.Score)
                bestAction.SetTo_SendWorkersToOwnedNode(fromNode, toNode, numSent, actionScore, debuggerEntry);

            // ==== Undo the action to reset the townstate to its original state
            aiTownState.Undo_SendWorkersToOwnedNode(fromNode, toNode, numSent);
        }
        return bestAction;
    }
}
