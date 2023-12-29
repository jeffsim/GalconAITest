using UnityEngine;

public partial class PlayerAI
{
    PlayerData player;
    AI_TownState aiTownState;
    int minWorkersInNodeBeforeConsideringSendingAnyOut = 3;
    int maxDepth;
    int debugOutput_ActionsTried;
    int debugOutput_callsToRecursivelyDetermineBestAction;

    AIAction[] actionPool;
    int actionPoolIndex;
    int maxPoolSize = 100000;

    BuildingDefn[] buildingDefns;
    int numBuildingDefns;

#if DEBUG
    int lastMaxDepth = -1;
#endif

    public PlayerAI(PlayerData playerData)
    {
        player = playerData;
        aiTownState = new AI_TownState(player);

        // Create pool of actions to avoid allocs
        actionPool = new AIAction[maxPoolSize];
        for (int i = 0; i < maxPoolSize; i++)
            actionPool[i] = new AIAction();

        // Convert dictionary to array for speed
        buildingDefns = new BuildingDefn[GameDefns.Instance.BuildingDefns.Count];
        numBuildingDefns = 0;
        foreach (var buildingDefn in GameDefns.Instance.BuildingDefns.Values)
            buildingDefns[numBuildingDefns++] = buildingDefn;
    }

    public void InitializeStaticData(TownData townData)
    {
        aiTownState.InitializeStaticData(townData);
    }

    internal void Update(TownData townData)
    {
        maxDepth = GameMgr.Instance.MaxAIDepth;

        aiTownState.UpdateState(townData);

#if DEBUG
        if (lastMaxDepth != GameMgr.Instance.MaxAIDepth)
        {
            lastMaxDepth = GameMgr.Instance.MaxAIDepth;
            ConsoleClearer.ClearConsole();
        }

        if (GameMgr.Instance.DebugOutputStrategyFull)
        {
            for (int i = 0; i < actionPool.Length; i++)
                actionPool[i].Reset();
        }
#endif

        // Determine the best action to take, and then take it
        debugOutput_ActionsTried = -1;
        debugOutput_callsToRecursivelyDetermineBestAction = -1;
        actionPoolIndex = 0;

        var bestAction = RecursivelyDetermineBestAction();
        if (GameMgr.Instance.DebugOutputStrategyFull)
            Debug.Log("Actions Tried: " + debugOutput_ActionsTried + "; Recursions:" + debugOutput_callsToRecursivelyDetermineBestAction);
        performAction(bestAction);
    }

    // Determine the best action that can be taken given the current aiTownState and return that action, ensuring
    // that aiTownState is fully restored to its original state before returning.
    // Actions a player-owned Node can take:
    // 1. Send 50% of workers to a node that neighbors the node
    // 2. Construct a building in a node we own.
    AIAction RecursivelyDetermineBestAction(int curDepth = 0)
    {
        Debug.Assert(debugOutput_ActionsTried < 100000, "stuck in loop in RecursivelyDetermineBestAction");

#if DEBUG
        debugOutput_callsToRecursivelyDetermineBestAction++;
#endif

        AIAction bestAction = actionPool[actionPoolIndex++];
        float curStateScore = aiTownState.EvaluateScore(bestAction.DebugOutput_ScoreReasons);
        bestAction.ScoreBeforeSubActions = curStateScore;
        if (curDepth == maxDepth || aiTownState.IsGameOver())
        {
            bestAction.Type = AIActionType.DoNothing; // ???
            bestAction.Score = curStateScore;
            return bestAction;
        }

        bestAction.Score = 0;
        for (int i = 0; i < aiTownState.NumNodes; i++)
        {
            var node = aiTownState.Nodes[i];
            if (node.OwnedBy != player) continue; // only process actions from/in nodes that we own

            TrySendWorkersToEmptyNode(node, ref bestAction, curDepth);
            TryConstructBuildingInNode(node, ref bestAction, curDepth);
        }

        if (bestAction.Score <= curStateScore)
        {
            // couldn't find an action that resulted in a better state; do nothing
            bestAction.Type = AIActionType.DoNothing; // ???
            bestAction.Score = curStateScore;
        }

        return bestAction;
    }

    private void TrySendWorkersToEmptyNode(AI_NodeState fromNode, ref AIAction bestAction, int curDepth)
    {
        debugOutput_ActionsTried++;

        if (fromNode.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut)
            return; // not enough workers in node to send any out

        if (!fromNode.HasBuilding)
            return; // Must have a building in a node to send workers from it 

        var count = fromNode.NumNeighbors;
        for (int i = 0; i < count; i++)
        {
            var toNode = fromNode.NeighborNodes[i];
            if (toNode.OwnedBy != null) continue; // This task can't send workers to nodes owned by anyone (including this player).  Those are handled in other actions
            if (toNode.IsResourceNode) continue; // Can't send to resource nodes

            Debug.Assert(toNode.NumWorkers == 0);
            Debug.Assert(toNode.OwnedBy == null);
            aiTownState.SendWorkersToEmptyNode(fromNode, toNode, .5f, out int numSent);

#if DEBUG
            DebugAIStateReasons debugOutput_actionScoreReasons = null;
            if (GameMgr.Instance.DebugOutputStrategyFull)
                debugOutput_actionScoreReasons = new();
#endif
            // TODO: Can I avoid this extra call to EvaluateScore()?
            float actionScoreAfterOurActionButBeforeSubActions = aiTownState.EvaluateScore(debugOutput_actionScoreReasons);

            // Recursively determine the value of this action.
            var actionScore = RecursivelyDetermineBestAction(curDepth + 1);
            if (actionScore.Score > bestAction.Score)
            {
                // This is the best action so far in this 'level' of the AI stack; save the action so we can return it
                bestAction.Score = actionScoreAfterOurActionButBeforeSubActions;
                bestAction.Type = AIActionType.SendWorkersToNode;
                bestAction.Count = numSent;
                bestAction.SourceNode = fromNode;
                bestAction.DestNode = toNode;
#if DEBUG
                if (GameMgr.Instance.DebugOutputStrategy)
                {
                    bestAction.DebugOutput_NextAction = actionScore;
                    bestAction.DebugOutput_TriedActionNum = debugOutput_ActionsTried;
                    bestAction.DebugOutput_RecursionNum = debugOutput_callsToRecursivelyDetermineBestAction;
                    bestAction.DebugOutput_Depth = curDepth;
                    if (GameMgr.Instance.DebugOutputStrategyFull)
                        bestAction.DebugOutput_ScoreReasons = debugOutput_actionScoreReasons;
                }
#endif
            }

            // Undo the action
            aiTownState.Undo_SendWorkersToEmptyNode(fromNode, toNode, numSent);
        }
    }

    private void TryConstructBuildingInNode(AI_NodeState node, ref AIAction bestAction, int curDepth)
    {
        debugOutput_ActionsTried++;

        if (node.HasBuilding)
            return; // already has one

        // TODO: Only attempt to construct buildings that we have resources within 'reach' to build.
        for (int i = 0; i < numBuildingDefns; i++)
        {
            var buildingDefn = buildingDefns[i];
            if (!buildingDefn.CanBeBuiltByPlayer) continue;
            if (!aiTownState.ConstructionResourcesCanBeReachedFromNode(node, buildingDefn.ConstructionRequirements)) continue;

            // Update the townstate to reflect building the building, and consume the resources for it
            aiTownState.BuildBuilding(node, buildingDefn, out GoodType res1Id, out int resource1Amount, out GoodType res2Id, out int resource2Amount);

#if DEBUG
            DebugAIStateReasons debugOutput_actionScoreReasons = null;
            if (GameMgr.Instance.DebugOutputStrategyFull)
                debugOutput_actionScoreReasons = new();
#endif
            // TODO: Can I avoid this extra call to EvaluateScore()?
            float actionScoreAfterOurActionButBeforeSubActions = aiTownState.EvaluateScore(debugOutput_actionScoreReasons);

            // Recursively determine the value of this action
            var actionScore = RecursivelyDetermineBestAction(curDepth + 1);
            if (actionScore.Score > bestAction.Score)
            {
                // This is the best action so far; save the action so we can return it
                bestAction.Score = actionScoreAfterOurActionButBeforeSubActions;
                bestAction.Type = AIActionType.ConstructBuildingInOwnedNode;
                bestAction.SourceNode = node;
                bestAction.BuildingToConstruct = buildingDefn.Id;
#if DEBUG
                if (GameMgr.Instance.DebugOutputStrategy)
                {
                    bestAction.DebugOutput_NextAction = actionScore;
                    bestAction.DebugOutput_TriedActionNum = debugOutput_ActionsTried;
                    bestAction.DebugOutput_RecursionNum = debugOutput_callsToRecursivelyDetermineBestAction;
                    bestAction.DebugOutput_Depth = curDepth;
                    if (GameMgr.Instance.DebugOutputStrategyFull)
                        bestAction.DebugOutput_ScoreReasons = debugOutput_actionScoreReasons;
                }
#endif
            }

            // Undo the action
            aiTownState.Undo_BuildBuilding(node, res1Id, resource1Amount, res2Id, resource2Amount);
        }
    }
}
