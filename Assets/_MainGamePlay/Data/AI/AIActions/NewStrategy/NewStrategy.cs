using System;
using UnityEngine;

/*
    sigh.  different approach
    
    identify strategic needs (goals). DON'T do it recursively because FUCKME
    MAYBE do one-level deep goals => subgoals
        I need wood
            nothing drives this need except it's just hardcoded. you always need wood
            could be somewhat heuristic; e.g. start of game don't need iron; end of game don't need wood as much
            priority is impacted by how much you have
            IDEALLY it's impacted by how much you need e.g. to build a barracks - but I don't want to recurse
        
        I want to capture more territory / kill enemy
            subgoals
                need a barracks to generate warriors
                need an archery range to counteract the enemies' mages

                // GOAP version:
                need warriors to attack/defend
                    need a barracks to train them
                        need stone to build barracks
                        need to capture empty node to build barracks
                            need to ...



        I want to build a barracks
            subgoals
                need stone to build the barracks

    create list of all subgoals from the above goals

    goals.SortBy(priority)
    List subGoals = goals.AllSubgoals
    foreach (subgoal)
        determine priority
        determine feasibility
            can I do it now?
            if not, what's the very first step towards doing it?
                (I need wood but don't have a path to it - find the forest nearest to me and build towards it)
                (I want a barracks but can't build one wihtout stone - this is NOT handled here but above. economic chain not in here.
    pick the best, and take the first step towards doing it
*/

public class NewStrategy
{
    const int MAX_DEPTH = 4;
    const int MAX_ACTIONS = 250;
    const int MAX_BUILDING_UPGRADE_LEVEL = 4; // todo: per building

    Strategy_AIAction[] actions;
    public int NumActionsConsidered;

    Strategy_TownState Town;
    PlayerData Player;

    // Undo data structure to revert actions
    struct ActionUndoData
    {
        public int FromNodeNumWorkers;
        public int ToNodeNumWorkers;
        public int ToNodeOwnerId;
        public BuildingType ToNodeBuildingType;
        public int ToNodeBuildingLevel;
        public ulong NodesVisited;
    }

    private ActionUndoData[] actionUndoData = new ActionUndoData[MAX_DEPTH];

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
                strategyNode.NeighborIndices[j] = connection.End.NodeId;
            }

            Town.Nodes[i] = strategyNode;
        }
    }

    public Strategy_AIAction DecideAction(TownData sourceTownData)
    {
        InitializeToTown(sourceTownData);
        NumActionsConsidered = 0;
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

        // Update constructible buildings
        state.NumConstructibleBuildings = 0;
        foreach (var building in GameDefns.Instance.BuildingDefns.Values)
            if (building.CanBeBuiltByPlayer && building.IsEnabled)
            {
                bool canBuild = true;
                // Add resource checks if needed
                if (canBuild)
                    state.ConstructibleBuildings[state.NumConstructibleBuildings++] = building;
            }

        var actionCount = EnumerateActions(state);
        for (int i = 0; i < actionCount; i++)
        {
            NumActionsConsidered++;
            var action = actions[i];

            // Apply action
            ApplyAction(ref state, action, currentDepth);

            EvaluateActions(state, currentDepth + 1, ref bestAction, ref bestValue);

            // Unapply action
            UnapplyAction(ref state, action, currentDepth);

            if (currentDepth == 0)
            {
                float actionValue = EvaluateState(state);
                if (actionValue > bestValue)
                {
                    bestValue = actionValue;
                    bestAction = action;
                }
            }
        }
    }
    int actionCount;

    private int EnumerateActions(Strategy_TownState state)
    {
        actionCount = 0;

        /* TODO: This is a "fromNode" approach.  Consider flipping to a "toNode" approach and see what changes
            foreach (nodeOwnedByPlayer)
                considerUpgrade(node.Building)
                considerButtressTo()    <== should consider pincher
            foreach (nodeOwnedByEnemy)
                considerAttackTo(nodeOwnedByEnemy)    <== should consider pincher
            foreach (unownedNode w/o building)
                considerConstrucingTo(, allBuildingTypes)    <== should consider pincher
        
            considerAttackTo(targetNode)
                // should consider pincher
                float bestAttack = -1;
                int numAttackersNeededToConsiderAttack = targetNode.NumWorkers + 5; // tweak 5 based on AI style
                var fromNodes = targetNode.NodesWithPathToNodeOwnedBy(Player)
                foreach (node in fromNodes)
                    attackPriority = node.hasEnoughWorkers + deltaInWorkers(node, targetNode) + distnace
                    bestAttack = max(...)

        */
        foreach (var node in state.Nodes)
        {
            if (node.OwnerId != Player.Id) continue;
            if ((state.NodesVisited & (1UL << node.NodeId)) != 0) continue; // avoid pingponging between nodes down the recusion

            Debug.Assert(actionCount < MAX_ACTIONS);

            for (int n = 0; n < node.NumNeighbors && actionCount < MAX_ACTIONS; n++)
            {
                int neighborIndex = node.NeighborIndices[n];
                var neighbor = state.Nodes[neighborIndex];

                if (neighbor.OwnerId == 0 && neighbor.BuildingType == BuildingType.None)
                    CheckAction_ConstructBuilding(node, neighbor, state);
                else if (neighbor.OwnerId != Player.Id)
                    CheckAction_AttackEnemyNode(node, neighbor, state);
                else if (neighbor.OwnerId == Player.Id)
                    CheckAction_ButtressBuilding(node, neighbor, state);
                // TODO: Buttress/attack/etc from NOT neighbors (e.g. 3 away)
                //  ignoring perf, could just recurse here down into node.neighbors.neighbors...
                //  that feels like the 'correct' solution ignoring perf.  could recurse to every node player owns.
                //  need to do pathing (eek).  
                //      option: when node.ownerchanges do a BFS etc for every ownedNode to created list sorted by distance of every
                //          other owned node... however; don't want to do that in the recusive simulation here...
            }
            CheckAction_UpgradeBuilding(node);
        }

        return actionCount;
    }

    private void CheckAction_ConstructBuilding(Strategy_Node node, Strategy_Node neighbor, Strategy_TownState state)
    {
        for (var b = 0; b < state.NumConstructibleBuildings && actionCount < MAX_ACTIONS; b++)
        {
            var building = state.ConstructibleBuildings[b];
            // Decide if we want to construct a building here.
            // TODO: prefilter out here.  try to limit space by culling poor choices here based on game state
            // * Strategically; don't build barracks if no reason; don't build carpenter if too early; etc
            int numWorkers = node.NumWorkers / 2;
            actions[actionCount++] = new Strategy_AIAction(ActionType.CaptureNodeAndConstructBuilding)
            {
                SourceNodeIndex = node.NodeId,
                TargetNodeIndex = neighbor.NodeId,
                BuildingType = building.BuildingType,
                NumWorkers = numWorkers
            };
        }
    }

    private bool CheckAction_AttackEnemyNode(Strategy_Node nodeFrom, Strategy_Node nodeTo, Strategy_TownState state)
    {
        if (nodeFrom.NumWorkers < nodeTo.NumWorkers) return false;

        // TODO: Real calc
        int numAttackers = nodeFrom.NumWorkers / 2;
        actions[actionCount++] = new Strategy_AIAction(ActionType.AttackEnemyNode)
        {
            SourceNodeIndex = nodeFrom.NodeId,
            TargetNodeIndex = nodeTo.NodeId,
            NumWorkers = numAttackers
        };
        return true;
    }

    void CheckAction_ButtressBuilding(Strategy_Node nodeFrom, Strategy_Node nodeTo, Strategy_TownState state)
    {
        if (nodeFrom.NumWorkers < 4) return;

        for (int n = 0; n < nodeTo.NumNeighbors; n++)
        {
            int neighborIndex = nodeTo.NeighborIndices[n];
            var neighbor = state.Nodes[neighborIndex];
            if (neighbor.OwnerId != Player.Id && neighbor.NumWorkers > nodeTo.NumWorkers)
            {
                int numWorkers = nodeFrom.NumWorkers / 2;
                actions[actionCount++] = new Strategy_AIAction(ActionType.ButtressBuilding)
                {
                    SourceNodeIndex = nodeFrom.NodeId,
                    TargetNodeIndex = nodeTo.NodeId,
                    NumWorkers = numWorkers
                };
                return;
            }
        }
    }

    private void CheckAction_UpgradeBuilding(Strategy_Node node)
    {
        // == Check if we want to perform the action
        if (!node.IsUpgradableBuilding || node.BuildingLevel == MAX_BUILDING_UPGRADE_LEVEL) return;
        if (node.NumWorkers < node.BuildingLevel * node.BuildingLevel) return;

        // == We want to perform it - add it to the list of potential actions
        actions[actionCount++] = new Strategy_AIAction(ActionType.UpgradeBuilding)
        {
            TargetNodeIndex = node.NodeId
        };
    }

    private void ApplyAction(ref Strategy_TownState state, Strategy_AIAction action, int currentDepth)
    {
        ref Strategy_Node fromNode = ref state.Nodes[action.SourceNodeIndex];
        ref Strategy_Node toNode = ref state.Nodes[action.TargetNodeIndex];
        int numWorkers = action.NumWorkers;

        var undoData = new ActionUndoData
        {
            FromNodeNumWorkers = fromNode.NumWorkers,
            ToNodeNumWorkers = toNode.NumWorkers,
            ToNodeOwnerId = toNode.OwnerId,
            ToNodeBuildingType = toNode.BuildingType,
            ToNodeBuildingLevel = toNode.BuildingLevel,
            NodesVisited = state.NodesVisited
        };

        actionUndoData[currentDepth] = undoData;
        state.NodesVisited |= 1UL << fromNode.NodeId;

        // Simulate action
        switch (action.Type)
        {
            case ActionType.AttackEnemyNode:
                bool attackerWonBattle = fromNode.NumWorkers > toNode.NumWorkers;
                fromNode.NumWorkers -= numWorkers;
                toNode.NumWorkers = fromNode.NumWorkers - toNode.NumWorkers;
                if (attackerWonBattle)
                    toNode.OwnerId = Player.Id;
                break;

            case ActionType.CaptureNodeAndConstructBuilding:
                fromNode.NumWorkers -= numWorkers;
                toNode.NumWorkers = numWorkers;
                toNode.OwnerId = Player.Id;
                toNode.BuildingType = action.BuildingType;
                toNode.BuildingLevel = 1;
                break;

            case ActionType.ButtressBuilding:
                fromNode.NumWorkers -= numWorkers;
                toNode.NumWorkers += numWorkers;
                break;

            case ActionType.UpgradeBuilding:
                toNode.BuildingLevel++;
                break;
        }
    }

    private void UnapplyAction(ref Strategy_TownState state, Strategy_AIAction action, int currentDepth)
    {
        ref Strategy_Node fromNode = ref state.Nodes[action.SourceNodeIndex];
        ref Strategy_Node toNode = ref state.Nodes[action.TargetNodeIndex];

        var undoData = actionUndoData[currentDepth];

        fromNode.NumWorkers = undoData.FromNodeNumWorkers;
        toNode.NumWorkers = undoData.ToNodeNumWorkers;
        toNode.OwnerId = undoData.ToNodeOwnerId;
        toNode.BuildingType = undoData.ToNodeBuildingType;
        toNode.BuildingLevel = undoData.ToNodeBuildingLevel;
        state.NodesVisited = undoData.NodesVisited;

        // undo visited flag too
        // TODO: ???
        state.NodesVisited &= ~(1UL << fromNode.NodeId);
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
