using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

class AI_TownState
{
    public AI_NodeState[] Nodes;

    PlayerData player;

    Dictionary<GoodDefn, int> PlayerTownInventory = new(100);

    public Dictionary<int, bool> HaveSentWorkersToNode = new();
    public Dictionary<int, bool> HaveSentWorkersFromNode = new();

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
        {
            foreach (var invItem in node.Inventory)
                PlayerTownInventory[invItem.Key] = invItem.Value;
        }

        HaveSentWorkersToNode.Clear();
        HaveSentWorkersFromNode.Clear();
    }

    internal float EvaluateScore()
    {
        // TODO: Add weights based on AI's personality
        float score = 0;

        // Add score for each node we own; subtract score for each node owned by another player
        foreach (var node in Nodes)
            if (node.OwnedBy == player) score += 1;
            // else if (node.OwnedBy != null) score -= 1;

        // Add score for each building in a node we own that is "useful"
        foreach (var node in Nodes)
            if (node.OwnedBy == player)
                if (!node.HasBuilding)
                    score += .1f; // some score for owning empty nodes
                else
                {
                    // Resource gathering buildings are useful if they can reach a resource node.
                    // These buildings are more useful the close to the resource node they are.
                    var building = node.Building;
                    if (building.buildingDefn.CanGatherResources)
                    {
                        int dist = DistanceToResourceNode(node, building.buildingDefn.GatherableResource);
                        if (dist == 1)
                            score += 1.5f;
                    }

                    // Defensive buildings are useful if...

                    // Storage buildings are useful if...

                    // Crafting buildings are useful if...
                }

        return score;
    }

    private int DistanceToResourceNode(AI_NodeState node, GoodDefn gatherableResource)
    {
        int curDist = 0;
        // For now, only look at neighboring nodes.  Nee to recurse out.  PriorityQueue/super-simple A*
        foreach (var neighbor in node.NeighborNodes)
            if (neighbor.HasBuilding && neighbor.Building.buildingDefn.CanBeGatheredFrom && neighbor.Building.buildingDefn.GatheredResource == gatherableResource)
                curDist = 1;
        return curDist;
    }

    internal int GetNumItem(GoodDefn good)
    {
        if (PlayerTownInventory.TryGetValue(good, out int num))
            return num;
        return 0;
    }

    internal void SendWorkersToEmptyNode(AI_NodeState sourceNode, AI_NodeState destNode, float percentToSend, out int numSent)
    {
        numSent = Math.Max(1, (int)(sourceNode.NumWorkers * percentToSend));

        // We are capturing a new node; need to update lists (e.g. PlayerOwnedNodes)
        sourceNode.NumWorkers -= numSent;
        destNode.NumWorkers += numSent;
        destNode.OwnedBy = player;

        // HaveSentWorkersFromNode[sourceNode.NodeId] = true;
        // HaveSentWorkersToNode[destNode.NodeId] = true;
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
            PlayerTownInventory[resource1] -= resource1Amount;
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
            PlayerTownInventory[resource2] -= resource2Amount;
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
            PlayerTownInventory[resource1] += resource1Amount;
        if (resource2 != null)
            PlayerTownInventory[resource2] += resource2Amount;
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

    internal bool CraftingResourcesCanBeReachedFromNode(AI_NodeState node, List<Good_CraftingRequirements> craftingReqs)
    {
        foreach (var req in craftingReqs)
            if (!resourcesCanBeReachedFromNode(node, req))
                return false;
        return true;
    }
    internal bool ConstructionResourcesCanBeReachedFromNode(AI_NodeState node, List<Good_CraftingRequirements> craftingReqs)
    {
        foreach (var req in craftingReqs)
            if (!resourcesCanBeReachedFromNode(node, req))
                return false;
        return true;
    }

    private bool resourcesCanBeReachedFromNode(AI_NodeState node, Good_CraftingRequirements req)
    {
        // TODO: For now just see if resources are owned by player anywhere in Town; this needs to be updated
        // to only consider nodes that can be traversed to from 'node'
        return PlayerTownInventory.ContainsKey(req.Good) && PlayerTownInventory[req.Good] >= req.Amount;
    }
}
