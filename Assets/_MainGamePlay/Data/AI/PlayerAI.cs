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
    int minWorkersInNodeBeforeConsideringSendingAnyOut = 3;
    int maxDepth;
    public int debugOutput_ActionsTried;

    public AIAction BestNextActionToTake = new();
    static AIAction[] actionPool;
    static int actionPoolIndex;
    static int maxPoolSize = 25000;

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
        maxDepth = AITestScene.Instance.MaxAIDepth;

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

        // Determine the best action to take, and then take it
        debugOutput_ActionsTried = -1;
        actionPoolIndex = 0;

#if DEBUG
        AIDebugger.TrackForCurrentPlayer = player == AITestScene.Instance.DebugPlayerToViewDetailsOn;
        AIDebugger.Clear();
#endif

        var tryGOAP = false;
        var tryNewStrategy = false;
        if (tryGOAP)
        {
            var aiMapState = new AIMap_State(townData);
            InitializeGOAP(aiMapState, 1);
            var goal = DetermineBestGoal();
        }
        else if (tryNewStrategy)
        {
            var strategy = new NewStrategy(townData, player);
            var action = strategy.DecideAction();
            BestNextActionToTake.CopyFrom(action);
        }
        else
            BestNextActionToTake.CopyFrom(DetermineBestActionToPerform(0));

        if (AITestScene.Instance.DebugOutputStrategyToConsole && AIDebugger.TrackForCurrentPlayer)
            Debug.Log("Actions Tried: " + debugOutput_ActionsTried);
        // performAction(BestNextActionToTake);

#if DEBUG
        // for ALL entries, calculate the count of all child entries under it and store in entry.AllChildEntriesCount
        AIDebugger.rootEntry.CalculateAllChildEntriesCount();

        if (triggerAIDebuggerUpdate)
        {
            townData.OnAIDebuggerUpdate?.Invoke(player.Id);
            triggerAIDebuggerUpdate = false;
        }
#endif
    }
}
