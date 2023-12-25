
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu()]
public class BuildingDefn : BaseDefn
{
    public string Name;
    public Color Color = Color.white;
    public WorkerDefn WorkerDefn;
    public List<Good_CraftingRequirements> ConstructionRequirements = new();
}