using System;
using System.Collections.Generic;
using UnityEditor;

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
