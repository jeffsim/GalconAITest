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

    // Reset method to clear data
    public void Reset()
    {
        FromNode = null;
        OrigNumInSourceNode = 0;
        ToNode = null;
        OrigNumInDestNode = 0;
        OrigToNodeOwner = null;
        NumSent = 0;
        AttackResult = default;
    }
}

public class AITask_AttackToNode : AITask
{
    const int MAX_NEIGHBORS_TO_CHECK = 10;

    AI_NodeState[] nDeepNeighbors = new AI_NodeState[MAX_NEIGHBORS_TO_CHECK];

    // Pools for reusing collections and AttackState instances
    Stack<List<AttackState>> attackStatesPool = new Stack<List<AttackState>>();
    Stack<List<AttackResult>> attackResultsPool = new Stack<List<AttackResult>>();
    Stack<Dictionary<AI_NodeState, int>> attackFromNodesPool = new Stack<Dictionary<AI_NodeState, int>>();
    Stack<AttackState> attackStatePool = new Stack<AttackState>();

    public AITask_AttackToNode(PlayerData player, AI_TownState aiTownState, int maxDepth, int minWorkersInNodeBeforeConsideringSendingAnyOut)
        : base(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut) { }

    public override AIAction TryTask(AI_NodeState toNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions)
    {
        var bestAction = player.AI.GetAIAction();

        if (toNode.OwnedBy == null || toNode.OwnedBy == player) return bestAction;

        // Collect neighbor nodes
        int num = GetFriendlyNeighborsWithEnoughWorkers(toNode, nDeepNeighbors);

        // Generate nodes to attack from
        bool haveEnoughWorkersToAttack = GetNodesToAttackFrom(nDeepNeighbors, num, toNode.NumWorkers);
        if (!haveEnoughWorkersToAttack) return bestAction;

        // Get reusable collections from the pool or create new ones
        List<AttackState> attackStates = attackStatesPool.Count > 0 ? attackStatesPool.Pop() : new List<AttackState>();
        List<AttackResult> attackResults = attackResultsPool.Count > 0 ? attackResultsPool.Pop() : new List<AttackResult>();
        Dictionary<AI_NodeState, int> attackFromNodes = attackFromNodesPool.Count > 0 ? attackFromNodesPool.Pop() : new Dictionary<AI_NodeState, int>();

        // Clear collections before use
        attackStates.Clear();
        attackResults.Clear();
        attackFromNodes.Clear();

        // Perform attacks from multiple nodes
        foreach (var fromNode in nodesToAttackFrom)
        {
            // Get an AttackState instance from the pool or create a new one
            AttackState attackState = attackStatePool.Count > 0 ? attackStatePool.Pop() : new AttackState();

            // Reset the attackState to clear any previous data
            attackState.Reset();

            // Record original state before attack
            attackState.FromNode = fromNode;
            attackState.OrigNumInSourceNode = fromNode.NumWorkers;
            attackState.ToNode = toNode;
            attackState.OrigNumInDestNode = toNode.NumWorkers;
            attackState.OrigToNodeOwner = toNode.OwnedBy;

            aiTownState.AttackFromNode(fromNode, toNode, out AttackResult attackResult, out _, out _, out int numSent, out _);
            attackResults.Add(attackResult);
            attackFromNodes[fromNode] = numSent;

            // Record the attack result and numSent
            attackState.NumSent = numSent;
            attackState.AttackResult = attackResult;

            // Add the attackState to the list
            attackStates.Add(attackState);
        }

        // Create a debugger entry
        var debuggerEntry = aiDebuggerParentEntry.AddEntry_AttackFromMultipleNodes(attackFromNodes, toNode, attackResults, 0, player.AI.debugOutput_ActionsTried++, curDepth);

        // Determine the score of the combined action
        var actionScore = GetActionScore(curDepth, debuggerEntry);
        if (actionScore > bestAction.Score)
            bestAction.SetTo_AttackFromMultipleNodes(attackFromNodes, toNode, attackResults, actionScore, debuggerEntry);

        // Undo the attacks to reset the state
        for (int a = attackStates.Count - 1; a >= 0; a--)
        {
            var attackState = attackStates[a];
            aiTownState.Undo_AttackFromNode(attackState.FromNode, attackState.ToNode, attackState.AttackResult,
                                            attackState.OrigNumInSourceNode, attackState.OrigNumInDestNode,
                                            attackState.NumSent, attackState.OrigToNodeOwner);

            // Return the AttackState instance to the pool
            attackStatePool.Push(attackState);
        }

        // Return collections to the pool
        attackStatesPool.Push(attackStates);
        attackResultsPool.Push(attackResults);
        attackFromNodesPool.Push(attackFromNodes);

        return bestAction;
    }

    Queue<AI_NodeState> queue = new Queue<AI_NodeState>(10);
    HashSet<AI_NodeState> visited = new HashSet<AI_NodeState>(10);
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

    List<AI_NodeState> nodesToAttackFrom = new List<AI_NodeState>(10);

    bool GetNodesToAttackFrom(AI_NodeState[] nodes, int numNodes, int numEnemies)
    {
        nodesToAttackFrom.Clear();
        int count = 0;
        for (int i = 0; i < numNodes; i++)
        {
            var node = nodes[i];
            nodesToAttackFrom.Add(node);
            count += node.NumWorkers;
            if (count > numEnemies)
                return true;
        }
        return false;
    }
}
