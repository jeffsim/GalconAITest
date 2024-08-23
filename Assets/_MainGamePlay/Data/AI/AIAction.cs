using System;
using System.Collections.Generic;

public enum AIActionType
{
    ERROR_StuckInLoop,
    DoNothing,
    SendWorkersToEmptyNode,
    SendWorkersToOwnedNode,
    ConstructBuildingInEmptyNode,
    ConstructBuildingInOwnedEmptyNode,
    AttackFromNode,
    NoAction_GameOver,
    NoAction_MaxDepth,
    RootAction
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
        ScoresFrom_ResourceGatherersCloseToResourceNodes.Clear();
        ScoresFrom_BuildingsThatGenerateWorkers.Clear();
        ScoresFrom_EnemyOwnedNodes.Clear();
    }

    public List<DebugAIStateReason> ScoresFrom_NodesOwned = new();
    public List<DebugAIStateReason> ScoresFrom_NumEmptyNodesOwned = new();
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
    public AI_NodeState DestNode;

    public AIAction NextAction;
    
    // Build building
    public BuildingDefn BuildingToConstruct;

    // Attacking
    public AttackResult AttackResult;

#if DEBUG
    public DebugAIStateReasons DebugOutput_ScoreReasonsBeforeSubActions = new();
    public int DebugOutput_TriedActionNum; // for debug output purposes
    public int DebugOutput_Depth; // for debug output purposes

    public void Reset()
    {
        Score = 0;
        Count = 0;
        BuildingToConstruct = null;
        Type = AIActionType.DoNothing;
        SourceNode = null;
        DestNode = null;
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
        NextAction = sourceAction.NextAction;
        SourceNode = sourceAction.SourceNode;
        DestNode = sourceAction.DestNode;
        DebugOutput_ScoreReasonsBeforeSubActions = sourceAction.DebugOutput_ScoreReasonsBeforeSubActions;
        DebugOutput_TriedActionNum = sourceAction.DebugOutput_TriedActionNum;
        DebugOutput_Depth = sourceAction.DebugOutput_Depth;
    }
#endif

    internal void SetTo_ConstructBuildingInEmptyNode(AI_NodeState fromNode, AI_NodeState toNode, int numSent, BuildingDefn buildingDefn, float score)
    {
        Score = score;
        Type = AIActionType.ConstructBuildingInEmptyNode;
        SourceNode = fromNode;
        DestNode = toNode;
        Count = numSent;
        BuildingToConstruct = buildingDefn;
    }
}
