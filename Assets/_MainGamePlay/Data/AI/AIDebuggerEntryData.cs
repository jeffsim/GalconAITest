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

    public List<AIDebuggerEntryData> ChildEntries = new(10);
    public int AllChildEntriesCount;

    public bool IsHighestOptionOfPeers;

    static int curPoolIndex;
    static int MaxPoolSize = 100000;
    static AIDebuggerEntryData[] Pool;
    public AIDebuggerEntryData BestNextAction;
    public bool IsInBestStrategyPath;
    
    public static void InitializePool()
    {
        Pool = new AIDebuggerEntryData[MaxPoolSize];
        for (int i = 0; i < Pool.Length; i++)
            Pool[i] = new AIDebuggerEntryData();
        curPoolIndex = 0;
    }

    public static void ResetPool()
    {
        curPoolIndex = 0;
    }

    internal static AIDebuggerEntryData GetFromPool(AIActionType actionType, AI_NodeState fromNode, AI_NodeState toNode, int numSent, BuildingDefn buildingDefn, float scoreAfterActionAndBeforeSubActions, int actionNum, int curDepth, AIDebuggerEntryData curEntry)
    {
        if (Pool == null)
            InitializePool();
        if (curPoolIndex >= MaxPoolSize)
        {
            // resize pool.  TODO: More performant way?  Only for debugging scenarios so :shrug:
            MaxPoolSize *= 2;
            var newPool = new AIDebuggerEntryData[MaxPoolSize];
            for (int i = 0; i < Pool.Length; i++)
                newPool[i] = Pool[i];
            for (int i = Pool.Length; i < newPool.Length; i++)
                newPool[i] = new AIDebuggerEntryData();
            Pool = newPool;
        }

        var entry = Pool[curPoolIndex++];
        entry.ActionType = actionType;
        entry.FromNode = fromNode;
        entry.ToNode = toNode;
        entry.NumSent = numSent;
        entry.BuildingDefn = buildingDefn;
        entry.Score = scoreAfterActionAndBeforeSubActions;
        entry.ActionNumber = actionNum;
        entry.RecurseDepth = curDepth;
        entry.ParentEntry = curEntry;
        entry.ChildEntries.Clear();
        return entry;
    }

    public void DebugOutput()
    {
        if (!GameMgr.Instance.DebugOutputStrategyToConsole) return;
        switch (ActionType)
        {
            case AIActionType.ConstructBuildingInEmptyNode:
                Debug.Log(CurSpacing + ActionNumber + ": Send " + NumSent + " from " + FromNode.NodeId + "=>" + ToNode.NodeId + " to construct " + BuildingDefn.Id + ".  Score: " + Score.ToString("0.0"));
                break;
            case AIActionType.AttackFromNode:
                Debug.Log(CurSpacing + ActionNumber + ": Attack " + ToNode.NodeId + " with " + NumSent + " sent from " + FromNode.NodeId + ".  Score: " + Score.ToString("0.0"));
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
            case AIActionType.AttackFromNode: return "Attack " + ToNode.NodeId + " with " + NumSent + " sent from " + FromNode.NodeId;
            // case AIActionType.ConstructBuildingInOwnedEmptyNode: return BuildingDefn.Id + " in owned node " + ToNode.NodeId;
            case AIActionType.ConstructBuildingInEmptyNode: return "Send " + NumSent + " from " + FromNode.NodeId + "=>" + ToNode.NodeId + " to build " + BuildingDefn.Id;
            case AIActionType.SendWorkersToOwnedNode: return NumSent + " from " + FromNode.NodeId + "=>" + ToNode.NodeId;
            // case AIActionType.SendWorkersToEmptyNode: return NumSent + " from " + FromNode.NodeId + "=>" + ToNode.NodeId;
            default: return "TODO: " + ActionType + "";
        }
    }

    internal void CalculateAllChildEntriesCount()
    {
        // for ALL entries, calculate the count of all child entries under it and store in entry.AllChildEntriesCount
        AllChildEntriesCount = ChildEntries.Count;
        foreach (var childEntry in ChildEntries)
        {
            childEntry.CalculateAllChildEntriesCount();
            AllChildEntriesCount += childEntry.AllChildEntriesCount;
        }
    }
}
#endif