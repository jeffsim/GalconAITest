using System;
using UnityEngine;

public partial class PlayerAI
{
    PlayerData player;
    AI_TownState aiTownState;
    int minWorkersInNodeBeforeConsideringSendingAnyOut = 3;
    int maxDepth = 8;

    public PlayerAI(PlayerData playerData)
    {
        player = playerData;
        aiTownState = new AI_TownState(player);
    }

    public void InitializeStaticData(TownData townData)
    {
        aiTownState.InitializeStaticData(townData);
    }

    internal void Update(TownData townData)
    {
        aiTownState.UpdateState(townData);

        // Determine the best action to take, and then take it
        var bestAction = RecursivelyDetermineBestAction();
        performAction(bestAction);
    }

    // Actions a player-owned Node can take:
    // 1. Send 50% of workers to a node that neighbors the node
    // 2. Construct a building in a node we own.
    AIAction RecursivelyDetermineBestAction(int curDepth = 0)
    {
        // Keep track of the best action in this 'level' of the AI stack, and return it
        AIAction bestAction = new(); // todo. Pool?  Or just precreate a static list of 100000 of them and use + increment indexer (so no push/pop)? etc
        bestAction.Score = aiTownState.EvaluateScore();

        if (curDepth == maxDepth || aiTownState.IsGameOver())
        {
            return bestAction;
        }

        foreach (var node in aiTownState.Nodes)
        {
            if (node.OwnedBy != player) continue; // only process nodes that we own

            TrySendWorkersFromNode(node, ref bestAction, curDepth);
            TryConstructBuildingInNode(node, ref bestAction, curDepth);
        }
        return bestAction;
    }

    // Try constructing a building in the specified node
    private void TryConstructBuildingInNode(AI_NodeState node, ref AIAction bestAction, int curDepth)
    {
        if (node.HasBuilding)
            return; // already has one

        // Only attempt to construct buildings that we have resources within 'reach' to build.
        foreach (var buildingDefn in GameDefns.Instance.BuildingDefns.Values)
        {
            if (!buildingDefn.CanBeBuiltByPlayer)
                continue;
            if (!aiTownState.CraftingResourcesCanBeReachedFromNode(node, buildingDefn.ConstructionRequirements))
                continue;

            // Update the townstate to reflect building the building, and consume the resources for it
            aiTownState.BuildBuilding(node, buildingDefn, out GoodDefn resource1, out int resource1Amount, out GoodDefn resource2, out int resource2Amount);

            // Recursively determine the value of this action
            var actionScore = RecursivelyDetermineBestAction(curDepth + 1);
            if (actionScore.Score > bestAction.Score)
            {
                // This is the best action so far; save the action so we can return it
                bestAction.Score += actionScore.Score;
                bestAction.Type = AIActionType.ConstructBuildingInOwnedNode;
                bestAction.SourceNode = node;
                bestAction.BuildingToConstruct = buildingDefn;
#if DEBUG
                bestAction.NextAction = actionScore; // track so I can output the next N steps in the optimal strategy
#endif
            }

            // Undo the action
            aiTownState.Undo_BuildBuilding(node, resource1, resource1Amount, resource2, resource2Amount);
        }
    }

    // Try expanding to neighboring empty nodes.
    private void TrySendWorkersFromNode(AI_NodeState node, ref AIAction bestAction, int curDepth)
    {
        if (node.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut)
            return; // not enough workers in node to send any out

        // Must have a building in a node to send workers from it 
        if (!node.HasBuilding)
            return;

        // Can't send to nodes we don't own (that's handled separately via Attack action)
        if (node.OwnedBy != player)
            return;

        if (aiTownState.HaveSentWorkersFromNode.ContainsKey(node.NodeId))
            return; // don't send workers from the same node twice in an AI stack, or from a node we sent workers to in the stack.

        foreach (var neighborNode in node.NeighborNodes)
        {
            if (aiTownState.HaveSentWorkersToNode.ContainsKey(neighborNode.NodeId))
                continue;  // don't send workers to the same node twice in an AI stack.  This disallows some odd strategies but cleans up lots of odd stuff

            aiTownState.SendWorkersToEmptyNode(node, neighborNode, .5f, out int numSent);

            // Recursively determine the value of this action.
            var actionScore = RecursivelyDetermineBestAction(curDepth + 1);
            if (actionScore.Score > bestAction.Score)
            {
                // This is the best action so far in this 'level' of the AI stack; save the action so we can return it
                bestAction.Score = actionScore.Score;
                bestAction.Type = AIActionType.SendWorkersToNode;
                bestAction.Count = numSent;
                bestAction.SourceNode = node;
                bestAction.DestNode = neighborNode;
#if DEBUG
                bestAction.NextAction = actionScore; // track so I can output the next N steps in the optimal strategy
#endif
            }

            // Undo the action
            aiTownState.Undo_SendWorkersToEmptyNode(node, neighborNode, numSent);
        }
    }

    private void performAction(AIAction bestAction)
    {
        if (bestAction.Type == AIActionType.DoNothing)
            return; // no action to take

        var actionToOutput = bestAction;
        int spaces = 0;
        while (actionToOutput.NextAction != null)
        {
            // create empty string with 'spaces' indentation
            string str = new string(' ', Math.Max(0, spaces - 1) * 4);
            if (actionToOutput != bestAction)
                str += "\u21B3";

            switch (actionToOutput.Type)
            {
                case AIActionType.SendWorkersToNode:
                    Debug.Log(str + "Send " + actionToOutput.Count + " workers from " + actionToOutput.SourceNode.NodeId + " to " + actionToOutput.DestNode.NodeId);
                    break;
                case AIActionType.ConstructBuildingInOwnedNode:
                    Debug.Log(str + "Construct " + actionToOutput.BuildingToConstruct.Name + " in " + actionToOutput.SourceNode.NodeId);
                    break;
                case AIActionType.DoNothing:
                    Debug.Log(str + "Do nothing");
                    break;
                default:
                    throw new Exception("Unhandled AIActionType: " + actionToOutput.Type);
            }
            spaces++;
            actionToOutput = actionToOutput.NextAction;
        }
    }
    /*
                // Try constructing a building in node if we have resources to build it and those resources are accessible
                if (!node.HasBuilding)
                {
                    foreach (BuildingDefn buildingDefn in GameDefns.Instance.BuildingDefns.Values)
                    {
                        if (buildingDefn.CanBeBuiltByPlayer && BuildingCanBePurchased(buildingDefn, node))
                        {
                            // Update the townstate to reflect building the building, and consume the resources for it
                            aiTownState.BuildBuilding(node, buildingDefn, out GoodDefn resource1, out int resource1Amount, out GoodDefn resource2, out int resource2Amount);

                            // Recursively determine the value of this action
                            var actionScore = RecursivelyDetermineBestAction(curDepth + 1);
                            if (actionScore.Score > bestAction.Score)
                            {
                                // This is the best action so far; save the action so we can return it
                                bestAction.Score = actionScore.Score;
                                bestAction.Type = AIActionType.ConstructBuildingInOwnedNode;
                                bestAction.SourceNode = node;
                                bestAction.BuildingToConstruct = buildingDefn;
                                bestAction.NextAction = actionScore;
                            }

                            // Undo the action
                            aiTownState.Undo_BuildBuilding(node, resource1, resource1Amount, resource2, resource2Amount);
                        }
                    }
                }
        internal bool BuildingCanBePurchased(BuildingDefn buildingDefn, AI_NodeState buildInNode)
        {
            // TODO: I'm still not handling the case where goods needed are in a Node that is blocked by enemy nodes.
            //       The following code needs only look at nodes that are reachable from buildInNode.

            // return true if the user has in stock the necessary resources to build the building
            foreach (var cost in buildingDefn.ConstructionRequirements)
                if (aiTownState.GetNumItem(cost.Good) < cost.Amount)
                    return false;
            return true;
        }   */
}
