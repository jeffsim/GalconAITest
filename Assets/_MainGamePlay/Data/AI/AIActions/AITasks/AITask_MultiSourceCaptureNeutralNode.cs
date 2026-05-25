using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Multi-source construct on empty neutral nodes (e.g. contested gateway #11). Neutral
/// territory is ONLY taken by building on it; this mirrors AttackToNode worker allocation
/// across adjacent owned nodes when a single source cannot muster enough workers.
/// </summary>
public class AITask_MultiSourceCaptureNeutralNode : AITask
{
    const int MAX_NEIGHBORS_TO_CHECK = 10;
    const int MaxBuildingsPerSite = 2;

    AI_NodeState[] nDeepNeighbors = new AI_NodeState[MAX_NEIGHBORS_TO_CHECK];
    Dictionary<AI_NodeState, int> capturePlanScratch = new Dictionary<AI_NodeState, int>(10);
    BuildingDefn[] topBuildings = new BuildingDefn[MaxBuildingsPerSite];
    float[] topHeuristics = new float[MaxBuildingsPerSite];

    Stack<Dictionary<AI_NodeState, int>> captureFromNodesPool = new Stack<Dictionary<AI_NodeState, int>>();
    Stack<Dictionary<AI_NodeState, int>> origSourceWorkersPool = new Stack<Dictionary<AI_NodeState, int>>();

    public AITask_MultiSourceCaptureNeutralNode(PlayerData player, AI_TownState aiTownState, int maxDepth, int minWorkersInNodeBeforeConsideringSendingAnyOut)
        : base(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut) { }

    public override float PreviewHeuristic(AI_NodeState toNode)
    {
        if (!IsEmptyNeutralTarget(toNode)) return 0f;
        if (!IsAdjacentToPlayerTerritory(toNode)) return 0f;
        if (AI_ActionHeuristics.IsCaptureAlreadyCommitted(toNode, player)) return 0f;

        int num = GetFriendlyNeighborsWithEnoughWorkers(toNode, nDeepNeighbors, emergency: true);
        if (!TryPlanCaptureAllocations(nDeepNeighbors, num, toNode, capturePlanScratch, out int totalPlanned))
            return 0f;

        int topCount = SelectTopBuildingsForSite(toNode);
        if (topCount == 0) return 0f;

        float h = AI_ActionHeuristics.GetBuildHeuristic(aiTownState, topBuildings[0], toNode);
        if (h <= 0f) return 0f;

        return h * AI_ActionHeuristics.GetCapturePersonalityMultiplier(player, toNode);
    }

    public override bool TryTask(AI_NodeState toNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions, out AIAction bestAction)
    {
        bestAction = null;
        if (!IsEmptyNeutralTarget(toNode)) return false;
        if (!IsAdjacentToPlayerTerritory(toNode)) return false;
        if (AI_ActionHeuristics.IsCaptureAlreadyCommitted(toNode, player)) return false;

        int num = GetFriendlyNeighborsWithEnoughWorkers(toNode, nDeepNeighbors, emergency: true);
        var captureFromNodes = captureFromNodesPool.Count > 0 ? captureFromNodesPool.Pop() : new Dictionary<AI_NodeState, int>();
        captureFromNodes.Clear();

        if (!TryPlanCaptureAllocations(nDeepNeighbors, num, toNode, captureFromNodes, out _))
        {
            captureFromNodesPool.Push(captureFromNodes);
            return false;
        }

        int topCount = SelectTopBuildingsForSite(toNode);
        if (topCount == 0)
        {
            captureFromNodesPool.Push(captureFromNodes);
            return false;
        }

        bestAction = player.AI.GetAIAction();
        var origSourceWorkers = origSourceWorkersPool.Count > 0 ? origSourceWorkersPool.Pop() : new Dictionary<AI_NodeState, int>();

        for (int t = 0; t < topCount; t++)
        {
            var buildingDefn = topBuildings[t];
            float heuristicBonus = topHeuristics[t];
            if (buildingDefn == null) continue;

            float runningPeerBest = Mathf.Max(bestScoreAmongPeerActions, bestAction.Score);
            if (ShouldPruneByHeuristic_Capture(heuristicBonus, toNode, runningPeerBest))
                break;

            int d2 = toNode.NumWorkers;
            aiTownState.SendMultiSourceWorkersToConstructBuildingInEmptyNode(
                captureFromNodes, toNode, buildingDefn, curDepth, origSourceWorkers,
                out GoodType res1Id, out int resource1Amount, out GoodType res2Id, out int resource2Amount,
                out int origDestWorkers, out PlayerData origOwner);

            var debuggerEntry = aiDebuggerParentEntry?.AddEntry_CaptureNeutralNode(captureFromNodes, toNode, buildingDefn, 0, player.AI.debugOutput_ActionsTried++, curDepth);

            var actionScore = GetActionScore(curDepth, debuggerEntry);
            actionScore = AI_ActionHeuristics.ApplyHeuristicAndPersonality_Capture(actionScore, heuristicBonus, player, toNode);
            if (actionScore > bestAction.Score)
                bestAction.SetTo_CaptureNeutralNode(captureFromNodes, toNode, buildingDefn, actionScore, debuggerEntry);

            aiTownState.Undo_SendMultiSourceWorkersToConstructBuildingInEmptyNode(
                origSourceWorkers, toNode, res1Id, resource1Amount, res2Id, resource2Amount, origDestWorkers, origOwner);
            Debug.Assert(d2 == toNode.NumWorkers);
        }

        origSourceWorkersPool.Push(origSourceWorkers);
        captureFromNodesPool.Push(captureFromNodes);
        return bestAction.Type != AIActionType.DoNothing;
    }

    static bool IsEmptyNeutralTarget(AI_NodeState toNode) =>
        toNode.OwnedBy == null && !toNode.HasBuilding && !toNode.CanBeGatheredFrom;

    bool IsAdjacentToPlayerTerritory(AI_NodeState toNode)
    {
        foreach (var nb in toNode.NeighborNodes)
            if (nb.OwnedBy == player) return true;
        return false;
    }

    int SelectTopBuildingsForSite(AI_NodeState toNode)
    {
        int filled = 0;
        for (int i = 0; i < MaxBuildingsPerSite; i++)
        {
            topBuildings[i] = null;
            topHeuristics[i] = 0f;
        }

        for (int i = 0; i < player.AI.numBuildingDefns; i++)
        {
            var buildingDefn = player.AI.buildableBuildingDefns[i];
            if (!AI_ActionHeuristics.CanBuildBuilding(aiTownState, buildingDefn, toNode)) continue;

            float heuristicBonus = AI_ActionHeuristics.GetBuildHeuristic(aiTownState, buildingDefn, toNode);
            if (heuristicBonus <= 0f) continue;

            int insertAt = filled;
            for (int k = 0; k < filled; k++)
            {
                if (heuristicBonus > topHeuristics[k])
                {
                    insertAt = k;
                    break;
                }
            }
            if (insertAt >= MaxBuildingsPerSite) continue;

            int shiftEnd = Mathf.Min(filled, MaxBuildingsPerSite - 1);
            for (int k = shiftEnd; k > insertAt; k--)
            {
                topBuildings[k] = topBuildings[k - 1];
                topHeuristics[k] = topHeuristics[k - 1];
            }
            topBuildings[insertAt] = buildingDefn;
            topHeuristics[insertAt] = heuristicBonus;
            if (filled < MaxBuildingsPerSite) filled++;
        }
        return filled;
    }

    Queue<AI_NodeState> queue = new Queue<AI_NodeState>(10);
    HashSet<AI_NodeState> visited = new HashSet<AI_NodeState>(10);
    const int MAX_DEPTH = 4;

    int GetFriendlyNeighborsWithEnoughWorkers(AI_NodeState toNode, AI_NodeState[] buffer, bool emergency)
    {
        int index = 0;
        int currentDepth = 0;
        visited.Clear();
        visited.Add(toNode);
        queue.Clear();
        queue.Enqueue(toNode);

        while (queue.Count > 0 && currentDepth < MAX_DEPTH && index < MAX_NEIGHBORS_TO_CHECK)
        {
            int nodesAtLevel = queue.Count;
            for (int i = 0; i < nodesAtLevel; i++)
            {
                var currentNode = queue.Dequeue();
                foreach (var neighbor in currentNode.NeighborNodes)
                {
                    if (neighbor.OwnedBy == player && !visited.Contains(neighbor))
                    {
                        if (index < MAX_NEIGHBORS_TO_CHECK
                            && AI_ActionHeuristics.GetWorkersWillingToSendForDefense(neighbor, minWorkersInNodeBeforeConsideringSendingAnyOut, emergency) > 0)
                            buffer[index++] = neighbor;
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }
            currentDepth++;
        }
        return index;
    }

    bool TryPlanCaptureAllocations(AI_NodeState[] nodes, int numNodes, AI_NodeState target, Dictionary<AI_NodeState, int> allocations, out int totalPlanned)
    {
        allocations.Clear();
        totalPlanned = 0;

        float overkill = player.AIDefn != null ? player.AIDefn.AttackOverkillMultiplier : 1f;
        int targetAttackers = AI_ActionHeuristics.GetGatewayCaptureTargetForce(target, player, overkill, numNodes);

        int totalWilling = 0;
        for (int i = 0; i < numNodes; i++)
            totalWilling += AI_ActionHeuristics.GetWorkersWillingToSendForDefense(nodes[i], minWorkersInNodeBeforeConsideringSendingAnyOut, emergency: true);

        if (totalWilling < targetAttackers)
            return false;

        int remaining = targetAttackers;
        for (int i = 0; i < numNodes && remaining > 0; i++)
        {
            var node = nodes[i];
            int willing = AI_ActionHeuristics.GetWorkersWillingToSendForDefense(node, minWorkersInNodeBeforeConsideringSendingAnyOut, emergency: true);
            if (willing <= 0) continue;
            int send = Math.Min(willing, remaining);
            allocations[node] = send;
            totalPlanned += send;
            remaining -= send;
        }
        return remaining <= 0;
    }
}
