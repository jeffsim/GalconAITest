using System;
using System.Collections.Generic;
using UnityEngine;

public enum GoodType
{
    Unset,
    
    // Raw Resources
    Wood, Stone, GoldOre, IronOre, Flour, Water,

    // Crafted Resources
    StoneWoodPlank,
    WoodPlank,
    GoldBar, IronBar,
    Planks, Bricks, Coins, Tools, Weapons,

    // Food
    Fish, Meat, Bread, Beer,
};

[Serializable]
public class Good_CraftingRequirements
{
    public GoodDefn Good;
    public int Amount;
}

[CreateAssetMenu()]
public class GoodDefn : BaseDefn
{
    public string FriendlyName;
    public GoodType GoodType;
    public List<Good_CraftingRequirements> CraftingRequirements = new List<Good_CraftingRequirements>();
    public Sprite Sprite;
}
