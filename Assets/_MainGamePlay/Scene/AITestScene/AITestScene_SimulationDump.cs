using System.Text;
using UnityEngine;

public partial class AITestScene
{
    /// <summary>
    /// Builds a succinct snapshot of the current simulation state, logs it, and copies it to the clipboard.
    /// </summary>
    public void DumpSimulationState()
    {
        string report = BuildSimulationStateReport();
        Debug.Log(report);
        GUIUtility.systemCopyBuffer = report;
        Debug.Log("Simulation state copied to clipboard.");
    }

    public void OnDumpStateClicked() => DumpSimulationState();

    string BuildSimulationStateReport()
    {
        if (Town == null) return "=== SIMULATION STATE ===\n(no town loaded)";

        var sb = new StringBuilder(4096);
        sb.AppendLine("=== SIMULATION STATE ===");

        string townName = TestTownDefn != null ? TestTownDefn.Id : "?";
        sb.AppendLine($"Town: {townName} | Nodes: {Town.Nodes.Count}");
        sb.AppendLine("AI: utility-based single-pass evaluator");
        sb.AppendLine();

        AppendConfigSection(sb);
        sb.AppendLine();
        AppendPlayersSection(sb);
        sb.AppendLine();
        AppendNodesSection(sb);
        sb.AppendLine();
        AppendGraphSection(sb);
        sb.AppendLine();
        AppendChokepointsSection(sb);

        return sb.ToString();
    }

    void AppendConfigSection(StringBuilder sb)
    {
        sb.AppendLine("--- CONFIG ---");
        AppendPlayerAIDefnConfig(sb, 1, Player1AIDefn);
        AppendPlayerAIDefnConfig(sb, 2, Player2AIDefn);
        AppendPlayerAIDefnConfig(sb, 3, Player3AIDefn);
    }

    static void AppendPlayerAIDefnConfig(StringBuilder sb, int slot, PlayerAIDefn defn)
    {
        if (defn == null)
        {
            sb.AppendLine($"  Player{slot} AIDefn: (none)");
            return;
        }
        sb.AppendLine($"  Player{slot} AIDefn: {defn.Id} | agg={defn.Aggression:F2} exp={defn.Expansion:F2} cau={defn.Caution:F2} tempo={defn.Tempo:F2}");
    }

    void AppendPlayersSection(StringBuilder sb)
    {
        sb.AppendLine("--- PLAYERS ---");
        foreach (var player in Town.Players)
        {
            if (player == null) continue;

            int nodeCount = 0;
            int totalWorkers = 0;
            var inventory = new System.Collections.Generic.Dictionary<GoodType, int>();

            foreach (var node in Town.Nodes)
            {
                if (node.OwnedBy != player) continue;
                nodeCount++;
                totalWorkers += node.NumWorkers;
                foreach (var inv in node.Inventory)
                {
                    if (inv.Value == 0) continue;
                    inventory.TryGetValue(inv.Key, out int cur);
                    inventory[inv.Key] = cur + inv.Value;
                }
            }

            sb.Append($"  P{player.Id} {player.Name}");
            if (player.AIDefn != null)
                sb.Append($" [agg={player.AIDefn.Aggression:F2} exp={player.AIDefn.Expansion:F2} cau={player.AIDefn.Caution:F2} tempo={player.AIDefn.Tempo:F2}]");
            sb.AppendLine();
            sb.AppendLine($"    nodes={nodeCount} workers={totalWorkers}");

            if (inventory.Count > 0)
            {
                sb.Append("    inventory:");
                foreach (var inv in inventory)
                    sb.Append($" {inv.Key}={inv.Value}");
                sb.AppendLine();
            }
            else sb.AppendLine("    inventory: (empty)");

            var action = player.AI?.BestNextActionToTake;
            sb.AppendLine($"    planned: {FormatAIAction(action)}");
            if (player.AI != null)
                player.AI.DecisionRecord.AppendDump(sb);
            AppendRecentActions(sb, player);
            sb.AppendLine();
        }
    }

    static void AppendRecentActions(StringBuilder sb, PlayerData player)
    {
        var history = player.AI?.RecentExecutedActions;
        if (history == null || history.Count == 0)
        {
            sb.AppendLine("    recentActions: (none)");
            return;
        }
        sb.AppendLine($"    recentActions (last {history.Count}, oldest first):");
        for (int i = 0; i < history.Count; i++)
            sb.AppendLine($"      {history[i]}");
    }

    void AppendNodesSection(StringBuilder sb)
    {
        sb.AppendLine("--- NODES ---");
        var sorted = new System.Collections.Generic.List<NodeData>(Town.Nodes);
        sorted.Sort((a, b) => a.NodeId.CompareTo(b.NodeId));

        foreach (var node in sorted)
        {
            string owner = node.OwnedBy == null ? "neutral" : $"P{node.OwnedBy.Id}";
            GetTerritoryInfo(node, out int enemyWorkersNearby, out bool onEdge);

            sb.Append($"  #{node.NodeId} {owner}");
            if (node.Building != null)
                sb.Append($" {node.Building.Defn.BuildingType} L{node.Building.Level}");
            else
                sb.Append(" (no building)");

            sb.Append($" | workers {node.NumWorkers}/{GetMaxWorkers(node)}");
            if (node.OwnedBy != null)
                sb.Append($" | edge={(onEdge ? "Y" : "N")} enemyN={enemyWorkersNearby}");
            if (node.ChokepointScore > 0.05f)
                sb.Append($" choke={node.ChokepointScore:F2}");

            string inv = FormatInventory(node);
            if (inv.Length > 0) sb.Append($" | {inv}");

            sb.Append(" | neighbors:");
            AppendNeighborIds(sb, node);
            sb.AppendLine();
        }
    }

    void AppendGraphSection(StringBuilder sb)
    {
        sb.AppendLine("--- GRAPH ---");
        var seen = new System.Collections.Generic.HashSet<(int, int)>();
        foreach (var node in Town.Nodes)
            foreach (var conn in node.NodeConnections)
            {
                int a = conn.Start.NodeId;
                int b = conn.End.NodeId;
                var key = a < b ? (a, b) : (b, a);
                if (!seen.Add(key)) continue;
                sb.AppendLine($"  #{key.Item1} -- #{key.Item2}{(conn.IsBidirectional ? "" : " (dir)")}");
            }
    }

    void AppendChokepointsSection(StringBuilder sb)
    {
        sb.AppendLine("--- CHOKEPOINTS (inter-camp betweenness, 1.0 = peak) ---");
        var ranked = new System.Collections.Generic.List<NodeData>();
        foreach (var node in Town.Nodes)
            if (node.ChokepointScore > 0.05f) ranked.Add(node);
        ranked.Sort((a, b) => b.ChokepointScore.CompareTo(a.ChokepointScore));
        if (ranked.Count == 0)
        {
            sb.AppendLine("  (none -- map has fewer than 2 starting camps)");
            return;
        }
        foreach (var node in ranked)
            sb.AppendLine($"  #{node.NodeId} score={node.ChokepointScore:F2}");
    }

    public static string FormatAIAction(AIAction action)
    {
        if (action == null) return "null";
        switch (action.Type)
        {
            case AIActionType.DoNothing:
                return "DoNothing";
            case AIActionType.SendWorkersToOwnedNode:
                return $"Support {action.Count} #{action.SourceNode?.NodeId} -> #{action.DestNode?.NodeId} (score {action.Score:F2})";
            case AIActionType.SendMultiSourceWorkersToOwnedNode:
                return $"Multi-source support #{action.DestNode?.NodeId} {FormatAttackFrom(action)} (score {action.Score:F2})";
            case AIActionType.ConstructBuildingInEmptyNode:
                return $"Build {action.BuildingToConstruct?.Id} send {action.Count} #{action.SourceNode?.NodeId} -> #{action.DestNode?.NodeId} (score {action.Score:F2})";
            case AIActionType.CaptureNeutralResourceNode:
                return $"Capture resource #{action.DestNode?.NodeId} send {action.Count} from #{action.SourceNode?.NodeId} (score {action.Score:F2})";
            case AIActionType.CaptureNeutralNode:
                return $"Build {action.BuildingToConstruct?.Id} on #{action.DestNode?.NodeId} {FormatAttackFrom(action)} (score {action.Score:F2})";
            case AIActionType.UpgradeBuilding:
                return $"Upgrade #{action.SourceNode?.NodeId} (score {action.Score:F2})";
            case AIActionType.AttackToNode:
                return $"Attack #{action.DestNode?.NodeId} {FormatAttackFrom(action)} (score {action.Score:F2})";
            default:
                return $"{action.Type} (score {action.Score:F2})";
        }
    }

    static string FormatAttackFrom(AIAction action)
    {
        if (action.AttackFromNodes == null || action.AttackFromNodes.Count == 0)
            return "from ?";
        var sb = new StringBuilder();
        sb.Append("from ");
        bool first = true;
        foreach (var kvp in action.AttackFromNodes)
        {
            if (!first) sb.Append(", ");
            sb.Append($"#{kvp.Key.NodeId}({kvp.Value})");
            first = false;
        }
        return sb.ToString();
    }

    static string FormatInventory(NodeData node)
    {
        var sb = new StringBuilder();
        foreach (var inv in node.Inventory)
        {
            if (inv.Value == 0) continue;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append($"{inv.Key}={inv.Value}");
        }
        return sb.ToString();
    }

    static int GetMaxWorkers(NodeData node) => node.Building?.MaxWorkers ?? 0;

    static void AppendNeighborIds(StringBuilder sb, NodeData node)
    {
        bool first = true;
        foreach (var conn in node.NodeConnections)
        {
            var other = conn.Start == node ? conn.End : conn.Start;
            if (!first) sb.Append(',');
            sb.Append('#').Append(other.NodeId);
            first = false;
        }
    }

    static void GetTerritoryInfo(NodeData node, out int enemyWorkersNearby, out bool onEdge)
    {
        enemyWorkersNearby = 0;
        onEdge = false;
        foreach (var conn in node.NodeConnections)
        {
            var other = conn.Start == node ? conn.End : conn.Start;
            if (other.OwnedBy == node.OwnedBy) continue;
            onEdge = true;
            if (other.OwnedBy != null)
                enemyWorkersNearby += other.NumWorkers;
        }
    }
}
