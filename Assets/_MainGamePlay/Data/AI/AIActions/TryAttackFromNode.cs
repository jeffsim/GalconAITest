public enum AttackResult { Undefined, AttackerWon, DefenderWon, BothSidesDied };

public partial class PlayerAI
{
    private AIAction TryAttackFromNode(AI_NodeState fromNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions)
    {
        var bestAction = new AIAction() { Type = AIActionType.DoNothing };

        // Attack from nodes that have at least 1 worker and a building, and at least 1 neighbor that is owned by another player
        // TODO: Attack farther away nodes too (as long as we have buildings in interim nodes)
        if (!fromNode.HasBuilding || fromNode.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut)
            return bestAction;

        // are any neighbors owned by another player?
        foreach (var toNode in fromNode.NeighborNodes)
        {
            // ==== Verify we can perform the action
            if (toNode.OwnedBy == null || toNode.OwnedBy == player) continue;

            // ==== Perform the action and update the aiTownState to reflect the action
            aiTownState.AttackFromNode(fromNode, toNode, out AttackResult attackResult, out int origNumInSourceNode, out int origNumInDestNode, out int numSent, out PlayerData origToNodeOwner);
            var debuggerEntry = aiDebuggerParentEntry.AddEntry_AttackFromNode(fromNode, toNode, attackResult, numSent, 0, debugOutput_ActionsTried++, curDepth);
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
                bestAction.SetTo_AttackFromNode(fromNode, toNode, numSent, attackResult, actionScore, debuggerEntry);

            // ==== Undo the action to reset the townstate to its original state
            aiTownState.Undo_AttackFromNode(fromNode, toNode, attackResult, origNumInSourceNode, origNumInDestNode, numSent, origToNodeOwner);
        }
        return bestAction;
    }
}
