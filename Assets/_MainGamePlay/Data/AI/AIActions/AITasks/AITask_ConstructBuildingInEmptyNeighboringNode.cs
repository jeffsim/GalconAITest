using System;
using UnityEngine;

public class AITask_ConstructBuilding : AITask
{
    // Limit how many candidate buildings we will simulate+recurse on per (from, to) site.
    // Construct is the dominant branching multiplier in the recursive search; pre-scoring all
    // valid buildings by heuristic and recursing on only the top few is the largest single
    // reduction we can apply without lowering MaxAIDepth.
    const int MaxBuildingsPerSite = 2;

    // Scratch buffers for SelectTopBuildingsForSite. THESE GET STOMPED by recursion: when
    // TryTask iterates these, GetActionScore recurses, and PreviewHeuristic / a deeper TryTask
    // call SelectTopBuildingsForSite again, overwriting these arrays. Always snapshot to a
    // per-depth buffer (snapshotBuildingsPerDepth) before the simulate+recurse loop so the
    // outer iteration reads stable values.
    BuildingDefn[] topBuildings = new BuildingDefn[MaxBuildingsPerSite];
    float[] topHeuristics = new float[MaxBuildingsPerSite];

    // Per-depth snapshot of top-K (stable across recursion). One slot per recursion depth
    // because the search is depth-first and each ply needs its own captured top-K.
    BuildingDefn[][] snapshotBuildingsPerDepth;
    float[][] snapshotHeuristicsPerDepth;

    public AITask_ConstructBuilding(PlayerData player, AI_TownState aiTownState, int maxDepth, int minWorkersInNodeBeforeConsideringSendingAnyOut) : base(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut) { }

    // Hybrid search keys Construct off the owned source node (fromNode). One Phase-1 candidate
    // per owned node keeps top-K diverse; TryTask then iterates this source's empty neutral
    // neighbors × top buildings in one call so the inner branch-and-bound can prune effectively
    // across all (toNode, building) pairs while peer best is accumulating.
    public override float PreviewHeuristic(AI_NodeState fromNode)
    {
        if (fromNode.OwnedBy != player) return 0f;
        if (fromNode.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut) return 0f;

        // Best heuristic across (neighbor, building) pairs reachable from this fromNode.
        // Reuses the same selector the simulation loop uses so Phase 1 and Phase 2 agree.
        // Multiply each candidate's heuristic by ITS OWN danger-blended capture personality
        // before comparing -- a contested toNode (e.g. peak chokepoint surrounded by
        // contested-neutral workers) gets agg-weighted, a safe interior neutral gets terr-
        // weighted. Without the per-toNode multiplier, a single high-raw-heuristic candidate
        // at a dangerous site would win the per-fromNode preview and crowd out the safer
        // neighbor whose post-personality score would actually be higher.
        float bestSiteScore = 0f;
        foreach (var toNode in fromNode.NeighborNodes)
        {
            if (toNode.OwnedBy != null) continue;
            if (toNode.HasBuilding) continue;

            int topCount = SelectTopBuildingsForSite(toNode);
            if (topCount <= 0) continue;

            float capturePersonality = AI_ActionHeuristics.GetCapturePersonalityMultiplier(player, toNode);
            float scored = topHeuristics[0] * capturePersonality;
            if (scored > bestSiteScore)
                bestSiteScore = scored;
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
            if (AI_ActionHeuristics.IsCaptureAlreadyCommitted(toNode, player)) continue;

            int topCount = SelectTopBuildingsForSite(toNode);
            if (topCount == 0) continue;

            // Snapshot to per-depth buffer; recursion via GetActionScore below re-enters this
            // task instance and stomps the topBuildings/topHeuristics arrays.
            EnsureSnapshotCapacity(curDepth);
            var snapBuildings = snapshotBuildingsPerDepth[curDepth];
            var snapHeuristics = snapshotHeuristicsPerDepth[curDepth];
            for (int s = 0; s < topCount; s++)
            {
                snapBuildings[s] = topBuildings[s];
                snapHeuristics[s] = topHeuristics[s];
            }

            for (int t = 0; t < topCount; t++)
            {
                var buildingDefn = snapBuildings[t];
                float heuristicBonus = snapHeuristics[t];
                if (buildingDefn == null) continue;

                // Branch-and-bound: skip simulate+recurse when the heuristic-only optimistic
                // score for this candidate cannot beat what a peer action has already produced.
                // After top-K sort, scores are descending, so once one is pruned the rest are
                // weakly dominated. Use the capture-aware bound so the optimistic estimate
                // matches the post-personality scoring (terr/agg blend for this specific
                // toNode) -- a plain Capture-bound would over-estimate on contested neutrals.
                float runningPeerBest = Mathf.Max(bestScoreAmongPeerActions, bestAction.Score);
                if (ShouldPruneByHeuristic_Capture(heuristicBonus, toNode, runningPeerBest))
                    break;

                int d1 = fromNode.NumWorkers, d2 = toNode.NumWorkers;

                float overkill = player.AIDefn != null ? player.AIDefn.AttackOverkillMultiplier : 1f;
                if (!AI_ActionHeuristics.TryGetCaptureWorkersToSend(fromNode, toNode, player, minWorkersInNodeBeforeConsideringSendingAnyOut, overkill, out int numToSend))
                    continue;

                aiTownState.SendWorkersToConstructBuildingInEmptyNode(fromNode, toNode, buildingDefn, curDepth, out GoodType res1Id, out int resource1Amount, out GoodType res2Id, out int resource2Amount, numToSend, out int numSent);
                if (numSent <= 0)
                    continue;
                var debuggerEntry = aiDebuggerParentEntry?.AddEntry_ConstructBuildingInEmptyNode(fromNode, toNode, numSent, buildingDefn, 0, player.AI.debugOutput_ActionsTried++, curDepth);

                var actionScore = GetActionScore(curDepth, debuggerEntry);
                actionScore = AI_ActionHeuristics.ApplyHeuristicAndPersonality_Capture(actionScore, heuristicBonus, player, toNode);
                if (actionScore > bestAction.Score)
                    bestAction.SetTo_ConstructBuildingInEmptyNode(fromNode, toNode, numSent, buildingDefn, actionScore, debuggerEntry);

                aiTownState.Undo_SendWorkersToConstructBuildingInEmptyNode(fromNode, toNode, res1Id, resource1Amount, res2Id, resource2Amount, d1, d2);
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

    void EnsureSnapshotCapacity(int curDepth)
    {
        int requiredLen = curDepth + 1;
        int currentLen = snapshotBuildingsPerDepth?.Length ?? 0;
        if (currentLen >= requiredLen) return;

        int newLen = Math.Max(requiredLen, 8);
        var newB = new BuildingDefn[newLen][];
        var newH = new float[newLen][];
        if (snapshotBuildingsPerDepth != null)
        {
            Array.Copy(snapshotBuildingsPerDepth, newB, currentLen);
            Array.Copy(snapshotHeuristicsPerDepth, newH, currentLen);
        }
        for (int i = 0; i < newLen; i++)
        {
            if (newB[i] == null) newB[i] = new BuildingDefn[MaxBuildingsPerSite];
            if (newH[i] == null) newH[i] = new float[MaxBuildingsPerSite];
        }
        snapshotBuildingsPerDepth = newB;
        snapshotHeuristicsPerDepth = newH;
    }
}
