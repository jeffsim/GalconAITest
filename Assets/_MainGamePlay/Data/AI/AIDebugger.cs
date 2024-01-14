using System;
using System.Collections.Generic;
using UnityEngine;

#if DEBUG
public class AIDebuggerEntryData
{
    public int ActionNumber;
    public int RecurseDepth;
    public AIActionType ActionType;
    public AI_NodeState FromNode;

    // optional based on actiontype:
    public AI_NodeState ToNode;
    public AttackResult AttackResult;
    public int NumSent;
    public BuildingDefn BuildingDefn;

    public string InformationString()
    {
        switch (ActionType)
        {
            case AIActionType.AttackFromNode:
                return "Attack result: " + AttackResult + "; numSent: " + NumSent;
            case AIActionType.ConstructBuildingInOwnedNode:
                return BuildingDefn.Id + " in node " + ToNode.NodeId;
            case AIActionType.SendWorkersToNode:
                return NumSent + " from " + FromNode.NodeId + "=>" + ToNode.NodeId;
            default:
                return "TODO: " + ActionType + "";
        }
    }
}

public static class AIDebugger
{
    static int Indent = 0;
    private static string CurSpacing => new string(' ', Math.Max(0, Indent) * 4) + (Indent == 0 ? "" : "\u21B3");
    static int CurActionNum;
    static int RecurseDepth;
    static AI_NodeState ActionFromNode;

    public static List<AIDebuggerEntryData> Entries = new List<AIDebuggerEntryData>();

    public static void Clear()
    {
        Entries.Clear();
        CurActionNum = 0;
        RecurseDepth = 0;
        Indent = 0;
    }

    internal static void PushTryActionStart(int thisActionNum, AIActionType actionType, AI_NodeState fromNode, int curDepth, int recurseCount)
    {
        // Debug.Log(CurSpacing + thisActionNum + ": from node " + fromNode.NodeId + ": " + actionType);// + " (depth " + curDepth + ", recurse " + recurseCount + ")");
        CurActionNum = thisActionNum;
        RecurseDepth = Indent;
        ActionFromNode = fromNode;

        Indent++;
    }

    internal static void PopTryActionStart()
    {
        Indent--;
    }

    internal static void TrackPerformAction_Attack(AI_NodeState toNode, AttackResult attackResult, int numSent, float scoreAfterActionAndBeforeSubActions)
    {
        // Debug.Log(CurSpacing + "Attack on node " + toNode.NodeId + " result: " + attackResult + "; numSent: " + numSent + "; scoreAfterActionAndBeforeSubActions: " + scoreAfterActionAndBeforeSubActions.ToString("0.0"));
        Entries.Add(new AIDebuggerEntryData() { ActionNumber = CurActionNum, RecurseDepth = RecurseDepth, FromNode = ActionFromNode, 
                                                ActionType = AIActionType.AttackFromNode, ToNode = toNode, AttackResult = attackResult, NumSent = numSent });
    }

    internal static void TrackPerformAction_ConstructBuilding(AI_NodeState toNode, BuildingDefn buildingDefn, float scoreAfterActionAndBeforeSubActions)
    {
        // Debug.Log(CurSpacing + "Construct building " + buildingDefn.Id + " in node " + toNode.NodeId + "; scoreAfterActionAndBeforeSubActions: " + scoreAfterActionAndBeforeSubActions.ToString("0.0"));
        Entries.Add(new AIDebuggerEntryData() { ActionNumber = CurActionNum, RecurseDepth = RecurseDepth, FromNode = ActionFromNode, 
                                                ActionType = AIActionType.ConstructBuildingInOwnedNode, ToNode = toNode, BuildingDefn = buildingDefn });
    }

    internal static void TrackPerformAction_SendWorkersToOwnedNode(AI_NodeState toNode, int numSent, float scoreAfterActionAndBeforeSubActions)
    {
        // Debug.Log(CurSpacing + "Send " + numSent + " workers to node " + toNode.NodeId + "; scoreAfterActionAndBeforeSubActions: " + scoreAfterActionAndBeforeSubActions.ToString("0.0"));
        Entries.Add(new AIDebuggerEntryData() { ActionNumber = CurActionNum, RecurseDepth = RecurseDepth, FromNode = ActionFromNode, 
                                                ActionType = AIActionType.SendWorkersToNode, ToNode = toNode, NumSent = numSent });
    }
}
#endif