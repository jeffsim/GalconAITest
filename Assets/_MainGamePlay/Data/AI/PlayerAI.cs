using System;
using System.Collections.Generic;
using UnityEditor;

public enum AIActionType { SendWorkersToNode, ConstructBuildingInOwnedNode };

public class AIAction
{
    public AIActionType Type;
    public int Count;
    public NodeData SourceNode;
    public NodeData DestNode;
}

class AI_PlayerState
{

}

class AI_BuildingState
{
    public BuildingDefn buildingDefn;

    public AI_BuildingState(BuildingData building)
    {
        buildingDefn = building?.Defn;
    }
}

class AI_NodeState
{
    private NodeData nodeData;
    public List<AI_NodeState> NeighborNodes;
    public int NumWorkers;

    public AI_BuildingState Building;
    public bool HasBuilding => Building.buildingDefn != null;
    public PlayerData OwnedBy;

    public AI_NodeState(NodeData nodeData)
    {
        this.nodeData = nodeData;
        Building = new AI_BuildingState(nodeData.Building);
        NumWorkers = nodeData.NumWorkers;
        OwnedBy = nodeData.OwnedBy;
    }
}

class AI_TownState
{
    public AI_NodeState[] Nodes;

    TownData townData;
    PlayerData player;

    public AI_TownState(TownData townData, PlayerData player)
    {
        this.townData = townData;
        this.player = player;

        // Initialize Node list
        Nodes = new AI_NodeState[townData.Nodes.Count];
        for (int i = 0; i < townData.Nodes.Count; i++)
            Nodes[i] = new AI_NodeState(townData.Nodes[i]);

        for (int i = 0; i < townData.Nodes.Count; i++)
            foreach (var neighborNode in townData.Nodes[i].NodeConnections)
                Nodes[i].NeighborNodes.Add(Nodes[townData.Nodes.IndexOf(neighborNode.End)]);
    }

    internal void SendWorkersToEmptyNode(AI_NodeState sourceNode, AI_NodeState destNode, int numToSend)
    {
        // We are capturing a new node; need to update lists (e.g. PlayerOwnedNodes)
        sourceNode.NumWorkers -= numToSend;
        destNode.NumWorkers += numToSend;
        destNode.OwnedBy = player;
    }

    internal void Undo_SendWorkersToEmptyNode(AI_NodeState sourceNode, AI_NodeState destNode, int numSent)
    {
        // We are undo'ing capture of a new node; need to restore lists (e.g. PlayerOwnedNodes)
        sourceNode.NumWorkers += numSent;
        destNode.NumWorkers -= numSent;
        destNode.OwnedBy = null;
    }

    internal void SendWorkersToOwnedNode(AI_NodeState sourceNode, AI_NodeState destNode, int numToSend)
    {
        sourceNode.NumWorkers -= numToSend;
        destNode.NumWorkers += numToSend;
    }

    internal void Undo_SendWorkersToOwnedNode(AI_NodeState sourceNode, AI_NodeState destNode, int numSent)
    {
        sourceNode.NumWorkers += numSent;
        destNode.NumWorkers -= numSent;
    }

    internal void SendWorkersToAttackNode(AI_NodeState sourceNode, AI_NodeState destNode, int numToSend,
            out bool nodeOwnerChanged, out int originalDestNodeNumWorkers, out PlayerData originalOwner)
    {
        originalDestNodeNumWorkers = destNode.NumWorkers;
        originalOwner = destNode.OwnedBy;

        // For now, assume 1:1 attack.  In the future support e.g. stronger attackers, defensive bonus, etc.
        sourceNode.NumWorkers -= numToSend;
        destNode.NumWorkers -= numToSend;

        nodeOwnerChanged = false;
        if (destNode.NumWorkers == 0)
        {
            // attackers and defenders both died
            destNode.OwnedBy = null;
            nodeOwnerChanged = true;
        }
        else if (destNode.NumWorkers < 0)
        {
            // we captured the node
            nodeOwnerChanged = true;
            destNode.OwnedBy = player;

            destNode.NumWorkers = -destNode.NumWorkers;
        }
    }

    internal void Undo_SendWorkersToAttackNode(AI_NodeState sourceNode, AI_NodeState destNode, int numSent,
                                               bool nodeOwnerChanged, int originalDestNodeNumWorkers, PlayerData originalOwner)
    {
        if (nodeOwnerChanged)
        {
            sourceNode.NumWorkers += numSent;
            destNode.NumWorkers = originalDestNodeNumWorkers;
            destNode.OwnedBy = originalOwner;
        }
    }
}

public class PlayerAI
{
    PlayerData player;

    public PlayerAI(PlayerData playerData)
    {
        player = playerData;
    }

    internal void Update(TownData townData)
    {
        // Recursively take actions, modifying the state data (and then restoring it) as we go.  Find the highest value action.
        var state = new AI_TownState(townData, player);
        recursivelyDetermineBestAction(state);
    }

    // Actions player can take:
    // 1. Send workers to a node that neighbors a node we own.
    // 2. Construct a building in a node we own.
    // 3. Upgrade a building in a node we own.
    // 4. Attack a node that neighbors a node we own.
    // 5. Send workers to a node that neighbors a node we own to better defend it.
    float recursivelyDetermineBestAction(AI_TownState state, int curDepth = 0)
    {
        float bestValue = 0;
        foreach (var node in state.Nodes)
        {
            if (node.OwnedBy != player)
                continue; // only process nodes we own

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
        state.SendWorkersToAttackNode(sourceNode, targetNode, numToSend, out bool nodeOwnerChanged, out int originalDestNodeNumWorkers, out PlayerData originalOwner);
        var value = recursivelyDetermineBestAction(state, curDepth + 1);
        state.Undo_SendWorkersToAttackNode(sourceNode, targetNode, numToSend, nodeOwnerChanged, originalDestNodeNumWorkers, originalOwner);
        return value;
    }

    private float SendWorkersToEmptyNode(AI_NodeState sourceNode, AI_NodeState targetNode, float percentToSend, AI_TownState state, int curDepth)
    {
        int numToSend = Math.Max(0, Math.Min(sourceNode.NumWorkers - 1, (int)(sourceNode.NumWorkers * percentToSend)));
        state.SendWorkersToEmptyNode(sourceNode, targetNode, numToSend);
        var value = recursivelyDetermineBestAction(state, curDepth + 1);
        state.Undo_SendWorkersToEmptyNode(sourceNode, targetNode, numToSend);
        return value;
    }

    private float SendWorkersToOwnedNode(AI_NodeState sourceNode, AI_NodeState targetNode, float percentToSend, AI_TownState state, int curDepth)
    {
        int numToSend = Math.Max(0, Math.Min(sourceNode.NumWorkers - 1, (int)(sourceNode.NumWorkers * percentToSend)));
        state.SendWorkersToOwnedNode(sourceNode, targetNode, numToSend);
        var value = recursivelyDetermineBestAction(state, curDepth + 1);
        state.Undo_SendWorkersToOwnedNode(sourceNode, targetNode, numToSend);
        return value;
    }
}
