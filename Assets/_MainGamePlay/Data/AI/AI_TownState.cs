using System;
using System.Collections.Generic;
using UnityEngine;

public partial class AI_TownState
{
    public AI_NodeState[] Nodes;
    public int NumNodes;
    PlayerData player;

    int PlayerTownInventory_Wood;
    int PlayerTownInventory_Stone;
    int PlayerTownInventory_StoneWoodPlank;

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
        for (int i = 0; i < NumNodes; i++)
        {
            Nodes[i].NumNeighbors = Nodes[i].NeighborNodes.Count;
            Nodes[i].UpdateDistanceToResource();
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
    }

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
            resource1 = GoodType.Unset;
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
            resource2 = GoodType.Unset;
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
    }

    internal void AttackFromNode(AI_NodeState fromNode, AI_NodeState toNode, out AttackResult attackResult, out int origNumInSourceNode, out int origNumInDestNode, out int numSent, out PlayerData origToNodeOwner)
    {
        origNumInSourceNode = fromNode.NumWorkers;
        origNumInDestNode = toNode.NumWorkers;
        origToNodeOwner = toNode.OwnedBy;

        // For now, assume 1:1 attack.  In the future support e.g. stronger attackers, defensive bonus, etc.
        numSent = Math.Max(1, (int)(fromNode.NumWorkers * .5f));
        fromNode.NumWorkers -= numSent;
        toNode.NumWorkers -= numSent;

        if (toNode.NumWorkers == 0)
        {
            // attackers and defenders both died
            toNode.OwnedBy = null;
            attackResult = AttackResult.BothSidesDied;
        }
        else if (toNode.NumWorkers < 0)
        {
            // we captured the node
            toNode.OwnedBy = player;
            toNode.NumWorkers = -toNode.NumWorkers;
            attackResult = AttackResult.AttackerWon;
        }
        else
        {
            attackResult = AttackResult.DefenderWon;
        }
    }

    internal void Undo_AttackFromNode(AI_NodeState fromNode, AI_NodeState toNode, AttackResult attackResult, int origNumInSourceNode, int origNumInDestNode, int numSent, PlayerData origToNodeOwner)
    {
        fromNode.NumWorkers = origNumInSourceNode;
        toNode.OwnedBy = origToNodeOwner;
        toNode.NumWorkers = origNumInDestNode;
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
        {
            var req = craftingReqs[i];
            switch (req.Good.GoodType)
            {
                case GoodType.Wood: if (PlayerTownInventory_Wood < req.Amount) return false; break;
                case GoodType.Stone: if (PlayerTownInventory_Stone < req.Amount) return false; break;
                case GoodType.StoneWoodPlank: if (PlayerTownInventory_StoneWoodPlank < req.Amount) return false; break;
            }
        }
        return true;
    }
}
