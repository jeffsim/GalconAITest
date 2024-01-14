using System;
using System.Collections.Generic;
using UnityEngine;

#if DEBUG
public static class AIDebugger
{
    public static AIDebuggerEntryData topEntry = new();
    public static AIDebuggerEntryData curEntry;

    public static void Clear()
    {
        curEntry = topEntry;
        curEntry.ChildEntries.Clear();
    }

    internal static void TrackPerformAction_ConstructBuildingInEmptyNode(AI_NodeState fromNode, AI_NodeState toNode, int numSent, BuildingDefn buildingDefn, float scoreAfterActionAndBeforeSubActions, int actionNum, int curDepth, int recurseCount)
    {
        if (!GameMgr.Instance.DebugOutputStrategy) return;

        PushPerformedAction(new AIDebuggerEntryData()
        {
            ActionType = AIActionType.ConstructBuildingInEmptyNode,
            FromNode = fromNode,
            ToNode = toNode,
            NumSent = numSent,
            BuildingDefn = buildingDefn,
            Score = scoreAfterActionAndBeforeSubActions,
            ActionNumber = actionNum,
            RecurseDepth = curDepth,
            ParentEntry = curEntry
        });
    }

    internal static void TrackPerformAction_Attack(AI_NodeState fromNode, AI_NodeState toNode, AttackResult attackResult, int numSent, float scoreAfterActionAndBeforeSubActions, int actionNum, int curDepth, int recurseCount)
    {
        if (!GameMgr.Instance.DebugOutputStrategy) return;

        PushPerformedAction(new AIDebuggerEntryData()
        {
            ActionType = AIActionType.AttackFromNode,
            FromNode = fromNode,
            ToNode = toNode,
            NumSent = numSent,
            Score = scoreAfterActionAndBeforeSubActions,
            ActionNumber = actionNum,
            RecurseDepth = curDepth,
            ParentEntry = curEntry
        });
    }

    private static void PushPerformedAction(AIDebuggerEntryData aIDebuggerEntryData)
    {
        if (!GameMgr.Instance.DebugOutputStrategy) return;
        
        aIDebuggerEntryData.DebugOutput();
        curEntry.ChildEntries.Add(aIDebuggerEntryData);
        curEntry = aIDebuggerEntryData;
    }

    internal static void PopPerformedAction()
    {
        if (!GameMgr.Instance.DebugOutputStrategy) return;
       
        curEntry = curEntry.ParentEntry;
    }

    // internal static void TrackPerformAction_ConstructBuildingInEmptyNode(AI_NodeState toNode, AI_NodeState toNode1, BuildingDefn buildingDefn, float scoreAfterActionAndBeforeSubActions)
    // {
    //     Debug.Log(CurSpacing + "Construct building " + buildingDefn.Id + " in node " + toNode.NodeId + "; scoreAfterActionAndBeforeSubActions: " + scoreAfterActionAndBeforeSubActions.ToString("0.0"));
    //     Entries.Add(new AIDebuggerEntryData()
    //     {
    //         ActionNumber = CurActionNum,
    //         RecurseDepth = RecurseDepth,
    //         FromNode = ActionFromNode,
    //         ActionType = AIActionType.ConstructBuildingInEmptyNode,
    //         ToNode = toNode,
    //         BuildingDefn = buildingDefn,
    //         Score = scoreAfterActionAndBeforeSubActions,
    // ParentEntry = curEntry
    //     });
    // }



















    internal static void TrackPerformAction_ConstructBuildingInOwnedEmptyNode(AI_NodeState toNode, BuildingDefn buildingDefn, float scoreAfterActionAndBeforeSubActions)
    {
        // Debug.Log(CurSpacing + "Construct building " + buildingDefn.Id + " in node " + toNode.NodeId + "; scoreAfterActionAndBeforeSubActions: " + scoreAfterActionAndBeforeSubActions.ToString("0.0"));
        // Entries.Add(new AIDebuggerEntryData()
        // {
        //     ActionNumber = CurActionNum,
        //     RecurseDepth = RecurseDepth,
        //     FromNode = ActionFromNode,
        //     ActionType = AIActionType.ConstructBuildingInOwnedEmptyNode,
        //     ToNode = toNode,
        //     BuildingDefn = buildingDefn,
        //     Score = scoreAfterActionAndBeforeSubActions,
        //     ParentEntry = curEntry
        // });
    }

    internal static void TrackPerformAction_Attack2(AI_NodeState toNode, AttackResult attackResult, int numSent, float scoreAfterActionAndBeforeSubActions)
    {
        // Debug.Log(CurSpacing + "Attack on node " + toNode.NodeId + " result: " + attackResult + "; numSent: " + numSent + "; scoreAfterActionAndBeforeSubActions: " + scoreAfterActionAndBeforeSubActions.ToString("0.0"));
        // Entries.Add(new AIDebuggerEntryData()
        // {
        //     ActionNumber = CurActionNum,
        //     RecurseDepth = RecurseDepth,
        //     FromNode = ActionFromNode,
        //     ActionType = AIActionType.AttackFromNode,
        //     ToNode = toNode,
        //     AttackResult = attackResult,
        //     NumSent = numSent,
        //     Score = scoreAfterActionAndBeforeSubActions,
        //     ParentEntry = curEntry
        // });
    }


    internal static void TrackPerformAction_SendWorkersToOwnedNode(AI_NodeState toNode, int numSent, float scoreAfterActionAndBeforeSubActions)
    {
        // Debug.Log(CurSpacing + "Send " + numSent + " workers to node " + toNode.NodeId + "; scoreAfterActionAndBeforeSubActions: " + scoreAfterActionAndBeforeSubActions.ToString("0.0"));
        // Entries.Add(new AIDebuggerEntryData()
        // {
        //     ActionNumber = CurActionNum,
        //     RecurseDepth = RecurseDepth,
        //     FromNode = ActionFromNode,
        //     ActionType = AIActionType.SendWorkersToOwnedNode,
        //     ToNode = toNode,
        //     NumSent = numSent,
        //     Score = scoreAfterActionAndBeforeSubActions,
        //     ParentEntry = curEntry
        // });
    }

    internal static void TrackPerformAction_SendWorkersToEmptyNode(AI_NodeState toNode, int numSent, float scoreAfterActionAndBeforeSubActions)
    {
        // Debug.Log(CurSpacing + "Send " + numSent + " workers to node " + toNode.NodeId + "; scoreAfterActionAndBeforeSubActions: " + scoreAfterActionAndBeforeSubActions.ToString("0.0"));
        // Entries.Add(new AIDebuggerEntryData()
        // {
        //     ActionNumber = CurActionNum,
        //     RecurseDepth = RecurseDepth,
        //     FromNode = ActionFromNode,
        //     ActionType = AIActionType.SendWorkersToEmptyNode,
        //     ToNode = toNode,
        //     NumSent = numSent,
        //     Score = scoreAfterActionAndBeforeSubActions,
        //     ParentEntry = curEntry
        // });
    }
}
#endif