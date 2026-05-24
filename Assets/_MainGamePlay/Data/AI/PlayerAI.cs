using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PlayerAI
{
    public override string ToString()
    {
        if (BestNextActionToTake == null) return "null BestNextActionToTake";
        return BestNextActionToTake.ToString();
    }

    PlayerData player;
    AI_TownState aiTownState;
    int minWorkersInNodeBeforeConsideringSendingAnyOut = 6;
    int maxDepth;
    public int debugOutput_ActionsTried;

    public AIDebuggerEntryData DebugRootEntry;

    public AIAction BestNextActionToTake = new();
    // Last meaningful plan/execute; used for map arrows when BestNextActionToTake is cleared or DoNothing.
    public AIAction LastActionToTake = new();
    AIAction[] actionPool;
    int actionPoolIndex;

    // Pre-action EvaluateScore at the current recursion depth.
    // Cached once per DetermineBestActionToPerform call so each peer task can compute an
    // optimistic upper bound (baseline + heuristicBonus * personality) for branch-and-bound
    // without re-running EvaluateScore per candidate.
    internal float currentDepthBaselineScore;
    public AIAction GetAIAction()
    {
        EnsureActionPoolCapacity(actionPoolIndex + 1);
        return actionPool[actionPoolIndex++].Reset();
    }
    int maxPoolSize = 25000;

    void EnsureActionPoolCapacity(int requiredCapacity)
    {
        if (actionPool == null)
        {
            int initialSize = Math.Max(maxPoolSize, requiredCapacity);
            actionPool = new AIAction[initialSize];
            for (int i = 0; i < initialSize; i++)
                actionPool[i] = new AIAction();
            return;
        }

        if (requiredCapacity <= actionPool.Length)
            return;

        int newSize = actionPool.Length;
        while (newSize < requiredCapacity)
            newSize *= 2;

        var newPool = new AIAction[newSize];
        Array.Copy(actionPool, newPool, actionPool.Length);
        for (int i = actionPool.Length; i < newSize; i++)
            newPool[i] = new AIAction();
        actionPool = newPool;

#if DEBUG
        Debug.LogWarning($"PlayerAI action pool grew to {newSize} for {player?.Name} (required {requiredCapacity})");
#endif
    }

    public BuildingDefn[] buildableBuildingDefns;
    public int numBuildingDefns;

    // Pool of recyclable AIGoal instances. AIGoalEnumerator.EnumerateGoals returns spent
    // goals here at the start of each enumeration so per-Update GC pressure stays flat.
    Stack<AIGoal> goalPool = new Stack<AIGoal>();

    // Read-only views for the simulation dump / debugger panel; the AI internally owns
    // these collections and we only want callers reading, not mutating, them.
    public List<AIGoal> GetActiveGoalsForDump() => aiTownState?.ActiveGoals;
    public Dictionary<GoodType, int> GetResourceDemandForDump() => aiTownState?.ResourceDemand;

#if DEBUG
    int lastMaxDepth = -1;
#endif

    // Decision cache: skip the full search when nothing the AI would key on has changed.
    // Sentinel -1 for "never searched" so the very first Update always runs.
    int lastSearchedWorldRevision = -1;
    int lastSearchedMaxAIDepth = -1;
#if DEBUG
    PlayerData lastSearchedDebugPlayer;
    bool lastSearchedTrackDebugger;
    bool lastSearchedHybridEnabled;
#endif

    public List<AITask> Tasks = new();

    // Realtime: world-time at which this AI is scheduled to make its next decision. The
    // realtime loop in TownData fires Update on this player when WorldTime crosses this
    // threshold, then ScheduleNextDecision rolls a new threshold using DecisionInterval +/-
    // DecisionVariance from PlayerAIDefn. Negative sentinel forces an immediate first decision.
    public float NextRealtimeDecisionTime = -1f;

    public PlayerAI(PlayerData playerData)
    {
        player = playerData;
        aiTownState = new AI_TownState(player);

        // Create pool of actions to avoid allocs during search.
        EnsureActionPoolCapacity(maxPoolSize);

        // Convert dictionary to array for speed
        buildableBuildingDefns = new BuildingDefn[GameDefns.Instance.BuildingDefns.Count];
        numBuildingDefns = 0;
        foreach (var buildingDefn in GameDefns.Instance.BuildingDefns.Values)
            if (buildingDefn.CanBeBuiltByPlayer)
                buildableBuildingDefns[numBuildingDefns++] = buildingDefn;
    }

    public void InitializeStaticData(TownData townData)
    {
        aiTownState.InitializeStaticData(townData);
    }

    public void RememberLastAction(AIAction action)
    {
        if (action == null || action.Type == AIActionType.DoNothing || action.Type == AIActionType.RootAction)
            return;
        LastActionToTake.CopyFrom(action);
    }

    public AIAction GetActionForArrowDisplay()
    {
        if (BestNextActionToTake != null && BestNextActionToTake.Type != AIActionType.DoNothing)
            return BestNextActionToTake;
        if (LastActionToTake != null && LastActionToTake.Type != AIActionType.DoNothing)
            return LastActionToTake;
        return null;
    }

    public void ScheduleNextRealtimeDecision(float currentWorldTime)
    {
        var defn = player.AIDefn;
        float interval = defn != null ? defn.DecisionIntervalSeconds : 1f;
        float variance = defn != null ? defn.DecisionVarianceSeconds : 0.5f;
        float jitter = UnityEngine.Random.Range(-variance, variance);
        // Floor at a small minimum so we don't accidentally schedule zero-or-negative deltas
        // when variance >= interval (which would re-trigger every frame).
        float delta = Mathf.Max(0.05f, interval + jitter);
        NextRealtimeDecisionTime = currentWorldTime + delta;
    }

    // In realtime mode the world is constantly mutating outside of WorldRevision bumps (workers
    // arriving, resources accumulating). The "skip search if revision unchanged" cache is unsafe
    // there, so we force a fresh search whenever the realtime path drives Update.
    internal void InvalidateDecisionCache()
    {
        lastSearchedWorldRevision = -1;
    }

    internal void Update(TownData townData)
    {
        maxDepth = AITestScene.Instance.MaxAIDepth - 1;

        aiTownState.UpdateState(townData);

#if DEBUG
        bool triggerAIDebuggerUpdate = false;
        if (lastMaxDepth != AITestScene.Instance.MaxAIDepth)
        {
            lastMaxDepth = AITestScene.Instance.MaxAIDepth;
            ConsoleClearer.ClearConsole();

            triggerAIDebuggerUpdate = true;
        }

        if (AITestScene.Instance.DebugOutputStrategyReasons)
        {
            for (int i = 0; i < actionPool.Length; i++)
                actionPool[i].Reset();
        }
#endif

        // Decision cache: re-run the full search only when an input the AI keys on has changed.
        // Without this, AITestScene.Update calls Town.Update each frame, triggering a depth-7
        // search every frame even when the world is idle.
        bool cacheValid = lastSearchedWorldRevision == townData.WorldRevision
                       && lastSearchedMaxAIDepth == AITestScene.Instance.MaxAIDepth;
#if DEBUG
        cacheValid = cacheValid
                  && lastSearchedDebugPlayer == AITestScene.Instance.DebugPlayerToViewDetailsOn
                  && lastSearchedTrackDebugger == AITestScene.Instance.TrackSearchDebugger
                  && lastSearchedHybridEnabled == AITestScene.Instance.EnableHybridSearch;
#endif
        // Realtime mutates between WorldRevision bumps (in-flight workers, production carry).
        // Keep BestNextActionToTake in sync for the per-player "next move" arrow overlay.
        if (cacheValid && (AITestScene.Instance == null || !AITestScene.Instance.Realtime))
            return;

        // Determine the best action to take, and then take it
        debugOutput_ActionsTried = 0;
        actionPoolIndex = 0;

#if DEBUG
        AIDebugger.TrackForCurrentPlayer = player == AITestScene.Instance.DebugPlayerToViewDetailsOn;
        AIDebugger.Clear();
#endif

        if (Tasks.Count == 0)
        {
            Tasks.Add(new AITask_TryButtressOwnedNode(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut));
            Tasks.Add(new AITask_AttackToNode(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut));
            Tasks.Add(new AITask_ConstructBuilding(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut));
            Tasks.Add(new AITask_UpgradeBuilding(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut));
        }
        AIDebugger.rootEntry.BestNextAction = null;

        AI_ActionHeuristics.UpdateTerritoryDetails(aiTownState, player);

        // Enumerate strategic goals (capture/defend/economic) once per real-game
        // Update. The recursive search below is then biased by the demand vector
        // these goals imply -- e.g. an aggressive player generates many CaptureNode
        // goals which in turn raise demand for Barracks construction resources,
        // which then raises the build heuristic for Woodcutters / StoneMiners.
        AIGoalEnumerator.EnumerateGoals(player, aiTownState, buildableBuildingDefns, numBuildingDefns, aiTownState.ActiveGoals, goalPool);
        AI_ActionHeuristics.UpdateResourceDemand(aiTownState, buildableBuildingDefns, numBuildingDefns, aiTownState.ActiveGoals);

#if DEBUG
        bool useHybrid = AITestScene.Instance.EnableHybridSearch;
#else
        const bool useHybrid = true;
#endif
        var bestAction = useHybrid
            ? DetermineBestActionToPerform_Hybrid(0, AIDebugger.rootEntry)
            : DetermineBestActionToPerform(0, AIDebugger.rootEntry);
        if (bestAction == null)
            BestNextActionToTake.SetToNothing();
        else
        {
            BestNextActionToTake.CopyFrom(bestAction);
            RememberLastAction(bestAction);
        }
        player.AI.DebugRootEntry = BestNextActionToTake.AIDebuggerEntry;
        if (AITestScene.Instance.DebugOutputStrategyToConsole && AIDebugger.TrackForCurrentPlayer)
            Debug.Log("Actions Tried: " + debugOutput_ActionsTried);

#if DEBUG
        // for ALL entries, calculate the count of all child entries under it and store in entry.AllChildEntriesCount
        AIDebugger.rootEntry.CalculateAllChildEntriesCount();

        if (triggerAIDebuggerUpdate)
        {
            townData.OnAIDebuggerUpdate?.Invoke(player.Id);
            triggerAIDebuggerUpdate = false;
        }
#endif

        lastSearchedWorldRevision = townData.WorldRevision;
        lastSearchedMaxAIDepth = AITestScene.Instance.MaxAIDepth;
#if DEBUG
        lastSearchedDebugPlayer = AITestScene.Instance.DebugPlayerToViewDetailsOn;
        lastSearchedTrackDebugger = AITestScene.Instance.TrackSearchDebugger;
        lastSearchedHybridEnabled = AITestScene.Instance.EnableHybridSearch;
#endif
    }
}
