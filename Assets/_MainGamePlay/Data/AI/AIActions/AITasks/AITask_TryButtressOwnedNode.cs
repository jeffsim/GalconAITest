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
        // even when the current frame's enemy-neighbor count looks low. Destination garrison
        // is physical + in-flight friendly so help already on the way counts toward "calm".
        int destGarrison = toNode.EffectiveDefenseGarrison;
        bool emergency = AI_ActionHeuristics.GetEffectiveFrontierPressure(toNode) > destGarrison
                         || toNode.NumContestedNeutralWorkersNearby > destGarrison / 2
                         || toNode.AttackHeat >= AI_ActionHeuristics.AttackHeatEmergencyThreshold;
        // Personality-aware overkill: the dest's desired garrison (scaled by DefenseWeight
        // and ChokepointScore) exceeds what raw visible pressure alone would require.
        // Mirrors GetWorkersWillingToSendForDefense's threshold relaxation -- compute here
        // so the source-capacity gate below uses the same posture the willing check will.
        bool destNeedsOverkill = AI_ActionHeuristics.DestNeedsPersonalityOverkill(toNode, player);
        // Relax source-capacity gate when destination is emergency OR personality-driven
        // overkill demand exists. Without the overkill branch, the new personality-scaled
        // desired count produced a meaningful deficit but every source below 75% capacity
        // was bounced here before GetWorkersWillingToSendForDefense ever ran.
        float capacityFraction = (emergency || destNeedsOverkill) ? 0.5f : 0.75f;
        int minOnSource = emergency
            ? minWorkersInNodeBeforeConsideringSendingAnyOut
            : (int)(fromNode.MaxWorkers * capacityFraction);
        // Source check: physical NumWorkers only -- in-flight friendly workers on fromNode
        // are NOT available to dispatch; they're already in motion toward something else.
        if (fromNode.NumWorkers < minOnSource)
            return false;

        if (fromNode.IsVisited)
            return false;

        // All deficit terms below are destination-side ("how many more workers does toNode
        // need?"). Subtract the projected garrison (physical + in-flight friendly) so we
        // don't double-dispatch when a previous wave is already in transit -- that was the
        // root cause of the "Support 9 #22 -> #7" loop where #22 had only 1 physical worker
        // but a stack of perceived-incoming made the AI think it could keep dispatching.
        int deficit = AI_ActionHeuristics.GetFrontierWorkerDeficit(toNode, player);
        if (AI_ActionHeuristics.NeedsResourceStaffingButtress(aiTownState, toNode))
        {
            int desired = AI_ActionHeuristics.GetDesiredWorkersForResourceNode(aiTownState, toNode);
            deficit = Math.Max(deficit, desired - destGarrison);
        }
        if (AI_ActionHeuristics.IsUnderstaffedFrontier(toNode))
        {
            int capacityDeficit = toNode.MaxWorkers - destGarrison;
            deficit = Math.Max(deficit, capacityDeficit);
        }
        float riskTolerance = AI_ActionHeuristics.GetUpgradeRiskTolerance(player);
        if (AI_ActionHeuristics.NeedsUpgradeOverloadButtress(toNode, riskTolerance))
        {
            int overloadDesired = AI_ActionHeuristics.GetDesiredOverloadForUpgrade(toNode, riskTolerance);
            deficit = Math.Max(deficit, overloadDesired - destGarrison);
        }
        if (deficit <= 0)
            return false;

        int willing = AI_ActionHeuristics.GetWorkersWillingToSendForDefense(fromNode, minWorkersInNodeBeforeConsideringSendingAnyOut, emergency, toNode.AttackHeat, destNeedsOverkill);
        int numToSend = Math.Min(willing, deficit);
        if (numToSend <= 0)
            return false;

        // Drip-prevention: skip 1-2 worker dispatches from non-overflowing sources. Worker
        // generation on an interior camp constantly ticks NumWorkers one above the source's
        // reservation floor, and without this gate the AI would dispatch that single spare
        // worker on every AI tick, producing a steady visible drip of tiny support waves
        // (#12 6/40 -> #11 sending 1 worker per tick). The overflow exception requires a
        // MEANINGFUL overflow (more than MinButtressWaveSize past MaxWorkers); a 1-2-worker
        // overflow doesn't bypass the gate, because that's exactly the pingpong case
        // (#1 82/80 and #9 80/80 shipping 2-worker overflows back and forth every tick via
        // the in-between Outpost). True over-cap stockpiles (#0 at 295/10) still bypass the
        // gate so the decaying surplus drains into useful work instead of just rotting.
        bool sourceMeaningfullyOverflowing = fromNode.NumWorkers > fromNode.MaxWorkers + AI_ActionHeuristics.MinButtressWaveSize;
        if (numToSend < AI_ActionHeuristics.MinButtressWaveSize && !sourceMeaningfullyOverflowing)
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
        if (last == null) return false;
        // Block immediate reverse shuffle: last was A->B, now trying B->A.
        if (last.Type == AIActionType.SendWorkersToOwnedNode)
            return last.SourceNode == toNode && last.DestNode == fromNode;
        // Multi-source variant: last action lands workers on B from {A, ...}; now trying B->A
        // would undo part of that reinforcement on the next tick.
        if (last.Type == AIActionType.SendMultiSourceWorkersToOwnedNode)
            return last.DestNode == fromNode && last.AttackFromNodes != null && last.AttackFromNodes.ContainsKey(toNode);
        return false;
    }
}
