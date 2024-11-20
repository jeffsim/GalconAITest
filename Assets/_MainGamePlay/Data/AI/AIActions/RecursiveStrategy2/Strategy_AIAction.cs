using System;
using UnityEngine;

public struct Strategy_AIAction
{
    public ActionType Type;
    public int SourceNodeIndex;
    public int TargetNodeIndex;
    public int NumWorkers;
    public BuildingType BuildingType;

    public Strategy_AIAction(ActionType type) : this()
    {
        Type = type;
    }
}
public enum ActionType
{
    CaptureNodeAndConstructBuilding,
    AttackEnemyNode,
    ButtressBuilding,
    UpgradeBuilding
}
