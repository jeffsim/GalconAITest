using NUnit.Framework;

public partial class PlayerAI
{
    private AIAction TrySendWorkersToConstructBuildingInEmptyNeighboringNode(AI_NodeState fromNode, int curDepth, float bestScoreAtDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry)
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

                // ==== Perform the action
                aiTownState.SendWorkersToConstructBuildingInEmptyNode(fromNode, toNode, buildingDefn, curDepth, out GoodType res1Id, out int resource1Amount, out GoodType res2Id, out int resource2Amount, .5f, out int numSent); // TODO: Try different #s?
                var actionScore = aiTownState.EvaluateScore(curDepth, maxDepth, out DebugAIStateReasons debug_actionScoreReasons);
#if DEBUG
                // In debug keep track of ALL actions tried, not just the best one; this is so we can see what the AI is considering in AIDebuggerPanel
                var debuggerEntry = aiDebuggerParentEntry.AddEntry_ConstructBuildingInEmptyNode(fromNode, toNode, numSent, buildingDefn, actionScore, debugOutput_ActionsTried++, curDepth);
#endif
                if (curDepth == maxDepth)
                {
                    if (actionScore > bestScoreAtDepth) {
                        bestAction.SetTo_ConstructBuildingInEmptyNode(fromNode, toNode, numSent, buildingDefn, actionScore);
                        aiDebuggerParentEntry.BestNextAction = debuggerEntry;
                        bestScoreAtDepth = actionScore;
                    }
                }
                else
                {
                    // ==== Recursively determine what the best action is to perform after we've performed this action
                    var bestNextAction = DetermineBestActionToPerform(curDepth + 1, bestScoreAtDepth, debuggerEntry);
                    if (bestNextAction.Score > bestAction.Score)
                    {
                        // The gamestate resulting from this action AND the best series of actions AFTER this action is the best we've seen; save this action
                        bestAction.SetTo_ConstructBuildingInEmptyNode(fromNode, toNode, numSent, buildingDefn, bestNextAction.Score);
#if DEBUG
                        bestAction.NextAction = bestNextAction;
                        aiDebuggerParentEntry.BestNextAction = debuggerEntry;
                        // bestAction.TrackStrategyDebugInfoInAction(debug_actionScoreReasons, actionNumberOnEntry, curDepth);
#endif
                    }
                }
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