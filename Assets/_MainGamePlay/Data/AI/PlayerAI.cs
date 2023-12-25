using System;
using System.Collections.Generic;
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
        AIAction bestAction = new(); // todo. Pool?  Or just precreate a static list of 100000 of them and use + increment indexer (so no push/pop)? etc

        if (curDepth == maxDepth || aiTownState.IsGameOver())
        {
            bestAction.Score = aiTownState.EvaluateScore();
            return bestAction;
        }
        foreach (var node in aiTownState.Nodes)
        {
            if (node.OwnedBy != player)
                continue; // only process nodes that we own

            // Try expanding to neighboring empty nodes.
            if (node.NumWorkers > minWorkersInNodeBeforeConsideringSendingAnyOut)
                foreach (var neighborNode in node.NeighborNodes)
                    if (!neighborNode.HasBuilding)
                    {
                        // Update the townstate to reflect building the building, and consume the resources for it
                        int numToSend = Math.Max(1, node.NumWorkers / 2);
                        aiTownState.SendWorkersToEmptyNode(node, neighborNode, numToSend);

                        // Recursively determine the value of this action.  Note that the following function gaurantees to restore any changes to townstate.
                        var actionScore = RecursivelyDetermineBestAction(curDepth + 1);
                        if (actionScore.Score > bestAction.Score)
                        {
                            // This is the best action so far; save the action so we can return it
                            bestAction.Score = actionScore.Score;
                            bestAction.Type = AIActionType.SendWorkersToNode;
                            bestAction.Count = numToSend;
                            bestAction.SourceNode = node;
                            bestAction.DestNode = neighborNode;
                            bestAction.NextAction = actionScore;
                        }

                        // Undo the action
                        aiTownState.Undo_SendWorkersToEmptyNode(node, neighborNode, numToSend);
                    }
        }
        return bestAction;
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
