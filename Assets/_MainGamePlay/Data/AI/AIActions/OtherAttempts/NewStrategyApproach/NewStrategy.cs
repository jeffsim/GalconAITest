using UnityEngine;

public struct Strategy_AIAction
{
    public ActionType Type;
    public int TargetNodeIndex;
    public int NumWorkers;
    public BuildingType BuildingType;
}

public enum ActionType
{
    CaptureNode,
    ConstructBuilding,
    UpgradeBuilding
}

public class NewStrategy
{
    private const int MAX_ACTIONS = 250;
    private Strategy_AIAction[] preallocatedActions;
    private int actionCount;

    private Strategy_TownState Town;
    private PlayerData Player;

    public NewStrategy(PlayerData player)
    {
        Player = player;
        Town = new Strategy_TownState();
        Town.Initialize();

        preallocatedActions = new Strategy_AIAction[MAX_ACTIONS];
        actionCount = 0;
    }

    void InitializeToTown(TownData sourceTownData)
    {
        for (int i = 0; i < sourceTownData.Nodes.Count && i < Town.Nodes.Length; i++)
        {
            var sourceNode = sourceTownData.Nodes[i];
            var strategyNode = Town.Nodes[i];

            strategyNode.NodeId = sourceNode.NodeId;
            strategyNode.OwnerId = sourceNode.OwnedBy?.Id ?? 0;
            strategyNode.NumWorkers = sourceNode.NumWorkers;
            strategyNode.BuildingType = sourceNode.Building == null ? BuildingType.None : sourceNode.Building.Defn.BuildingType;
            strategyNode.IsUpgradableBuilding = sourceNode.Building?.Defn.CanBeUpgraded ?? false;
            strategyNode.BuildingLevel = sourceNode.Building?.Level ?? 0;

            strategyNode.NumNeighbors = sourceNode.NodeConnections.Count;
            for (int j = 0; j < sourceNode.NodeConnections.Count && j < Strategy_Node.MAX_NEIGHBORS; j++)
            {
                var connection = sourceNode.NodeConnections[j];
                strategyNode.Neighbors[j] = Town.Nodes[connection.End.NodeId];
            }

            Town.Nodes[i] = strategyNode;
        }
    }
    public int NumActionsConsidered, NumNodesConsidered;

    public Strategy_AIAction DecideAction(TownData sourceTownData)
    {
        InitializeToTown(sourceTownData);
        NumActionsConsidered = 0;
        NumNodesConsidered = 0;
        const int MAX_DEPTH = 4;
        Strategy_AIAction bestAction = default;
        float bestValue = float.MinValue;

        EvaluateActions(Town, Player, 0, MAX_DEPTH, ref bestAction, ref bestValue);

        return bestAction;
    }

    private void EvaluateActions(Strategy_TownState state, PlayerData player, int currentDepth, int maxDepth, ref Strategy_AIAction bestAction, ref float bestValue)
    {
        if (NumActionsConsidered > 1000000)
            return;

        if (currentDepth >= maxDepth)
        {
            float value = EvaluateState(state, player);
            if (value > bestValue)
                bestValue = value;
            return;
        }

        // create list of buildings that player can currently construct
        // TODO: avoid alloc.  Move into strategy_townstate
        int buildingIndex = 0;
        foreach (var building in GameDefns.Instance.BuildingDefns.Values)
            if (building.CanBeBuiltByPlayer && building.IsEnabled)
            {
                var resourcesNeeded = building.ConstructionRequirements;
                bool canBuild = true;
                // foreach (var rn in resourcesNeeded)
                // canBuild &= state.Resources[rn.Good.GoodType] < rn.Amount;
                if (canBuild)
                    state.ConstructibleBuildings[buildingIndex++] = building;
            }


        EnumerateActions(state, player);

        for (int i = 0; i < actionCount; i++)
        {
            NumActionsConsidered++;
            var action = preallocatedActions[i];

            Strategy_TownState newState = state;

            ApplyAction(ref newState, action, player);

            EvaluateActions(newState, player, currentDepth + 1, maxDepth, ref bestAction, ref bestValue);

            if (currentDepth == 0)
            {
                float actionValue = EvaluateState(newState, player);
                if (actionValue > bestValue)
                {
                    bestValue = actionValue;
                    bestAction = action;
                }
            }
        }
    }

    private void EnumerateActions(Strategy_TownState state, PlayerData player)
    {
        actionCount = 0;

        foreach (var node in state.Nodes)
        {
            NumNodesConsidered++;
            Debug.Assert(actionCount < MAX_ACTIONS);

            if (node.OwnerId == player.Id)
            {
                // consider nodes that (a) the player does not own and (b) neighbors the player's nodes
                for (int n = 0; n < node.NumNeighbors && actionCount < MAX_ACTIONS; n++)
                {
                    var neighbor = node.Neighbors[n];
                    if (neighbor.OwnerId == 0)
                    {
                        // Unowned.  Construct building on it.
                        for (var b = 0; b < state.ConstructibleBuildings.Length; b++)
                        {
                            var building = state.ConstructibleBuildings[b];
                            if (building == null) break;
                            // TODO: Prefilter buildings to reduce search space.  e.g. don't consider woodcutter if have surplus of wood?
                            preallocatedActions[actionCount++] = new Strategy_AIAction
                            {
                                Type = ActionType.ConstructBuilding,
                                TargetNodeIndex = neighbor.NodeId,
                                BuildingType = building.BuildingType
                            };
                        }
                    }
                    else if (neighbor.OwnerId != player.Id)
                    {
                        // owned by enemy.  Capture it.
                        // TODO: Prefilter; e.g. don't consider capture if node doesn't have enough workers to win.
                        // TODO: This approach fails on pincer movement.
                        // TODO: calculate number of workers to send
                        int numWorkers = node.NumWorkers / 2;
                        preallocatedActions[actionCount++] = new Strategy_AIAction
                        {
                            Type = ActionType.CaptureNode,
                            TargetNodeIndex = neighbor.NodeId,
                            NumWorkers = numWorkers
                        };
                    }
                    else
                    {
                        // player owns neighboring node; should we send workers to buttress it?
                        // if neighboringNode.neighbors is owned by enemy and needs help, send workers
                        var neighborsOfNeighbor = neighbor.Neighbors;
                        for (int nn = 0; nn < neighbor.NumNeighbors; nn++)
                        {
                            var neighborOfNeighbor = neighborsOfNeighbor[nn];
                            if (neighborOfNeighbor.OwnerId != player.Id)
                            {
                                // owned by enemy.  Capture it.
                                // TODO: calculate number of workers to send
                                int numWorkers = node.NumWorkers / 2;
                                preallocatedActions[actionCount++] = new Strategy_AIAction
                                {
                                    Type = ActionType.CaptureNode,
                                    TargetNodeIndex = neighbor.NodeId,
                                    NumWorkers = numWorkers
                                };
                            }
                        }
                    }
                }

                // Check if should/can upgrade building
                if (node.BuildingType != BuildingType.None && node.OwnerId == player.Id && node.IsUpgradableBuilding && node.BuildingLevel < 3)
                {
                    // TODO: prefilter.
                    preallocatedActions[actionCount++] = new Strategy_AIAction
                    {
                        Type = ActionType.UpgradeBuilding
                    };
                }
            }
        }
    }

    private void ApplyAction(ref Strategy_TownState state, Strategy_AIAction action, PlayerData player)
    {
        ref Strategy_Node targetNode = ref state.Nodes[action.TargetNodeIndex];

        switch (action.Type)
        {
            case ActionType.CaptureNode:
                targetNode.OwnerId = player.Id;
                targetNode.NumWorkers = 1;
                break;

            case ActionType.ConstructBuilding:
                targetNode.BuildingType = action.BuildingType;
                targetNode.BuildingLevel = 1;
                break;

            case ActionType.UpgradeBuilding:
                if (targetNode.BuildingLevel < 3)
                {
                    targetNode.BuildingLevel++;
                }
                break;
        }
    }

    private float EvaluateState(Strategy_TownState state, PlayerData player)
    {
        float score = 0;

        foreach (var node in state.Nodes)
        {
            if (node.OwnerId == player.Id)
            {
                score += node.NumWorkers * 10;
                score += node.BuildingLevel * 20;
            }
        }

        return score;
    }
}
