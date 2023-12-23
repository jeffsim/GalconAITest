using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

public class ItemCountDictionary : SerializedDictionary<ItemDefn, int> { }

[Serializable]
public class Town_NodeDefn
{
    public override string ToString()
    {
        Debug.Assert(NodeDefn != null, "null NodeDefn.  Node = " + Id);
        return NodeDefn.Type + " (" + PlayerIdOwnedBy + ")";
    }

    public int Id;

    // used for debugging town w/o deleting buildings
    public bool IsBuildingEnabled = true;

    // Location of the node in the Town
    public Vector3 WorldLoc;

    // Which player starts out owning this node
    // 0 = no one, 1 = HumanPlayer, 2 = AI Player 1.  These are mapped to actual RaceDefns in TownDefn.PlayerRaces
    public int PlayerIdOwnedBy;

    public NodeDefn NodeDefn;

    // How much FoW is cleared when this Node is captured by a player
    public int FogOfWarVisibilityRadiusOnClear = 40;

    public float BuildingRotation = 0;

    // Building that starts in the Node.  note: for NodeDefns that specify a default building, this can override?
    public BuildingDefn BuildingInNode;

    public int BuildingLevel = 1;

    // Number of workers that start out assigned to this Node
    // This is only used if the Node contains a building
    // [ShowIf("Node.CanAssignWorkers", true)]
    public int NumStartingWorkers;

    // Starting inventory 
    // note: supply chains become a challenge in larger levels - player should build storage buildings near the front lines...
    [SerializeReference] public ItemCountDictionary StartingItemCounts = new ItemCountDictionary();

    // What buildings are allowed to be constructed in this node.  If empty, then all buildings are allowed
    public List<BuildingDefn> AllowedBuildings = new List<BuildingDefn>();
}