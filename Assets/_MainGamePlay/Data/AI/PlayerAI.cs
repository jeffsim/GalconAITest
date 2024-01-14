using UnityEngine;

public partial class PlayerAI
{
    PlayerData player;
    AI_TownState aiTownState;
    int minWorkersInNodeBeforeConsideringSendingAnyOut = 3;
    int maxDepth;
    int debugOutput_ActionsTried;
    int debugOutput_callsToRecursivelyDetermineBestAction;

    static AIAction[] actionPool;
    int actionPoolIndex;
    int maxPoolSize = 25000;

    BuildingDefn[] buildableBuildingDefns;
    int numBuildingDefns;

#if DEBUG
    int lastMaxDepth = -1;
#endif

    public PlayerAI(PlayerData playerData)
    {
        player = playerData;
        aiTownState = new AI_TownState(player);

        // Create pool of actions to avoid allocs.  Can do this statically because it's only used by one player at a time.
        if (actionPool == null)
        {
            actionPool = new AIAction[maxPoolSize];
            for (int i = 0; i < maxPoolSize; i++)
                actionPool[i] = new AIAction();
        }

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

    internal void Update(TownData townData)
    {
        maxDepth = GameMgr.Instance.MaxAIDepth;

        aiTownState.UpdateState(townData);

#if DEBUG
        bool triggerAIDebuggerUpdate = false;
        if (lastMaxDepth != GameMgr.Instance.MaxAIDepth)
        {
            lastMaxDepth = GameMgr.Instance.MaxAIDepth;
            ConsoleClearer.ClearConsole();

            triggerAIDebuggerUpdate = true;
        }

        if (GameMgr.Instance.DebugOutputStrategyReasons)
        {
            for (int i = 0; i < actionPool.Length; i++)
                actionPool[i].Reset();
        }
#endif

        // Determine the best action to take, and then take it
        debugOutput_ActionsTried = -1;
        debugOutput_callsToRecursivelyDetermineBestAction = -1;
        actionPoolIndex = 0;

#if DEBUG
        AIDebugger.Clear();
#endif
        var bestAction = RecursivelyDetermineBestAction(0, 0);
        if (GameMgr.Instance.DebugOutputStrategyToConsole)
            Debug.Log("Actions Tried: " + debugOutput_ActionsTried + "; Recursions:" + debugOutput_callsToRecursivelyDetermineBestAction);
        performAction(bestAction);

#if DEBUG
        if (triggerAIDebuggerUpdate)
        {
            townData.OnAIDebuggerUpdate?.Invoke();
            triggerAIDebuggerUpdate = false;
        }
#endif
    }
}
