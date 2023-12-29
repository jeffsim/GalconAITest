using UnityEngine;
using UnityEngine.Assertions.Must;

public partial class PlayerAI
{
    // Determine the best action that can be taken given the current aiTownState and return that action, ensuring
    // that aiTownState is fully restored to its original state before returning.
    // Actions a player-owned Node can take:
    // 1. Send 50% of workers to a node that neighbors the node
    // 2. Construct a building in a node we own. 
    AIAction RecursivelyDetermineBestAction(int curDepth = 0)
    {
#if DEBUG
      var recurseCount = ++debugOutput_callsToRecursivelyDetermineBestAction;
#endif
        Debug.Assert(debugOutput_ActionsTried < maxPoolSize, "stuck in loop in RecursivelyDetermineBestAction");
        AIAction bestAction = actionPool[actionPoolIndex++];
        float curStateScore = aiTownState.EvaluateScore(bestAction.DebugOutput_ScoreReasons);
        bestAction.ScoreBeforeSubActions = curStateScore;
        if (curDepth == maxDepth || aiTownState.IsGameOver())
        {
            bestAction.Type = curDepth == maxDepth ? AIActionType.NoAction_MaxDepth : AIActionType.NoAction_GameOver;
            bestAction.Score = curStateScore;
            return bestAction;
        }

        bestAction.Score = 0;
        for (int i = 0; i < aiTownState.NumNodes; i++)
        {
            var node = aiTownState.Nodes[i];
            if (node.OwnedBy != player) continue; // only process actions from/in nodes that we own

            TrySendWorkersToEmptyNode(node, ref bestAction, curDepth, recurseCount);
            TryConstructBuildingInNode(node, ref bestAction, curDepth, recurseCount);
        }

        if (bestAction.Score <= curStateScore)
        {
            // couldn't find an action that resulted in a better state; do nothing
            bestAction.Type = AIActionType.DoNothing; // ???
            bestAction.DebugOutput_NextAction = null;
            bestAction.Score = curStateScore;
        }

        return bestAction;
    }

    private void TrySendWorkersToEmptyNode(AI_NodeState fromNode, ref AIAction bestAction, int curDepth, int recurseCount)
    {
#if DEBUG
        var thisActionNum = ++debugOutput_ActionsTried;
#endif

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
                    bestAction.DebugOutput_TriedActionNum = thisActionNum;
                    bestAction.DebugOutput_RecursionNum = recurseCount;
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

    private void TryConstructBuildingInNode(AI_NodeState node, ref AIAction bestAction, int curDepth, int recurseCount)
    {
#if DEBUG
        var thisActionNum = ++debugOutput_ActionsTried;
#endif

        if (node.HasBuilding)
            return; // already has one

        // TODO: Only attempt to construct buildings that we have resources within 'reach' to build.
        for (int i = 0; i < numBuildingDefns; i++)
        {
            var buildingDefn = buildableBuildingDefns[i];
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
                bestAction.BuildingToConstruct = buildingDefn;
#if DEBUG
                if (GameMgr.Instance.DebugOutputStrategy)
                {
                    bestAction.DebugOutput_NextAction = actionScore;
                    bestAction.DebugOutput_TriedActionNum = thisActionNum;
                    bestAction.DebugOutput_RecursionNum = recurseCount;
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
