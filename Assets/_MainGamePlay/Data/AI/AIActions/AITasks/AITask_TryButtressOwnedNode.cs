using System.Diagnostics;

public class AITask_TryButtressOwnedNode : AITask
{
    public AITask_TryButtressOwnedNode(PlayerData player, AI_TownState aiTownState, int maxDepth, int minWorkersInNodeBeforeConsideringSendingAnyOut) : base(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut) { }

    public override float PreviewHeuristic(AI_NodeState toNode)
    {
        if (toNode.OwnedBy != player) return 0f;
        // Note: do not gate on PlayerHasExcessWorkers here; the actual source-node check inside
        // TryTask (fromNode.NumWorkers >= MaxWorkers * 3/4) is the correct precondition. The
        // global "excess" check disables buttress in balanced states where a high-DefenseWeight
        // AI should still reinforce a vulnerable frontier from a healthy source.
        float h = AI_ActionHeuristics.GetButtressHeuristic(aiTownState, toNode);
        if (h <= 0f) return 0f;

        // Apply personality so Phase 1 candidate ranking matches actual scoring. Without this,
        // a low-DefenseWeight AI's buttresses still rank by raw heuristic and crowd out higher-
        // priority actions like attacks for a high-AggressivenessWeight AI.
        return h * AI_ActionHeuristics.GetPersonalityMultiplier(player, AIHeuristicActionType.Buttress);
    }

    override public bool TryTask(AI_NodeState toNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions, out AIAction bestAction)
    {
        bestAction = null;

        if (toNode.OwnedBy != player)
            return false;

        // Source-node validity (fromNode.NumWorkers >= MaxWorkers * 3/4 below) is the real
        // precondition. The previous PlayerHasExcessWorkers guard blocked buttress in balanced
        // states even when a high-DefenseWeight personality should reinforce a frontier.

        float heuristicBonus = AI_ActionHeuristics.GetButtressHeuristic(aiTownState, toNode);
        if (heuristicBonus <= 0f)
            return false;

        if (ShouldPruneByHeuristic(heuristicBonus, AIHeuristicActionType.Buttress, bestScoreAmongPeerActions))
            return false;

        var fromNode = AI_ActionHeuristics.GetFriendlyNodeWithMostWorkers(toNode, player);
        if (fromNode == null || fromNode == toNode)
            return false;

        // Strict less-than (matches GetWorkersWillingToSend). A node at exactly 75% capacity
        // IS willing to send half its workers; the previous <= here over-rejected those edge
        // cases and starved Buttress for entire searches when the only candidate source sat
        // at the boundary.
        if (fromNode.NumWorkers < fromNode.MaxWorkers * 3f / 4f)
            return false;

        if (fromNode.IsVisited)
            return false;

        bestAction = player.AI.GetAIAction();

        int d1 = fromNode.NumWorkers, d2 = toNode.NumWorkers;
        aiTownState.SendWorkersToOwnedNode(fromNode, toNode, .5f, out int numSent);
        var debuggerEntry = aiDebuggerParentEntry?.AddEntry_SendWorkersToOwnedNode(fromNode, toNode, numSent, 0, player.AI.debugOutput_ActionsTried++, curDepth);

        var actionScore = GetActionScore(curDepth, debuggerEntry);
        actionScore = AI_ActionHeuristics.ApplyHeuristicAndPersonality(actionScore, heuristicBonus, player, AIHeuristicActionType.Buttress);
        if (actionScore > bestAction.Score)
            bestAction.SetTo_SendWorkersToOwnedNode(fromNode, toNode, numSent, actionScore, debuggerEntry);

        aiTownState.Undo_SendWorkersToOwnedNode(fromNode, toNode, numSent);
        Debug.Assert(d1 == fromNode.NumWorkers && d2 == toNode.NumWorkers);
        return true;
    }
}
