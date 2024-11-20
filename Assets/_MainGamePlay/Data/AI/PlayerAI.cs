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

    RecursiveStrategy2 strategyRecursive;
    //Strategy_NonRecursive strategyNonrecursive;

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
        // actionPoolIndex = 0;

#if DEBUG
        AIDebugger.TrackForCurrentPlayer = player == AITestScene.Instance.DebugPlayerToViewDetailsOn;
        AIDebugger.Clear();
#endif

        int aiApproach = 1;
        switch (aiApproach)
        {
            case 0: // GOAP approach
                {
                    // var aiMapState = new AIMap_State(townData);
                    // InitializeGOAP(aiMapState, playerId);
                    // var goal = DetermineBestGoal();
                }
                break;

            case 1: // GOAP approach2
                {
                    // DO ONCE  vv
                    var townState = new AI_TownState(player);
                    townState.InitializeStaticData(townData);
                    var aiAgent = new AIAgent(player.Id, townState);
                    // DO ONCE  ^^

                    // DO PER UPDATE
                    townState.UpdateState(townData);
                    aiAgent.Update();
                    Debug.Log(aiAgent.CurrentGoal);
                    Debug.Log(aiAgent.CurrentPlan);
                }
                break;
            case 2: // Another recursive approach
                {
                    strategyRecursive ??= new RecursiveStrategy2(player);
                    var bestAction = strategyRecursive.DecideAction(townData);

                    // BestNextActionToTake.CopyFrom(townData, action);
                    Debug.Log(strategyRecursive.NumActionsConsidered);
                    BestNextActionToTake.SetToNothing();

                    // NOTE: This approach doesn't currently set BestNextActionToTake since I'm just trying to get it to work.
                }
                break;

            case 3: // trying a nonrecursive approach because FFFS
                {
                    // strategyNonrecursive ??= new Strategy_NonRecursive(townData, player);
                    // var bestAction = strategyNonrecursive.DecideAction();
                    // if (bestAction == null)
                    //     BestNextActionToTake.SetToNothing();
                    // else
                    //     BestNextActionToTake.CopyFrom(bestAction);
                }
                break;

            case 4: // Main working approach
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
                break;
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
