using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu()]
public class BuildingDefn : BaseDefn
{
    public string Name;
    public Color Color = Color.white;
    public WorkerDefn WorkerDefn;

    // Construction
    [Header("Construction"), Space(10)]
    public bool CanBeBuiltByPlayer = true;
    [ShowIf("CanBeBuiltByPlayer")]
    public List<Good_CraftingRequirements> ConstructionRequirements = new();

    // Gathering
    [Header("Gathering"), Space(10)]
    public bool CanGatherResources = false;
    [ShowIf("CanGatherResources")]
    public GoodDefn GatherableResource;

    // ResourceNode
    [Header("Resource"), Space(10)]
    public bool CanBeGatheredFrom = false;
    [ShowIf("CanBeGatheredFrom")]
    public GoodDefn GatheredResource;

    // Crafting
    [Header("Crafting"), Space(10)]
    public bool CanCraftGoods = false;
    [ShowIf("CanCraftGoods")]
    public List<GoodDefn> CraftableGoods = new();
}