using UnityEngine;

public partial class PlayerAI
{
    private void TrySendWorkersToConstructBuildingInEmptyNeighboringNode(AI_NodeState fromNode, ref AIAction bestAction, int curDepth, int recurseCount, int actionNumberOnEntry)
    {
        if (fromNode.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut)
            return; // not enough workers in node to send any out

        var count = fromNode.NumNeighbors;
        for (int i = 0; i < count; i++)
        {
            var toNode = fromNode.NeighborNodes[i];

            // Verify we can perform the action
            if (toNode.OwnedBy != null) continue; // This task can't send workers to nodes owned by anyone (including this player).  Those are handled in other actions
            if (toNode.HasBuilding) continue; // Node already has a building. note: resource nodes e.g. forest don't count as having a building until e.g. woodcutter is built

            // If here then we can send workers to this node.  Now determine what building we can construct in this node
            for (int j = 0; j < numBuildingDefns; j++)
            {
                var buildingDefn = buildableBuildingDefns[j];

                // ==== Verify we can perform the action

                // Don't build resource gatherers if there are no resource nodes within reach.  
                if (buildingDefn.CanGatherResources)
                if (toNode.NodeId == 26)
                {
                    // Debug.Log("Debug");
                }
                if (buildingDefn.CanGatherResources && !(toNode.CanBeGatheredFrom && toNode.ResourceGatheredFromThisNode == buildingDefn.ResourceThisNodeCanGoGather.GoodType)) continue;

                // Do we have resources to build this building?
                if (!aiTownState.ConstructionResourcesCanBeReachedFromNode(toNode, buildingDefn.ConstructionRequirements)) continue;

                // Don't build barracks unless enemy is in neighboring node. NOTE: should instead check if enemy is within X nodes
                // if (buildingDefn.CanGenerateWorkers && toNode.DistanceToClosestEnemyNode(player) > 1) continue;

                // ... etc for other building types


                // ==== Perform the action and get the score of the state after the action is performed
                float percentOfWorkersToSend = .5f; // TODO: Try different #s?
                aiTownState.SendWorkersToConstructBuildingInEmptyNode(fromNode, toNode, buildingDefn, curDepth, out GoodType res1Id, out int resource1Amount, out GoodType res2Id, out int resource2Amount, percentOfWorkersToSend, out int numSent);
                aiTownState.EvaluateScore(curDepth, maxDepth, out float scoreAfterActionAndBeforeSubActions, out DebugAIStateReasons debugOutput_actionScoreReasons);

#if DEBUG
                Debug.Assert(buildingDefn != null);
                var prevEntry = AIDebugger.TrackPerformAction_ConstructBuildingInEmptyNode(fromNode, toNode, numSent, buildingDefn, scoreAfterActionAndBeforeSubActions, debugOutput_ActionsTried++, curDepth, recurseCount);
#endif

                // ==== Recursively determine what the best action is after this action is performed
                var actionScore = RecursivelyDetermineBestAction(curDepth + 1, scoreAfterActionAndBeforeSubActions);
                if (actionScore.Score > bestAction.Score)
                {
                    // This is the best action so far; save the action so we can return it
                    bestAction.Score = actionScore.ScoreBeforeSubActions;
                    bestAction.Type = AIActionType.ConstructBuildingInEmptyNode;
                    bestAction.SourceNode = fromNode;
                    bestAction.Count = numSent;
                    bestAction.DestNode = toNode;
                    bestAction.BuildingToConstruct = buildingDefn;
#if DEBUG
                    bestAction.DebugOutput_NextAction = actionScore;
                    if (AIDebugger.ShouldTrackEntries) prevEntry.BestNextAction = AIDebugger.curEntry;
                    bestAction.TrackStrategyDebugInfoInAction(actionScore, debugOutput_actionScoreReasons, actionNumberOnEntry, recurseCount, curDepth);
#endif
                }

#if DEBUG
                AIDebugger.PopPerformedAction(prevEntry);
#endif
                // ==== Undo the action
                aiTownState.Undo_SendWorkersToConstructBuildingInEmptyNode(fromNode, toNode, res1Id, resource1Amount, res2Id, resource2Amount, numSent);
            }
        }
    }
}