using NUnit.Framework;

public partial class PlayerAI
{
    private AIAction TrySendWorkersToConstructBuildingInEmptyNeighboringNode(AI_NodeState fromNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions)
    {
        var bestAction = new AIAction() { Type = AIActionType.DoNothing };

        if (fromNode.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut)
            return bestAction; // not enough workers in node to send any out

        foreach (var toNode in fromNode.NeighborNodes)
        {
            // Verify we can perform the action
            if (toNode.OwnedBy != null) continue; // This task can't send workers to nodes owned by anyone (including this player).  Those are handled in other actions
            if (toNode.HasBuilding) continue; // Node already has a building. note: resource nodes e.g. forest don't count as having a building until e.g. woodcutter is built

            // If here then we can send workers to this node.  Now determine what building we can construct in this node
            for (int j = 0; j < numBuildingDefns; j++)
            {
                var buildingDefn = buildableBuildingDefns[j];

                // ==== Verify we can perform the action
                if (!canBuildBuilding(buildingDefn, toNode)) continue;

                // ==== Perform the action and update the aiTownState to reflect the action
                aiTownState.SendWorkersToConstructBuildingInEmptyNode(fromNode, toNode, buildingDefn, curDepth, out GoodType res1Id, out int resource1Amount, out GoodType res2Id, out int resource2Amount, .5f, out int numSent); // TODO: Try different #s?
#if DEBUG
                var debuggerEntry = aiDebuggerParentEntry.AddEntry_ConstructBuildingInEmptyNode(fromNode, toNode, numSent, buildingDefn, 0, debugOutput_ActionsTried++, curDepth);
                debuggerEntry.Debug_ActionScoreBeforeSubactions = aiTownState.EvaluateScore(curDepth, maxDepth, out _);
#endif

                // ==== Determine the score of the action we just performed; recurse down into subsequent actions if we're not at the max depth
                float actionScore;
                if (curDepth == maxDepth)
                {
                    // We're a leaf node; determine the value of the aiTownState now that we're all the way down here
                    actionScore = aiTownState.EvaluateScore(curDepth, maxDepth, out _);
                }
                else
                {
                    // We're not a leaf node; recursively determine what the best action is to perform after we've performed this action
                    var bestNextAction = DetermineBestActionToPerform(curDepth + 1, debuggerEntry);
                    actionScore = bestNextAction.Score; // 'the score of having taken this action and then the best action after this action'
                }
#if DEBUG
                debuggerEntry.FinalActionScore = actionScore;
#endif

                // ==== If this action is the best better so far amongst our peers (in our parent node) then track it as the best action
                if (actionScore > bestAction.Score)
                {
                    bestAction.SetTo_ConstructBuildingInEmptyNode(fromNode, toNode, numSent, buildingDefn, actionScore);
                    aiDebuggerParentEntry.BestNextAction = debuggerEntry;
                }

                //                 // We've created a new gamestate; before 
                //                 var actionScore = aiTownState.EvaluateScore(curDepth, maxDepth, out DebugAIStateReasons debug_actionScoreReasons);
                // #if DEBUG
                //                 // In debug keep track of ALL actions tried, not just the best one; this is so we can see what the AI is considering in AIDebuggerPanel
                //                 var debuggerEntry = aiDebuggerParentEntry.AddEntry_ConstructBuildingInEmptyNode(fromNode, toNode, numSent, buildingDefn, actionScore, debugOutput_ActionsTried++, curDepth);
                // #endif
                //                 if (curDepth == maxDepth)
                //                 {
                //                     if (actionScore > bestScoreAtDepth) {
                //                         bestAction.SetTo_ConstructBuildingInEmptyNode(fromNode, toNode, numSent, buildingDefn, actionScore);
                //                         aiDebuggerParentEntry.BestNextAction = debuggerEntry;
                //                         bestScoreAtDepth = actionScore;
                //                     }
                //                 }
                //                 else
                //                 {
                //                     // ==== Recursively determine what the best action is to perform after we've performed this action
                //                     var bestNextAction = DetermineBestActionToPerform(curDepth + 1, bestScoreAtDepth, debuggerEntry);
                //                     if (bestNextAction.Score > bestAction.Score)
                //                     {
                //                         // The gamestate resulting from this action AND the best series of actions AFTER this action is the best we've seen; save this action
                //                         bestAction.SetTo_ConstructBuildingInEmptyNode(fromNode, toNode, numSent, buildingDefn, bestNextAction.Score);
                // #if DEBUG
                //                         bestAction.NextAction = bestNextAction;
                //                         aiDebuggerParentEntry.BestNextAction = debuggerEntry;
                //                         // bestAction.TrackStrategyDebugInfoInAction(debug_actionScoreReasons, actionNumberOnEntry, curDepth);
                // #endif
                //                     }
                //                 }

                // ==== Undo the action to reset the townstate to its original state
                aiTownState.Undo_SendWorkersToConstructBuildingInEmptyNode(fromNode, toNode, res1Id, resource1Amount, res2Id, resource2Amount, numSent);
            }
        }
        return bestAction;
    }

    private bool canBuildBuilding(BuildingDefn buildingDefn, AI_NodeState toNode)
    {
        // Don't build resource gatherers if the toNode doesn't have the resource that the building can gather
        if (toNode.CanBeGatheredFrom)
        {
            if (!buildingDefn.CanGatherResources)
                return false;
            if (toNode.ResourceGatheredFromThisNode != buildingDefn.ResourceThisNodeCanGoGather.GoodType)
                return false;
        }
        else if (buildingDefn.CanGatherResources)
            return false;

        // Don't build barracks unless enemy is in neighboring node. NOTE: should instead check if enemy is within X nodes
        // if (buildingDefn.CanGenerateWorkers && toNode.DistanceToClosestEnemyNode(player) > 1);
        // return false;
        // ... etc for other building types

        // Do we have resources to build this building?
        if (!aiTownState.ConstructionResourcesCanBeReachedFromNode(toNode, buildingDefn.ConstructionRequirements))
            return false;

        return true;
    }
}