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
        // Neutral nodes (OwnedBy == null) are valid expansion targets: AttackFromNode handles
        // them correctly (toNode.NumWorkers <= 0 -> capture). Without this, an Expansion AI on
        // a map with neutrals can't move at all -- Construct requires resources Green doesn't
        // have early game, and there is no other task wired to fulfill CaptureNode goals.
        if (toNode.OwnedBy == player) return 0f;

        int num = GetFriendlyNeighborsWithEnoughWorkers(toNode, nDeepNeighbors);
        if (!GetNodesToAttackFrom(nDeepNeighbors, num, toNode.NumWorkers, out int totalWillingToSend))
            return 0f;

        float h = AI_ActionHeuristics.GetAttackHeuristic(aiTownState, toNode, totalWillingToSend);
        if (h <= 0f) return 0f;

        // Apply personality so Phase 1 candidate ranking matches actual scoring. Aggressive AIs
        // (e.g. AggressivenessWeight=2) should see attack candidates rank above their own
        // buttresses; pacifist AIs (Weight=0) should see attacks drop out of top-K entirely.
        // Neutral targets use Expansion personality (this is grabbing unclaimed ground).
        var actionType = AI_ActionHeuristics.ResolveCaptureActionType(toNode);
        return h * AI_ActionHeuristics.GetPersonalityMultiplier(player, actionType);
    }

    override public bool TryTask(AI_NodeState toNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions, out AIAction bestAction)
    {
        bestAction = null;

        // Neutral targets allowed (see PreviewHeuristic comment). Only friendlies are excluded.
        if (toNode.OwnedBy == player) return false;

        int num = GetFriendlyNeighborsWithEnoughWorkers(toNode, nDeepNeighbors);

        int totalWillingToSend = 0;
        bool haveEnoughWorkersToAttack = GetNodesToAttackFrom(nDeepNeighbors, num, toNode.NumWorkers, out totalWillingToSend);
        if (!haveEnoughWorkersToAttack) return false;

        float heuristicBonus = AI_ActionHeuristics.GetAttackHeuristic(aiTownState, toNode, totalWillingToSend);
        if (heuristicBonus <= 0f) return false;

        // Capture-against-neutral is expansion-personality, capture-against-enemy is
        // aggression-personality. Determined once here and used for both pruning and scoring
        // so they stay consistent.
        var actionType = AI_ActionHeuristics.ResolveCaptureActionType(toNode);

        if (ShouldPruneByHeuristic(heuristicBonus, actionType, bestScoreAmongPeerActions))
            return false;

        bestAction = player.AI.GetAIAction();

        List<AttackState> attackStates = attackStatesPool.Count > 0 ? attackStatesPool.Pop() : new List<AttackState>();
        List<AttackResult> attackResults = attackResultsPool.Count > 0 ? attackResultsPool.Pop() : new List<AttackResult>();
        Dictionary<AI_NodeState, int> attackFromNodes = attackFromNodesPool.Count > 0 ? attackFromNodesPool.Pop() : new Dictionary<AI_NodeState, int>();

        attackStates.Clear();
        attackResults.Clear();
        attackFromNodes.Clear();

        foreach (var fromNode in nodesToAttackFrom)
        {
            AttackState attackState = attackStatePool.Count > 0 ? attackStatePool.Pop() : new AttackState();
            attackState.Reset();

            attackState.FromNode = fromNode;
            attackState.OrigNumInSourceNode = fromNode.NumWorkers;
            attackState.ToNode = toNode;
            attackState.OrigNumInDestNode = toNode.NumWorkers;
            attackState.OrigToNodeOwner = toNode.OwnedBy;

            aiTownState.AttackFromNode(fromNode, toNode, out AttackResult attackResult, out _, out _, out int numSent, out _);
            attackResults.Add(attackResult);
            attackFromNodes[fromNode] = numSent;

            attackState.NumSent = numSent;
            attackState.AttackResult = attackResult;
            attackStates.Add(attackState);
        }

        var debuggerEntry = aiDebuggerParentEntry?.AddEntry_AttackToNode(attackFromNodes, toNode, attackResults, 0, player.AI.debugOutput_ActionsTried++, curDepth);

        var actionScore = GetActionScore(curDepth, debuggerEntry);
        actionScore = AI_ActionHeuristics.ApplyHeuristicAndPersonality(actionScore, heuristicBonus, player, actionType);
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
                        // The outer while-loop's MAX_NEIGHBORS bound is checked once per level,
                        // not per inner iteration -- a single dense level can blow past it and
                        // OOB into nDeepNeighbors. Keep the inner write strictly bounded.
                        if (index < MAX_NEIGHBORS_TO_CHECK
                            && AI_ActionHeuristics.GetWorkersWillingToSend(neighbor, minWorkersInNodeBeforeConsideringSendingAnyOut) > 0)
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

    bool GetNodesToAttackFrom(AI_NodeState[] nodes, int numNodes, int numEnemies, out int totalWillingToSend)
    {
        nodesToAttackFrom.Clear();
        totalWillingToSend = 0;
        for (int i = 0; i < numNodes; i++)
        {
            var node = nodes[i];
            int willing = AI_ActionHeuristics.GetWorkersWillingToSend(node, minWorkersInNodeBeforeConsideringSendingAnyOut);
            if (willing <= 0) continue;

            nodesToAttackFrom.Add(node);
            totalWillingToSend += willing;
            if (totalWillingToSend >= numEnemies)
                return true;
        }
        return false;
    }
}
