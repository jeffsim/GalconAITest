using System.Collections.Generic;
using UnityEngine;

public enum Affinity
{
    Hates,
    Neutral,
    Likes,
    DoesntHateButWantsTheirNodes
};

[CreateAssetMenu(fileName = "RaceDefn")]
public class RaceDefn : BaseDefn
{
    public GameObject Visual;
    public string Name;
    public string Description;
    public Dictionary<RaceDefn, Affinity> Affinities = new Dictionary<RaceDefn, Affinity>();

    public Texture WorkerColorTexture;

    public Color Color = Color.white;
    public Material HouseRoofMaterial;
    public bool IsComputerAI = true;
    public bool AttacksEnemies = true;
    public bool ExpandsTerritory = true;

    // e.g. the 10 workers that start out in a forest waiting to be taken over by another player -- these belong to the "nonaggressivetowernpc" that just sits there doing nothing, ever.
    public bool WorkersSitIdle = false;

    // player is neutral to all; wary but doesn't attack anyone - until they are attacked; then they hate the attacker
    public bool NeutralUntilAttacked = false;
}