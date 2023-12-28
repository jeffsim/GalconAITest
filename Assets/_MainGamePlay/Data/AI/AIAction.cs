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
        foreach (var reason in ScoresFrom_NodesOwned)
            str += " | Node owned: " + reason.Node.NodeId + " (" + reason.ScoreValue + ")";
        foreach (var reason in ScoresFrom_NumEmptyNodesOwned)
            str += " | Empty Node owned: " + reason.Node.NodeId + " (" + reason.ScoreValue + ")";
        foreach (var reason in ScoresFrom_ResourceGatherersCloseToResourceNodes)
            str += " | Resource gatherer near resource: " + reason.Node.NodeId + " (" + reason.ScoreValue + ")";
        return str;
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
#endif
}
