using System;
using System.Collections.Generic;

public partial class AI_TownState
{
    public AI_NodeState[] Nodes;
    public int NumNodes;
    public PlayerData player;

    // public int NumWood = 0;
    // public int NumStone = 0;

    public Dictionary<GoodType, int> PlayerTownInventory = new();

    // Goal-driven resource demand vector: how much of each GoodType the player wants on hand
    // to fulfill its currently-active strategic goals. Derived from ActiveGoals once per
    // real-game Update by AI_ActionHeuristics.UpdateResourceDemand. Read by GetBuildHeuristic
    // (favor gatherers for resources we are short on) and GetAttackHeuristic (favor capturing
    // resource sources we need).
    public Dictionary<GoodType, int> ResourceDemand = new Dictionary<GoodType, int>();

    // Strategic goals the player is currently pursuing -- e.g. "capture node #25", "build
    // a Barracks". Rebuilt once per real-game Update by AIGoalEnumerator and frozen for the
    // duration of the recursive search. The resource demand vector is derived from this list.
    public List<AIGoal> ActiveGoals = new List<AIGoal>();

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
                foreach (var invItem in node.Inventory)
                    PlayerTownInventory[invItem.Key] += invItem.Value;
        }

        // In realtime mode, project our own in-flight workers into the AI's view of the world
        // so we don't pile on duplicate sends. Step mode keeps the legacy "snapshot only what's
        // physically there" behavior (Realtime is detected by the scene flag).
        bool realtime = AITestScene.Instance != null && AITestScene.Instance.Realtime;
        var viewer = realtime ? player : null;
        for (int i = 0; i < NumNodes; i++)
            Nodes[i].Update(viewer);
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

    internal void SendWorkersToConstructBuildingInEmptyNode(AI_NodeState sendFromNode, AI_NodeState buildInNode, BuildingDefn buildingDefn, int turnNumber, out GoodType resource1, out int resource1Amount, out GoodType resource2, out int resource2Amount, int numToSend, out int numSent)
    {
        numSent = Math.Min(Math.Max(0, numToSend), sendFromNode.NumWorkers);
        if (numSent <= 0)
        {
            resource1 = GoodType.Unset;
            resource1Amount = 0;
            resource2 = GoodType.Unset;
            resource2Amount = 0;
            return;
        }
        sendFromNode.NumWorkers -= numSent;

        if (buildInNode.OwnedBy == null && buildInNode.NumWorkers > 0)
        {
            if (numSent <= buildInNode.NumWorkers)
            {
                buildInNode.NumWorkers -= numSent;
                sendFromNode.NumWorkers += numSent;
                numSent = 0;
                resource1 = GoodType.Unset;
                resource1Amount = 0;
                resource2 = GoodType.Unset;
                resource2Amount = 0;
                return;
            }
            buildInNode.NumWorkers = numSent - buildInNode.NumWorkers;
        }
        else
        {
            buildInNode.NumWorkers += numSent;
        }
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

    internal void Undo_SendWorkersToConstructBuildingInEmptyNode(AI_NodeState sendFromNode, AI_NodeState buildInNode, GoodType resource1, int resource1Amount, GoodType resource2, int resource2Amount, int origSendFromWorkers, int origBuildInWorkers)
    {
        sendFromNode.NumWorkers = origSendFromWorkers;
        buildInNode.NumWorkers = origBuildInWorkers;
        buildInNode.OwnedBy = null;
        buildInNode.ClearBuilding();

        // Undo Consume resources
        if (resource1 != GoodType.Unset)
        {
            PlayerTownInventory[resource1] += resource1Amount;
        }
        if (resource2 != GoodType.Unset)
        {
            PlayerTownInventory[resource2] += resource2Amount;
        }
    }

    internal void AttackFromNode(AI_NodeState fromNode, AI_NodeState toNode, int numToSend, out AttackResult attackResult, out int origNumInSourceNode, out int origNumInDestNode, out int numSent, out PlayerData origToNodeOwner)
    {
        origNumInSourceNode = fromNode.NumWorkers;
        origNumInDestNode = toNode.NumWorkers;
        origToNodeOwner = toNode.OwnedBy;

        // For now, assume 1:1 attack.  In the future support e.g. stronger attackers, defensive bonus, etc.
        numSent = Math.Min(Math.Max(0, numToSend), fromNode.NumWorkers);
        if (numSent <= 0)
        {
            attackResult = AttackResult.Undefined;
            return;
        }
        fromNode.NumWorkers -= numSent;
        toNode.NumWorkers -= numSent;

        // if (toNode.NumWorkers == 0)
        // {
        //     // attackers and defenders both died
        //     toNode.OwnedBy = null;
        //     attackResult = AttackResult.BothSidesDied;
        // }
        // else 
        // AITask_AttackToNode filters out neutral targets, so origToNodeOwner is always non-null
        // here. The branch is still belt-and-suspenders for direct callers, but we only ever
        // claim enemy nodes -- neutral expansion goes through SendWorkersToConstructBuildingInEmptyNode.
        if (toNode.NumWorkers <= 0 && origToNodeOwner != null)
        {
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

        // NOTE: If update this then need to update elsewhere too.  grep on TODO-042
        node.MaxWorkers = 10 * (int)Math.Pow(2, node.BuildingLevel - 1);
    }

    internal void Undo_UpgradeBuilding(AI_NodeState node, int origLevel, int origNumWorkers)
    {
        node.BuildingLevel = origLevel;
        node.NumWorkers = origNumWorkers;

        // NOTE: If update this then need to update elsewhere too.  grep on TODO-042
        node.MaxWorkers = 10 * (int)Math.Pow(2, node.BuildingLevel - 1);
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
