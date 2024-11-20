using System;
using UnityEditorInternal;
using UnityEngine;

public struct Strategy_AIAction
{
    public ActionType Type;
    public int SourceNodeIndex;
    public int TargetNodeIndex;
    public int NumWorkers;
    public BuildingType BuildingType;

    public Strategy_AIAction(ActionType type) : this()
    {
        Type = type;
    }
}

public enum ActionType
{
    CaptureNodeAndConstructBuilding,
    AttackEnemyNode,
    ButtressBuilding,
    UpgradeBuilding
}

public class NewStrategy
{
    const int MAX_DEPTH = 3;
    const int MAX_ACTIONS = 250;
    const int MAX_BUILDING_UPGRADE_LEVEL = 4; // todo: per building

    Strategy_AIAction[] actions;
    public int NumActionsConsidered, NumNodesConsidered;

    Strategy_TownState Town;
    PlayerData Player;

    public NewStrategy(PlayerData player)
    {
        Player = player;
        Town = new Strategy_TownState();
        Town.Initialize();

        actions = new Strategy_AIAction[MAX_ACTIONS];
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
            for (int j = 0; j < sourceNode.NodeConnections.Count; j++)
            {
                var connection = sourceNode.NodeConnections[j];
                strategyNode.Neighbors[j] = Town.Nodes[connection.End.NodeId];
            }

            Town.Nodes[i] = strategyNode;
        }
    }

    public Strategy_AIAction DecideAction(TownData sourceTownData)
    {
        InitializeToTown(sourceTownData);
        NumActionsConsidered = 0;
        NumNodesConsidered = 0;
        Town.NodesVisited = 0UL;
        Strategy_AIAction bestAction = default;
        float bestValue = float.MinValue;

        EvaluateActions(Town, 0, ref bestAction, ref bestValue);

        return bestAction;
    }

    private void EvaluateActions(Strategy_TownState state, int currentDepth, ref Strategy_AIAction bestAction, ref float bestValue)
    {
        if (NumActionsConsidered > 1000000)
            return;

        if (currentDepth >= MAX_DEPTH)
        {
            bestValue = Math.Max(EvaluateState(state), bestValue);
            return;
        }

        // TODO: Need to 'tick' resource gathering once per step.  here?

        // create list of buildings that player can currently construct
        // TODO: precalc (building.CanBeBuiltByPlayer && building.IsEnabled) and only update Resources here
        state.NumConstructibleBuildings = 0;
        foreach (var building in GameDefns.Instance.BuildingDefns.Values)
            if (building.CanBeBuiltByPlayer && building.IsEnabled)
            {
                bool canBuild = true;
                // var resourcesNeeded = building.ConstructionRequirements;
                // foreach (var rn in resourcesNeeded)
                // canBuild &= state.Resources[rn.Good.GoodType] < rn.Amount;
                if (canBuild)
                    state.ConstructibleBuildings[state.NumConstructibleBuildings++] = building;
            }

        var actionCount = EnumerateActions(state);
        for (int i = 0; i < actionCount; i++)
        {
            NumActionsConsidered++;
            var action = actions[i];

            Strategy_TownState newState = state;

            ApplyAction(ref newState, action);

            EvaluateActions(newState, currentDepth + 1, ref bestAction, ref bestValue);

            if (currentDepth == 0)
            {
                float actionValue = EvaluateState(newState);
                if (actionValue > bestValue)
                {
                    bestValue = actionValue;
                    bestAction = action;
                }
            }
        }
    }

    private int EnumerateActions(Strategy_TownState state)
    {
        int actionCount = 0;

        foreach (var node in state.Nodes)
        {
            // NodesVisited is a 64bit flag field used to disallow pingponging between nodes.  if recurse from node 2 to 4 then don't allow recursing back to 2
            // so: track which nodes have been visited in this path
            if ((state.NodesVisited & (1UL << node.NodeId)) != 0) continue;

            NumNodesConsidered++;
            Debug.Assert(actionCount < MAX_ACTIONS);

            if (node.OwnerId != Player.Id) continue; // TODO: Keep list of player's nodes

            // consider actions which move workers from node to another node
            // TODO: Pincher attacks
            // TODO: send workers from non-neighor (e.g. 3 away) to attack or construct or enable to uplevel
            //  don't want to do A* in this perf-critical loop though
            for (int n = 0; n < node.NumNeighbors && actionCount < MAX_ACTIONS; n++)
            {
                var neighbor = node.Neighbors[n];
                if (neighbor.OwnerId == 0 && neighbor.BuildingType == BuildingType.None) // e.g.  forest has no owner and buildingtype=none
                {
                    // Unowned.  Construct building on it.
                    for (var b = 0; b < state.NumConstructibleBuildings && actionCount < MAX_ACTIONS; b++)
                    {
                        var building = state.ConstructibleBuildings[b];
                        if (WantToConstructBuildingOnEmptyNode(node, neighbor, building, out int numWorkers))
                            actions[actionCount++] = new Strategy_AIAction(ActionType.CaptureNodeAndConstructBuilding)
                            {
                                SourceNodeIndex = node.NodeId,
                                TargetNodeIndex = neighbor.NodeId,
                                BuildingType = building.BuildingType,
                                NumWorkers = numWorkers
                            };
                    }
                }
                else if (neighbor.OwnerId != Player.Id)
                {
                    // owned by enemy.  Capture it.
                    if (false && WantToAttackEnemyNode(node, neighbor, out int numWorkers))
                        actions[actionCount++] = new Strategy_AIAction(ActionType.AttackEnemyNode)
                        {
                            SourceNodeIndex = node.NodeId,
                            TargetNodeIndex = neighbor.NodeId,
                            NumWorkers = numWorkers
                        };
                }
                else
                {
                    // player owns neighboring node; should we send workers to buttress it?
                    if (false && WantToButtressBuilding(node, neighbor, out int numWorkers))
                        actions[actionCount++] = new Strategy_AIAction(ActionType.ButtressBuilding)
                        {
                            SourceNodeIndex = node.NodeId,
                            TargetNodeIndex = neighbor.NodeId,
                            NumWorkers = numWorkers
                        };
                }
            }

            // Check if should/can upgrade building in node
            if (false && WantToUpgradeBuilding(node))
                actions[actionCount++] = new Strategy_AIAction(ActionType.UpgradeBuilding);
        }

        return actionCount;
    }

    private bool WantToConstructBuildingOnEmptyNode(Strategy_Node node, Strategy_Node neighbor, BuildingDefn building, out int numWorkers)
    {
        // TODO: Prefilter buildings here to reduce search space.  e.g. don't consider woodcutter if have surplus of wood?
        numWorkers = node.NumWorkers / 2;
        return true;
    }

    private bool WantToAttackEnemyNode(Strategy_Node nodeFrom, Strategy_Node nodeTo, out int numWorkers)
    {
        numWorkers = 0;

        // don't attack if nodeFrom has fewer workers than nodeTo
        // todo: other logic too
        if (nodeFrom.NumWorkers < nodeTo.NumWorkers) return false;

        return true;
    }

    bool WantToButtressBuilding(Strategy_Node nodeFrom, Strategy_Node nodeTo, out int numWorkers)
    {
        numWorkers = 0;

        // don't buttress if nodeFrom doesn't have enough workers
        if (nodeFrom.NumWorkers < 4) return false; // TODO: magic #

        // if enemies near nodeTo then buttress it from nodeFrom
        for (int n = 0; n < nodeTo.NumNeighbors; n++)
        {
            var neighbor = nodeTo.Neighbors[n];
            if (neighbor.OwnerId != Player.Id && neighbor.NumWorkers > nodeTo.NumWorkers)
            {
                // buttress nodeTo using workers from nodeFrom to defend against attacks from neighbor
                // TODO: Smarter logic here.  e.g. if enemy has 2x workers, send 2x workers etc
                // TODO: The logic here should be to either (a) defend from enemies or (b) enable upgrade of building
                numWorkers = nodeFrom.NumWorkers / 2;
                return true;
            }
        }
        return false;
    }

    bool WantToUpgradeBuilding(Strategy_Node node)
    {
        // Filter out any buildings we shouldn't upgrade
        // to get here, player must own the node which means there must be a building on it - so no need to check those
        if (!node.IsUpgradableBuilding || node.BuildingLevel == MAX_BUILDING_UPGRADE_LEVEL)
            return false;

        // 1. Ensure building has enough workers to do it.  Must have Level ^ 2 workers
        if (node.NumWorkers < node.BuildingLevel * node.BuildingLevel) return false;

        // 2. Ensure building isn't under immediate threat (no neighbors owned by enemy w/ > N workers)
        // for (int n = 0; n < node.NumNeighbors; n++)
        // {
        //     var neighbor = node.Neighbors[n];
        //     if (neighbor.OwnerId != Player.Id && neighbor.NumWorkers > node.NumWorkers)
        //         return false; // is under threat, don't upgrade yet
        // }

        // TODO: Other prefilters?  Some buildings don't make sense to upgrade given various global states.

        return true; // safe to upgrade
    }

    private void ApplyAction(ref Strategy_TownState state, Strategy_AIAction action)
    {
        ref Strategy_Node fromNode = ref state.Nodes[action.SourceNodeIndex];
        ref Strategy_Node toNode = ref state.Nodes[action.TargetNodeIndex];
        int numWorkers = action.NumWorkers;

        // Simulate action
        switch (action.Type)
        {
            case ActionType.AttackEnemyNode:
                // simulate attack; send workers from fromNode to toNode and assume 1:1 attack/defend
                bool attackerWonBattle = fromNode.NumWorkers > toNode.NumWorkers;
                toNode.NumWorkers = fromNode.NumWorkers - toNode.NumWorkers;
                fromNode.NumWorkers -= numWorkers;
                if (attackerWonBattle)
                    toNode.OwnerId = Player.Id;
                state.NodesVisited |= 1UL << toNode.NodeId;
                break;

            case ActionType.CaptureNodeAndConstructBuilding:
                toNode.BuildingType = action.BuildingType;
                toNode.BuildingLevel = 1;
                toNode.OwnerId = Player.Id;
                toNode.NumWorkers = numWorkers;
                fromNode.NumWorkers -= numWorkers;

                state.NodesVisited |= 1UL << toNode.NodeId;
                break;

            case ActionType.ButtressBuilding:
                toNode.NumWorkers += numWorkers;
                fromNode.NumWorkers -= numWorkers;
                break;

            case ActionType.UpgradeBuilding:
                toNode.BuildingLevel++;
                break;
        }
    }

    private float EvaluateState(Strategy_TownState state)
    {
        float score = 0;

        foreach (var node in state.Nodes)
        {
            if (node.OwnerId == Player.Id)
            {
                score += node.NumWorkers * 10;
                score += node.BuildingLevel * 20;
            }
            else if (node.OwnerId != 0)
            {
                score -= node.NumWorkers * 5;
            }
        }

        return score;
    }
}
