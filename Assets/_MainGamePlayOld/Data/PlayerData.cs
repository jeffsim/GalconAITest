using System;
using System.Collections.Generic;
using UnityEngine;

public enum PlayerType { Human, AI };
public enum Race { Human, Watcher, Orc, Elf, Dwarf, Undead };

public class PlayerData 
{
    private RaceDefn _race;
    public RaceDefn Race => _race == null ? _race = GameDefns.Instance.RaceDefns[RaceDefnId] : _race;
    public string RaceDefnId;

    [SerializeReference] public TownData Town;
    public int Id;
    public PlayerType Type => Race.IsComputerAI ? PlayerType.AI : PlayerType.Human;
    public Color Color => Race.Color;
    public bool IsAI => Race.IsComputerAI;
    public bool Hates(PlayerData player) => AffinityTo(player) == Affinity.Hates;
    public bool WantsPlayersNodes(PlayerData player) => AffinityTo(player) == Affinity.DoesntHateButWantsTheirNodes;
    public bool Likes(PlayerData player) => AffinityTo(player) == Affinity.Likes;

    public List<int> HatredOverrides = new List<int>();

    [SerializeReference] public List<NeedData> AllUnmetNeeds = new List<NeedData>();


    public float GetItemNeedPriority(ItemDefn item)
    {
        float priority = 0;
        foreach (var need in AllUnmetNeeds)
            if (need.ItemDefnId == item.Id)
                priority += need.Priority;
        return priority;
    }

    public void UpdateNeeds()
    {
        // Generate list of all unmet needs, sorted by highest priority first
        AllUnmetNeeds.Clear();
        foreach (var node in Town.Nodes)
            if (node.OwnedBy == this)
                foreach (var need in node.NeededItemsDict.Values)
                    if (!need.IsBeingMet)
                    {
                        var time = .016f; // GameTime.deltaTime
                        need.Priority += 0.1f * time; // all needs steadily grow until met
                        AllUnmetNeeds.Add(need);
                    }

        AllUnmetNeeds.Sort((a, b) => (int)((b.Priority - a.Priority) * 1000));

        foreach (var need in AllUnmetNeeds)
        {
            // Find all nodes with the specified item and from which the needing node can pull
            // TODO (PERF): create Town.AllItems and then first check if any of the item exist; if not, continue on with next need.
            var nodesWithNeededItem = new List<NodeData>();
            var neededItem = need.ItemType;
            foreach (var node in Town.Nodes)
            {
                if (node.NumItemInNode(neededItem) == 0) continue; // if node doesn't have item, then can't pull from it
                if (node.NeedsItem(neededItem)) continue;  // don't pull from nodes that need the item themselves
                if (!Town.PathBetweenNodesExists(node, need.NodeWithNeed)) continue; // can't pull from node if can't get to it

                // Finally, check ownership.  Ownership scenarios:
                //  source owner    Dest owner   Dest.PlayerPreparingSite    Result
                //  Player1         Player1      n/a                         Yes
                //  Player1         Player2      n/a                         No
                //  Player1         Null         Player1                     Yes            // ex: going to node that is being constructed
                //  Player1         Null         Player2                     No
                //  Null            Player1      n/a                     Yes            // ex: coming from gold mine
                //  Null            Player1      n/a2                     No
                var sourceOwner = node.OwnedBy;
                var destOwner = need.NodeWithNeed.OwnedBy;
                if (sourceOwner == destOwner || sourceOwner == need.NodeWithNeed.PlayerPreparingSite)
                    nodesWithNeededItem.Add(node);
            }
            if (nodesWithNeededItem.Count == 0) continue; // no node has the item

            nodesWithNeededItem.Sort((a, b) => Town.DistanceBetweenNodes(a, need.NodeWithNeed) - Town.DistanceBetweenNodes(b, need.NodeWithNeed));

            // Note that the need could be for > 1 of the item; some could be served from closest node, others from next-closest, and so on
            int loopCheck = 0;
            while (need.NumNotBeingServed > 0 && nodesWithNeededItem.Count > 0)
            {
                if (loopCheck++ > 10000)
                {
                    Debug.Log("stuck in need loop");
                    break;
                }
                // There's still at least one item needed AND at least one node with the item

                // Take from the closest (by path distance) node with item
                var closestNodeWithNeededItem = nodesWithNeededItem[0];

                var numOfItemInNode = closestNodeWithNeededItem.NumItemInNode(need.ItemType);
                var numOfItemToTransportFromNode = Math.Min(need.NumNotBeingServed, numOfItemInNode);

                for (int i = 0; i < numOfItemToTransportFromNode; i++)
                {
                    // Assign the item to the need, and create an ItemInTransport to transport it
                    Town.TransportItemFromNodeToNode(need, closestNodeWithNeededItem, need.NodeWithNeed);
                }
                // We've exhausted our ability to pull items from the closestNode; remove it and we'll look at the next-closest node if we still need more
                nodesWithNeededItem.Remove(closestNodeWithNeededItem);
            }
        }
    }

    public void AttackedByPlayer(PlayerData attacker)
    {
        if (Race.NeutralUntilAttacked)
            if (!HatredOverrides.Contains(attacker.Id))
                HatredOverrides.Add(attacker.Id);
    }

    public Affinity AffinityTo(PlayerData player)
    {
        if (player == null)
            return Affinity.Neutral;

        if (HatredOverrides.Contains(player.Id))
            return Affinity.Hates;

        if (Race.Affinities.ContainsKey(player.Race))
            return Race.Affinities[player.Race];
        return Affinity.Neutral;
    }
}
