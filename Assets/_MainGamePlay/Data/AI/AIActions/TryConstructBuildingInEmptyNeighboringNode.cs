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
                var debuggerEntry = aiDebuggerParentEntry.AddEntry_ConstructBuildingInEmptyNode(fromNode, toNode, numSent, buildingDefn, 0, debugOutput_ActionsTried++, curDepth);
                // debuggerEntry.Debug_ActionScoreBeforeSubactions = aiTownState.EvaluateScore(curDepth, maxDepth, out _);

                // ==== Determine the score of the action we just performed; recurse down into subsequent actions if we're not at the max depth
                float actionScore;
                AIAction bestNextAction = curDepth < maxDepth ? DetermineBestActionToPerform(curDepth + 1, debuggerEntry) : null;
                if (bestNextAction != null)
                    actionScore = bestNextAction.Score; // Score of the best action after this action
                else
                    actionScore = aiTownState.EvaluateScore(curDepth, maxDepth, out _); // Evaluate score of the current state after this action
                debuggerEntry.FinalActionScore = actionScore;

                // ==== If this action is the best so far amongst our peers (in our parent node) then track it as the best action
                if (actionScore > bestAction.Score)
                    bestAction.SetTo_ConstructBuildingInEmptyNode(fromNode, toNode, numSent, buildingDefn, actionScore, debuggerEntry);

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