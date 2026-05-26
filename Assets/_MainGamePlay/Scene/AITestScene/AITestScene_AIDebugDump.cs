using System.Text;
using UnityEngine;

public partial class AITestScene
{
    /// <summary>
    /// Per-player AI decision dump: top-N considered candidates with one-line "why" each,
    /// the chosen action, and a snapshot of the strategic analysis (frontier pressure,
    /// deficits, demand) that drove those scores. Copies to clipboard.
    /// </summary>
    public void DumpAIDebugState()
    {
        string report = BuildAIDebugReport();
        Debug.Log(report);
        GUIUtility.systemCopyBuffer = report;
        Debug.Log("AI debug state copied to clipboard.");
    }

    public void OnDumpAIDebugClicked() => DumpAIDebugState();

    string BuildAIDebugReport()
    {
        if (Town == null) return "=== AI DEBUG ===\n(no town loaded)";

        // Fresh search so planned action and decision record reflect the current board.
        Town.WorldRevision++;
        foreach (var p in Town.Players)
            p?.AI?.InvalidateDecisionCache();
        Town.Update();

        var sb = new StringBuilder(8192);
        sb.AppendLine("=== AI DEBUG ===");
        sb.AppendLine($"WorldRevision={Town.WorldRevision} WorldTime={Town.WorldTime:F2} Realtime={Realtime}");
        sb.AppendLine();

        sb.AppendLine("--- PER PLAYER ---");

        foreach (var player in Town.Players)
        {
            if (player?.AI == null) continue;
            sb.AppendLine($"P{player.Id} {player.Name}:");
            sb.AppendLine($"  planned: {FormatAIAction(player.AI.BestNextActionToTake)}");

            AppendAnalysisSnapshot(sb, player);
            player.AI.DecisionRecord.AppendDump(sb);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    static void AppendAnalysisSnapshot(StringBuilder sb, PlayerData player)
    {
        var analysis = player.AI.GetAnalysis();
        var view = player.AI.GetWorldView();
        if (analysis == null || view == null || view.Nodes == null) return;

        sb.AppendLine($"  owned={analysis.TotalOwnedNodes} totalWorkers={analysis.TotalOwnedWorkers}");
        sb.Append("  demand:");
        if (analysis.ResourceShortage.Count == 0) sb.Append(" (none)");
        else foreach (var kv in analysis.ResourceShortage) sb.Append($" {kv.Key}={kv.Value}");
        sb.AppendLine();

        sb.Append("  ownedBuildingTypes:");
        if (analysis.OwnedBuildingTypes.Count == 0) sb.Append(" (none)");
        else foreach (var t in analysis.OwnedBuildingTypes) sb.Append(' ').Append(t);
        sb.AppendLine();

        // Per-frontier-node pressure / deficit snapshot.
        sb.AppendLine("  frontier nodes:");
        bool anyFrontier = false;
        for (int i = 0; i < view.NumNodes; i++)
        {
            if (!analysis.IsFrontier[i]) continue;
            anyFrontier = true;
            var n = view.Nodes[i];
            sb.AppendLine($"    #{n.NodeId} workers={n.NumWorkers}/{n.MaxWorkers} pressure={analysis.FrontierPressure[i]} deficit={analysis.DefensiveDeficit[i]} safeSend={analysis.SafeToSendFrom[i]} incomingFriendly={n.IncomingFriendlyWorkers} incomingHostile={n.IncomingHostileWorkers} heat={n.AttackHeat:F2}");
        }
        if (!anyFrontier) sb.AppendLine("    (none -- no enemy or contested-neutral neighbors)");
    }
}
