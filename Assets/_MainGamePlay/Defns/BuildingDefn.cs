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

    // Defensive
    [Header("Defensive"), Space(10)]
    public bool CanBeAttacked = false;
    [ShowIf("CanBeAttacked")]
    public int Health = 100;
    public bool IsDefensive = false;

    // Gathering
    [Header("Gathering"), Space(10)]
    public bool CanGatherResources = false;
    [ShowIf("CanGatherResources")]
    public GoodDefn ResourceThisNodeCanGoGather;
    [ShowIf("CanGatherResources")]
    public int ResourceProducedPerTurn = 3;
    [ShowIf("CanGatherResources")]
    [Tooltip("Deprecated: use ResourcesPerSecondPerWorker. Kept for reference only.")]
    public float SecondsPerResourceProduced = 1.5f;
    [ShowIf("@CanGatherResources || CanBeGatheredFrom")]
    [Tooltip("Step mode: each worker produces this many resources per turn.")]
    public int ResourceProducedPerWorkerPerTurn = 1;
    [ShowIf("@CanGatherResources || CanBeGatheredFrom")]
    [Tooltip("Realtime: each worker produces this many resources per second.")]
    public float ResourcesPerSecondPerWorker = 0.667f;

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
    // Realtime mode only. How long (seconds) between each generated worker. Step mode keeps
    // using BuildingData.WorkersGeneratedPerTurn unchanged.
    [ShowIf("CanGenerateWorkers")]
    [Tooltip("Realtime: seconds between each generated worker. <=0 disables realtime generation.")]
    public float SecondsPerWorkerGenerated = 2f;
}