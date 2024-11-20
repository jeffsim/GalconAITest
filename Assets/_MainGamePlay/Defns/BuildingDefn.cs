using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public enum BuildingType
{
    None,
    Barracks,
    Camp,
    Carpenter,
    Forest,
    GoldMind,
    Outpost,
    StoneMine,
    StoneMiner,
    TownStatue,
    Woodcutter,

    LumberjackHut,
    Sawmill,
    StoneQuarry,
    StoneMason,

}
[CreateAssetMenu()]
public class BuildingDefn : BaseDefn
{
    public string Name;
    public string Description;
    public BuildingType BuildingType;
    public Color Color = Color.white;
    public WorkerDefn WorkerDefn;

    public bool CanBeUpgraded = true;
    
    // Construction
    [Header("Construction"), Space(10)]
    public bool CanBeBuiltByPlayer = true;
    [ShowIf("CanBeBuiltByPlayer")]
    public List<Good_CraftingRequirements> ConstructionRequirements = new();

    // Gathering
    [Header("Gathering"), Space(10)]
    public bool CanGatherResources = false;
    [ShowIf("CanGatherResources")]
    public GoodDefn ResourceThisNodeCanGoGather;

    // ResourceNode
    [Header("Resource"), Space(10)]
    public bool CanBeGatheredFrom = false;
    [ShowIf("CanBeGatheredFrom")]
    public GoodDefn ResourceGatheredFromThisNode;

    // Crafting
    [Header("Crafting"), Space(10)]
    public bool CanCraftGoods = false;
    [ShowIf("CanCraftGoods")]
    public List<GoodDefn> CraftableGoods = new();

    // Generating workers
    [Header("CanGenerateWorkers"), Space(10)]
    public bool CanGenerateWorkers = false;
    [ShowIf("CanGenerateWorkers")]
    public WorkerDefn GeneratableWorker;
}