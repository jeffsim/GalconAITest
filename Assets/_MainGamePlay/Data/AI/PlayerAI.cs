using UnityEngine;

public partial class PlayerAI
{
    PlayerData player;
    AI_TownState aiTownState;
    int minWorkersInNodeBeforeConsideringSendingAnyOut = 3;
    int maxDepth;
    int steps;

    AIAction[] actionPool;
    int actionPoolIndex;
    int maxPoolSize = 100000;

    BuildingDefn[] buildingDefns;
    int numBuildingDefns;

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
        maxDepth = GameMgr.Instance.MaxAISteps;

        aiTownState.UpdateState(townData);

        // Determine the best action to take, and then take it
        steps = -1;
        actionPoolIndex = 0;

        var bestAction = RecursivelyDetermineBestAction();
        if (GameMgr.Instance.DebugOutputStrategy)
            Debug.Log(steps);
        performAction(bestAction);
    }

    // Determine the best action that can be taken given the current aiTownState and return that action, ensuring
    // that aiTownState is fully restored to its original state before returning.
    // Actions a player-owned Node can take:
    // 1. Send 50% of workers to a node that neighbors the node
    // 2. Construct a building in a node we own.
    AIAction RecursivelyDetermineBestAction(int curDepth = 0)
    {
        steps++;
        Debug.Assert(steps < 100000, "stuck in loop in RecursivelyDetermineBestAction");

        AIAction bestAction = actionPool[actionPoolIndex++];
#if DEBUG
        if (GameMgr.Instance.DebugOutputStrategy)
            bestAction.ScoreReasons.Reset();
        float curStateScore = aiTownState.EvaluateScore(bestAction.ScoreReasons);
#else
        float curStateScore = aiTownState.EvaluateScore();
#endif
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
            if (node.OwnedBy != player) continue; // only process nodes that we own

            TrySendWorkersToEmptyNode(node, ref bestAction, curDepth);
            TryConstructBuildingInNode(node, ref bestAction, curDepth);
        }
        bestAction.Score += curStateScore;
        return bestAction;
    }

    // Try constructing a building in the specified node
    private void TryConstructBuildingInNode(AI_NodeState node, ref AIAction bestAction, int curDepth)
    {
        if (node.HasBuilding)
            return; // already has one

        // Only attempt to construct buildings that we have resources within 'reach' to build.
        for (int i = 0; i < numBuildingDefns; i++)
        {
            var buildingDefn = buildingDefns[i];
            if (!buildingDefn.CanBeBuiltByPlayer)
                continue;
            if (!aiTownState.ConstructionResourcesCanBeReachedFromNode(node, buildingDefn.ConstructionRequirements))
                continue;

            // Update the townstate to reflect building the building, and consume the resources for it
            aiTownState.BuildBuilding(node, buildingDefn, out GoodType res1Id, out int resource1Amount, out GoodType res2Id, out int resource2Amount);

            // Recursively determine the value of this action
            var actionScore = RecursivelyDetermineBestAction(curDepth + 1);
            if (actionScore.Score > bestAction.Score)
            {
                // This is the best action so far; save the action so we can return it
                bestAction.Score = actionScore.Score;
                bestAction.Type = AIActionType.ConstructBuildingInOwnedNode;
                bestAction.SourceNode = node;
                bestAction.BuildingToConstruct = buildingDefn.Id;
#if DEBUG
                bestAction.NextAction = actionScore; // track so I can output the next N steps in the optimal strategy
                bestAction.StepNum = steps;
                bestAction.Depth = curDepth;
#endif
            }

            // Undo the action
            aiTownState.Undo_BuildBuilding(node, res1Id, resource1Amount, res2Id, resource2Amount);
        }
    }

    // Try expanding to neighboring empty nodes.
    private void TrySendWorkersToEmptyNode(AI_NodeState fromNode, ref AIAction bestAction, int curDepth)
    {
        if (fromNode.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut)
            return; // not enough workers in node to send any out

        if (!fromNode.HasBuilding)
            return; // Must have a building in a node to send workers from it 

        //  if (aiTownState.HaveSentWorkersFromNode.ContainsKey(node.NodeId))
        //   if (aiTownState.HaveSentWorkersToNode.ContainsKey(node.NodeId))
        //     return; // don't send workers from the same node twice in an AI stack, or from a node we sent workers to in the stack.

        var count = fromNode.NeighborNodes.Count;
        for (int i = 0; i < count; i++)
        {
            var toNode = fromNode.NeighborNodes[i];
            // if (aiTownState.HaveSentWorkersToNode.ContainsKey(toNode.NodeId))
            //     continue; // don't send workers to the same node twice in an AI stack.  This disallows some odd strategies but cleans up lots of odd stuff

            // if (toNode.HasBuilding)
            //     continue; // Only sending to empty nodes here.  Buttressing nodes is handled in another action

            if (toNode.OwnedBy != null)
                continue; // This task can't send workers to nodes owned by anyone (including this player).  Those are handled in other actions

            if (toNode.IsResourceNode)
                continue; // Can't send to resource nodes

            Debug.Assert(toNode.NumWorkers == 0);
            Debug.Assert(toNode.OwnedBy == null);
            aiTownState.SendWorkersToEmptyNode(fromNode, toNode, .5f, out int numSent);

            // Recursively determine the value of this action.
            var actionScore = RecursivelyDetermineBestAction(curDepth + 1);
            if (actionScore.Score > bestAction.Score)
            {
                // This is the best action so far in this 'level' of the AI stack; save the action so we can return it
                bestAction.Score = actionScore.Score;
                bestAction.Type = AIActionType.SendWorkersToNode;
                bestAction.Count = numSent;
                bestAction.SourceNode = fromNode;
                bestAction.DestNode = toNode;
#if DEBUG
                bestAction.NextAction = actionScore; // track so I can output the next N steps in the optimal strategy
                bestAction.StepNum = steps;
                bestAction.Depth = curDepth;

                if (GameMgr.Instance.DebugOutputStrategy)
                    bestAction.ScoreReasons.CopyFrom(actionScore.ScoreReasons);
#endif
            }

            // Undo the action
            aiTownState.Undo_SendWorkersToEmptyNode(fromNode, toNode, numSent);
        }
    }
}
