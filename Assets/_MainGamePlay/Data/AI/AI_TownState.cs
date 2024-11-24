using System;
using System.Collections.Generic;

public partial class AI_TownState
{
    public AI_NodeState[] Nodes;
    public int NumNodes;
    public PlayerData player;

    public Dictionary<GoodType, int> PlayerTownInventory = new();

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
                if (!Nodes[i].NeighborNodes.Contains(endNode))
                    Nodes[i].NeighborNodes.Add(endNode);
                if (nodeConn.IsBidirectional)
                {
                    if (!endNode.NeighborNodes.Contains(Nodes[i]))
                        endNode.NeighborNodes.Add(Nodes[i]);
                }
            }
        }
        for (int i = 0; i < NumNodes; i++)
        {
            Nodes[i].NumNeighbors = Nodes[i].NeighborNodes.Count;
            Nodes[i].SetDistanceToResources();
        }
    }

    internal void UpdateState(TownData townData)
    {
        // update things that change in the 'real' game; e.g. the list of Nodes in the town doesn't change, but the items in the nodes' inventories do

        // Accessing scriptableobjects is slower than shit.  create 'cached versions'

        // Initialize inventory. start with 0 for all item types o ensure keys exist
        foreach (var key in GameDefns.Instance.GoodDefns.Values)
            PlayerTownInventory[key.GoodType] = 0;

        for (int i = 0; i < NumNodes; i++)
        {
            var node = townData.Nodes[i];
            if (node.OwnedBy == player)
            {
                foreach (var invItem in node.Inventory)
                    PlayerTownInventory[invItem.Key] += invItem.Value;
            }
        }

        for (int i = 0; i < NumNodes; i++)
            Nodes[i].Update();
    }

    internal int GetNumItem(GoodDefn good) => PlayerTownInventory[good.GoodType];

    internal void SendWorkersToOwnedNode(AI_NodeState sourceNode, AI_NodeState destNode, float percentToSend, out int numSent)
    {
        numSent = Math.Max(1, (int)(sourceNode.NumWorkers * percentToSend));
        sourceNode.NumWorkers -= numSent;
        destNode.NumWorkers += numSent;
        NodeOwnershipOrWorkersChanged = true;
    }

    internal void Undo_SendWorkersToOwnedNode(AI_NodeState sourceNode, AI_NodeState destNode, int numSent)
    {
        sourceNode.NumWorkers += numSent;
        destNode.NumWorkers -= numSent;
    }

    internal void SendWorkersToConstructBuildingInEmptyNode(AI_NodeState sendFromNode, AI_NodeState buildInNode, BuildingDefn buildingDefn, int turnNumber, out GoodType resource1, out int resource1Amount, out GoodType resource2, out int resource2Amount, float percentToSend, out int numSent)
    {
        numSent = Math.Max(1, (int)(sendFromNode.NumWorkers * percentToSend));
        sendFromNode.NumWorkers -= numSent;
        buildInNode.NumWorkers += numSent;
        buildInNode.OwnedBy = player;

        // Debug.Assert(buildingDefn.CanBeBuiltByPlayer, "Error: building buildable building");
        // Debug.Assert(!node.HasBuilding, "can only build in empty nodes.");
        buildInNode.SetBuilding(buildingDefn, turnNumber);

        // Consume resources
        var reqs = buildingDefn.ConstructionRequirements;
        // Debug.Assert(reqs.Count <= 2, "only support buildings with 1 or 2 construction requirements for now.");

        // == resource 1
        if (reqs.Count > 0)
        {
            resource1 = reqs[0].Good.GoodType;
            resource1Amount = reqs[0].Amount;

            // TODO: Need to consume from particular nodes, not just the town inventory
            PlayerTownInventory[resource1] -= resource1Amount;
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
            PlayerTownInventory[resource2] -= resource2Amount;
        }
        else
        {
            resource2 = GoodType.Unset;
            resource2Amount = 0;
        }
        NodeOwnershipOrWorkersChanged = true;
    }

    internal void Undo_SendWorkersToConstructBuildingInEmptyNode(AI_NodeState sendFromNode, AI_NodeState buildInNode, GoodType resource1, int resource1Amount, GoodType resource2, int resource2Amount, int numSent)
    {
        buildInNode.NumWorkers -= numSent;
        sendFromNode.NumWorkers += numSent;
        buildInNode.OwnedBy = null;
        buildInNode.ClearBuilding();

        // Undo Consume resources
        if (resource1 != GoodType.Unset) PlayerTownInventory[resource1] += resource1Amount;
        if (resource2 != GoodType.Unset) PlayerTownInventory[resource2] += resource2Amount;
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

        // if (toNode.NumWorkers == 0)
        // {
        //     // attackers and defenders both died
        //     toNode.OwnedBy = null;
        //     attackResult = AttackResult.BothSidesDied;
        // }
        // else 
        if (toNode.NumWorkers <= 0)
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

        NodeOwnershipOrWorkersChanged = true;
    }

    internal void Undo_AttackFromNode(AI_NodeState fromNode, AI_NodeState toNode, AttackResult attackResult, int origNumInSourceNode, int origNumInDestNode, int numSent, PlayerData origToNodeOwner)
    {
        fromNode.NumWorkers = origNumInSourceNode;
        toNode.OwnedBy = origToNodeOwner;
        toNode.NumWorkers = origNumInDestNode;
    }

    internal void UpgradeBuilding(AI_NodeState node, out int origLevel, out int origNumWorkers)
    {
        origLevel = node.BuildingLevel;
        origNumWorkers = node.NumWorkers;
        node.BuildingLevel++;
        node.NumWorkers /= 2;
    }

    internal void Undo_UpgradeBuilding(AI_NodeState node, int origLevel, int origNumWorkers)
    {
        node.BuildingLevel = origLevel;
        node.NumWorkers = origNumWorkers;
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
            if (PlayerTownInventory[req.Good.GoodType] < req.Amount)
                return false;
        }
        return true;
    }

    public Dictionary<GoodType, int> PlayerInventory = new Dictionary<GoodType, int>();

    internal int GetNumGood(GoodType good) => PlayerInventory.ContainsKey(good) ? PlayerInventory[good] : 0;

    internal bool HasSufficientGoods(List<Good_CraftingRequirements> requirements)
    {
        for (int i = 0; i < requirements.Count; i++)
        {
            var req = requirements[i];
            if (GetNumGood(req.Good.GoodType) < req.Amount)
                return false;
        }
        return true;
    }

    internal void ConsumeGoods(List<Good_CraftingRequirements> requirements)
    {
        for (int i = 0; i < requirements.Count; i++)
        {
            var req = requirements[i];
            PlayerInventory[req.Good.GoodType] -= req.Amount;
        }
    }

    internal void ProduceGood(GoodType good, int amount)
    {
        if (!PlayerInventory.ContainsKey(good))
            PlayerInventory[good] = 0;
        PlayerInventory[good] += amount;
    }
}
