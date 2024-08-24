
using System;

public class AITask_UpgradeBuilding : AITask
{
    public AITask_UpgradeBuilding(PlayerData player, AI_TownState aiTownState, int maxDepth, int minWorkersInNodeBeforeConsideringSendingAnyOut) : base(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut) { }

    override public AIAction TryTask(AI_NodeState fromNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions)
    {
        var bestAction = new AIAction() { Type = AIActionType.DoNothing };

        // ==== Verify we can perform the action
        var buildingInNode = fromNode.BuildingDefn;
        if (buildingInNode == null || !buildingInNode.CanBeUpgraded)
            return bestAction;

        if (fromNode.NumWorkers < fromNode.MaxWorkers)
            return bestAction;

        // ==== Perform the action and update the aiTownState to reflect the action
        aiTownState.UpgradeBuilding(fromNode, out int origLevel, out int origNumWorkers);
        var debuggerEntry = aiDebuggerParentEntry.AddEntry_UpgradeBuilding(fromNode, 0, player.AI.debugOutput_ActionsTried++, curDepth);

        // ==== Determine the score of the action we just performed (recurse down); if this is the best so far amongst our peers (in our parent node) then track it as the best action
        var actionScore = GetActionScore(curDepth, debuggerEntry);
        if (actionScore > bestAction.Score)
            bestAction.SetTo_UpgradeBuilding(fromNode, actionScore, debuggerEntry);

        // ==== Undo the action to reset the townstate to its original state
        aiTownState.Undo_UpgradeBuilding(fromNode, origLevel, origNumWorkers);
        return bestAction;
    }
}
