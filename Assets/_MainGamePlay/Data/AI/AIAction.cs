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

#if DEBUG
public class DebugAIStateReason
{
    public float ScoreValue;
    public AI_NodeState Node;
}

public class DebugAIStateReasons
{
    public override string ToString()
    {
        var str = "";
        if (ScoresFrom_NodesOwned.Count > 0) str += addReasonScoresString("Nodes", ScoresFrom_NodesOwned);
        if (ScoresFrom_NumEmptyNodesOwned.Count > 0) str += " | " + addReasonScoresString("Empty Nodes", ScoresFrom_NumEmptyNodesOwned);
        if (ScoresFrom_BuildingsNearEnemyNodes.Count > 0) str += " | " + addReasonScoresString("Nearby enemies", ScoresFrom_BuildingsNearEnemyNodes);
        if (ScoresFrom_ResourceGatherersCloseToResourceNodes.Count > 0) str += " | " + addReasonScoresString("Res Gatherers", ScoresFrom_ResourceGatherersCloseToResourceNodes);
        if (ScoresFrom_BuildingsThatGenerateWorkers.Count > 0) str += " | " + addReasonScoresString("Worker Gens", ScoresFrom_BuildingsThatGenerateWorkers);
        if (ScoresFrom_EnemyOwnedNodes.Count > 0) str += " | " + addReasonScoresString("Enemy Nodes", ScoresFrom_EnemyOwnedNodes);
        return str;
    }

    private string addReasonScoresString(string msg, List<DebugAIStateReason> reasons)
    {
        var str = msg + " (";
        for (int i = 0; i < reasons.Count; i++)
        {
            if (i > 0) str += ", ";
            str += reasons[i].Node.NodeId;
        }
        return str + ")=" + reasons[0].ScoreValue * reasons.Count;
    }

    internal void Reset()
    {
        ScoresFrom_NodesOwned.Clear();
        ScoresFrom_NumEmptyNodesOwned.Clear();
        ScoresFrom_BuildingsNearEnemyNodes.Clear();
        ScoresFrom_ResourceGatherersCloseToResourceNodes.Clear();
        ScoresFrom_BuildingsThatGenerateWorkers.Clear();
        ScoresFrom_EnemyOwnedNodes.Clear();
    }

    public List<DebugAIStateReason> ScoresFrom_NodesOwned = new();
    public List<DebugAIStateReason> ScoresFrom_NumEmptyNodesOwned = new();
    public List<DebugAIStateReason> ScoresFrom_BuildingsNearEnemyNodes = new();
    public List<DebugAIStateReason> ScoresFrom_ResourceGatherersCloseToResourceNodes = new();
    public List<DebugAIStateReason> ScoresFrom_BuildingsThatGenerateWorkers = new();
    public List<DebugAIStateReason> ScoresFrom_EnemyOwnedNodes = new();

    public float TotalScore
    {
        get
        {
            float score = 0;
            foreach (var reason in ScoresFrom_NodesOwned) score += reason.ScoreValue;
            foreach (var reason in ScoresFrom_NumEmptyNodesOwned) score += reason.ScoreValue;
            foreach (var reason in ScoresFrom_BuildingsNearEnemyNodes) score += reason.ScoreValue;
            foreach (var reason in ScoresFrom_ResourceGatherersCloseToResourceNodes) score += reason.ScoreValue;
            foreach (var reason in ScoresFrom_BuildingsThatGenerateWorkers) score += reason.ScoreValue;
            foreach (var reason in ScoresFrom_EnemyOwnedNodes) score += reason.ScoreValue;
            return score;
        }
    }
}

#endif

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
    public List<AI_NodeState> SourceNodes;// for AttackFromMultipleNodes
    public Dictionary<AI_NodeState, int> NumSentFromEachNode; // for AttackFromMultipleNodes

    public AI_NodeState DestNode;


    // public AIAction NextAction;

    // Build building
    public BuildingDefn BuildingToConstruct;

    // Attacking
    public AttackResult AttackResult;
    public List<AttackResult> AttackResults; // for AttackFromMultipleNodes

#if DEBUG
    public DebugAIStateReasons DebugOutput_ScoreReasonsBeforeSubActions = new();
    public int DebugOutput_TriedActionNum; // for debug output purposes
    public int DebugOutput_Depth; // for debug output purposes
    public AIDebuggerEntryData AIDebuggerEntry;

    public void Reset()
    {
        Score = 0;
        Count = 0;
        BuildingToConstruct = null;
        Type = AIActionType.DoNothing;
        SourceNode = null;
        SourceNodes = new();
        NumSentFromEachNode = new();
        DestNode = null;
        AIDebuggerEntry = null;
        AttackResult = AttackResult.Undefined;
        AttackResults = new();
        DebugOutput_ScoreReasonsBeforeSubActions.Reset();
        DebugOutput_TriedActionNum = -1;
        DebugOutput_Depth = -1;
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
        SourceNodes = sourceAction.SourceNodes == null ? null : new(sourceAction.SourceNodes);
        DestNode = sourceAction.DestNode;
        AttackResult = sourceAction.AttackResult;
        AttackResults = sourceAction.AttackResults == null ? null : new(sourceAction.AttackResults);
        NumSentFromEachNode = sourceAction.NumSentFromEachNode == null ? null : new(sourceAction.NumSentFromEachNode);
        
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
    internal void SetTo_AttackFromMultipleNodes(List<AI_NodeState> fromNodes, AI_NodeState toNode, Dictionary<AI_NodeState, int> numSentFromEachNode, List<AttackResult> attackResults, float score, AIDebuggerEntryData debuggerEntry)
    {
        AIDebuggerEntry = debuggerEntry;
        Score = score;
        AttackResults = attackResults;
        Type = AIActionType.AttackFromMultipleNodes;
        SourceNodes = fromNodes;
        DestNode = toNode;
        Debug.Assert(numSentFromEachNode != null);
        NumSentFromEachNode = new(numSentFromEachNode);
    }

    internal void SetTo_UpgradeBuilding(AI_NodeState fromNode, float score, AIDebuggerEntryData debuggerEntry)
    {
        AIDebuggerEntry = debuggerEntry;
        Score = score;
        Type = AIActionType.UpgradeBuilding;
        SourceNode = fromNode;
    }
}
