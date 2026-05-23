using System.Diagnostics;

public class AITask_UpgradeBuilding : AITask
{
    public AITask_UpgradeBuilding(PlayerData player, AI_TownState aiTownState, int maxDepth, int minWorkersInNodeBeforeConsideringSendingAnyOut) : base(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut) { }

    public override float PreviewHeuristic(AI_NodeState fromNode)
    {
        if (fromNode.OwnedBy != player) return 0f;
        var buildingInNode = fromNode.BuildingDefn;
        if (buildingInNode == null || !buildingInNode.CanBeUpgraded) return 0f;
        if (fromNode.NumWorkers < fromNode.MaxWorkers) return 0f;
        float h = AI_ActionHeuristics.GetUpgradeHeuristic(fromNode);
        if (h <= 0f) return 0f;

        // Apply personality so Phase 1 candidate ranking matches actual scoring; Upgrade is
        // economic-expansion aligned, so a low-EconomicExpansionWeight AI should not crowd top-K with upgrades.
        return h * AI_ActionHeuristics.GetPersonalityMultiplier(player, AIHeuristicActionType.Upgrade);
    }

    override public bool TryTask(AI_NodeState fromNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions, out AIAction bestAction)
    {
        bestAction = null;

        if (fromNode.OwnedBy != player)
            return false;

        var buildingInNode = fromNode.BuildingDefn;
        if (buildingInNode == null || !buildingInNode.CanBeUpgraded)
            return false;

        if (fromNode.NumWorkers < fromNode.MaxWorkers)
            return false;

        float heuristicBonus = AI_ActionHeuristics.GetUpgradeHeuristic(fromNode);
        if (heuristicBonus <= 0f)
            return false;

        if (ShouldPruneByHeuristic(heuristicBonus, AIHeuristicActionType.Upgrade, bestScoreAmongPeerActions))
            return false;

        bestAction = player.AI.GetAIAction();

        int d1 = fromNode.NumWorkers;
        aiTownState.UpgradeBuilding(fromNode, out int origLevel, out int origNumWorkers);
        var debuggerEntry = aiDebuggerParentEntry?.AddEntry_UpgradeBuilding(fromNode, 0, player.AI.debugOutput_ActionsTried++, curDepth);

        var actionScore = GetActionScore(curDepth, debuggerEntry);
        actionScore = AI_ActionHeuristics.ApplyHeuristicAndPersonality(actionScore, heuristicBonus, player, AIHeuristicActionType.Upgrade);
        if (actionScore > bestAction.Score)
            bestAction.SetTo_UpgradeBuilding(fromNode, actionScore, debuggerEntry);

        aiTownState.Undo_UpgradeBuilding(fromNode, origLevel, origNumWorkers);
        Debug.Assert(d1 == fromNode.NumWorkers);
        return true;
    }
}
