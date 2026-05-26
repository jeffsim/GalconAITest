using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu()]
public class GameSettingsDefn : BaseDefn
{
    /// Populated at load time from every BuildingDefn whose CanBeBuiltByPlayer is true.
    /// Consumed by the human-player building picker; AI generators iterate the full
    /// BuildingDefns dictionary themselves.
    [HideInInspector] public List<BuildingDefn> PlayerBuildableBuildings = new();
}
