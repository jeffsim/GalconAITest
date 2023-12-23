using UnityEngine;

public enum WorkerType { Unset, Worker, Defender };

public class RaceVisualDictionary : SerializedDictionary<RaceDefn, GameObject> { }

[CreateAssetMenu(fileName = "WorkerDefn")]
public class WorkerDefn : BaseDefn
{
    public string FriendlyName;
    public WorkerType WorkerType = WorkerType.Worker;

    // Visual
    [SerializeReference] public RaceVisualDictionary RacialVisuals = new RaceVisualDictionary();
    public GameObject DefaultRacialVisual; // use this if not present in RacialVisual

    // e.g. a Warrior needs a Shield item and a Sword item in the building to begin training
    [SerializeReference] public ItemCountDictionary ItemsNeededToTrain = new ItemCountDictionary();

    // ===== Health/Defense stats

    // how much base health they have
    public int Health = 10;

    // How much add'l random health to add to base.  ranges from 0..[this value]
    public int HealthRandom = 4;        // 10 + (0..4) = 10-14

    // % chance they'll defend against an attack. Combined with building offense (IF currentnode == assignednode)
    public float DefendChance = .9f;

    // ===== Attack stats

    // % chance they'll hit on attack. Combined with building defense (IF currentnode == assignednode)
    public float AttackHitChance = .9f;

    // how much base damage they do
    public int AttackDamage = 10;

    // How much add'l random damage to add to base.  ranges from 0..[this value]
    public int AttackDamageRandom = 4;
}
