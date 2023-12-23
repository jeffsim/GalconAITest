using System;
using System.Collections.Generic;

public class AIPlayer
{
    internal string GetHash()
    {
        var hash = "";
        foreach (var item in ItemsNeeded.Values)
            if (item < 10)
                hash += item + ",";
            else
                hash += Math.Min(10, (int)(item / 10)) + ",";
        foreach (var item in ItemsOwned.Values)
            if (item < 10)
                hash += item + ",";
            else
                hash += Math.Min(10, (int)(item / 10)) + ",";
        return hash;
    }

    public SmallItemCountDictionary ItemsNeeded = new SmallItemCountDictionary();
    public SmallItemCountDictionary ItemsOwned = new SmallItemCountDictionary();
    public int Id;
    public List<BuildingDefn> BuildingsCanConstruct = new List<BuildingDefn>(30);

    public float WorkerMovementSpeed;
    AIGameData GameData;

    public int NumItemsOwned(ItemType itemType) => ItemsOwned[itemType];
    public int NumItemsNeeded(ItemType itemType) => ItemsNeeded[itemType];

    RaceDefn RaceDefn;
    public bool IsAggressive => RaceDefn.AttacksEnemies;

    #region Pooling
    static Queue<AIPlayer> _pool = new Queue<AIPlayer>(Settings.PoolSizes);
    static public void WarmUpPool()
    {
        for (int i = 0; i < Settings.PoolSizes; i++)
            _pool.Enqueue(new AIPlayer());
    }

    static AIPlayer Get() => _pool.Count > 0 ? _pool.Dequeue() : new AIPlayer();
    static public AIPlayer Get(AIGameData gameData, PlayerData player) => Get().OnetimeInitialize(gameData, player);
    // static public AIPlayer Get(AIPlayer sourkcePlayer) => Get().Initialize(sourcePlayer);

    public void ReturnToPool() => _pool.Enqueue(this);
    static public void ResetPool() => _pool.Clear();
    #endregion

    private AIPlayer()
    {
    }
    
    // This should only happen during Town create/load
    public AIPlayer OnetimeInitialize(AIGameData gameData, PlayerData player)
    {
        GameData = gameData;
        Id = player.Id;
        RaceDefn = player.Race;

        return this;
    }

    public AIPlayer CopyFrom(PlayerData player, TownData townData)
    {
        foreach (var need in player.AllUnmetNeeds)
            ItemsNeeded[need.ItemType] = need.NumNeeded;

        WorkerMovementSpeed = WorkerData.movementSpeed; // TODO: read from playerDefn; some workers may be faster than others

        foreach (var node in townData.Nodes)
            if (node.OwnedBy != null && node.OwnedBy.Id == Id && node.NodeState == NodeState.ConstructionCompleted)
                foreach (var itemDefnId in node.ItemsInNode.Keys)
                    ItemsOwned[itemDefnId] += node.ItemsInNode[itemDefnId];

        return this;
    }

    /// <summary>
    /// Initializes the AIPlayer from the specificed source AIPlayer.  This is called many times during an AI evaluation
    /// </summary>
    public AIPlayer CopyFrom(AIPlayer sourceData)
    {
        ItemsNeeded.CopyFrom(sourceData.ItemsNeeded);
        ItemsOwned.CopyFrom(sourceData.ItemsOwned);

        // Following don't change
        // Id = sourceData.Id;
        // RaceDefn = sourceData.RaceDefn;
        // WorkerMovementSpeed = sourceData.WorkerMovementSpeed;

        return this;
    }

    public void AddItem(ItemType itemType, int numToAdd = 1)
    {
        ItemsOwned[itemType] += numToAdd;
    }

    public bool HaveMatsToCraftItem(ItemDefn item)
    {
        bool hasAllMats = true;
        foreach (var mat in item.ItemsNeededToCraftItem)
            hasAllMats &= NumItemsOwned(mat.Item.ItemType) >= mat.Count;
        return hasAllMats;
    }
}