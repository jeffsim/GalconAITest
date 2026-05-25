using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Multi-source friendly reinforcement for an owned frontier under heavy pressure.
/// Mirrors AITask_MultiSourceCaptureNeutralNode's allocation pattern but lands workers on
/// an owned dest instead of constructing a building. The single-source buttress task still
/// handles the easy case where one interior alone can close the deficit; this task fires
/// only when several interiors must combine to close it. In realtime mode this is the
/// difference between "one #1->#0 send per decision tick" and "stack #1+#2+#9 onto #0 in
/// a single tick before the attacker resolves".
/// </summary>
public class AITask_MultiSourceButtress : AITask
{
    const int MAX_NEIGHBORS_TO_CHECK = 10;
    const int MAX_BFS_DEPTH = 4;

    AI_NodeState[] friendlyNeighborBuffer = new AI_NodeState[MAX_NEIGHBORS_TO_CHECK];
    Queue<AI_NodeState> bfsQueue = new Queue<AI_NodeState>(16);
    HashSet<AI_NodeState> bfsVisited = new HashSet<AI_NodeState>(16);
    Dictionary<AI_NodeState, int> planScratch = new Dictionary<AI_NodeState, int>(MAX_NEIGHBORS_TO_CHECK);

    Stack<Dictionary<AI_NodeState, int>> sendFromPool = new Stack<Dictionary<AI_NodeState, int>>();
    Stack<Dictionary<AI_NodeState, int>> origSourceWorkersPool = new Stack<Dictionary<AI_NodeState, int>>();

    public AITask_MultiSourceButtress(PlayerData player, AI_TownState aiTownState, int maxDepth, int minWorkersInNodeBeforeConsideringSendingAnyOut)
        : base(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut) { }

    public override float PreviewHeuristic(AI_NodeState toNode)
    {
        if (toNode.OwnedBy != player) return 0f;

        float h = AI_ActionHeuristics.GetButtressHeuristic(aiTownState, toNode);
        if (h <= 0f) return 0f;

        if (!ShouldPreferMultiSource(toNode, out _))
            return 0f;

        return h * AI_ActionHeuristics.GetPersonalityMultiplier(player, AIHeuristicActionType.Buttress);
    }

    public override bool TryTask(AI_NodeState toNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions, out AIAction bestAction)
    {
        bestAction = null;
        if (toNode.OwnedBy != player) return false;

        float heuristicBonus = AI_ActionHeuristics.GetButtressHeuristic(aiTownState, toNode);
        if (heuristicBonus <= 0f) return false;
        if (ShouldPruneByHeuristic(heuristicBonus, AIHeuristicActionType.Buttress, bestScoreAmongPeerActions))
            return false;

        if (!ShouldPreferMultiSource(toNode, out int deficit))
            return false;

        bool emergency = IsEmergency(toNode);
        int numFound = FindFriendlyNeighbors(toNode, friendlyNeighborBuffer, emergency);
        if (numFound < 2) return false;

        var sendFromNodes = sendFromPool.Count > 0 ? sendFromPool.Pop() : new Dictionary<AI_NodeState, int>();
        sendFromNodes.Clear();
        if (!TryPlanAllocations(friendlyNeighborBuffer, numFound, toNode, deficit, emergency, sendFromNodes, out int totalPlanned)
            || totalPlanned <= 0
            || sendFromNodes.Count < 2)
        {
            sendFromPool.Push(sendFromNodes);
            return false;
        }

        bestAction = player.AI.GetAIAction();
        var origSourceWorkers = origSourceWorkersPool.Count > 0 ? origSourceWorkersPool.Pop() : new Dictionary<AI_NodeState, int>();

        aiTownState.SendMultiSourceWorkersToOwnedNode(sendFromNodes, toNode, origSourceWorkers, out int origDestWorkers, out int totalSent);
        var debuggerEntry = aiDebuggerParentEntry?.AddEntry_SendMultiSourceWorkersToOwnedNode(sendFromNodes, toNode, 0, player.AI.debugOutput_ActionsTried++, curDepth);

        var actionScore = GetActionScore(curDepth, debuggerEntry);
        actionScore = AI_ActionHeuristics.ApplyHeuristicAndPersonality(actionScore, heuristicBonus, player, AIHeuristicActionType.Buttress);
        if (actionScore > bestAction.Score)
            bestAction.SetTo_SendMultiSourceWorkersToOwnedNode(sendFromNodes, toNode, actionScore, debuggerEntry);

        aiTownState.Undo_SendMultiSourceWorkersToOwnedNode(origSourceWorkers, toNode, origDestWorkers);

        origSourceWorkersPool.Push(origSourceWorkers);
        sendFromPool.Push(sendFromNodes);
        return bestAction.Type != AIActionType.DoNothing;
    }

    bool ShouldPreferMultiSource(AI_NodeState toNode, out int deficit)
    {
        deficit = ComputeDeficit(toNode);
        if (deficit <= 0) return false;

        bool emergency = IsEmergency(toNode);

        // Multi-source's unique value is when NO single neighbor can cover the deficit on
        // its own (the single-source task would fire and be less disruptive in that case).
        int numFound = FindFriendlyNeighbors(toNode, friendlyNeighborBuffer, emergency);
        if (numFound < 2) return false;

        int bestSingleWilling = 0;
        int totalWilling = 0;
        for (int i = 0; i < numFound; i++)
        {
            int willing = AI_ActionHeuristics.GetWorkersWillingToSendForDefense(
                friendlyNeighborBuffer[i], minWorkersInNodeBeforeConsideringSendingAnyOut, emergency, toNode.AttackHeat);
            if (willing <= 0) continue;
            totalWilling += willing;
            if (willing > bestSingleWilling) bestSingleWilling = willing;
        }

        if (totalWilling <= 0) return false;
        // Single-source path already covers the easy case.
        if (bestSingleWilling >= deficit) return false;
        return true;
    }

    int ComputeDeficit(AI_NodeState toNode)
    {
        // Destination-side garrison includes in-flight friendly reinforcements -- a previous
        // single-source buttress wave in transit must count toward "already covered" or
        // multi-source will stack ANOTHER wave on top of it.
        int destGarrison = toNode.EffectiveDefenseGarrison;
        int deficit = AI_ActionHeuristics.GetFrontierWorkerDeficit(toNode);

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
        return deficit;
    }

    static bool IsEmergency(AI_NodeState toNode)
    {
        int destGarrison = toNode.EffectiveDefenseGarrison;
        return AI_ActionHeuristics.GetEffectiveFrontierPressure(toNode) > destGarrison
            || toNode.NumContestedNeutralWorkersNearby > destGarrison / 2
            || toNode.AttackHeat >= AI_ActionHeuristics.AttackHeatEmergencyThreshold;
    }

    // BFS through player-owned territory only. Workers crossing hostile/neutral ground get
    // intercepted by ResolveWorkerArrival in realtime, so restricting expansion to friendly
    // nodes guarantees every collected source has a fully owned path back to the dest.
    int FindFriendlyNeighbors(AI_NodeState toNode, AI_NodeState[] buffer, bool emergency)
    {
        bfsVisited.Clear();
        bfsVisited.Add(toNode);
        bfsQueue.Clear();
        bfsQueue.Enqueue(toNode);

        int index = 0;
        int currentDepth = 0;
        while (bfsQueue.Count > 0 && currentDepth < MAX_BFS_DEPTH && index < MAX_NEIGHBORS_TO_CHECK)
        {
            int nodesAtLevel = bfsQueue.Count;
            for (int i = 0; i < nodesAtLevel; i++)
            {
                var current = bfsQueue.Dequeue();
                foreach (var neighbor in current.NeighborNodes)
                {
                    if (bfsVisited.Contains(neighbor)) continue;
                    if (neighbor.OwnedBy != player) continue;
                    bfsVisited.Add(neighbor);
                    bfsQueue.Enqueue(neighbor);

                    if (index >= MAX_NEIGHBORS_TO_CHECK) continue;
                    if (neighbor.IsVisited) continue; // claimed by an in-progress recursion ply

                    // Don't drain a hotter chokepoint to help a cooler one. Mirrors the guard
                    // in GetButtressSourceNode so this task can't suddenly start pulling from
                    // sources the single-source task correctly refuses.
                    if (neighbor.AttackHeat >= AI_ActionHeuristics.AttackHeatChokepointThreshold
                        && neighbor.AttackHeat > toNode.AttackHeat)
                        continue;

                    int willing = AI_ActionHeuristics.GetWorkersWillingToSendForDefense(
                        neighbor, minWorkersInNodeBeforeConsideringSendingAnyOut, emergency, toNode.AttackHeat);
                    if (willing <= 0) continue;

                    buffer[index++] = neighbor;
                }
            }
            currentDepth++;
        }
        return index;
    }

    // Greedy fill: take from each neighbor up to its willing count, in BFS order (so closer
    // sources contribute first), until the deficit is met or sources run out.
    bool TryPlanAllocations(AI_NodeState[] nodes, int numNodes, AI_NodeState toNode, int deficit, bool emergency, Dictionary<AI_NodeState, int> allocations, out int totalPlanned)
    {
        allocations.Clear();
        totalPlanned = 0;

        int remaining = deficit;
        for (int i = 0; i < numNodes && remaining > 0; i++)
        {
            var node = nodes[i];
            int willing = AI_ActionHeuristics.GetWorkersWillingToSendForDefense(
                node, minWorkersInNodeBeforeConsideringSendingAnyOut, emergency, toNode.AttackHeat);
            if (willing <= 0) continue;
            int send = Math.Min(willing, remaining);
            allocations[node] = send;
            totalPlanned += send;
            remaining -= send;
        }

        return totalPlanned > 0;
    }
}
