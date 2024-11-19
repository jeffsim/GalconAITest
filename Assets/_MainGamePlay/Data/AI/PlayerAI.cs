using System.Collections.Generic;
using NUnit.Framework.Constraints;
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
    int minWorkersInNodeBeforeConsideringSendingAnyOut = 8;
    int maxDepth;
    public int debugOutput_ActionsTried;

    public AIAction BestNextActionToTake = new();
    static AIAction[] actionPool;
    static int actionPoolIndex;
    static int maxPoolSize = 25000;

    public BuildingDefn[] buildableBuildingDefns;
    public int numBuildingDefns;

#if DEBUG
    int lastMaxDepth = -1;
#endif

    public List<AITask> Tasks = new();

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

    NewStrategy strategy;

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

        // Determine the best action to take, and then take it
        debugOutput_ActionsTried = -1;
        actionPoolIndex = 0;

#if DEBUG
        AIDebugger.TrackForCurrentPlayer = player == AITestScene.Instance.DebugPlayerToViewDetailsOn;
        AIDebugger.Clear();
#endif

        // var tryGOAP = false;
        var tryNewStrategy = true;
        // if (tryGOAP)
        // {
        //     var aiMapState = new AIMap_State(townData);
        //     InitializeGOAP(aiMapState, 1);
        //     var goal = DetermineBestGoal();
        // }
        if (tryNewStrategy)
        {
            if (player.Id != 1) return;
            strategy ??= new NewStrategy(player);
            var action = strategy.DecideAction(townData);
            
            // BestNextActionToTake.CopyFrom(townData, action);
            Debug.Log(strategy.NumActionsConsidered);
            BestNextActionToTake.SetToNothing();
            return;
        }
        else
        {
            // TODO: Only do once, not each time
            Tasks.Clear();
            // Tasks.Add(new AITask_TryButtressOwnedNode(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut));
            Tasks.Add(new AITask_AttackFromNode(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut));
            Tasks.Add(new AITask_ConstructBuilding(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut));
            Tasks.Add(new AITask_UpgradeBuilding(player, aiTownState, maxDepth, minWorkersInNodeBeforeConsideringSendingAnyOut));

            AIDebugger.rootEntry.BestNextAction = null;
            var bestAction = DetermineBestActionToPerform(0, AIDebugger.rootEntry);
            if (bestAction == null)
                BestNextActionToTake.SetToNothing();
            else
                BestNextActionToTake.CopyFrom(bestAction);
        }
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
    }
}
