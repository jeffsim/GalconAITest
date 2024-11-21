using System.Collections.Generic;

class AttackState
{
    public AI_NodeState FromNode;
    public int OrigNumInSourceNode;
    public AI_NodeState ToNode;
    public int OrigNumInDestNode;
    public PlayerData OrigToNodeOwner;
    public int NumSent;
    public AttackResult AttackResult;
}
public class AITask_AttackToNode : AITask
{
    public AITask_AttackToNode(PlayerData player, AI_TownState aiTownState, int maxDepth, int minWorkersInNodeBeforeConsideringSendingAnyOut)
        : base(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut) { }

    public override AIAction TryTask(AI_NodeState toNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions)
    {
        var bestAction = player.AI.GetAIAction();

        if (toNode.OwnedBy == null || toNode.OwnedBy == player) return bestAction;

        // Collect all neighbor nodes up to N levels deep that are owned by the player
        List<AI_NodeState> nDeepNeighbors = GetDeepNeighborsWithPotentialAttackers(toNode);

        // Generate all combinations of the neighbor nodes to simulate multiple attacks
        var sourceNodeCombinations = GenerateNodeCombinations(nDeepNeighbors);

        foreach (var sourceNodes in sourceNodeCombinations)
        {
            // Confirm that there are enough workers in the combination of nodes to send any out
            var totalNumSent = 0;
            foreach (var node in sourceNodes)
                totalNumSent += node.NumWorkers;
            if (totalNumSent < 20) // TODO: magic number
                continue;

            // Save original state before performing attacks
            List<AttackState> attackStates = new();
            var attackResults = new List<AttackResult>();
            var numSentFromEachNode = new Dictionary<AI_NodeState, int>();

            // Perform attacks from multiple nodes
            foreach (var fromNode in sourceNodes)
            {
                // Record original state before attack
                var attackState = new AttackState
                {
                    FromNode = fromNode,
                    OrigNumInSourceNode = fromNode.NumWorkers,
                    ToNode = toNode,
                    OrigNumInDestNode = toNode.NumWorkers,
                    OrigToNodeOwner = toNode.OwnedBy
                };

                aiTownState.AttackFromNode(fromNode, toNode, out AttackResult attackResult, out _, out _, out int numSent, out _);
                attackResults.Add(attackResult);
                numSentFromEachNode[fromNode] = numSent;

                // Record the attack result and numSent
                attackState.NumSent = numSent;
                attackState.AttackResult = attackResult;

                // Add the attackState to the list
                attackStates.Add(attackState);
            }

            // Create a debugger entry
            var debuggerEntry = aiDebuggerParentEntry.AddEntry_AttackFromMultipleNodes(sourceNodes, toNode, attackResults, numSentFromEachNode, 0, player.AI.debugOutput_ActionsTried++, curDepth);

            // Determine the score of the combined action
            var actionScore = GetActionScore(curDepth, debuggerEntry);
            if (actionScore > bestAction.Score)
                bestAction.SetTo_AttackFromMultipleNodes(sourceNodes, toNode, numSentFromEachNode, attackResults, actionScore, debuggerEntry);

            // Undo the attacks to reset the state
            // Reverse the order of attacks to undo properly
            for (int i = attackStates.Count - 1; i >= 0; i--)
            {
                var attackState = attackStates[i];
                aiTownState.Undo_AttackFromNode(attackState.FromNode, attackState.ToNode, attackState.AttackResult,
                                                attackState.OrigNumInSourceNode, attackState.OrigNumInDestNode,
                                                attackState.NumSent, attackState.OrigToNodeOwner);
            }
        }

        return bestAction;
    }

    private List<AI_NodeState> GetDeepNeighborsWithPotentialAttackers(AI_NodeState toNode)
    {
        List<AI_NodeState> result = new();
        HashSet<AI_NodeState> visited = new() { toNode };
        Queue<(AI_NodeState Node, int Depth)> queue = new();
        queue.Enqueue((toNode, 0));

        while (queue.Count > 0)
        {
            var (currentNode, depth) = queue.Dequeue();
            if (depth >= 2) continue;

            foreach (var neighbor in currentNode.NeighborNodes)
            {
                if (!visited.Contains(neighbor) && neighbor.OwnedBy == player)
                {
                    visited.Add(neighbor);
                    if (neighbor.NumWorkers > 10)  // TODO: Magic number
                        result.Add(neighbor);
                    queue.Enqueue((neighbor, depth + 1));
                }
            }
        }
        return result;
    }

    private List<List<AI_NodeState>> GenerateNodeCombinations(List<AI_NodeState> nodes)
    {
        List<List<AI_NodeState>> combinations = new();

        int combinationCount = 1 << nodes.Count; // 2^n combinations
        for (int i = 1; i < combinationCount; i++) // Start from 1 to exclude empty set
        {
            List<AI_NodeState> combination = new();
            for (int j = 0; j < nodes.Count; j++)
            {
                if ((i & (1 << j)) != 0)
                {
                    combination.Add(nodes[j]);
                }
            }
            combinations.Add(combination);
        }

        return combinations;
    }
}
