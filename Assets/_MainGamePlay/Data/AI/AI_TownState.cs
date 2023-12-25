using System;

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
            out int originalSourceNodeNumWorkers, out int originalDestNodeNumWorkers, out PlayerData originalOwner)
    {
        // store all values we need to undo the action (anything we change below)
        originalSourceNodeNumWorkers = sourceNode.NumWorkers;
        originalDestNodeNumWorkers = destNode.NumWorkers;
        originalOwner = destNode.OwnedBy;

        // For now, assume 1:1 attack.  In the future support e.g. stronger attackers, defensive bonus, etc.
        sourceNode.NumWorkers -= numToSend;
        destNode.NumWorkers -= numToSend;

        if (destNode.NumWorkers == 0)
        {
            // attackers and defenders both died
            destNode.OwnedBy = null;
        }
        else if (destNode.NumWorkers < 0)
        {
            // we captured the node
            destNode.OwnedBy = player;

            destNode.NumWorkers = -destNode.NumWorkers;
        }
    }

    internal void Undo_SendWorkersToAttackNode(AI_NodeState sourceNode, AI_NodeState destNode,
                                               int originalSourceNodeNumWorkers, int originalDestNodeNumWorkers, PlayerData originalOwner)
    {
        sourceNode.NumWorkers = originalSourceNodeNumWorkers;
        destNode.NumWorkers = originalDestNodeNumWorkers;
        destNode.OwnedBy = originalOwner;
    }

    internal void BuildBuilding(AI_NodeState node, BuildingDefn buildingDefn)
    {
        node.Building.buildingDefn = buildingDefn;
    }

    internal void Undo_BuildBuilding(AI_NodeState node)
    {
        node.Building.buildingDefn = null;
    }

    internal void ConsumeResources(BuildingDefn buildingDefn, AI_NodeState node)
    {
    }
}
