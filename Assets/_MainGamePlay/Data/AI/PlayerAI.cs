using UnityEngine;

public partial class PlayerAI
{
    PlayerData player;
    AI_TownState aiTownState;
    int minWorkersInNodeBeforeConsideringSendingAnyOut = 3;
    int maxDepth;
    int debugOutput_ActionsTried;
    int debugOutput_callsToRecursivelyDetermineBestAction;

    AIAction[] actionPool;
    int actionPoolIndex;
    int maxPoolSize = 500000;

    BuildingDefn[] buildableBuildingDefns;
    int numBuildingDefns;

#if DEBUG
    int lastMaxDepth = -1;
#endif

    public PlayerAI(PlayerData playerData)
    {
        player = playerData;
        aiTownState = new AI_TownState(player);

        // Create pool of actions to avoid allocs
        actionPool = new AIAction[maxPoolSize];
        for (int i = 0; i < maxPoolSize; i++)
            actionPool[i] = new AIAction();

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
        if (lastMaxDepth != GameMgr.Instance.MaxAIDepth)
        {
            lastMaxDepth = GameMgr.Instance.MaxAIDepth;
            ConsoleClearer.ClearConsole();
        }

        if (GameMgr.Instance.DebugOutputStrategyFull)
        {
            for (int i = 0; i < actionPool.Length; i++)
                actionPool[i].Reset();
        }
#endif

        // Determine the best action to take, and then take it
        debugOutput_ActionsTried = -1;
        debugOutput_callsToRecursivelyDetermineBestAction = -1;
        actionPoolIndex = 0;

        var bestAction = RecursivelyDetermineBestAction(0, 0);
        if (GameMgr.Instance.DebugOutputStrategyFull)
            Debug.Log("Actions Tried: " + debugOutput_ActionsTried + "; Recursions:" + debugOutput_callsToRecursivelyDetermineBestAction);
        performAction(bestAction);
    }
}
