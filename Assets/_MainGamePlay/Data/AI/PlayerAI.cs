using System;
using System.Collections.Generic;
using UnityEngine;

public enum AIActionType { DoNothing, SendWorkersToNode, ConstructBuildingInOwnedNode };

public class AIAction
{
    public float Score;
    public AIActionType Type;
    public int Count;
    public AI_NodeState SourceNode;
    public AI_NodeState DestNode;
}

public class PlayerAI
{
    PlayerData player;
    AI_TownState aiTownState;

    int minWorkersInNodeBeforeConsideringSendingAnyOut = 3;
    List<BuildingDefn> buildingList = new(20);

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

        // Recursively take actions, modifying the state data (and then restoring it) as we go.  Find the highest value action.
        var bestAction = RecursivelyDetermineBestAction_Simple();

        // enact the action
        if (bestAction.Type == AIActionType.DoNothing)
            return; // no action to take
        Debug.Log(bestAction.Type + " " + bestAction.Count + " workers from " + bestAction.SourceNode.NodeId + " to " + bestAction.DestNode.NodeId);
    }

    int maxDepth = 8;

    // Actions a player-owned Node can take:
    // 1. Send 50% of workers to a node that neighbors the node
    // 2. Construct a building in a node we own.
    AIAction RecursivelyDetermineBestAction_Simple(int curDepth = 0)
    {
        AIAction bestAction = new AIAction(); // fuck todo. Pool?  Or just precreate a static list of 100000 of them and use + increment indexer (so no push/pop)? etc

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
                        var actionScore = RecursivelyDetermineBestAction_Simple(curDepth + 1);
                        if (actionScore.Score > bestAction.Score)
                        {
                            // This is the best action so far; save the action so we can return it
                            bestAction.Score = actionScore.Score;
                            bestAction.Type = AIActionType.SendWorkersToNode;
                            bestAction.Count = numToSend;
                            bestAction.SourceNode = node;
                            bestAction.DestNode = neighborNode;
                        }

                        // Undo the action
                        aiTownState.Undo_SendWorkersToEmptyNode(node, neighborNode, numToSend);
                    }
        }
        return bestAction;
    }


    // Try constructing a building in node if we have resources to build it and those resources are accessible
    // if (!node.HasBuilding)
    // {
    //     foreach (BuildingDefn buildingDefn in GameDefns.Instance.BuildingDefns.Values)
    //     {
    //         if (buildingDefn.CanBeBuiltByPlayer && BuildingCanBePurchased(buildingDefn, node))
    //         {
    //             // Update the townstate to reflect building the building, and consume the resources for it
    //             aiTownState.BuildBuilding(node, buildingDefn, out GoodDefn resource1, out int resource1Amount, out GoodDefn resource2, out int resource2Amount);

    //             // Recursively determine the value of this action
    //             var value = RecursivelyDetermineBestAction_Simple(curDepth + 1);

    //             // Undo the action
    //             aiTownState.Undo_BuildBuilding(node, resource1, resource1Amount, resource2, resource2Amount);
    //         }
    //     }
    // }

    internal bool BuildingCanBePurchased(BuildingDefn buildingDefn, AI_NodeState buildInNode)
    {
        // TODO: I'm still not handling the case where goods needed are in a Node that is blocked by enemy nodes.
        //       The following code needs only look at nodes that are reachable from buildInNode.

        // return true if the user has in stock the necessary resources to build the building
        foreach (var cost in buildingDefn.ConstructionRequirements)
            if (aiTownState.GetNumItem(cost.Good) < cost.Amount)
                return false;
        return true;
    }

    // Actions a player-owned Node can take:
    // 1. Send workers to a node that neighbors a node we own.
    // 2. Construct a building in a node we own.
    // 3. Upgrade a building in a node we own.
    // 4. Attack a node that neighbors a node we own.
    // 5. Send workers to a node that neighbors a node we own to better defend it.
    float recursivelyDetermineBestAction_Full(AI_TownState state, int curDepth = 0)
    {
        float bestValue = 0;
        foreach (var node in state.Nodes)
        {
            if (node.OwnedBy != player)
                continue; // only process nodes we own

            // TODO: This is only checking "what if this player does X and then Y and then Z".  it doesn't
            //       take into account what enemy players can do between X and Y, and Y and Z.

            // Expand to neighboring nodes.
            foreach (var neighborNode in node.NeighborNodes)
            {
                if (!neighborNode.HasBuilding)
                {
                    // note: state value calculation should ensure taht we don't overly spread ourselves thin (unless that's the AI's weights)
                    // TODO: need to account for workers already walking to the node
                    bestValue = Math.Max(bestValue, SendWorkersToEmptyNode(node, neighborNode, 0.1f, state, curDepth));
                    bestValue = Math.Max(bestValue, SendWorkersToEmptyNode(node, neighborNode, 0.5f, state, curDepth));
                    bestValue = Math.Max(bestValue, SendWorkersToEmptyNode(node, neighborNode, 0.95f, state, curDepth));
                }
            }

            // Attack neighboring enemy nodes
            foreach (var neighborNode in node.NeighborNodes)
            {
                if (neighborNode.OwnedBy != player && neighborNode.OwnedBy.Hates(player))
                {
                    // TODO: don't send arbitrary % like this; instead send the # needed to win
                    // TODO: need to account for workers already walking to the node
                    // attack to disrupt enemies' strategy
                    bestValue = Math.Max(bestValue, SendWorkersToAttackNode(node, neighborNode, 0.1f, state, curDepth));
                    bestValue = Math.Max(bestValue, SendWorkersToAttackNode(node, neighborNode, 0.5f, state, curDepth));
                    bestValue = Math.Max(bestValue, SendWorkersToAttackNode(node, neighborNode, 0.95f, state, curDepth));
                }
            }

            // Defend a neighboring owned node
            // TODO: Can defend from farther inside, not just neighbors
            // TODO: how to know should defend?  I think it's from the state value calculation valuing defended nodes
            // TODO: Don't defend unless there's a reason to; e.g.:
            //   1. generally want a more even distribution of workers outside
            //   2. node has an enemy neighbor
            //   3. enemy is attacking other nodes.
            foreach (var neighborNode in node.NeighborNodes)
            {
                if (neighborNode.OwnedBy == player)
                {
                    // TODO: need to account for workers already walking to the node
                    bestValue = Math.Max(bestValue, SendWorkersToOwnedNode(node, neighborNode, 0.1f, state, curDepth));
                    bestValue = Math.Max(bestValue, SendWorkersToOwnedNode(node, neighborNode, 0.5f, state, curDepth));
                    bestValue = Math.Max(bestValue, SendWorkersToOwnedNode(node, neighborNode, 0.95f, state, curDepth));
                }
            }

            // Try constructing a building in node if we have resources to build it and those resources are accessible
            if (!node.HasBuilding)
            {
                // Prioritize buildings that gather resource if a neighboring node is generates that resource type
                // Prioritize buildings that progress a strategy
                // Prioritize buildings that provide defense

                // TODO: need to account for workers already walking to the node

                // note: prioritization doesn't apply here (that's baked into the state value calc), but we should apply logi here
                // to avoid trying to build "unuseful" buildings
            }

            // Try upgrading the building in node if we have resources to upgrade it and those resources are accessible
            if (node.HasBuilding)
            {
                // TODO: How do we determine if we *should* upgrade a building?
                // TODO: need to account for workers already walking to the node
            }
        }

        return bestValue;
    }

    private float SendWorkersToAttackNode(AI_NodeState sourceNode, AI_NodeState targetNode, float percentToSend, AI_TownState state, int curDepth)
    {
        int numToSend = Math.Max(0, Math.Min(sourceNode.NumWorkers - 1, (int)(sourceNode.NumWorkers * percentToSend)));
        state.SendWorkersToAttackNode(sourceNode, targetNode, numToSend, out int originalSourceNodeNumWorkers, out int originalDestNodeNumWorkers, out PlayerData originalOwner);
        var value = recursivelyDetermineBestAction_Full(state, curDepth + 1);
        state.Undo_SendWorkersToAttackNode(sourceNode, targetNode, originalSourceNodeNumWorkers, originalDestNodeNumWorkers, originalOwner);
        return value;
    }

    private float SendWorkersToEmptyNode(AI_NodeState sourceNode, AI_NodeState targetNode, float percentToSend, AI_TownState state, int curDepth)
    {
        int numToSend = Math.Max(0, Math.Min(sourceNode.NumWorkers - 1, (int)(sourceNode.NumWorkers * percentToSend)));
        state.SendWorkersToEmptyNode(sourceNode, targetNode, numToSend);
        var value = recursivelyDetermineBestAction_Full(state, curDepth + 1);
        state.Undo_SendWorkersToEmptyNode(sourceNode, targetNode, numToSend);
        return value;
    }

    private float SendWorkersToOwnedNode(AI_NodeState sourceNode, AI_NodeState targetNode, float percentToSend, AI_TownState state, int curDepth)
    {
        int numToSend = Math.Max(0, Math.Min(sourceNode.NumWorkers - 1, (int)(sourceNode.NumWorkers * percentToSend)));
        state.SendWorkersToOwnedNode(sourceNode, targetNode, numToSend);
        var value = recursivelyDetermineBestAction_Full(state, curDepth + 1);
        state.Undo_SendWorkersToOwnedNode(sourceNode, targetNode, numToSend);
        return value;
    }
}
