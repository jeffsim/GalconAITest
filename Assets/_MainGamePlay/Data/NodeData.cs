using System;
using System.Collections.Generic;
using UnityEngine;

public enum NodeState { IdleEmpty, PreparingSite, WaitingForConstructionMaterials, UnderConstruction, ConstructionCompleted };

public class WorkerData
{
    static public float movementSpeed = 10f;

    internal void Destroy()
    {
        throw new NotImplementedException();
    }
}

public class ItemTypeNeedDictionary : SerializedDictionary2<ItemType, NeedData> { }
public class IntStringDictionary : SerializedDictionary2<int, string> { }

public class NodeData
{
    // // ===== Defn =====
    private NodeDefn _defn;
    public NodeDefn Defn => _defn == null ? _defn = GameDefns.Instance.NodeDefns[DefnId] : _defn;
    public string DefnId;

    public int Id;
    public Vector3 WorldLoc;

    public PlayerData OwnedBy;
    public bool IsOwned => OwnedBy != null;

    public BuildingData BuildingInNode;
    public bool HasBuilding => BuildingInNode != null;

    // ===== Node State =====
    public NodeState NodeState = NodeState.IdleEmpty;

    // == Level
    public bool CanBeUpgraded => NumWorkers >= MaxWorkers;
    public int Level = 1;

    // ===== Inventory =====
    public Dictionary<ItemType, int> ItemsInNode = new Dictionary<ItemType, int>();

    // ===== Town and Nodes =====
    public TownData Town;
    public List<NodeData> ConnectedNodes = new List<NodeData>();
    public List<NodeConnectionData> Connections = new List<NodeConnectionData>();

    // // ===== Workers =====
    public List<WorkerData> Workers = new List<WorkerData>(); // workers that are assigned to us
    public int NumWorkers => Workers.Count;
    public int MaxWorkers = 10; // NOTE: if I change this then grep on MaxWorkers and change elsewhere too (e.g. in destroybuilding)

    public PlayerData PlayerPreparingSite;
    public bool HasBuildingUnderConstruction => HasBuilding && NodeState == NodeState.UnderConstruction;
    public IntStringDictionary PendingConstructionByPlayer = new IntStringDictionary();

    // TODO: Make these variable based on stats (eg research faster crafting) and item (e.g. wood gathers faster than stone)
    static public float craftSpeed = .025f; // time to craft one resource, in seconds
    static public float TimeToBleedWorker = 1.5f;
    static public float TimeToGainWorker = 1.5f; // in seconds.  TODO: upgradable etc

    // time to gather one resource, in seconds
    // By default, one worker gathers 1 resource every 30 seconds.  5 workers would gather 1 resource every secondsPerGather/5 = 6 seconds
    static public float secondsPerGather = 30;

    // ===== Needs =====
    public ItemTypeNeedDictionary NeededItemsDict = new();

    public NodeData(Town_NodeDefn townNode, TownData town)
    {
        DefnId = townNode.NodeDefn.Id;
        Id = townNode.Id;
        Town = town;
    }

    public void Update()
    {
    }

    // Called when user has said "I want to build [building] in this node"
    public void TrackIntentToConstructBuilding(BuildingDefn buildingDefn, PlayerData builder)
    {
        // Keep track of which building the specified player wants to build in this Node.  Note that multiple players could want
        // to build simultaneously in this node; the first player whose worker arrive begins building.  If another player subsequently
        // arrives then they pause construction and battle.  If the first player wins then they continue battling; if not then the attacking
        // player takes over the node, it is returned to Unset state, and their PendingConstruction building choice is the one that is chosen.
        // PendingConstructionByPlayer[builder.Id] = buildingDefn.Id;
        // LastSetPendingConstruction = buildingDefn.Id;
    }

    public bool HasBuildingOfType(BuildingDefn building)
    {
        return HasBuilding && BuildingInNode.DefnId == building.Id;
    }

    public void AddConnection(NodeData nodeData, Town_NodeConnectionDefn conn, bool isForwardConnection)
    {
        ConnectedNodes.Add(nodeData);
        Connections.Add(new NodeConnectionData(conn, isForwardConnection));
    }

    internal bool NeedsItem(ItemType itemDefnId)
    {
        foreach (var need in NeededItemsDict)
            if (need.Key == itemDefnId && need.Value.NumNeeded > 0)
                return true;
        return false;
    }

    public int NumItemInNode(ItemType item)
    {
        if (!ItemsInNode.ContainsKey(item))
            return 0;
        return ItemsInNode[item];
    }

    public bool HaveAllMatsInNodeToCraftItem(ItemDefn item)
    {
        bool hasAllMats = true;
        foreach (var mat in item.ItemsNeededToCraftItem)
            hasAllMats &= NumItemInNode(mat.Item.ItemType) >= mat.Count;
        return hasAllMats;
    }

    public void AddItem(ItemType item, int count = 1)
    {
        if (!ItemsInNode.ContainsKey(item))
            ItemsInNode[item] = count;
        else
            ItemsInNode[item] += count;
    }

    public void RemoveItem(ItemType item, int count = 1)
    {
        Debug.Assert(ItemsInNode.ContainsKey(item), "Removing Item node hasn't seen before " + item);
        Debug.Assert(ItemsInNode[item] >= count, "Removing too many items from Node.  " + item);
        ItemsInNode[item] -= count;
    }

    public void Upgrade(bool sacrificeWorkers = true)
    {
        // Destroy all workers and increase the number that can be assigned
        Level++;
        if (sacrificeWorkers)
        {
            int numWorkersToSacrifice = Math.Min(NumWorkers, MaxWorkers) / 2;
            for (int i = 0; i < numWorkersToSacrifice; i++)
            {
                var worker = Workers[0];
                worker.Destroy();
                Workers.Remove(worker);
            }
        }
        MaxWorkers += 10;
    }

    internal void DestroyBuilding()
    {
        throw new NotImplementedException();
    }
}