public enum AttackResult { Undefined, AttackerWon, DefenderWon, BothSidesDied };

public abstract class AITask
{
    protected PlayerData player;
    protected AI_TownState aiTownState;
    protected int maxDepth;
    protected int minWorkersInNodeBeforeConsideringSendingAnyOut;

    public AITask(PlayerData player, AI_TownState aiTownState, int maxDepth, int minWorkersInNodeBeforeConsideringSendingAnyOut)
    {
        this.player = player;
        this.aiTownState = aiTownState;
        this.maxDepth = maxDepth;
        this.minWorkersInNodeBeforeConsideringSendingAnyOut = minWorkersInNodeBeforeConsideringSendingAnyOut;
    }

    protected float GetActionScore(int curDepth, AIDebuggerEntryData debuggerEntry)
    {
#if DEBUG
        if (debuggerEntry != null && AITestScene.Instance.DebugOutputActionBeforeScore)
            debuggerEntry.Debug_ActionScoreBeforeSubactions = aiTownState.EvaluateScore(curDepth, maxDepth, out _);
#endif

        AIAction bestNextAction = null;
        if (curDepth < maxDepth)
        {
#if DEBUG
            bool useHybrid = AITestScene.Instance.EnableHybridSearch;
#else
            const bool useHybrid = true;
#endif
            bestNextAction = useHybrid
                ? player.AI.DetermineBestActionToPerform_Hybrid(curDepth + 1, debuggerEntry)
                : player.AI.DetermineBestActionToPerform(curDepth + 1, debuggerEntry);
        }

        float actionScore;
        if (bestNextAction != null)
            actionScore = bestNextAction.Score; // Score of the best action after this action
        else
            actionScore = aiTownState.EvaluateScore(curDepth, maxDepth, out _); // Evaluate score of the current state after this action
        if (debuggerEntry != null)
            debuggerEntry.FinalActionScore = actionScore;
        return actionScore;
    }
    public abstract bool TryTask(AI_NodeState fromNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions, out AIAction bestAction);

    // Fast, simulation-free heuristic preview used by Phase 1 of the hybrid search to rank
    // candidates across (node, task) pairs without paying for simulation+recursion. Returns
    // 0 when the task is not applicable to fromNode. Default implementation is a conservative
    // 0 so subclasses must opt in to participate in hybrid candidate generation.
    public virtual float PreviewHeuristic(AI_NodeState fromNode) => 0f;

    // Optimistic upper bound on this candidate's final score, mirroring the actual scoring
    // formula in AI_ActionHeuristics.ApplyHeuristicAndPersonality:
    //   (baseline (pre-action EvaluateScore) + heuristicBonus) * personality
    // Pre-action simulation only mildly perturbs EvaluateScore for a single action, so an
    // action whose optimistic heuristic-only score cannot beat the running peer best is
    // unlikely to win after sim+recurse and can be skipped. Personality = 0 collapses the
    // bound to zero which prunes every candidate of that type unconditionally — exactly the
    // behavior expected when a tactic weight is zeroed out.
    //
    // Margin: require optimistic to clear the peer best by a small percentage. Without it,
    // many candidates whose heuristics land within a few percent of each other (e.g. Upgrade
    // vs Construct on an overcrowded interior) all survive pruning and each spawn a full
    // recursive subtree. Empirically that pushed the action pool past 200K per Update with
    // maxDepth=12. 2% is small enough to keep meaningful contenders, large enough to cut
    // the bulk of the branches whose simulated result almost certainly tracks their heuristic.
    protected const float PruningMargin = 1.02f;

    protected bool ShouldPruneByHeuristic(float heuristicBonus, AIHeuristicActionType actionType, float bestScoreAmongPeerActions)
    {
        if (bestScoreAmongPeerActions <= 0f) return false; // first candidate; nothing to prune against
        float personality = AI_ActionHeuristics.GetPersonalityMultiplier(player, actionType);
        float optimistic = (player.AI.currentDepthBaselineScore + heuristicBonus) * personality;
        return optimistic <= bestScoreAmongPeerActions * PruningMargin;
    }
}
