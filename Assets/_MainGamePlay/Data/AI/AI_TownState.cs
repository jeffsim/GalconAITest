using System;
using System.Collections.Generic;
using UnityEngine;

class AI_TownState
{
    public AI_NodeState[] Nodes;
    public int NumNodes;
    PlayerData player;

    // Dictionary<GoodDefn, int> PlayerTownInventory = new(100);
    int PlayerTownInventory_Wood;
    int PlayerTownInventory_Stone;
    int PlayerTownInventory_StoneWoodPlank;

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
        NumNodes = townData.Nodes.Count;
        Nodes = new AI_NodeState[NumNodes];
        for (int i = 0; i < NumNodes; i++)
            Nodes[i] = new AI_NodeState(townData.Nodes[i]);
        for (int i = 0; i < NumNodes; i++)
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

        // Accessing scriptableobjects is slower than shit.  create 'cached versions'

        // Initialize inventory
        for (int i = 0; i < NumNodes; i++)
        {
            var node = townData.Nodes[i];
            foreach (var invItem in node.Inventory)
            {
                // PlayerTownInventory[invItem.Key] = invItem.Value;
                if (invItem.Key.GoodType == GoodType.Wood) PlayerTownInventory_Wood = invItem.Value;
                else if (invItem.Key.GoodType == GoodType.Stone) PlayerTownInventory_Stone = invItem.Value;
                else if (invItem.Key.GoodType == GoodType.StoneWoodPlank) PlayerTownInventory_StoneWoodPlank = invItem.Value;
            }
        }

        for (int i = 0; i < NumNodes; i++)
            Nodes[i].Update();

        HaveSentWorkersToNode.Clear();
        HaveSentWorkersFromNode.Clear();
    }

    internal float EvaluateScore()
    {
        // TODO: Add weights based on AI's personality
        float score = 0;

        for (int i = 0; i < NumNodes; i++)
        {
            var node = Nodes[i];
            if (node.OwnedBy == player)
            {
                // Add score for each node we own
                // TODO: subtract score for each node owned by another player
                score += 1;

                // Add score for each building in a node we own that is "useful"
                if (!node.HasBuilding)
                    score += .1f; // some score for owning empty nodes.  Base this on AI personality's "desire to expand"
                else
                {
                    // Resource gathering buildings are useful if they can reach a resource node.
                    // These buildings are more useful the close to the resource node they are.
                    if (node.CanGatherResources)
                    {
                        // Dictionaries are slow, and this is the innermost loop, so...
                        //  int dist = node.DistanceToClosestResourceNode[node.GatherableResourceDefnId];
                        int dist = int.MaxValue;
                        switch (node.GatherableResourceType)
                        {
                            case GoodType.Wood: dist = node.DistanceToClosestResourceNode_Wood; break;
                            case GoodType.Stone: dist = node.DistanceToClosestResourceNode_Stone; break;
                            case GoodType.StoneWoodPlank: dist = node.DistanceToClosestResourceNode_StoneWoodPlank; break;
                        }
                        if (dist == 1)
                            score += 1.5f;
                    }

                    // Defensive buildings are useful if...
                    // Storage buildings are useful if...
                    // Crafting buildings are useful if...
                }
            }
        }

        return score;
    }

    // private int DistanceToResourceNode(AI_NodeState node, GoodDefn gatherableResource)
    // {
    //     int curDist = 0;
    //     // For now, only look at neighboring nodes.  Need to recurse out.  PriorityQueue/super-simple A*
    //     foreach (var neighbor in node.NeighborNodes)
    //         if (neighbor.HasBuilding && neighbor.Building.CanBeGatheredFrom && neighbor.Building.GatheredResource == gatherableResource)
    //             curDist = 1;
    //     return curDist;
    // }

    internal int GetNumItem(GoodDefn good)
    {

        if (good.GoodType == GoodType.Wood) return PlayerTownInventory_Wood;
        else if (good.GoodType == GoodType.Stone) return PlayerTownInventory_Stone;
        else if (good.GoodType == GoodType.StoneWoodPlank) return PlayerTownInventory_StoneWoodPlank;
        Debug.Assert(false, "unhandled good type " + good.Id);
        // if (PlayerTownInventory.TryGetValue(good, out int num))
        // return num;
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

    internal void BuildBuilding(AI_NodeState node, BuildingDefn buildingDefn, out GoodType resource1, out int resource1Amount, out GoodType resource2, out int resource2Amount)
    {
        // Debug.Assert(buildingDefn.CanBeBuiltByPlayer, "Error: building buildable building");
        // Debug.Assert(!node.HasBuilding, "can only build in empty nodes.");
        node.SetBuilding(buildingDefn);

        // Consume resources
        var reqs = buildingDefn.ConstructionRequirements;
        // Debug.Assert(reqs.Count <= 2, "only support buildings with 1 or 2 construction requirements for now.");

        // == resource 1
        if (reqs.Count > 0)
        {
            resource1 = reqs[0].Good.GoodType;
            resource1Amount = reqs[0].Amount;

            // TODO: Need to consume from particular nodes, not just the town inventory
            // PlayerTownInventory[resource1] -= resource1Amount;
            if (resource1 == GoodType.Wood) PlayerTownInventory_Wood -= resource1Amount;
            else if (resource1 == GoodType.Stone) PlayerTownInventory_Stone -= resource1Amount;
            else if (resource1 == GoodType.StoneWoodPlank) PlayerTownInventory_StoneWoodPlank -= resource1Amount;
        }
        else
        {
            resource1 = GoodType.None;
            resource1Amount = 0;
        }

        // == resource 2
        if (reqs.Count > 1)
        {
            resource2 = reqs[1].Good.GoodType;
            resource2Amount = reqs[1].Amount;

            // TODO: Need to consume from particular nodes, not just the town inventory
            // PlayerTownInventory[resource2] -= resource2Amount;
            if (resource2 == GoodType.Wood) PlayerTownInventory_Wood -= resource2Amount;
            else if (resource2 == GoodType.Stone) PlayerTownInventory_Stone -= resource2Amount;
            else if (resource2 == GoodType.StoneWoodPlank) PlayerTownInventory_StoneWoodPlank -= resource2Amount;
        }
        else
        {
            resource2 = GoodType.None;
            resource2Amount = 0;
        }
    }

    internal void Undo_BuildBuilding(AI_NodeState node, GoodType resource1, int resource1Amount, GoodType resource2, int resource2Amount)
    {
        // Undo build building in empty node
        node.ClearBuilding();

        // Undo Consume resources
        if (resource1 == GoodType.Wood) PlayerTownInventory_Wood = resource1Amount;
        else if (resource1 == GoodType.Stone) PlayerTownInventory_Stone = resource1Amount;
        else if (resource1 == GoodType.StoneWoodPlank) PlayerTownInventory_StoneWoodPlank = resource1Amount;

        if (resource2 == GoodType.Wood) PlayerTownInventory_Wood = resource2Amount;
        else if (resource2 == GoodType.Stone) PlayerTownInventory_Stone = resource2Amount;
        else if (resource2 == GoodType.StoneWoodPlank) PlayerTownInventory_StoneWoodPlank = resource2Amount;
        // if (resource1 != null)
        //     PlayerTownInventory[resource1] = resource1Amount;
        // if (resource2 != null)
        //     PlayerTownInventory[resource2] = resource2Amount;
    }

    internal bool IsGameOver()
    {
        // game is over if we own all nodes or we own no nodes
        // todo: add other 'game over' conditions (e.g. complete quest, etc)
        int numNodesOwned = 0;
        for (int i = 0; i < NumNodes; i++)
            if (Nodes[i].OwnedBy == player)
                numNodesOwned++;
        return numNodesOwned == 0 || numNodesOwned == Nodes.Length;
    }

    internal bool ConstructionResourcesCanBeReachedFromNode(AI_NodeState node, List<Good_CraftingRequirements> craftingReqs)
    {
        var NumReqs = craftingReqs.Count;
        for (int i = 0; i < NumReqs; i++)
            if (!resourcesCanBeReachedFromNode(node, craftingReqs[i]))
                return false;
        return true;
    }

    private bool resourcesCanBeReachedFromNode(AI_NodeState node, Good_CraftingRequirements req)
    {
        // TODO: For now just see if resources are owned by player anywhere in Town; this needs to be updated
        // to only consider nodes that can be traversed to from 'node'

        // Dictionaries = slow.  hardcoding resources
        // return PlayerTownInventory.ContainsKey(req.Good) && PlayerTownInventory[req.Good] >= req.Amount;
        if (req.Good.GoodType == GoodType.Wood) return PlayerTownInventory_Wood >= req.Amount;
        else if (req.Good.GoodType == GoodType.Stone) return PlayerTownInventory_Stone >= req.Amount;
        else if (req.Good.GoodType == GoodType.StoneWoodPlank) return PlayerTownInventory_StoneWoodPlank >= req.Amount;
        return false;
    }
}
