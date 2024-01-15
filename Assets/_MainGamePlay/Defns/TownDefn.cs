
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[Serializable]
public class NodeDefn
{
    public int NodeId; // TODO: Not using this right
    public Vector3 WorldLoc;
    public int OwnedByPlayerId = -1;
    public BuildingDefn StartingBuilding;
    public int NumStartingWorkers = 0;
    public SerializedDictionary<GoodDefn, int> StartingInventory = new();
}

[Serializable]
public class NodeConnectionDefn
{
    public Vector2Int Nodes;
    public bool IsBidirectional = true;
}

[CreateAssetMenu()]
public class TownDefn : BaseDefn
{
    public List<NodeDefn> Nodes = new();
    public List<NodeConnectionDefn> NodeConnections = new();
}
