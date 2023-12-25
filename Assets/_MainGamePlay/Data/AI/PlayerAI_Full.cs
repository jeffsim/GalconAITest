using System;
using UnityEngine;

// Actions a player-owned Node can take:
// 1. Send workers to a node that neighbors a node we own.
// 2. Construct a building in a node we own.
// 3. Upgrade a building in a node we own.
// 4. Attack a node that neighbors a node we own.
// 5. Send workers to a node that neighbors a node we own to better defend it.
public partial class PlayerAI
{
    float recursivelyDetermineBestAction_Full(AI_TownState state, int curDepth = 0)
    {
        float bestValue = 0;
        foreach (var node in state.Nodes)
        {
            if (node.OwnedBy != player) continue; // only process nodes we own

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
