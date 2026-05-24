using System.Text;
using UnityEngine;

public partial class AITestScene
{
    /// <summary>
    /// Extended AI diagnostic dump: action candidacy, skipped targets, hybrid top-K previews.
    /// Copies to clipboard like DumpSimulationState.
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
        if (Town == null)
            return "=== AI DEBUG ===\n(no town loaded)";

        // Fresh search so planned action and actionsTried reflect current board.
        Town.WorldRevision++;
        foreach (var p in Town.Players)
            p?.AI?.InvalidateDecisionCache();
        Town.Update();

        var sb = new StringBuilder(8192);
        sb.AppendLine("=== AI DEBUG ===");
        sb.AppendLine($"WorldRevision={Town.WorldRevision} WorldTime={Town.WorldTime:F2} Realtime={Realtime}");
#if DEBUG
        sb.AppendLine($"MaxAIDepth={MaxAIDepth} HybridSearch={EnableHybridSearch} TrackDebugger={TrackSearchDebugger}");
        sb.AppendLine($"DebugPlayer={DebugPlayerToViewDetailsOn?.Name ?? "none"}");
#endif
        sb.AppendLine();
        sb.AppendLine("Registered tasks (index -> name):");
        if (Town.Players[0]?.AI != null)
        {
            var tasks = Town.Players[0].AI.Tasks;
            for (int i = 0; i < tasks.Count; i++)
                sb.AppendLine($"  [{i}] {tasks[i].GetType().Name}");
        }
        sb.AppendLine();
        sb.AppendLine("--- PER PLAYER ---");

        foreach (var player in Town.Players)
        {
            if (player?.AI == null) continue;
            sb.AppendLine($"P{player.Id} {player.Name}:");
            sb.AppendLine($"  planned: {FormatAIAction(player.AI.BestNextActionToTake)}");
#if DEBUG
            sb.AppendLine($"  actionsTried: {player.AI.debugOutput_ActionsTried}");
#endif
            player.AI.AppendAIDiagnostics(sb, Town);
            sb.AppendLine();
        }

        sb.AppendLine("--- CAPTURE GOAL vs ACTION GAP ---");
        sb.AppendLine("Neutral Forest/StoneMine nodes have HasBuilding=true.");
        sb.AppendLine("Construct skips HasBuilding; CaptureNeutralResource handles them.");
        sb.AppendLine("If CaptureResource lines show canSend=false, check willingSend/minWorkers/threat.");

        return sb.ToString();
    }

}
