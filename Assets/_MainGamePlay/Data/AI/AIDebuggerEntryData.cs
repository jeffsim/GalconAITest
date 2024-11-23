using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if DEBUG
public class AIDebuggerEntryData
{
    public override string ToString() => ActionNumber + ": " + InformationString() + " (" + FinalActionScore.ToString("0.0") + ")";

    private string CurSpacing => new string(' ', Math.Max(0, RecurseDepth) * 4) + (RecurseDepth == 0 ? "" : "\u21B3");
    public int ActionNumber;
    public int RecurseDepth;
    public AIActionType ActionType;
    public AI_NodeState FromNode;

    // optional based on actiontype:
    public AI_NodeState ToNode;
    public AttackResult AttackResult;
    public List<AttackResult> AttackResults = new(10);
    public int NumSent;

    public Dictionary<AI_NodeState, int> NumSentFromEachNode = new(10);

    public BuildingDefn BuildingDefn;
#if DEBUG
    public float Debug_ActionScoreBeforeSubactions;
#endif
    public float FinalActionScore;
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

    public AIDebuggerEntryData()
    {
        // Should only gte called in initialization of pool
    }
    public static void ResetPool()
    {
        curPoolIndex = 0;
    }

    internal static AIDebuggerEntryData GetFromPool(AIActionType actionType, AI_NodeState fromNode, AI_NodeState toNode, int numSent, BuildingDefn buildingDefn, float finalActionScore, int actionNum, int curDepth, AIDebuggerEntryData curEntry)
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
        entry.AllChildEntriesCount = 0;
        entry.BuildingDefn = buildingDefn;
        entry.FinalActionScore = finalActionScore;
        entry.ActionNumber = actionNum;
        entry.BestNextAction = null;
        entry.RecurseDepth = curDepth;
        entry.ParentEntry = curEntry;
        entry.BestNextAction = null;
        entry.IsInBestStrategyPath = false;
        entry.IsHighestOptionOfPeers = false;
        entry.AttackResult = AttackResult.Undefined;
        entry.AttackResults.Clear();
        entry.ChildEntries.Clear();
        return entry;
    }

    // HACK
    internal static AIDebuggerEntryData GetFromPool2(AIActionType actionType, AI_NodeState toNode, Dictionary<AI_NodeState, int> numSentFromEachNode, List<AttackResult> attackResults, float finalActionScore, int actionNum, int curDepth, AIDebuggerEntryData curEntry)
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
        entry.FromNode = null;
        entry.ToNode = toNode;
        entry.NumSent = -1;
        entry.NumSentFromEachNode.Clear();
        foreach (var kvp in numSentFromEachNode)
            entry.NumSentFromEachNode[kvp.Key] = kvp.Value;
        entry.AllChildEntriesCount = 0;
        entry.BuildingDefn = null;
        entry.FinalActionScore = finalActionScore;
        entry.ActionNumber = actionNum;
        entry.BestNextAction = null;
        entry.RecurseDepth = curDepth;
        entry.ParentEntry = curEntry;
        entry.BestNextAction = null;
        entry.IsInBestStrategyPath = false;
        entry.IsHighestOptionOfPeers = false;
        entry.AttackResult = AttackResult.Undefined;
        entry.AttackResults.Clear();
        entry.AttackResults.AddRange(attackResults);
        entry.ChildEntries.Clear();
        return entry;
    }

    internal AIDebuggerEntryData AddEntry_ConstructBuildingInEmptyNode(AI_NodeState fromNode, AI_NodeState toNode, int numSent, BuildingDefn buildingDefn, float finalActionScore, int actionNum, int curDepth)
    {
        var newEntry = GetFromPool(
                        AIActionType.ConstructBuildingInEmptyNode,
                        fromNode,
                        toNode,
                        numSent,
                        buildingDefn,
                        finalActionScore,
                        actionNum,
                        curDepth,
                        this);
        ChildEntries.Add(newEntry);
        return newEntry;
    }
    internal AIDebuggerEntryData AddEntry_SendWorkersToOwnedNode(AI_NodeState fromNode, AI_NodeState toNode, int numSent, float finalActionScore, int actionNum, int curDepth)
    {
        var newEntry = GetFromPool(
                        AIActionType.SendWorkersToOwnedNode,
                        fromNode,
                        toNode,
                        numSent,
                        null,
                        finalActionScore,
                        actionNum,
                        curDepth,
                        this);
        ChildEntries.Add(newEntry);
        return newEntry;
    }

    internal AIDebuggerEntryData AddEntry_AttackToNode(Dictionary<AI_NodeState, int> attackFromNodes, AI_NodeState toNode, List<AttackResult> attackResults, float finalActionScore, int actionNum, int curDepth)
    {
        Debug.Assert(attackFromNodes != null);

        var newEntry = GetFromPool2(
                        AIActionType.AttackToNode,
                        toNode,
                        attackFromNodes,
                        attackResults,
                        finalActionScore,
                        actionNum,
                        curDepth,
                        this);
        ChildEntries.Add(newEntry);
        return newEntry;
    }

    internal AIDebuggerEntryData AddEntry_UpgradeBuilding(AI_NodeState fromNode, float finalActionScore, int actionNum, int curDepth)
    {
        var newEntry = GetFromPool(
                         AIActionType.UpgradeBuilding,
                        fromNode,
                        null,
                        0,
                        null,
                        finalActionScore,
                        actionNum,
                        curDepth,
                        this);
        ChildEntries.Add(newEntry);
        return newEntry;
    }

    public void DebugOutput()
    {
        if (!AITestScene.Instance.DebugOutputStrategyToConsole) return;
        switch (ActionType)
        {
            case AIActionType.ConstructBuildingInEmptyNode:
                Debug.Log(CurSpacing + ActionNumber + ": Send " + NumSent + " from " + FromNode.NodeId + "=>" + ToNode.NodeId + " to construct " + BuildingDefn.Id + ".  Score: " + FinalActionScore.ToString("0.0"));
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
            case AIActionType.ConstructBuildingInEmptyNode: return "Send " + NumSent + " from " + FromNode.NodeId + "=>" + ToNode.NodeId + " to build " + BuildingDefn.Id;
            case AIActionType.SendWorkersToOwnedNode: return "Send " + NumSent + " from " + FromNode.NodeId + "=>" + ToNode.NodeId;
            case AIActionType.UpgradeBuilding: return "Upgrade " + FromNode.NodeId + " (" + FromNode.BuildingDefn.Id + ") to " + FromNode.BuildingLevel;
            case AIActionType.AttackToNode:
                var numSent = NumSentFromEachNode.Values.Sum();
                var sentFrom = string.Join(",", NumSentFromEachNode.Select(n => n.Key.NodeId));
                // same as above but replace "AttackerWon" with "A" and "DefenderWon" with "D"
                var attackResults = string.Join(",", AttackResults.Select(r => r switch
                {
                    AttackResult.AttackerWon => "A",
                    AttackResult.DefenderWon => "D",
                    _ => "U",
                }));
                return "Attack " + ToNode.NodeId + " with " + numSent + " sent from " + sentFrom + " (" + attackResults + ")";

            default: return "TODO: " + ActionType + "";
        }
    }
    string AttackResultString()
    {
        return AttackResult switch
        {
            AttackResult.AttackerWon => "Won",
            AttackResult.DefenderWon => "Lost",
            AttackResult.BothSidesDied => "Tie",
            _ => "Undefined",
        };
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