using UnityEngine;

public class AITask_ConstructBuilding : AITask
{
    public AITask_ConstructBuilding(PlayerData player, AI_TownState aiTownState, int maxDepth, int minWorkersInNodeBeforeConsideringSendingAnyOut) : base(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut) { }

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

            for (int i = 0; i < player.AI.numBuildingDefns; i++)
            {
                var buildingDefn = player.AI.buildableBuildingDefns[i];

                if (!AI_ActionHeuristics.CanBuildBuilding(aiTownState, buildingDefn, toNode)) continue;

                float heuristicBonus = AI_ActionHeuristics.GetBuildHeuristic(aiTownState, buildingDefn, toNode);
                if (heuristicBonus <= 0f) continue;

                int d1 = fromNode.NumWorkers, d2 = toNode.NumWorkers;

                aiTownState.SendWorkersToConstructBuildingInEmptyNode(fromNode, toNode, buildingDefn, curDepth, out GoodType res1Id, out int resource1Amount, out GoodType res2Id, out int resource2Amount, .5f, out int numSent);
                var debuggerEntry = aiDebuggerParentEntry.AddEntry_ConstructBuildingInEmptyNode(fromNode, toNode, numSent, buildingDefn, 0, player.AI.debugOutput_ActionsTried++, curDepth);

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
}
