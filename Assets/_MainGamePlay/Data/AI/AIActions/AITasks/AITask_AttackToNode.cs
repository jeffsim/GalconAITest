using System;
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

    Stack<List<AttackState>> attackStatesPool = new Stack<List<AttackState>>();
    Stack<List<AttackResult>> attackResultsPool = new Stack<List<AttackResult>>();
    Stack<Dictionary<AI_NodeState, int>> attackFromNodesPool = new Stack<Dictionary<AI_NodeState, int>>();
    Stack<AttackState> attackStatePool = new Stack<AttackState>();

    public AITask_AttackToNode(PlayerData player, AI_TownState aiTownState, int maxDepth, int minWorkersInNodeBeforeConsideringSendingAnyOut)
        : base(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut) { }

    public override float PreviewHeuristic(AI_NodeState toNode)
    {
        // Neutral expansion is ConstructBuilding (send workers + build), not AttackToNode.
        if (toNode.OwnedBy == null) return 0f;
        if (toNode.OwnedBy == player) return 0f;
        // Don't pile on if we already have enough in-flight to take this node.
        if (toNode.AttackAlreadySufficient) return 0f;

        int num = GetFriendlyNeighborsWithEnoughWorkers(toNode, nDeepNeighbors);
        if (!TryPlanAttackAllocations(nDeepNeighbors, num, toNode.NumWorkers, GetAttackOverkillMultiplier(), attackPlanScratch, out int totalPlanned))
            return 0f;

        float h = AI_ActionHeuristics.GetAttackHeuristic(aiTownState, toNode, totalPlanned);
        if (h <= 0f) return 0f;

        return h * AI_ActionHeuristics.GetPersonalityMultiplier(player, AIHeuristicActionType.Attack);
    }

    override public bool TryTask(AI_NodeState toNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions, out AIAction bestAction)
    {
        bestAction = null;

        // Only enemy-owned nodes. Neutral territory is captured by constructing a building
        // (AITask_ConstructBuilding), not by walking workers onto an empty node.
        if (toNode.OwnedBy == null || toNode.OwnedBy == player) return false;
        // Don't pile on if we already have enough in-flight to take this node.
        if (toNode.AttackAlreadySufficient) return false;

        int num = GetFriendlyNeighborsWithEnoughWorkers(toNode, nDeepNeighbors);

        Dictionary<AI_NodeState, int> attackFromNodes = attackFromNodesPool.Count > 0 ? attackFromNodesPool.Pop() : new Dictionary<AI_NodeState, int>();
        attackFromNodes.Clear();
        if (!TryPlanAttackAllocations(nDeepNeighbors, num, toNode.NumWorkers, GetAttackOverkillMultiplier(), attackFromNodes, out int totalPlanned))
        {
            attackFromNodesPool.Push(attackFromNodes);
            return false;
        }

        float heuristicBonus = AI_ActionHeuristics.GetAttackHeuristic(aiTownState, toNode, totalPlanned);
        if (heuristicBonus <= 0f)
        {
            attackFromNodesPool.Push(attackFromNodes);
            return false;
        }

        if (ShouldPruneByHeuristic(heuristicBonus, AIHeuristicActionType.Attack, bestScoreAmongPeerActions))
        {
            attackFromNodesPool.Push(attackFromNodes);
            return false;
        }

        bestAction = player.AI.GetAIAction();

        List<AttackState> attackStates = attackStatesPool.Count > 0 ? attackStatesPool.Pop() : new List<AttackState>();
        List<AttackResult> attackResults = attackResultsPool.Count > 0 ? attackResultsPool.Pop() : new List<AttackResult>();

        attackStates.Clear();
        attackResults.Clear();

        foreach (var kvp in attackFromNodes)
        {
            var fromNode = kvp.Key;
            int numToSend = kvp.Value;
            AttackState attackState = attackStatePool.Count > 0 ? attackStatePool.Pop() : new AttackState();
            attackState.Reset();

            attackState.FromNode = fromNode;
            attackState.OrigNumInSourceNode = fromNode.NumWorkers;
            attackState.ToNode = toNode;
            attackState.OrigNumInDestNode = toNode.NumWorkers;
            attackState.OrigToNodeOwner = toNode.OwnedBy;

            aiTownState.AttackFromNode(fromNode, toNode, numToSend, out AttackResult attackResult, out _, out _, out int numSent, out _);
            attackResults.Add(attackResult);

            attackState.NumSent = numSent;
            attackState.AttackResult = attackResult;
            attackStates.Add(attackState);
        }

        var debuggerEntry = aiDebuggerParentEntry?.AddEntry_AttackToNode(attackFromNodes, toNode, attackResults, 0, player.AI.debugOutput_ActionsTried++, curDepth);

        var actionScore = GetActionScore(curDepth, debuggerEntry);
        actionScore = AI_ActionHeuristics.ApplyHeuristicAndPersonality(actionScore, heuristicBonus, player, AIHeuristicActionType.Attack);
        if (actionScore > bestAction.Score)
            bestAction.SetTo_AttackToNode(attackFromNodes, toNode, attackResults, actionScore, debuggerEntry);

        for (int a = attackStates.Count - 1; a >= 0; a--)
        {
            var attackState = attackStates[a];
            aiTownState.Undo_AttackFromNode(attackState.FromNode, attackState.ToNode, attackState.AttackResult,
                                            attackState.OrigNumInSourceNode, attackState.OrigNumInDestNode,
                                            attackState.NumSent, attackState.OrigToNodeOwner);
            attackStatePool.Push(attackState);
        }

        attackStatesPool.Push(attackStates);
        attackResultsPool.Push(attackResults);
        attackFromNodesPool.Push(attackFromNodes);

        return true;
    }

    Queue<AI_NodeState> queue = new Queue<AI_NodeState>(10);
    HashSet<AI_NodeState> visited = new HashSet<AI_NodeState>(10);
    const int MAX_DEPTH = 4;

    // Max hop distance from target at which sources are considered "close enough" to form
    // a coordinated wave. Sources within this range must collectively meet the target force
    // requirement on their own. Sources beyond this range are bonus overkill only.
    const int COORDINATED_WAVE_MAX_HOPS = 2;

    int[] nDeepNeighborHops = new int[MAX_NEIGHBORS_TO_CHECK];

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
                        if (index < MAX_NEIGHBORS_TO_CHECK
                            && AI_ActionHeuristics.GetWorkersWillingToSend(neighbor, minWorkersInNodeBeforeConsideringSendingAnyOut) > 0)
                        {
                            nDeepNeighbors[index] = neighbor;
                            nDeepNeighborHops[index] = currentDepth + 1;
                            index++;
                        }
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
            }
            currentDepth++;
        }
        return index;
    }

    Dictionary<AI_NodeState, int> attackPlanScratch = new Dictionary<AI_NodeState, int>(10);

    float GetAttackOverkillMultiplier()
    {
        return player.AIDefn != null ? player.AIDefn.AttackOverkillMultiplier : 1f;
    }

    bool TryPlanAttackAllocations(AI_NodeState[] nodes, int numNodes, int numDefenders, float overkillMultiplier, Dictionary<AI_NodeState, int> allocations, out int totalPlanned)
    {
        allocations.Clear();
        totalPlanned = 0;

        int targetAttackers = AI_ActionHeuristics.GetTargetForceWithOverkill(numDefenders, overkillMultiplier);

        // Sources within COORDINATED_WAVE_MAX_HOPS must be able to win on their own —
        // they form the core wave that arrives together. Distant sources (hop 3+) are only
        // included as bonus overkill, never relied upon to meet the target force.
        int closeWilling = 0;
        int totalWilling = 0;
        for (int i = 0; i < numNodes; i++)
        {
            int willing = AI_ActionHeuristics.GetWorkersWillingToSend(nodes[i], minWorkersInNodeBeforeConsideringSendingAnyOut);
            totalWilling += willing;
            if (nDeepNeighborHops[i] <= COORDINATED_WAVE_MAX_HOPS)
                closeWilling += willing;
        }
        if (closeWilling < targetAttackers)
            return false;

        int remaining = targetAttackers;
        for (int i = 0; i < numNodes && remaining > 0; i++)
        {
            var node = nodes[i];
            int willing = AI_ActionHeuristics.GetWorkersWillingToSend(node, minWorkersInNodeBeforeConsideringSendingAnyOut);
            if (willing <= 0) continue;

            int send = Math.Min(willing, remaining);
            allocations[node] = send;
            totalPlanned += send;
            remaining -= send;
        }
        return remaining <= 0;
    }
}
