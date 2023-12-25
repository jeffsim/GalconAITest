using System;
using System.Collections.Generic;
using UnityEditor;

public enum AIActionType { SendWorkersToNode, ConstructBuildingInOwnedNode };

public class AIAction
{
    public AIActionType Type;
    public int Count;
    public NodeData SourceNode;
    public NodeData DestNode;
}

class AI_PlayerState
{

}

class AI_BuildingState
{
    public BuildingDefn buildingDefn;

    public AI_BuildingState(BuildingData building)
    {
        buildingDefn = building?.Defn;
    }
}

class AI_NodeState
{
    private NodeData nodeData;
    public List<AI_NodeState> NeighborNodes = new();
    public int NumWorkers;

    public AI_BuildingState Building;
    public bool HasBuilding => Building.buildingDefn != null;
    public PlayerData OwnedBy;

    public AI_NodeState(NodeData nodeData)
    {
        this.nodeData = nodeData;
        Building = new AI_BuildingState(nodeData.Building);
        NumWorkers = nodeData.NumWorkers;
        OwnedBy = nodeData.OwnedBy;
    }
}
