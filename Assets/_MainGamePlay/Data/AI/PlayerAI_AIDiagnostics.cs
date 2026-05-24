using System;
using System.Text;
using UnityEngine;

public partial class PlayerAI
{
    static readonly string[] TaskNames =
    {
        "CaptureNeutralResource",
        "MultiSourceCaptureNeutral",
        "Buttress",
        "Attack",
        "Construct",
        "Upgrade",
    };

    /// <summary>
    /// Diagnostic dump: why each action type does or does not apply to the current board state.
    /// Does not mutate the live search cache beyond refreshing mirror state from TownData.
    /// </summary>
    public void AppendAIDiagnostics(StringBuilder sb, TownData town)
    {
        int depth = AITestScene.Instance != null ? AITestScene.Instance.MaxAIDepth - 1 : 6;
        aiTownState.UpdateState(town);
        AI_ActionHeuristics.UpdateTerritoryDetails(aiTownState, player);
        AIGoalEnumerator.EnumerateGoals(player, aiTownState, buildableBuildingDefns, numBuildingDefns, aiTownState.ActiveGoals, goalPool);
        AI_ActionHeuristics.UpdateResourceDemand(aiTownState, buildableBuildingDefns, numBuildingDefns, aiTownState.ActiveGoals);

        float baseline = aiTownState.EvaluateScore(0, depth, out _);
        sb.AppendLine($"  evaluateBaseline={baseline:F3} minWorkersToSend={minWorkersInNodeBeforeConsideringSendingAnyOut}");

        var planned = BestNextActionToTake;
        sb.AppendLine($"  plannedNow: {(planned != null ? planned.Type + " score=" + planned.Score.ToString("F3") : "null")}");

        AppendHybridCandidatesPreview(sb, depth);

        sb.AppendLine("  --- owned nodes -> neutral neighbors ---");
        var nodes = aiTownState.Nodes;
        float overkill = player.AIDefn != null ? player.AIDefn.AttackOverkillMultiplier : 1f;

        for (int i = 0; i < nodes.Length; i++)
        {
            var from = nodes[i];
            if (from.OwnedBy != player) continue;

            int willing = AI_ActionHeuristics.GetWorkersWillingToSend(from, minWorkersInNodeBeforeConsideringSendingAnyOut);
            int frontierPressure = AI_ActionHeuristics.GetFrontierPressure(from);
            float upgradePreview = Tasks.Count > 5 ? Tasks[5].PreviewHeuristic(from) : 0f;
            sb.AppendLine($"  from #{from.NodeId} workers={from.NumWorkers}/{from.MaxWorkers} willingSend={willing} contestedNeutralNear={from.NumContestedNeutralWorkersNearby} frontierPressure={frontierPressure} upgradeEligible={(from.BuildingDefn != null && from.BuildingDefn.CanBeUpgraded && from.NumWorkers >= from.MaxWorkers)} previewUpgrade={upgradePreview:F2} canButtressSource={AI_ActionHeuristics.CanButtressFromAnySource(from, player, minWorkersInNodeBeforeConsideringSendingAnyOut)}");

            foreach (var to in from.NeighborNodes)
            {
                if (to.OwnedBy != null) continue;

                sb.Append($"    -> #{to.NodeId}");
                if (to.CanBeGatheredFrom)
                    sb.Append($" resource({to.ResourceGatheredFromThisNode})");
                else if (to.HasBuilding)
                    sb.Append($" hasBuilding({to.BuildingDefn?.BuildingType})");
                else
                    sb.Append(" empty");
                sb.AppendLine();

                if (to.HasBuilding && !to.CanBeGatheredFrom)
                    sb.AppendLine("       Construct: SKIP (HasBuilding, not a gather-from resource node)");
                else if (to.HasBuilding && to.CanBeGatheredFrom)
                    sb.AppendLine("       Construct: SKIP (HasBuilding — use CaptureNeutralResource instead)");
                else
                {
                    int buildCount = CountValidConstructOptions(from, to);
                    sb.AppendLine($"       Construct: {buildCount} valid build option(s)");
                }

                if (to.CanBeGatheredFrom)
                {
                    bool canCapture = AI_ActionHeuristics.TryGetCaptureWorkersToSend(from, to, player, minWorkersInNodeBeforeConsideringSendingAnyOut, overkill, out int send);
                    float captureH = canCapture ? AI_ActionHeuristics.GetCaptureResourceNodeHeuristic(aiTownState, to, send) : 0f;
                    int shortage = AI_ActionHeuristics.GetResourceShortage(aiTownState, to.ResourceGatheredFromThisNode);
                    sb.AppendLine($"       CaptureResource: eligible=Y canSend={canCapture} send={send} previewH={captureH:F2} shortage({to.ResourceGatheredFromThisNode})={shortage}");
                }
                else if (!to.HasBuilding)
                {
                    int emergencyWilling = AI_ActionHeuristics.GetWorkersWillingToSendForDefense(from, minWorkersInNodeBeforeConsideringSendingAnyOut, emergency: true);
                    sb.AppendLine($"       MultiSourceCapture: emergencyWillingFromThisNode={emergencyWilling} touchesEnemy={AI_ActionHeuristics.NeutralNeighborTouchesEnemy(to, player)}");
                }
                else
                {
                    sb.AppendLine("       CaptureResource: SKIP (not CanBeGatheredFrom)");
                }

                int threat = AI_ActionHeuristics.GetNeutralCaptureThreat(to, player);
                int gatewayThreat = AI_ActionHeuristics.GetGatewayCaptureThreat(to, player);
                sb.AppendLine($"       neutralThreat={threat} gatewayThreat={gatewayThreat} targetForce={AI_ActionHeuristics.GetTargetForceWithOverkill(threat, overkill)} gatewayTarget={AI_ActionHeuristics.GetGatewayCaptureTargetForce(to, player, overkill, 3)}");
            }
        }
    }

    void AppendHybridCandidatesPreview(StringBuilder sb, int depth)
    {
        sb.AppendLine("  --- phase-1 hybrid candidates (preview heuristics) ---");
        if (Tasks.Count == 0)
        {
            sb.AppendLine("    (no tasks registered)");
            return;
        }

        var nodes = aiTownState.Nodes;
        int topK = GetHybridBeamWidthForDepth(0);
        var buffer = new HybridCandidate[HybridCandidateBufferSize];
        int count = 0;

        for (int i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i];
            for (int t = 0; t < Tasks.Count; t++)
            {
                float h = Tasks[t].PreviewHeuristic(node);
                if (h <= 0f) continue;
                InsertDiagnosticCandidate(buffer, ref count, topK, h, node, t);
            }
        }

        if (count == 0)
        {
            sb.AppendLine("    (none — every task returned previewH=0)");
            return;
        }

        for (int k = 0; k < count; k++)
        {
            var c = buffer[k];
            string taskName = c.TaskIndex < TaskNames.Length ? TaskNames[c.TaskIndex] : Tasks[c.TaskIndex].GetType().Name;
            sb.AppendLine($"    [{k + 1}] h={c.HeuristicScore:F2} task={taskName} node=#{c.Node.NodeId}");
        }
    }

    static void InsertDiagnosticCandidate(HybridCandidate[] buffer, ref int count, int topK, float score, AI_NodeState node, int taskIndex)
    {
        int insertAt = count;
        for (int k = 0; k < count; k++)
        {
            if (score > buffer[k].HeuristicScore)
            {
                insertAt = k;
                break;
            }
        }
        if (insertAt >= topK) return;

        int shiftEnd = Math.Min(count, topK - 1);
        for (int k = shiftEnd; k > insertAt; k--)
            buffer[k] = buffer[k - 1];

        buffer[insertAt].HeuristicScore = score;
        buffer[insertAt].Node = node;
        buffer[insertAt].TaskIndex = taskIndex;
        if (count < topK) count++;
    }

    int CountValidConstructOptions(AI_NodeState from, AI_NodeState to)
    {
        if (to.HasBuilding) return 0;
        if (from.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut) return 0;

        int count = 0;
        for (int i = 0; i < numBuildingDefns; i++)
        {
            var bd = buildableBuildingDefns[i];
            if (!AI_ActionHeuristics.CanBuildBuilding(aiTownState, bd, to)) continue;
            if (AI_ActionHeuristics.GetBuildHeuristic(aiTownState, bd, to) <= 0f) continue;
            count++;
        }
        return count;
    }
}
