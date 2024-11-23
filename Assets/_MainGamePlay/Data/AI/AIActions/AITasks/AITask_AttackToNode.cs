using System.Collections.Generic;
using NUnit.Framework;

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

    // List<AttackState> attackStates;
    // List<AttackResult> attackResults;
    // Dictionary<AI_NodeState, int> attackFromNodes;

    public AITask_AttackToNode(PlayerData player, AI_TownState aiTownState, int maxDepth, int minWorkersInNodeBeforeConsideringSendingAnyOut)
        : base(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut) { }

    public override AIAction TryTask(AI_NodeState toNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions)
    {
        // TODO: Refactor TryTask to return bool; don't GetAIAction until we know we want to take this action

        var bestAction = player.AI.GetAIAction();

        if (toNode.OwnedBy == null || toNode.OwnedBy == player) return bestAction;

        // Collect all neighbor nodes up to N levels deep that are owned by the player and have more than [10] workers
        // note that nDeepNeighbors will be sorted by distance from toNode (since it's a BFS process)
        int num = GetFriendlyNeighborsWithEnoughWorkers(toNode, nDeepNeighbors);

        // Generate all combinations of the neighbor nodes to simulate multiple attacks
        bool haveEnoughWorkersToAttack = GetNodesToAttackFrom(nDeepNeighbors, num, toNode.NumWorkers);
        if (!haveEnoughWorkersToAttack) return bestAction;

        // Save original state before performing attacks
        List<AttackState> attackStates = new();
        var attackResults = new List<AttackResult>();
        var attackFromNodes = new Dictionary<AI_NodeState, int>();

        // (attackStates ??= new(MAX_NEIGHBORS_TO_CHECK)).Clear();
        // (attackResults ??= new(MAX_NEIGHBORS_TO_CHECK)).Clear();
        // (attackFromNodes ??= new(MAX_NEIGHBORS_TO_CHECK)).Clear();

        // Perform attacks from multiple nodes
        foreach (var fromNode in nodesToAttackFrom)
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
        // Reverse the order of attacks to undo properly
        for (int a = attackStates.Count - 1; a >= 0; a--)
        {
            var attackState = attackStates[a];
            aiTownState.Undo_AttackFromNode(attackState.FromNode, attackState.ToNode, attackState.AttackResult,
                                            attackState.OrigNumInSourceNode, attackState.OrigNumInDestNode,
                                            attackState.NumSent, attackState.OrigToNodeOwner);
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

    List<AI_NodeState> nodesToAttackFrom;

    bool GetNodesToAttackFrom(AI_NodeState[] nodes, int numNodes, int numEnemies)
    {
        // Notes:
        //  * All of the nodes in the incoming nodes array have more than 10 workers; i.e. are valid to pull from.
        //  * The nodes in the incoming nodes array are sorted by distance from target node.  We want to prioritize closer nodes;

        // Find combinations that result in > numEnemies (TODO: real heuristic.  TODO: Optimize for one)

        // TODO: Can I just grab the first combination that's added to combinations and return it?  The would be the set of source nodes
        // that (a) have > 10 workers, (b) are closest to the target node.  What it would miss is source nodes with even larger number of 
        // workers that are further away.

        // NOTE: maybe just take the top N (e.g. 3 or 10) and return that list?
        nodesToAttackFrom ??= new(10);
        nodesToAttackFrom.Clear();
        int count = 0;
        for (int i = 1; i < numNodes; i++) // Start from 1 to exclude empty set
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
