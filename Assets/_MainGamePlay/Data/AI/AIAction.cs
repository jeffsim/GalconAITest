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
    NoAction_MaxDepth
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
    public float Score;
    public float ScoreBeforeSubActions;

    public AIActionType Type = AIActionType.DoNothing;
    public int Count;
    public AI_NodeState SourceNode;
    public AI_NodeState DestNode;

    // Build building
    public BuildingDefn BuildingToConstruct;

    // Attacking
    public AttackResult AttackResult;

#if DEBUG
    public DebugAIStateReasons DebugOutput_ScoreReasonsBeforeSubActions = new();
    public int DebugOutput_TriedActionNum; // for debug output purposes
    public int DebugOutput_RecursionNum; // for debug output purposes
    public int DebugOutput_Depth; // for debug output purposes
    public AIAction DebugOutput_NextAction; // Keep track of the optimal actions to perform after this one; only used for debugging

    public void Reset()
    {
        Score = 0;
        ScoreBeforeSubActions = 0;
        Count = 0;
        BuildingToConstruct = null;
        Type = AIActionType.DoNothing;
        SourceNode = null;
        DestNode = null;
        DebugOutput_ScoreReasonsBeforeSubActions.Reset();
        DebugOutput_TriedActionNum = -1;
        DebugOutput_Depth = -1;
        DebugOutput_NextAction = null;
    }

    public void TrackStrategyDebugInfoInAction(AIAction actionScore, DebugAIStateReasons debugOutput_actionScoreReasons, int thisActionNum, int recurseCount, int curDepth)
    {
        if (!GameMgr.Instance.DebugOutputStrategy)
            return;

        DebugOutput_NextAction = actionScore;
        DebugOutput_TriedActionNum = thisActionNum;
        DebugOutput_RecursionNum = recurseCount;
        DebugOutput_Depth = curDepth;
        if (GameMgr.Instance.DebugOutputStrategyFull)
            DebugOutput_ScoreReasonsBeforeSubActions = debugOutput_actionScoreReasons;
    }
#endif
}
