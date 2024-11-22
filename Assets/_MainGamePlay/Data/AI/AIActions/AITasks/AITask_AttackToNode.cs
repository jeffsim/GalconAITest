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
    const int MAX_NEIGHBORS_TO_CHECK = 10; // TODO: magic number

    AI_NodeState[] nDeepNeighbors = new AI_NodeState[MAX_NEIGHBORS_TO_CHECK];

    public AITask_AttackToNode(PlayerData player, AI_TownState aiTownState, int maxDepth, int minWorkersInNodeBeforeConsideringSendingAnyOut)
        : base(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut) { }

    public override AIAction TryTask(AI_NodeState toNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions)
    {
        var bestAction = player.AI.GetAIAction();

        if (toNode.OwnedBy == null || toNode.OwnedBy == player) return bestAction;

        // Collect all neighbor nodes up to N levels deep that are owned by the player and have more than [10] workers
        // note that nDeepNeighbors will be sorted by distance from toNode (since it's a BFS process)
        int num = GetFriendlyNeighborsWithEnoughWorkers(toNode, nDeepNeighbors);

        // Generate all combinations of the neighbor nodes to simulate multiple attacks
        var sourceNodeCombinations = GenerateNodeCombinations(nDeepNeighbors, num, toNode.NumWorkers);

        for (int i = 0; i < sourceNodeCombinations.Count; i++)
        {
            var sourceNodes = sourceNodeCombinations[i];
            // Confirm that there are enough workers in the combination of nodes to send any out
            // var totalNumSent = 0;
            // foreach (var node in sourceNodes)
            //     totalNumSent += node.NumWorkers;
            // if (totalNumSent < 20) // TODO: magic number
            //     continue;

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
            for (int a = attackStates.Count - 1; a >= 0; a--)
            {
                var attackState = attackStates[a];
                aiTownState.Undo_AttackFromNode(attackState.FromNode, attackState.ToNode, attackState.AttackResult,
                                                attackState.OrigNumInSourceNode, attackState.OrigNumInDestNode,
                                                attackState.NumSent, attackState.OrigToNodeOwner);
            }
        }

        return bestAction;
    }

    Queue<AI_NodeState> queue = new(10);
    HashSet<AI_NodeState> visited = new(10);
    const int MAX_DEPTH = 3;

    int GetFriendlyNeighborsWithEnoughWorkers(AI_NodeState toNode, AI_NodeState[] nDeepNeighbors)
    {
        int index = 0;
        int currentDepth = 0;

        visited.Clear();
        visited.Add(toNode);
        queue.Clear();
        queue.Enqueue(toNode);

        while (queue.Count > 0 && currentDepth < MAX_DEPTH && index < MAX_NEIGHBORS_TO_CHECK)
        {
            int nodesAtCurrentLevel = queue.Count;
            for (int i = 0; i < nodesAtCurrentLevel; i++)
            {
                var currentNode = queue.Dequeue();
                foreach (var neighbor in currentNode.NeighborNodes)
                    if (neighbor.OwnedBy == player && !visited.Contains(neighbor))
                    {
                        if (neighbor.NumWorkers >= minWorkersInNodeBeforeConsideringSendingAnyOut)
                            nDeepNeighbors[index++] = neighbor;
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
            }
            currentDepth++;
        }
        return index;
    }

    private List<List<AI_NodeState>> GenerateNodeCombinations(AI_NodeState[] nodes, int num, int numEnemies)
    {
        // Notes:
        //  * All of the nodes in the incoming nodes array have more than 10 workers; i.e. are valid to pull from.
        //  * The nodes in the incoming nodes array are sorted by distance from target node.  We want to prioritize closer nodes;

        // Find combinations that result in > numEnemies (TODO: real heuristic.  TODO: Optimize for one)
        List<List<AI_NodeState>> combinations = new();

        // TODO: Can I just grab the first combination that's added to combinations and return it?  The would be the set of source nodes
        // that (a) have > 10 workers, (b) are closest to the target node.  What it would miss is source nodes with even larger number of 
        // workers that are further away.

        // NOTE: maybe just take the top N (e.g. 3 or 10) and return that list?
        // TODO: Change combinations to an array of lists.

        int combinationCount = 1 << num; // 2^n combinations
        for (int i = 1; i < combinationCount; i++) // Start from 1 to exclude empty set
        {
            int count = 0;
            List<AI_NodeState> combination = new();
            for (int j = 0; j < num; j++)
                if ((i & (1 << j)) != 0)
                {
                    count += nodes[j].NumWorkers;
                    combination.Add(nodes[j]);
                }
            if (count > numEnemies)
                combinations.Add(combination);
        }

        return combinations;
    }
}
