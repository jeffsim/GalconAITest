using UnityEngine;

public partial class Strategy_NonRecursive
{
    private void CheckPriority_BuildBuilding()
    {
        int playerNodesCount = PlayerNodes.Count;
        for (int i = 0; i < playerNodesCount; i++)
        {
            var fromNode = PlayerNodes[i];
            float rawValue = 0f;

            if (fromNode.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut)
                continue;

            for (int n = 0; n < fromNode.NeighborNodes.Count; n++)
            {
                var toNode = fromNode.NeighborNodes[n];

                if (toNode.OwnedBy != null) continue; // This task can't send workers to nodes owned by anyone (including this player).  Those are handled in other actions
                if (toNode.HasBuilding) continue; // Node already has a building. note: resource nodes e.g. forest don't count as having a building until e.g. woodcutter is built

                // 1. Iterate over possible buildings to construct
                for (int b = 0; b < Player.AI.numBuildingDefns; b++)
                {
                    var buildingDefn = Player.AI.buildableBuildingDefns[b];

                    // Check if the building can be constructed on this node
                    if (!canBuildBuilding(buildingDefn, toNode))
                        continue;

                    // 3. Calculate value based on strategic importance
                    // For example, defensive buildings have higher priority on border nodes
                    if (buildingDefn.IsDefensive)
                    {
                        if (toNode.IsOnTerritoryEdge)
                            rawValue += buildingStrategicScalingFactor * 1.5f;
                        else
                            rawValue += buildingStrategicScalingFactor;
                    }
                    else if (buildingDefn.CanGatherResources)
                    {
                        // if we are low on the resources that this building can gather AND we need or will need those resources, prioritize this building
                        var gatherableResource = buildingDefn.ResourceThisNodeCanGoGather.GoodType;
                        int numGatherableResourceOwned = Town.PlayerTownInventory[gatherableResource];
                        
                        int numGatherableResourceDesired = 20; // TODO: base this on how much we need the resource

                        rawValue += buildingResourceScalingFactor * (numGatherableResourceDesired - numGatherableResourceOwned);
                    }
                    else
                    {
                        // Other building types
                        rawValue += buildingStrategicScalingFactor;
                    }

                    // 4. Normalize the raw value
                    float clampedRawValue = Mathf.Clamp(rawValue, buildBuildingMinScore, buildBuildingMaxScore);
                    float normalizedValue = (clampedRawValue - 10f) / 30f;
                    // normalizedValue is now between 0.333 and 0.666

                    // 5. Apply AI personality multiplier
                    float finalValue = normalizedValue * personalityMultiplier_BuildBuilding;

                    // 6. Update Best Action if this action is better than the current best action
                    // TODO: Uncomment following and comment below to avoid unnecessary work.  Same with other CheckPriority methods
                    // if (finalValue > BestAction.Score)
                    {
                        int numSent = fromNode.NumWorkers / 2;
                        AIDebuggerEntryData debuggerEntry = null;
#if DEBUG
                        if (AITestScene.Instance.TrackDebugAIInfo)
                        {
                            debuggerEntry = AIDebugger.rootEntry.AddEntry_ConstructBuildingInEmptyNode(
                                fromNode,
                                toNode,
                                numSent,
                                buildingDefn,
                                finalValue,
                                Player.AI.debugOutput_ActionsTried++,
                                0
                            );
                        }
#endif
                    if (finalValue > BestAction.Score)
                        BestAction.SetTo_ConstructBuildingInEmptyNode(fromNode, toNode, numSent, buildingDefn, finalValue, debuggerEntry);
                    }
                }

                // Reset rawValue for the next building
                rawValue = 0f;
            }
        }
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
        if (!Town.ConstructionResourcesCanBeReachedFromNode(toNode, buildingDefn.ConstructionRequirements))
            return false;

        return true;
    }
}