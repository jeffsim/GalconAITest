using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.Serialization;
using UnityEngine;

public enum BuildingClass
{
    Unset, Camp, Crafter, Gatherer, Storage, Other, Defense, Empty
}
public enum BuildingType
{
    Unset, ArcheryRange, ArrowTower, Barracks, Camp, CoinMinter, GoldMiner, Lumbermill, MinersHut, Storage, Woodcutter, Empty, House, Wall, Bridge
}

public enum BuildingAutoAttackType
{
    None, Arrow, Fireball, PoisonCloud, Laser
}

public enum BuildingTrainingType
{
    // Workers assigned to building are NOT trained into a new class
    None,

    // Workers assigned to building are INSTANTLY trained into a new class
    InstantOnAssign,

    // Workers assigned to building are OVER TIME trained into a new class
    OverTime
}

[CreateAssetMenu(fileName = "BuildingDefn")]
public class BuildingDefn : BaseDefn
{
    public string FriendlyName;
    public GameObject Visual;
    public List<GameObject> VisualVariants = new List<GameObject>(); // If non-empty, then randomly pick one of these to use as the visual

    public BuildingClass BuildingClass = BuildingClass.Other;
    public BuildingType BuildingType = BuildingType.Unset;

    public bool CanBeDestroyedByPlayer = true;
    public bool CanAssignWorkers = true;

    // Can't own a forest
    public bool CanBeOwned = true;

    // Can't build a bridge on any node; so this is set to false for Bridge building, and nodes that can 
    // have bridges have Bridge added to AllowedBuildings
    public bool CanBeBuiltOnAnyNode = true;

    // e.g. in an ArcheryRange building, the workers in the building aren't actually doing anything while sitting there.  Used
    // to skip past AI checks.  TODO: specific to the ArcheryRange building, maybe it should be training them more?
    public bool WorkersSitIdle = false;

    [OdinSerialize] public Dictionary<ItemDefn, int> ItemsNeededToConstruct = new();

    // =============================================================
    [TitleGroup("Defense")]
    // =============================================================

    // The likelihood that an attacker's attack is defended automatically by the building
    public float DefenseChance = 0.65f;

    // If true then this building can attack enemies as they approach
    [ShowIf("BuildingClass", BuildingClass.Defense)]
    public bool BuildingCanAutoAttack = false;
    [ShowIf("BuildingClass", BuildingClass.Defense)]
    public BuildingAutoAttackType AutoAttackType;
    [ShowIf("BuildingClass", BuildingClass.Defense)]
    public float AutoAttackMinRange = 5;
    [ShowIf("BuildingClass", BuildingClass.Defense)]
    public float AutoAttackMaxRange = 20;
    [ShowIf("BuildingClass", BuildingClass.Defense)]
    public float SecondsBetweenAutoAttacks = .1f;

    [ShowIf("BuildingClass", BuildingClass.Defense)]
    public int ArrowDamage = 8;
    [ShowIf("BuildingClass", BuildingClass.Defense)]
    public int ArrowDamageRandom = 4; // 8 + (0..4) = 8-12


    // =============================================================
    [TitleGroup("Workers"), ShowIf("CanAssignWorkers")]
    // =============================================================
    // The defn to use for workers assigned to this building
    [ShowIf("CanAssignWorkers")]
    public WorkerDefn WorkerDefn;

    [ShowIf("CanAssignWorkers")]
    public bool GainsWorkersOverTime = false;

    // When workers are assigned to this building, do they need to be trained or instantly convert
    [ShowIf("CanAssignWorkers")]
    public BuildingTrainingType TrainingType = BuildingTrainingType.OverTime;

    // How long it takes to train a worker to this building's worker type
    [ShowIf("CanAssignWorkers")]
    public float TrainingTime = 2;


    // =============================================================
    [TitleGroup("Gatherers"), ShowIf("BuildingClass", BuildingClass.Gatherer)]
    // =============================================================
    // Max # of resources that each assigned gatherer can gather per turn
    [ShowIf("BuildingClass", BuildingClass.Gatherer)]
    public int DefaultNumResourcesGatherablePerGatherer = 1;

    // For now, each gathering building can only gather one resource type; I may change that over time
    [ShowIf("BuildingClass", BuildingClass.Gatherer)]
    public ItemDefn GatherableResource;


    // =============================================================
    [TitleGroup("Crafters"), ShowIf("BuildingClass", BuildingClass.Crafter)]
    // =============================================================
    // Items that will be crafted *if* resource allow 
    // TODO: how to choose between multiple craftable items in a building?  I'm assuming that crafting consumes resources...
    [ShowIf("BuildingClass", BuildingClass.Crafter)]
    public List<ItemDefn> CraftableItems = new List<ItemDefn>();

    // How many gatherers can be gathering from this room per turn
    [ShowIf("BuildingClass", BuildingClass.Crafter)]
    public int DefaultMaxCrafters = 1;
}
