using UnityEngine;

public class AITask_ConstructBuilding : AITask
{
    public AITask_ConstructBuilding(PlayerData player, AI_TownState aiTownState, int maxDepth, int minWorkersInNodeBeforeConsideringSendingAnyOut) : base(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut) { }

    override public bool TryTask(AI_NodeState fromNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions, out AIAction bestAction)
    {
        bestAction = null;
        if (fromNode.OwnedBy != player) // only process actions from/in nodes that we own
            return false;

        if (fromNode.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut)
            return false; // not enough workers in node to send any out

        bestAction = player.AI.GetAIAction();

        foreach (var toNode in fromNode.NeighborNodes)
        {
            // Verify we can perform the action
            if (toNode.OwnedBy != null) continue; // This task can't send workers to nodes owned by anyone (including this player).  Those are handled in other actions
            if (toNode.HasBuilding) continue; // Node already has a building. note: resource nodes e.g. forest don't count as having a building until e.g. woodcutter is built
            if (toNode.IsVisited) continue; // don't revisit nodes we visited earlier in the recursion; avoid ping-ponging between nodes

            // If here then we can send workers to this node.  Now determine what building we can construct in this node
            for (int i = 0; i < player.AI.numBuildingDefns; i++)
            {
                var buildingDefn = player.AI.buildableBuildingDefns[i];

                // ==== Verify we can perform the action
                if (!canBuildBuilding(buildingDefn, toNode)) continue;
 
                int d1 = fromNode.NumWorkers, d2 = toNode.NumWorkers;

                // ==== Perform the action and update the aiTownState to reflect the action
                aiTownState.SendWorkersToConstructBuildingInEmptyNode(fromNode, toNode, buildingDefn, curDepth, out GoodType res1Id, out int resource1Amount, out GoodType res2Id, out int resource2Amount, .5f, out int numSent); // TODO: Try different #s?
                var debuggerEntry = aiDebuggerParentEntry.AddEntry_ConstructBuildingInEmptyNode(fromNode, toNode, numSent, buildingDefn, 0, player.AI.debugOutput_ActionsTried++, curDepth);

                // ==== Determine the score of the action we just performed (recurse down); if this is the best so far amongst our peers (in our parent node) then track it as the best action
                var actionScore = GetActionScore(curDepth, debuggerEntry);
                if (actionScore > bestAction.Score)
                    bestAction.SetTo_ConstructBuildingInEmptyNode(fromNode, toNode, numSent, buildingDefn, actionScore, debuggerEntry);

                // ==== Undo the action to reset the townstate to its original state
                aiTownState.Undo_SendWorkersToConstructBuildingInEmptyNode(fromNode, toNode, res1Id, resource1Amount, res2Id, resource2Amount, numSent);
                Debug.Assert(d1 == fromNode.NumWorkers && d2 == toNode.NumWorkers);
            }
        }
        return true;
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