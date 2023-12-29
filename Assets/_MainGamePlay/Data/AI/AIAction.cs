using System;
using System.Collections.Generic;
using Mono.Cecil;

public enum AIActionType { DoNothing, SendWorkersToNode, ConstructBuildingInOwnedNode };

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
        if (ScoresFrom_NodesOwned.Count > 0) str += addReasonScoresString("Owned Nodes", ScoresFrom_NodesOwned);
        if (ScoresFrom_NumEmptyNodesOwned.Count > 0) str += " | " + addReasonScoresString("Owned Empty Nodes", ScoresFrom_NumEmptyNodesOwned);
        if (ScoresFrom_ResourceGatherersCloseToResourceNodes.Count > 0) str += " | " + addReasonScoresString("Owned Res Gatherers", ScoresFrom_ResourceGatherersCloseToResourceNodes);
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
    }

    internal void CopyFrom(DebugAIStateReasons source)
    {
        Reset();
        ScoresFrom_NodesOwned.AddRange(source.ScoresFrom_NodesOwned);
        ScoresFrom_NumEmptyNodesOwned.AddRange(source.ScoresFrom_NumEmptyNodesOwned);
        ScoresFrom_ResourceGatherersCloseToResourceNodes.AddRange(source.ScoresFrom_ResourceGatherersCloseToResourceNodes);
    }

    public List<DebugAIStateReason> ScoresFrom_NodesOwned = new();
    public List<DebugAIStateReason> ScoresFrom_NumEmptyNodesOwned = new();
    public List<DebugAIStateReason> ScoresFrom_ResourceGatherersCloseToResourceNodes = new();
    public float TotalScore
    {
        get
        {
            float score = 0;
            foreach (var reason in ScoresFrom_NodesOwned) score += reason.ScoreValue;
            foreach (var reason in ScoresFrom_NumEmptyNodesOwned) score += reason.ScoreValue;
            foreach (var reason in ScoresFrom_ResourceGatherersCloseToResourceNodes) score += reason.ScoreValue;
            return score;
        }
    }
}

#endif

public class AIAction
{
    public float Score;
    public float ScoreBeforeSubActions;

    public AIActionType Type = AIActionType.DoNothing;
    public int Count;
    public AI_NodeState SourceNode;
    public AI_NodeState DestNode;
    public string BuildingToConstruct;

#if DEBUG
    public DebugAIStateReasons ScoreReasons = new();
    public int StepNum; // for debug output purposes
    public int Depth; // for debug output purposes
    public AIAction NextAction; // Keep track of the optimal actions to perform after this one; only used for debugging

    public void Reset()
    {
        Score = 0;
        ScoreBeforeSubActions = 0;
        Count = 0;
        BuildingToConstruct = null;
        Type = AIActionType.DoNothing;
        SourceNode = null;
        DestNode = null;

        ScoreReasons.Reset();
        StepNum = -1;
        Depth = -1;
        NextAction = null;
    }
#endif
}
