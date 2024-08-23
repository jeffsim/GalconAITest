
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
        debuggerEntry.Debug_ActionScoreBeforeSubactions = aiTownState.EvaluateScore(curDepth, maxDepth, out _);
#endif

        float actionScore;
        AIAction bestNextAction = curDepth < maxDepth ? player.AI.DetermineBestActionToPerform(curDepth + 1, debuggerEntry) : null;
        if (bestNextAction != null)
            actionScore = bestNextAction.Score; // Score of the best action after this action
        else
            actionScore = aiTownState.EvaluateScore(curDepth, maxDepth, out _); // Evaluate score of the current state after this action
        debuggerEntry.FinalActionScore = actionScore;
        return actionScore;
    }

    public abstract AIAction TryTask(AI_NodeState fromNode, int curDepth, int actionNumberOnEntry, AIDebuggerEntryData aiDebuggerParentEntry, float bestScoreAmongPeerActions);
}
