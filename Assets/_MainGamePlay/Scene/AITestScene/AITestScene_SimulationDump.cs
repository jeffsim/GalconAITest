using System.Text;
using UnityEngine;

public partial class AITestScene
{
    /// <summary>
    /// Builds a succinct snapshot of the current simulation state, logs it, and copies it to the clipboard.
    /// Wire to a UI button via OnDumpStateClicked or call directly.
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
        if (Town == null)
            return "=== SIMULATION STATE ===\n(no town loaded)";

        var sb = new StringBuilder(4096);
        sb.AppendLine("=== SIMULATION STATE ===");

        string townName = TestTownDefn != null ? TestTownDefn.Id : "?";
        sb.AppendLine($"Town: {townName} | Nodes: {Town.Nodes.Count}");

#if DEBUG
        sb.AppendLine($"MaxAIDepth: {MaxAIDepth} | DebugPlayer: {DebugPlayerToViewDetailsOn?.Name ?? "none"}");
#endif
        sb.AppendLine("AI approach: recursive (4)");
        sb.AppendLine();

        AppendConfigSection(sb);
        sb.AppendLine();
        AppendPlayersSection(sb);
        sb.AppendLine();
        AppendNodesSection(sb);
        sb.AppendLine();
        AppendGraphSection(sb);

        return sb.ToString();
    }

    void AppendConfigSection(StringBuilder sb)
    {
        sb.AppendLine("--- CONFIG ---");
        AppendPlayerAIDefnConfig(sb, 1, Player1AIDefn);
        AppendPlayerAIDefnConfig(sb, 2, Player2AIDefn);
        AppendPlayerAIDefnConfig(sb, 3, Player3AIDefn);
    }

    void AppendPlayerAIDefnConfig(StringBuilder sb, int slot, PlayerAIDefn defn)
    {
        if (defn == null)
        {
            sb.AppendLine($"  Player{slot} AIDefn: (none)");
            return;
        }
        sb.AppendLine($"  Player{slot} AIDefn: {defn.Id} | terr={defn.TerritoryExpansionWeight:F2} econ={defn.EconomicExpansionWeight:F2} def={defn.DefenseWeight:F2} agg={defn.AggressivenessWeight:F2} overkill={defn.AttackOverkillMultiplier:F2}");
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
                sb.Append($" [terr={player.AIDefn.TerritoryExpansionWeight:F2} econ={player.AIDefn.EconomicExpansionWeight:F2} def={player.AIDefn.DefenseWeight:F2} agg={player.AIDefn.AggressivenessWeight:F2} overkill={player.AIDefn.AttackOverkillMultiplier:F2}]");
            sb.AppendLine();
            sb.AppendLine($"    nodes={nodeCount} workers={totalWorkers} hasExcessWorkers={PlayerHasExcessWorkers(player)}");

            if (inventory.Count > 0)
            {
                sb.Append("    inventory:");
                foreach (var inv in inventory)
                    sb.Append($" {inv.Key}={inv.Value}");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("    inventory: (empty)");
            }

            var action = player.AI?.BestNextActionToTake;
            sb.AppendLine($"    planned: {FormatAIAction(action)}");
#if DEBUG
            if (player.AI != null)
                sb.AppendLine($"    actionsTried: {player.AI.debugOutput_ActionsTried}");
#endif
            AppendPlannedActionPath(sb, player);
            AppendGoals(sb, player);
            AppendResourceDemand(sb, player);
            sb.AppendLine();
        }
    }

    void AppendGoals(StringBuilder sb, PlayerData player)
    {
        var goals = player.AI?.GetActiveGoalsForDump();
        if (goals == null || goals.Count == 0)
        {
            sb.AppendLine("    goals: (none)");
            return;
        }

        sb.AppendLine($"    goals ({goals.Count}):");
        foreach (var goal in goals)
        {
            string target;
            switch (goal.Type)
            {
                case AIGoalType.CaptureNode:
                    target = goal.TargetNode != null ? $"#{goal.TargetNode.NodeId}" : "?";
                    break;
                case AIGoalType.DefendFrontier:
                    target = goal.TargetNode != null ? $"#{goal.TargetNode.NodeId}" : "?";
                    break;
                case AIGoalType.EconomicTier:
                    target = goal.TargetBuilding != null ? goal.TargetBuilding.BuildingType.ToString() : "?";
                    break;
                case AIGoalType.MaintainStockpile:
                    target = goal.TargetGoodType.ToString();
                    break;
                case AIGoalType.StrategicUpgrade:
                    target = goal.TargetNode != null ? $"#{goal.TargetNode.NodeId}" : "?";
                    break;
                default:
                    target = "?";
                    break;
            }
            float urgency = goal.Value / System.Math.Max(1, goal.HorizonTurns);
            sb.AppendLine($"      {goal.Type} {target} | val={goal.Value:F2} horiz={goal.HorizonTurns} urg={urgency:F2} | {goal.DebugReason}");
        }
    }

    void AppendResourceDemand(StringBuilder sb, PlayerData player)
    {
        var demand = player.AI?.GetResourceDemandForDump();
        if (demand == null || demand.Count == 0)
        {
            sb.AppendLine("    demand: (none)");
            return;
        }
        sb.Append("    demand:");
        foreach (var kvp in demand)
            sb.Append($" {kvp.Key}={kvp.Value}");
        sb.AppendLine();
    }

    void AppendPlannedActionPath(StringBuilder sb, PlayerData player)
    {
#if DEBUG
        var entry = player.AI?.BestNextActionToTake?.AIDebuggerEntry;
        if (entry == null) return;

        int step = 1;
        entry = entry.BestNextAction;
        while (entry != null)
        {
            sb.AppendLine($"    path[{step}]: {FormatDebuggerEntry(entry)}");
            entry = entry.BestNextAction;
            step++;
        }
#endif
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
            {
                sb.Append($" | edge={(onEdge ? "Y" : "N")} enemyN={enemyWorkersNearby}");
                float buttressH = ComputeButtressHeuristicPreview(node.NumWorkers, GetMaxWorkers(node), enemyWorkersNearby, onEdge);
                if (buttressH > 0f)
                    sb.Append($" buttressH={buttressH:F2}");
            }

            string inv = FormatInventory(node);
            if (inv.Length > 0)
                sb.Append($" | {inv}");

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
        {
            foreach (var conn in node.NodeConnections)
            {
                int a = conn.Start.NodeId;
                int b = conn.End.NodeId;
                var key = a < b ? (a, b) : (b, a);
                if (!seen.Add(key)) continue;
                sb.AppendLine($"  #{key.Item1} -- #{key.Item2}{(conn.IsBidirectional ? "" : " (dir)")}");
            }
        }
    }

    static string FormatAIAction(AIAction action)
    {
        if (action == null) return "null";
        switch (action.Type)
        {
            case AIActionType.DoNothing:
                return "DoNothing";
            case AIActionType.SendWorkersToOwnedNode:
                return $"Support {action.Count} #{action.SourceNode?.NodeId} -> #{action.DestNode?.NodeId} (score {action.Score:F2})";
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

#if DEBUG
    static string FormatDebuggerEntry(AIDebuggerEntryData entry)
    {
        switch (entry.ActionType)
        {
            case AIActionType.SendWorkersToOwnedNode:
                return $"Support {entry.NumSent} #{entry.FromNode?.NodeId} -> #{entry.ToNode?.NodeId} score={entry.FinalActionScore:F2}";
            case AIActionType.ConstructBuildingInEmptyNode:
                return $"Build {entry.BuildingDefn?.Id} #{entry.FromNode?.NodeId} -> #{entry.ToNode?.NodeId} score={entry.FinalActionScore:F2}";
            case AIActionType.CaptureNeutralResourceNode:
                return $"Capture resource #{entry.ToNode?.NodeId} from #{entry.FromNode?.NodeId} score={entry.FinalActionScore:F2}";
            case AIActionType.UpgradeBuilding:
                return $"Upgrade #{entry.FromNode?.NodeId} score={entry.FinalActionScore:F2}";
            case AIActionType.AttackToNode:
                return $"Attack #{entry.ToNode?.NodeId} score={entry.FinalActionScore:F2}";
            default:
                return $"{entry.ActionType} score={entry.FinalActionScore:F2}";
        }
    }
#endif

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

    bool PlayerHasExcessWorkers(PlayerData player)
    {
        foreach (var node in Town.Nodes)
        {
            if (node.OwnedBy != player) continue;
            int max = GetMaxWorkers(node);
            if (node.NumWorkers > max || node.NumWorkers > 15)
                return true;
        }
        return false;
    }

    static float ComputeButtressHeuristicPreview(int numWorkers, int maxWorkers, int numEnemiesInNeighborNodes, bool isOnTerritoryEdge)
    {
        // Mirrors AI_ActionHeuristics.GetButtressHeuristic. Only credits the enemy-pressure
        // term when we are actually outnumbered; squaring a signed delta would over-credit
        // over-defended nodes and the dump would disagree with the real heuristic.
        float rawValue = 0f;
        int outnumberedBy = numEnemiesInNeighborNodes - numWorkers;
        if (outnumberedBy > 0)
            rawValue += outnumberedBy * outnumberedBy;
        if (isOnTerritoryEdge) rawValue += 10f;
        if (maxWorkers > 0 && numWorkers < maxWorkers / 2
            && (isOnTerritoryEdge || numEnemiesInNeighborNodes > 0))
        {
            float workersDeficit = maxWorkers - numWorkers;
            rawValue += workersDeficit * workersDeficit * 10f;
        }
        if (rawValue < 20f) return 0f;
        float clamped = Mathf.Clamp(rawValue, 20f, 40f);
        return (clamped - 20f) / 20f * 3f;
    }
}
