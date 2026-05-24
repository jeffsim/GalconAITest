using System;
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
        if (!AI_ActionHeuristics.CanButtressFromAnySource(toNode, player, minWorkersInNodeBeforeConsideringSendingAnyOut)) return 0f;

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

        var fromNode = AI_ActionHeuristics.GetButtressSourceNode(toNode, player, minWorkersInNodeBeforeConsideringSendingAnyOut);
        if (fromNode == null || fromNode == toNode)
            return false;

        if (IsButtressOscillation(fromNode, toNode))
            return false;

        // Use effective pressure (snapshot + heat) so chokepoints under sustained attack still
        // trip emergency and can pull from sources below the normal 75% capacity threshold,
        // even when the current frame's enemy-neighbor count looks low.
        bool emergency = AI_ActionHeuristics.GetEffectiveFrontierPressure(toNode) > toNode.NumWorkers
                         || toNode.NumContestedNeutralWorkersNearby > toNode.NumWorkers / 2
                         || toNode.AttackHeat >= AI_ActionHeuristics.AttackHeatEmergencyThreshold;
        int minOnSource = emergency
            ? minWorkersInNodeBeforeConsideringSendingAnyOut
            : (int)(fromNode.MaxWorkers * 3f / 4f);
        if (fromNode.NumWorkers < minOnSource)
            return false;

        if (fromNode.IsVisited)
            return false;

        int deficit = AI_ActionHeuristics.GetFrontierWorkerDeficit(toNode);
        if (AI_ActionHeuristics.NeedsResourceStaffingButtress(aiTownState, toNode))
        {
            int desired = AI_ActionHeuristics.GetDesiredWorkersForResourceNode(aiTownState, toNode);
            deficit = Math.Max(deficit, desired - toNode.NumWorkers);
        }
        if (AI_ActionHeuristics.IsUnderstaffedFrontier(toNode))
        {
            int capacityDeficit = toNode.MaxWorkers - toNode.NumWorkers;
            deficit = Math.Max(deficit, capacityDeficit);
        }
        float riskTolerance = AI_ActionHeuristics.GetUpgradeRiskTolerance(player);
        if (AI_ActionHeuristics.NeedsUpgradeOverloadButtress(toNode, riskTolerance))
        {
            int overloadDesired = AI_ActionHeuristics.GetDesiredOverloadForUpgrade(toNode, riskTolerance);
            deficit = Math.Max(deficit, overloadDesired - toNode.NumWorkers);
        }
        if (deficit <= 0)
            return false;

        int willing = AI_ActionHeuristics.GetWorkersWillingToSendForDefense(fromNode, minWorkersInNodeBeforeConsideringSendingAnyOut, emergency, toNode.AttackHeat);
        int numToSend = Math.Min(willing, deficit);
        if (numToSend <= 0)
            return false;

        bestAction = player.AI.GetAIAction();

        int d1 = fromNode.NumWorkers, d2 = toNode.NumWorkers;
        aiTownState.SendWorkersToOwnedNode(fromNode, toNode, numToSend, out int numSent);
        var debuggerEntry = aiDebuggerParentEntry?.AddEntry_SendWorkersToOwnedNode(fromNode, toNode, numSent, 0, player.AI.debugOutput_ActionsTried++, curDepth);

        var actionScore = GetActionScore(curDepth, debuggerEntry);
        actionScore = AI_ActionHeuristics.ApplyHeuristicAndPersonality(actionScore, heuristicBonus, player, AIHeuristicActionType.Buttress);
        if (actionScore > bestAction.Score)
            bestAction.SetTo_SendWorkersToOwnedNode(fromNode, toNode, numSent, actionScore, debuggerEntry);

        aiTownState.Undo_SendWorkersToOwnedNode(fromNode, toNode, numSent);
        Debug.Assert(d1 == fromNode.NumWorkers && d2 == toNode.NumWorkers);
        return true;
    }

    bool IsButtressOscillation(AI_NodeState fromNode, AI_NodeState toNode)
    {
        var last = player.AI.LastActionToTake;
        if (last == null || last.Type != AIActionType.SendWorkersToOwnedNode)
            return false;
        // Block immediate reverse shuffle: last was A->B, now trying B->A.
        return last.SourceNode == toNode && last.DestNode == fromNode;
    }
}
