using System;
using System.Collections.Generic;
using System.Diagnostics;
using Codice.CM.SEIDInfo;

public enum AIActionType
{
    ERROR_StuckInLoop,
    DoNothing,
    // SendWorkersToEmptyNode,
    SendWorkersToOwnedNode,
    ConstructBuildingInEmptyNode,
    ConstructBuildingInOwnedEmptyNode,
    AttackFromNode,
    AttackFromMultipleNodes,
    NoAction_GameOver,
    NoAction_MaxDepth,
    RootAction,
    UpgradeBuilding
};

public class AIAction
{
    public override string ToString()
    {
        return Type switch
        {
            AIActionType.SendWorkersToOwnedNode => "Send " + Count + " workers from " + SourceNode.NodeId + " to " + DestNode.NodeId,
            AIActionType.AttackFromNode => "Attack with " + Count + " workers from " + SourceNode.NodeId + " to " + DestNode.NodeId + " and capture it",
            AIActionType.ConstructBuildingInEmptyNode => "Send " + Count + " workers from " + SourceNode.NodeId + " to " + DestNode.NodeId + " to build " + BuildingToConstruct.Id,
            AIActionType.DoNothing => "Do nothing (No beneficial action found)",
            AIActionType.NoAction_MaxDepth => "Max depth reached",
            AIActionType.NoAction_GameOver => "Game Over",
            _ => throw new Exception("Unhandled AIActionType: " + Type),
        };
    }

    public float Score;

    public AIActionType Type = AIActionType.DoNothing;
    public int Count;
    public AI_NodeState SourceNode;
    public AI_NodeState DestNode;
    public BuildingDefn BuildingToConstruct;
    public AttackResult AttackResult;
    public List<AttackResult> AttackResults = new();
    public Dictionary<AI_NodeState, int> AttackFromNodes = new();

#if DEBUG
    public DebugAIStateReasons DebugOutput_ScoreReasonsBeforeSubActions = new();
    public int DebugOutput_TriedActionNum; // for debug output purposes
    public int DebugOutput_Depth; // for debug output purposes
    public AIDebuggerEntryData AIDebuggerEntry;

    public AIAction Reset()
    {
        Score = 0;
        Count = 0;
        BuildingToConstruct = null;
        Type = AIActionType.DoNothing;
        SourceNode = null;
        DestNode = null;
        AIDebuggerEntry = null;
        AttackResult = AttackResult.Undefined;
        AttackResults.Clear();
        AttackFromNodes.Clear();
        DebugOutput_ScoreReasonsBeforeSubActions.Reset();
        DebugOutput_TriedActionNum = -1;
        DebugOutput_Depth = -1;
        return this;
    }

    public void TrackStrategyDebugInfoInAction(DebugAIStateReasons debugOutput_actionScoreReasons, int thisActionNum, int curDepth)
    {
        if (!AITestScene.Instance.DebugOutputStrategyToConsole) return;

        DebugOutput_TriedActionNum = thisActionNum;
        DebugOutput_Depth = curDepth;
        if (AITestScene.Instance.DebugOutputStrategyReasons)
            DebugOutput_ScoreReasonsBeforeSubActions = debugOutput_actionScoreReasons;
    }

    internal void CopyFrom(AIAction sourceAction)
    {
        Score = sourceAction.Score;
        Count = sourceAction.Count;
        BuildingToConstruct = sourceAction.BuildingToConstruct;
        Type = sourceAction.Type;

        // NextAction = sourceAction.NextAction;
        SourceNode = sourceAction.SourceNode;
        DestNode = sourceAction.DestNode;
        AttackResult = sourceAction.AttackResult;
        if (sourceAction.AttackResults == null)
            AttackResults = null;
        else
        {
            AttackResults.Clear();
            AttackResults.AddRange(sourceAction.AttackResults);
        }
        if (sourceAction.AttackFromNodes == null)
            AttackFromNodes = null;
        else
        {
            AttackFromNodes.Clear();
            foreach (var kvp in sourceAction.AttackFromNodes)
                AttackFromNodes[kvp.Key] = kvp.Value;
        }

        DebugOutput_ScoreReasonsBeforeSubActions = sourceAction.DebugOutput_ScoreReasonsBeforeSubActions;
        DebugOutput_TriedActionNum = sourceAction.DebugOutput_TriedActionNum;
        DebugOutput_Depth = sourceAction.DebugOutput_Depth;
        AIDebuggerEntry = sourceAction.AIDebuggerEntry;
    }

    internal void SetToNothing()
    {
        Score = 0;
        Count = 0;
        BuildingToConstruct = null;
        Type = AIActionType.DoNothing;

        // NextAction = sourceAction.NextAction;
        SourceNode = null;
        DestNode = null;
        AttackResult = AttackResult.Undefined;
        DebugOutput_ScoreReasonsBeforeSubActions = null;
        DebugOutput_TriedActionNum = 0;
        DebugOutput_Depth = 0;
        AIDebuggerEntry = null;
    }
#endif

    internal void SetTo_ConstructBuildingInEmptyNode(AI_NodeState fromNode, AI_NodeState toNode, int numSent,
                                                     BuildingDefn buildingDefn, float score, AIDebuggerEntryData debuggerEntry)
    {
        AIDebuggerEntry = debuggerEntry;

        Score = score;
        Type = AIActionType.ConstructBuildingInEmptyNode;
        SourceNode = fromNode;
        DestNode = toNode;
        Count = numSent;
        BuildingToConstruct = buildingDefn;
    }

    internal void SetTo_SendWorkersToOwnedNode(AI_NodeState fromNode, AI_NodeState toNode, int numSent, float score, AIDebuggerEntryData debuggerEntry)
    {
        AIDebuggerEntry = debuggerEntry;
        Score = score;
        Type = AIActionType.SendWorkersToOwnedNode;
        SourceNode = fromNode;
        DestNode = toNode;
        Count = numSent;
    }

    internal void SetTo_AttackFromNode(AI_NodeState fromNode, AI_NodeState toNode, int numSent,
                                       AttackResult attackResult, float score, AIDebuggerEntryData debuggerEntry)
    {
        AIDebuggerEntry = debuggerEntry;
        Score = score;
        AttackResult = attackResult;
        Type = AIActionType.AttackFromNode;
        SourceNode = fromNode;
        DestNode = toNode;
        Count = numSent;
    }

    // New method
    internal void SetTo_AttackFromMultipleNodes(Dictionary<AI_NodeState, int> attackFromNodes, AI_NodeState toNode, List<AttackResult> attackResults, float score, AIDebuggerEntryData debuggerEntry)
    {
        AIDebuggerEntry = debuggerEntry;
        Score = score;
        Type = AIActionType.AttackFromMultipleNodes;
        DestNode = toNode;

        AttackFromNodes.Clear();
        foreach (var kvp in attackFromNodes)
            AttackFromNodes[kvp.Key] = kvp.Value;

        AttackResults.Clear();
        AttackResults.AddRange(attackResults);
    }

    internal void SetTo_UpgradeBuilding(AI_NodeState fromNode, float score, AIDebuggerEntryData debuggerEntry)
    {
        AIDebuggerEntry = debuggerEntry;
        Score = score;
        Type = AIActionType.UpgradeBuilding;
        SourceNode = fromNode;
    }
}
