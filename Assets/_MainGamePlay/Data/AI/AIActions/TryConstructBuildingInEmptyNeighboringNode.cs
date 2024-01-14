using UnityEngine;

public partial class PlayerAI
{
    private void TryConstructBuildingInEmptyNeighboringNode(AI_NodeState fromNode, ref AIAction bestAction, int curDepth, int recurseCount, int thisActionNum)
    {
#if DEBUG
        AIDebugger.PushTryActionStart(thisActionNum, AIActionType.ConstructBuildingInEmptyNode, fromNode, curDepth, recurseCount);
#endif

        if (fromNode.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut)
            return; // not enough workers in node to send any out

        var count = fromNode.NumNeighbors;
        for (int i = 0; i < count; i++)
        {
            var toNode = fromNode.NeighborNodes[i];

            // Verify we can perform the action
            if (toNode.OwnedBy != null) continue; // This task can't send workers to nodes owned by anyone (including this player).  Those are handled in other actions
            if (toNode.IsResourceNode) continue; // Can't send to or construct in resource nodes
            if (toNode.HasBuilding) continue; // Node already has a building

            // If here then we can send workers to this node.  Now determine what building we can construct in this node
            for (int j = 0; j < numBuildingDefns; j++)
            {
                var buildingDefn = buildableBuildingDefns[j];

                // Verify we can perform the action
                if (!aiTownState.ConstructionResourcesCanBeReachedFromNode(toNode, buildingDefn.ConstructionRequirements)) continue;

                // Don't build resource gatherers if there are no resource nodes within reach.  
                // NOTE: This assumes that resource gatherers are single purpose; e.g. can't also generate workers
              //  if (buildingDefn.CanGatherResources && toNode.DistanceToClosestGatherableResourceNode > 1) continue;

                // Don't build barracks unless enemy is in neighboring node. NOTE: should instead check if enemy is within X nodes
                if (buildingDefn.CanGenerateWorkers && toNode.DistanceToClosestEnemyNode > 1) continue;

                // ==== Perform the action and get the score of the state after the action is performed
                aiTownState.BuildBuilding(toNode, buildingDefn, out GoodType res1Id, out int resource1Amount, out GoodType res2Id, out int resource2Amount);
                aiTownState.EvaluateScore(curDepth, maxDepth, out float scoreAfterActionAndBeforeSubActions, out DebugAIStateReasons debugOutput_actionScoreReasons);

#if DEBUG
                AIDebugger.TrackPerformAction_ConstructBuildingInEmptyNode(toNode, buildingDefn, scoreAfterActionAndBeforeSubActions);
#endif

                // ==== Recursively determine what the best action is after this action is performed
                var actionScore = RecursivelyDetermineBestAction(curDepth + 1, scoreAfterActionAndBeforeSubActions);
                if (actionScore.Score > bestAction.Score)
                {
                    // This is the best action so far; save the action so we can return it
                    bestAction.Score = actionScore.ScoreBeforeSubActions;
                    bestAction.Type = AIActionType.ConstructBuildingInEmptyNode;
                    bestAction.SourceNode = fromNode;
                    bestAction.DestNode = toNode;
                    bestAction.BuildingToConstruct = buildingDefn;
#if DEBUG
                    bestAction.TrackStrategyDebugInfoInAction(actionScore, debugOutput_actionScoreReasons, thisActionNum, recurseCount, curDepth);
#endif
                }

                // ==== Undo the action
                aiTownState.Undo_BuildBuilding(toNode, res1Id, resource1Amount, res2Id, resource2Amount);
            }
        }
#if DEBUG
        AIDebugger.PopTryActionStart();
#endif
    }
}