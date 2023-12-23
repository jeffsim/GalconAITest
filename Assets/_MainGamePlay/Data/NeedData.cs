using System;
using System.Collections.Generic;
using UnityEngine;

public class NeedData
{
    public string ItemDefnId;
    public ItemType ItemType;

    // This is the absolute number of the item needed; e.g. doesn't reduce as items arrive in the node
    public int NumNeeded;

    // If 10 items are needed, and 3 are already in the node and 2 are in transit to the node, then this == 5
    public int NumNotBeingServed
    {
        get
        {
            return NumNeeded - (NodeWithNeed.NumItemInNode(ItemType) + ItemsInTransportToNodeWithNeed.Count);
        }
    }

    public float Priority;
    public bool IsBeingMet => NumNotBeingServed == 0;
    [SerializeReference] public NodeData NodeWithNeed;
    [SerializeReference] public List<ItemInTransportData> ItemsInTransportToNodeWithNeed = new List<ItemInTransportData>();

    public NeedData(NodeData node, ItemDefn neededItem, int numNeeded, float priority)
    {
        NodeWithNeed = node;
        ItemDefnId = neededItem.Id;
        ItemType = neededItem.ItemType;
        NumNeeded = numNeeded;
        Priority = priority;
    }
}