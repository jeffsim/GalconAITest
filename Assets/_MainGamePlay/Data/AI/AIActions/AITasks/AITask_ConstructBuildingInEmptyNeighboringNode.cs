using UnityEngine;

public class AITask_ConstructBuilding : AITask
{
    // Limit how many candidate buildings we will simulate+recurse on per (from, to) site.
    // Construct is the dominant branching multiplier in the recursive search; pre-scoring all
    // valid buildings by heuristic and recursing on only the top few is the largest single
    // reduction we can apply without lowering MaxAIDepth.
    const int MaxBuildingsPerSite = 2;

    BuildingDefn[] topBuildings = new BuildingDefn[MaxBuildingsPerSite];
    float[] topHeuristics = new float[MaxBuildingsPerSite];

    public AITask_ConstructBuilding(PlayerData player, AI_TownState aiTownState, int maxDepth, int minWorkersInNodeBeforeConsideringSendingAnyOut) : base(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut) { }

    public override float PreviewHeuristic(AI_NodeState fromNode)
    {
        if (fromNode.OwnedBy != player) return 0f;
        if (fromNode.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut) return 0f;

        // Best heuristic across (neighbor, building) pairs reachable from this fromNode.
        // Reuses the same selector the simulation loop uses so Phase 1 and Phase 2 agree.
        float bestSiteScore = 0f;
        foreach (var toNode in fromNode.NeighborNodes)
        {
            if (toNode.OwnedBy != null) continue;
            if (toNode.HasBuilding) continue;

            int topCount = SelectTopBuildingsForSite(toNode);
            if (topCount > 0 && topHeuristics[0] > bestSiteScore)
                bestSiteScore = topHeuristics[0];
        }
        return bestSiteScore;
    }

    override public bool TryTask(AI_NodeState fromNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions, out AIAction bestAction)
    {
        bestAction = null;
        if (fromNode.OwnedBy != player)
            return false;

        if (fromNode.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut)
            return false;

        bestAction = player.AI.GetAIAction();

        foreach (var toNode in fromNode.NeighborNodes)
        {
            if (toNode.OwnedBy != null) continue;
            if (toNode.HasBuilding) continue;
            if (toNode.IsVisited) continue;

            int topCount = SelectTopBuildingsForSite(toNode);
            if (topCount == 0) continue;

            for (int t = 0; t < topCount; t++)
            {
                var buildingDefn = topBuildings[t];
                float heuristicBonus = topHeuristics[t];

                // Branch-and-bound: skip simulate+recurse when the heuristic-only optimistic
                // score for this candidate cannot beat what a peer action has already produced.
                // After top-K sort, scores are descending, so once one is pruned the rest are
                // weakly dominated.
                float runningPeerBest = Mathf.Max(bestScoreAmongPeerActions, bestAction.Score);
                if (ShouldPruneByHeuristic(heuristicBonus, AIHeuristicActionType.Build, runningPeerBest))
                    break;

                int d1 = fromNode.NumWorkers, d2 = toNode.NumWorkers;

                aiTownState.SendWorkersToConstructBuildingInEmptyNode(fromNode, toNode, buildingDefn, curDepth, out GoodType res1Id, out int resource1Amount, out GoodType res2Id, out int resource2Amount, .5f, out int numSent);
                var debuggerEntry = aiDebuggerParentEntry?.AddEntry_ConstructBuildingInEmptyNode(fromNode, toNode, numSent, buildingDefn, 0, player.AI.debugOutput_ActionsTried++, curDepth);

                var actionScore = GetActionScore(curDepth, debuggerEntry);
                actionScore = AI_ActionHeuristics.ApplyHeuristicAndPersonality(actionScore, heuristicBonus, player, AIHeuristicActionType.Build);
                if (actionScore > bestAction.Score)
                    bestAction.SetTo_ConstructBuildingInEmptyNode(fromNode, toNode, numSent, buildingDefn, actionScore, debuggerEntry);

                aiTownState.Undo_SendWorkersToConstructBuildingInEmptyNode(fromNode, toNode, res1Id, resource1Amount, res2Id, resource2Amount, numSent);
                Debug.Assert(d1 == fromNode.NumWorkers && d2 == toNode.NumWorkers);
            }
        }
        return bestAction.Type != AIActionType.DoNothing;
    }

    // Pre-score every valid building for this site and keep only the top MaxBuildingsPerSite by heuristic.
    // Returns the number of entries filled in topBuildings/topHeuristics (descending by score).
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

            // Insert into top-K (descending). The arrays are tiny (K=2) so a linear shift is cheaper than a heap.
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
}
