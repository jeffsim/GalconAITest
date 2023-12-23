using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ProducableGoodNeededResource
{
    public ItemDefn Item;
    public int Count;
}

public enum GoodType { ImplicitGood, ExplicitGood }

public enum ItemType
{
    Unset, GoldCoin, GoldOre, Potato, Stone, StoneBlock, StoneWoodPlank, Wood, WoodPlank,

    // Test item types
    testWood, testWoodPlank, testStone, testStoneWoodPlank, testGoldOre
};

[CreateAssetMenu(fileName = "ItemDefn")]
public class ItemDefn : BaseDefn
{
    public string FriendlyName;
    public ItemType ItemType;
    public GameObject Visual;
    public GoodType GoodType = GoodType.ExplicitGood;

    public List<ProducableGoodNeededResource> ItemsNeededToCraftItem;

    // name of the tool to show when gathering/crafting this item; e.g. if this item = wood, tool = "axe"
    public string ToolToShow;
}
