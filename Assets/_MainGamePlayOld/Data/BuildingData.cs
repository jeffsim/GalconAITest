using System;
using System.Collections.Generic;
using UnityEngine;

public class BuildingData
{

    private BuildingDefn _defn;
    public BuildingDefn Defn => _defn == null ? _defn = GameDefns.Instance.BuildingDefns[DefnId] : _defn;
    public string DefnId;
    [SerializeReference] public NodeData NodeIn;
    public BuildingType Type => Defn.BuildingType;
    public BuildingClass Class => Defn.BuildingClass;

    public BuildingData(BuildingDefn buildingDefn, NodeData nodeIn)
    {
        DefnId = buildingDefn.Id;
        NodeIn = nodeIn;
    }
}
