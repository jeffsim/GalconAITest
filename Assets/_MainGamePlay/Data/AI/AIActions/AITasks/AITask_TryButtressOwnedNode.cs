using System.Diagnostics;

public class AITask_TryButtressOwnedNode : AITask
{
    public AITask_TryButtressOwnedNode(PlayerData player, AI_TownState aiTownState, int maxDepth, int minWorkersInNodeBeforeConsideringSendingAnyOut) : base(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut) { }

    override public bool TryTask(AI_NodeState toNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions, out AIAction bestAction)
    {
        bestAction = null;

        if (toNode.OwnedBy != player)
            return false;

        if (!AI_ActionHeuristics.PlayerHasExcessWorkers(aiTownState, player))
            return false;

        float heuristicBonus = AI_ActionHeuristics.GetButtressHeuristic(toNode);
        if (heuristicBonus <= 0f)
            return false;

        var fromNode = AI_ActionHeuristics.GetFriendlyNodeWithMostWorkers(toNode, player);
        if (fromNode == null || fromNode == toNode)
            return false;

        if (fromNode.NumWorkers <= fromNode.MaxWorkers * 3f / 4f)
            return false;

        if (fromNode.IsVisited)
            return false;

        bestAction = player.AI.GetAIAction();

        int d1 = fromNode.NumWorkers, d2 = toNode.NumWorkers;
        aiTownState.SendWorkersToOwnedNode(fromNode, toNode, .5f, out int numSent);
        var debuggerEntry = aiDebuggerParentEntry.AddEntry_SendWorkersToOwnedNode(fromNode, toNode, numSent, 0, player.AI.debugOutput_ActionsTried++, curDepth);

        var actionScore = GetActionScore(curDepth, debuggerEntry);
        actionScore = AI_ActionHeuristics.ApplyHeuristicAndPersonality(actionScore, heuristicBonus, player, AIHeuristicActionType.Buttress);
        if (actionScore > bestAction.Score)
            bestAction.SetTo_SendWorkersToOwnedNode(fromNode, toNode, numSent, actionScore, debuggerEntry);

        aiTownState.Undo_SendWorkersToOwnedNode(fromNode, toNode, numSent);
        Debug.Assert(d1 == fromNode.NumWorkers && d2 == toNode.NumWorkers);
        return true;
    }
}
