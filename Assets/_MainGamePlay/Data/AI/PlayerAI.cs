using UnityEngine;

public partial class PlayerAI
{
    PlayerData player;
    AI_TownState aiTownState;
    int minWorkersInNodeBeforeConsideringSendingAnyOut = 3;
    int maxDepth = 8;
    int steps;

    public PlayerAI(PlayerData playerData)
    {
        player = playerData;
        aiTownState = new AI_TownState(player);
    }

    public void InitializeStaticData(TownData townData)
    {
        aiTownState.InitializeStaticData(townData);
    }

    internal void Update(TownData townData)
    {
        aiTownState.UpdateState(townData);

        // Determine the best action to take, and then take it
        steps = 0;
        var bestAction = RecursivelyDetermineBestAction();
        Debug.Log(steps);
        performAction(bestAction);
    }

    // Actions a player-owned Node can take:
    // 1. Send 50% of workers to a node that neighbors the node
    // 2. Construct a building in a node we own.
    AIAction RecursivelyDetermineBestAction(int curDepth = 0)
    {
        steps++;
        Debug.Assert(steps < 10000, "stuck in loop in RecursivelyDetermineBestAction");

        // Keep track of the best action in this 'level' of the AI stack, and return it
        AIAction bestAction = new(); // todo. Pool?  Or just precreate a static list of 100000 of them and use + increment indexer (so no push/pop)? etc
        bestAction.Score = aiTownState.EvaluateScore();

        if (curDepth == maxDepth || aiTownState.IsGameOver())
            return bestAction;

        foreach (var node in aiTownState.Nodes)
        {
            if (node.OwnedBy != player) continue; // only process nodes that we own

            TrySendWorkersToEmptyNode(node, ref bestAction, curDepth);
            TryConstructBuildingInNode(node, ref bestAction, curDepth);
        }
        return bestAction;
    }

    // Try constructing a building in the specified node
    private void TryConstructBuildingInNode(AI_NodeState node, ref AIAction bestAction, int curDepth)
    {
        if (node.HasBuilding)
            return; // already has one

        // Only attempt to construct buildings that we have resources within 'reach' to build.
        foreach (var buildingDefn in GameDefns.Instance.BuildingDefns.Values)
        {
            if (!buildingDefn.CanBeBuiltByPlayer)
                continue;
            if (!aiTownState.ConstructionResourcesCanBeReachedFromNode(node, buildingDefn.ConstructionRequirements))
                continue;

            // Update the townstate to reflect building the building, and consume the resources for it
            aiTownState.BuildBuilding(node, buildingDefn, out GoodDefn resource1, out int resource1Amount, out GoodDefn resource2, out int resource2Amount);

            // Recursively determine the value of this action
            var actionScore = RecursivelyDetermineBestAction(curDepth + 1);
            if (actionScore.Score > bestAction.Score)
            {
                // This is the best action so far; save the action so we can return it
                bestAction.Score = actionScore.Score;
                bestAction.Type = AIActionType.ConstructBuildingInOwnedNode;
                bestAction.SourceNode = node;
                bestAction.BuildingToConstruct = buildingDefn;
#if DEBUG
                bestAction.NextAction = actionScore; // track so I can output the next N steps in the optimal strategy
#endif
            }

            // Undo the action
            aiTownState.Undo_BuildBuilding(node, resource1, resource1Amount, resource2, resource2Amount);
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

        foreach (var toNode in fromNode.NeighborNodes)
        {
            if (aiTownState.HaveSentWorkersToNode.ContainsKey(toNode.NodeId))
                continue; // don't send workers to the same node twice in an AI stack.  This disallows some odd strategies but cleans up lots of odd stuff

            if (toNode.HasBuilding)
                continue; // Only sending to empty nodes here.  Buttressing nodes is handled in another action

            if (toNode.OwnedBy != null && toNode.OwnedBy != player)
                continue; // Can't send workers to nodes owned by other players (that's handled separately via Attack action)

            if (toNode.IsResourceNode)
                continue; // Can't send to resource nodes

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
#endif
            }

            // Undo the action
            aiTownState.Undo_SendWorkersToEmptyNode(fromNode, toNode, numSent);
        }
    }
}
