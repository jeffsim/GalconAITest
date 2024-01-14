using System;
using System.Collections.Generic;
using UnityEngine;

#if DEBUG
public class AIDebuggerEntryData
{
    private string CurSpacing => new string(' ', Math.Max(0, RecurseDepth) * 4) + (RecurseDepth == 0 ? "" : "\u21B3");
    public int ActionNumber;
    public int RecurseDepth;
    public AIActionType ActionType;
    public AI_NodeState FromNode;

    // optional based on actiontype:
    public AI_NodeState ToNode;
    public AttackResult AttackResult;
    public int NumSent;
    public BuildingDefn BuildingDefn;
    public float Score;
    public AIDebuggerEntryData ParentEntry;

    public List<AIDebuggerEntryData> ChildEntries = new();

    public bool IsBestOption;

    public void DebugOutput()
    {
        return;
        switch (ActionType)
        {
            case AIActionType.ConstructBuildingInEmptyNode:
                Debug.Log(CurSpacing + ActionNumber + ": Send " + NumSent + " from " + FromNode.NodeId + "=>" + ToNode.NodeId + " to construct " + BuildingDefn.Id + ".  Score: " + Score.ToString("0.0"));
                break;
            default:
                Debug.Log("TODO: " + ActionType + "");
                break;
        }
    }

    public string InformationString()
    {
        switch (ActionType)
        {
            case AIActionType.AttackFromNode: return "Attack result: " + AttackResult + "; numSent: " + NumSent;
            case AIActionType.ConstructBuildingInOwnedEmptyNode: return BuildingDefn.Id + " in owned node " + ToNode.NodeId;
            case AIActionType.ConstructBuildingInEmptyNode: return "Send " + NumSent + " from " + FromNode.NodeId + "=>" + ToNode.NodeId + " to build " + BuildingDefn.Id;
            case AIActionType.SendWorkersToOwnedNode: return NumSent + " from " + FromNode.NodeId + "=>" + ToNode.NodeId;
            case AIActionType.SendWorkersToEmptyNode: return NumSent + " from " + FromNode.NodeId + "=>" + ToNode.NodeId;
            default: return "TODO: " + ActionType + "";
        }
    }
}
#endif