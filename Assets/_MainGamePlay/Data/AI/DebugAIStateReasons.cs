using System;
using System.Collections.Generic;
using System.Diagnostics;
using Codice.CM.SEIDInfo;

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
