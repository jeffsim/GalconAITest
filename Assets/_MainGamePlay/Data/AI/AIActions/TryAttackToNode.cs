using System;
using System.Collections.Generic;

public partial class PlayerAI
{
    private AIAction TryAttackToNode(AI_NodeState toNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions)
    {
        var bestAction = new AIAction() { Type = AIActionType.DoNothing };

        // toNode is owned by an enemy of 'player'.  We can attack the node if there's a path to the node AND
        // we either have one node with enough workers to attack, or we have multiple nodes that can together send workers to attack
        var nodesToAttackFromWithDistance = GetNodesToAttackFrom(toNode);
        if (nodesToAttackFromWithDistance.Count == 0)
            return bestAction; // no nodes can attack this node

        // are any neighbors owned by another player?
        // foreach (var toNode in fromNode.NeighborNodes)
        // {
        //     // ==== Verify we can perform the action
        //     if (toNode.OwnedBy == null || toNode.OwnedBy == player) continue;

        //     // ==== Perform the action and update the aiTownState to reflect the action
        //     aiTownState.AttackFromNode(fromNode, toNode, out AttackResult attackResult, out int origNumInSourceNode, out int origNumInDestNode, out int numSent, out PlayerData origToNodeOwner);
        //     var debuggerEntry = aiDebuggerParentEntry.AddEntry_AttackFromNode(fromNode, toNode, attackResult, numSent, 0, debugOutput_ActionsTried++, curDepth);
        //     // debuggerEntry.Debug_ActionScoreBeforeSubactions = aiTownState.EvaluateScore(curDepth, maxDepth, out _);

        //     // ==== Determine the score of the action we just performed; recurse down into subsequent actions if we're not at the max depth
        //     float actionScore;
        //     AIAction bestNextAction = curDepth < maxDepth ? DetermineBestActionToPerform(curDepth + 1, debuggerEntry) : null;
        //     if (bestNextAction != null)
        //         actionScore = bestNextAction.Score; // Score of the best action after this action
        //     else
        //         actionScore = aiTownState.EvaluateScore(curDepth, maxDepth, out _); // Evaluate score of the current state after this action
        //     debuggerEntry.FinalActionScore = actionScore;

        //     // ==== If this action is the best so far amongst our peers (in our parent node) then track it as the best action
        //     if (actionScore > bestAction.Score)
        //         bestAction.SetTo_AttackFromNode(fromNode, toNode, numSent, attackResult, actionScore, debuggerEntry);

        //     // ==== Undo the action to reset the townstate to its original state
        //     aiTownState.Undo_AttackFromNode(fromNode, toNode, attackResult, origNumInSourceNode, origNumInDestNode, numSent, origToNodeOwner);
        // }
        return bestAction;
    }

    private List<Tuple<AI_NodeState, int>> GetNodesToAttackFrom(AI_NodeState toNode)
    {
        var nodes = new List<Tuple<AI_NodeState, int>>();
        var visited = new HashSet<AI_NodeState>();
        int maxDistanceToAttackFrom = 3;
        
        void Recurse(AI_NodeState currentNode, int distance)
        {
            if (visited.Contains(currentNode))
                return;

            visited.Add(currentNode);

            if (currentNode.OwnedBy == player)
                nodes.Add(new(currentNode, distance));

            if (distance < maxDistanceToAttackFrom)
                foreach (var neighbor in currentNode.NeighborNodes)
                    if (neighbor.OwnedBy == player)
                        Recurse(neighbor, distance + 1);
        }

        Recurse(toNode, 0);

        return nodes;
    }
}
