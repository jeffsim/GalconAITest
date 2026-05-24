using System.Diagnostics;

/// <summary>
/// Capture neutral resource nodes (Forest / StoneMine) that already have a building on them.
/// AITask_ConstructBuilding skips HasBuilding targets, so without this task those nodes are
/// unreachable even when adjacent and listed as CaptureNode goals.
/// </summary>
public class AITask_CaptureNeutralResourceNode : AITask
{
    public AITask_CaptureNeutralResourceNode(PlayerData player, AI_TownState aiTownState, int maxDepth, int minWorkersInNodeBeforeConsideringSendingAnyOut)
        : base(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut) { }

    public override float PreviewHeuristic(AI_NodeState fromNode)
    {
        if (fromNode.OwnedBy != player) return 0f;
        if (fromNode.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut) return 0f;

        float best = 0f;
        foreach (var toNode in fromNode.NeighborNodes)
        {
            if (!IsCapturableResourceNode(toNode)) continue;
            if (AI_ActionHeuristics.IsCaptureAlreadyCommitted(toNode, player)) continue;

            float overkill = player.AIDefn != null ? player.AIDefn.AttackOverkillMultiplier : 1f;
            if (!AI_ActionHeuristics.TryGetCaptureWorkersToSend(fromNode, toNode, player, minWorkersInNodeBeforeConsideringSendingAnyOut, overkill, out int numToSend))
                continue;

            float h = AI_ActionHeuristics.GetCaptureResourceNodeHeuristic(aiTownState, toNode, numToSend);
            if (h > best) best = h;
        }
        if (best <= 0f) return 0f;

        return best * AI_ActionHeuristics.GetPersonalityMultiplier(player, AIHeuristicActionType.Capture);
    }

    public override bool TryTask(AI_NodeState fromNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions, out AIAction bestAction)
    {
        bestAction = null;
        if (fromNode.OwnedBy != player) return false;
        if (fromNode.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut) return false;

        bestAction = player.AI.GetAIAction();
        float overkill = player.AIDefn != null ? player.AIDefn.AttackOverkillMultiplier : 1f;

        foreach (var toNode in fromNode.NeighborNodes)
        {
            if (!IsCapturableResourceNode(toNode)) continue;
            if (AI_ActionHeuristics.IsCaptureAlreadyCommitted(toNode, player)) continue;
            if (toNode.IsVisited) continue;

            if (!AI_ActionHeuristics.TryGetCaptureWorkersToSend(fromNode, toNode, player, minWorkersInNodeBeforeConsideringSendingAnyOut, overkill, out int numToSend))
                continue;

            float heuristicBonus = AI_ActionHeuristics.GetCaptureResourceNodeHeuristic(aiTownState, toNode, numToSend);
            if (heuristicBonus <= 0f) continue;

            float runningPeerBest = bestAction.Score;
            if (bestScoreAmongPeerActions > runningPeerBest) runningPeerBest = bestScoreAmongPeerActions;
            if (ShouldPruneByHeuristic(heuristicBonus, AIHeuristicActionType.Capture, runningPeerBest))
                break;

            int d1 = fromNode.NumWorkers, d2 = toNode.NumWorkers;
            aiTownState.CaptureNeutralResourceNode(fromNode, toNode, numToSend, out int numSent, out int origSource, out int origDest, out PlayerData origOwner);
            if (numSent <= 0) continue;

            var debuggerEntry = aiDebuggerParentEntry?.AddEntry_CaptureNeutralResourceNode(fromNode, toNode, numSent, 0, player.AI.debugOutput_ActionsTried++, curDepth);

            var actionScore = GetActionScore(curDepth, debuggerEntry);
            actionScore = AI_ActionHeuristics.ApplyHeuristicAndPersonality(actionScore, heuristicBonus, player, AIHeuristicActionType.Capture);
            if (actionScore > bestAction.Score)
                bestAction.SetTo_CaptureNeutralResourceNode(fromNode, toNode, numSent, actionScore, debuggerEntry);

            aiTownState.Undo_CaptureNeutralResourceNode(fromNode, toNode, origSource, origDest, origOwner);
            Debug.Assert(d1 == fromNode.NumWorkers && d2 == toNode.NumWorkers);
        }

        return bestAction.Type != AIActionType.DoNothing;
    }

    static bool IsCapturableResourceNode(AI_NodeState toNode)
    {
        return toNode.OwnedBy == null && toNode.CanBeGatheredFrom;
    }
}
