using System;
using System.Collections.Generic;
using UnityEngine;

class AI_TownState
{
    public AI_NodeState[] Nodes;

    PlayerData player;

    Dictionary<GoodDefn, int> TownInventory = new(100);

    public AI_TownState(PlayerData player)
    {
        this.player = player;

    }

    // Initialize Town data that never changes; e.g. the list of nodes in the town.  Data that does change (e.g. Node inventories) is updated in UpdateState()
    public void InitializeStaticData(TownData townData)
    {
        // Initialize Node list
        Nodes = new AI_NodeState[townData.Nodes.Count];
        for (int i = 0; i < townData.Nodes.Count; i++)
            Nodes[i] = new AI_NodeState(townData.Nodes[i]);

        for (int i = 0; i < townData.Nodes.Count; i++)
        {
            var nodeConns = townData.Nodes[i].NodeConnections;
            foreach (var nodeConn in nodeConns)
            {
                var endIndex = townData.Nodes.IndexOf(nodeConn.End);
                var endNode = Nodes[endIndex];
                Nodes[i].NeighborNodes.Add(endNode);
                if (nodeConn.IsTwoWay)
                    endNode.NeighborNodes.Add(Nodes[i]);
            }
        }
    }

    internal void UpdateState(TownData townData)
    {
        // update things that change in the 'real' game; e.g. the list of Nodes in the town doesn't change, but the items in the nodes' inventories do

        // Initialize inventory
        foreach (var node in townData.Nodes)
            foreach (var invItem in node.Inventory)
                TownInventory[invItem.Key] = invItem.Value;
    }

    internal float EvaluateScore()
    {
        // TODO: Add weights based on AI's personality
        float score = 0;

        // Add score for each node we own; subtract score for each node owned by another player
        foreach (var node in Nodes)
            if (node.OwnedBy == player)
                score += 1;
            else if (node.OwnedBy != null)
                score -= 1;

        // Add score for each building in a node we own
        foreach (var node in Nodes)
            if (node.OwnedBy == player && node.HasBuilding)
                score += 1;

        return score;
    }

    internal int GetNumItem(GoodDefn good)
    {
        if (TownInventory.TryGetValue(good, out int num))
            return num;
        return 0;
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

    internal void BuildBuilding(AI_NodeState node, BuildingDefn buildingDefn, out GoodDefn resource1, out int resource1Amount, out GoodDefn resource2, out int resource2Amount)
    {
        Debug.Assert(buildingDefn.CanBeBuiltByPlayer, "Error: building buildable building");
        Debug.Assert(node.Building.buildingDefn == null, "can only build in empty nodes.");
        node.Building.buildingDefn = buildingDefn;

        // Consume resources
        var reqs = buildingDefn.ConstructionRequirements;
        Debug.Assert(reqs.Count <= 2, "only support buildings with 1 or 2 construction requirements for now.");

        // == resource 1
        if (reqs.Count > 0)
        {
            resource1 = reqs[0].Good;
            resource1Amount = reqs[0].Amount;

            // TODO: Need to consume from particular nodes, not just the town inventory
            TownInventory[resource1] -= resource1Amount;
        }
        else
        {
            resource1 = null;
            resource1Amount = 0;
        }

        // == resource 2
        if (reqs.Count > 1)
        {
            resource2 = reqs[1].Good;
            resource2Amount = reqs[1].Amount;

            // TODO: Need to consume from particular nodes, not just the town inventory
            TownInventory[resource2] -= resource2Amount;
        }
        else
        {
            resource2 = null;
            resource2Amount = 0;
        }
    }

    internal void Undo_BuildBuilding(AI_NodeState node, GoodDefn resource1, int resource1Amount, GoodDefn resource2, int resource2Amount)
    {
        // Undo build building in empty node
        node.Building.buildingDefn = null;

        // Undo Consume resources
        if (resource1 != null)
            TownInventory[resource1] += resource1Amount;
        if (resource2 != null)
            TownInventory[resource2] += resource2Amount;
    }

    internal bool IsGameOver()
    {
        // game is over if we own all nodes or we own no nodes
        // todo: add other 'game over' conditions (e.g. complete quest, etc)
        int numNodesOwned = 0;
        foreach (var node in Nodes)
            if (node.OwnedBy == player)
                numNodesOwned++;
        return numNodesOwned == 0 || numNodesOwned == Nodes.Length;
    }
}
