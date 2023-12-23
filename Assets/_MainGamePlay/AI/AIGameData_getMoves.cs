using System;
using System.Collections.Generic;
using UnityEngine;

// Abridged version of full gamedata used for AI evaluation
public partial class AIGameData
{
    List<AIMove> moves = new List<AIMove>(1000);
    public bool CurrentPlayerHatesPlayer(int playerId) => ConstantAIGameData.PlayerAffinities[CurrentPlayerId, playerId] == Affinity.Hates;
    public bool CurrentPlayerWantsPlayersNodes(int playerId) => ConstantAIGameData.PlayerAffinities[CurrentPlayerId, playerId] == Affinity.DoesntHateButWantsTheirNodes;

    internal List<AIMove> getMoves()
    {
        moves.Clear();
        moves.Add(AIMove.Get(AIAction.None, null)); // wait

        foreach (var node in Nodes)
        {
            // Can only take action from nodes that are owned by the current player
            if (!node.IsOwnedBy(CurrentPlayerId)) continue;

            // Do not take action from nodes with buildings under construction
            if (node.HasUnderConstructionBuilding(CurrentPlayerId)) continue;

            if (Town.getAIConstraint(AIConstraintType.NoBuildingUpgrades) == null) // no constraint
                addUpgradeMoves(node);

            // Verify we're allowed to send out from this node
            if (Town.getAIConstraint(AIConstraintType.DontAttackFromNode, node.Id) != null) continue;

            // addDestroyMoves(node);
            addReinforceFromMoves(node);
            addAttackFromMoves(node);
            addConstructFromMoves(node);
        }

        return moves;
    }

    private void addReinforceFromMoves(AINode node)
    {
        // Only reinforce from node if it has ample workers
        if (node.NumWorkersInNode < 10)
            return;

        // If node has a neighboring enemy and its # of workers isn't maxed out, then don't reinforce from it
        if (node.NumHopsToClosestEnemy == 1 && node.NumWorkersInNode < node.MaxNumWorkers)
            return;

        // Don't reinforce from a gatherer or crafter unless it's useless or it has too many workers (defined as 50% more than max)
        if ((node.HasCompletedBuildingOfClass(BuildingClass.Gatherer) || node.HasCompletedBuildingOfClass(BuildingClass.Crafter)) && node.NumWorkersInNode < node.MaxNumWorkers * 1.5f)
            return;

        foreach (var destNode in Nodes)
        {
            // Only reinforce to Nodes we control
            if (destNode.OwnedById != CurrentPlayerId)
                continue;

            // Node either needs to have a constructed building or a building pending construction
            if (!destNode.HasCompletedBuilding && !destNode.HasUnderConstructionBuilding(CurrentPlayerId))
                continue;

            // Only reinforce to complete buildings that can accept workers
            if (destNode.HasCompletedBuilding && !destNode.CompletedBuildingDefn.CanAssignWorkers)
                continue;

            // Don't reinforce to stronger node
            if (node.NumWorkersInNode < destNode.NumWorkersInNode)
                continue;

            // Don't reinforce from front-line node to inner node
            if (node.NumHopsToClosestEnemy < destNode.NumHopsToClosestEnemy)
                continue;

            // Don't reinforce to inner nodes
            if (destNode.NumHopsToClosestEnemy > 1)
                continue;

            // Don't reinforce to nodes that are materially above max workers
            if (destNode.NumWorkersInNode > destNode.MaxNumWorkers * 3)
                continue;

            // Only reinforce to buildings we can travel to (Within 3 nodes)
            if (!CurrentPlayerCanTravelFromTo(node, destNode))
                continue;

            // If here, then we can reinforce from node to destNode
            createActionsToSendWorkersFromToNode(moves, AIAction.SendWorkersToNode, node, destNode);
        }
    }

    private void addAttackFromMoves(AINode node)
    {
        // Only attack from node if it has ample workers
        if (node.NumWorkersInNode < 6)
            return;

        var totalAttackPower = node.NumWorkersInNode * node.WorkerAttackDamage;

        foreach (var destNode in Nodes)
        {
            // Only attack nodes that are controlled by players we hate
            if (!CurrentPlayerHatesPlayer(destNode.OwnedById) && !CurrentPlayerWantsPlayersNodes(destNode.OwnedById))
                continue;

            // Only attack TO destNode if we think we can win
            var totalDefensePower = destNode.NumWorkersInNode * destNode.WorkerDefensePower;
            if (totalAttackPower < totalDefensePower)
                continue;

            // Only attack nodes we're allowed to attack
            if (Town.getAIConstraint(AIConstraintType.DisallowAttackingNode, destNode.Id) != null)
                continue;

            // Only attack nodes we can travel to (Within 3 nodes)
            if (!CurrentPlayerCanTravelFromTo(node, destNode))
                continue;

            // If here, then we can attack from node to destNode
            createActionsToSendWorkersFromToNode(moves, AIAction.SendWorkersToNode, node, destNode);
        }
    }

    private void addConstructFromMoves(AINode sourceNode)
    {
        // Only construct from node if it has ample workers
        if (sourceNode.NumWorkersInNode < 6)
            return;

        // TODO (PERF): I shouldn't have to do this here, but if I don't then player item counts get out of sync.
        UpdatePlayerItems(CurrentPlayer);

        foreach (var destNode in Nodes)
        {
            // Only construct in unowned nodes
            if (destNode.OwnedById != 0)
                continue;

            // Only try to construct in nodes that do not have completed or under construction buildings
            // Node either needs to have a constructed building or a building pending construction
            if (destNode.HasCompletedBuilding || destNode.HasUnderConstructionBuilding(CurrentPlayerId))
                continue;

            // Only attack nodes we're allowed to attack
            if (Town.getAIConstraint(AIConstraintType.DisallowAttackingNode, destNode.Id) != null)
                continue;

            // Only construct buildings in nodes we can reach (Within 3 nodes)
            if (!CurrentPlayerCanTravelFromTo(sourceNode, destNode))
                continue;

            // If here then we can construct in destNode.  Determine which buildings we want to consider constructing
            foreach (var buildingDefn in CurrentPlayer.BuildingsCanConstruct)
                if (shouldConsiderConstructingBuildingDefn(buildingDefn, destNode))
                    createActionsToSendWorkersFromToNode(moves, AIAction.ConstructBuilding, sourceNode, destNode, buildingDefn);
        }
    }

    void addDestroyMoves(AINode node)
    {
        // Only destroy if there is strategic value in doing so; e.g. we've captured a woodcutter and we already have enough woodcutters and need a stoneminer instead
        // I *think* the above will get caught in the evaluation heuristic.
        if (!node.HasCompletedBuilding)
            return;

        // TODO: Can't destroy Camps for now - but will want to change this later
        if (node.CompletedBuildingDefn.BuildingClass == BuildingClass.Camp)
            return;

        moves.Add(AIMove.Get(AIAction.DestroyBuilding, node));
    }

    void addUpgradeMoves(AINode node)
    {
        // TODO: Add strategically "safe" upgrades - e.g. enemy doesn't have many workers nearby so it's safer to upgrade

        // Don't upgrade unless it's protected
        // For now, don't upgrade at all if neighboring enemy; later, upgrade if it's safe to do so
        if (node.NumHopsToClosestEnemy == 1 && node.NumWorkersInNode < node.MaxNumWorkers * 2)
            return;

        // if we have enough workers then consider upgrading
        if (node.NumWorkersInNode >= node.MaxNumWorkers)
            moves.Add(AIMove.Get(AIAction.UpgradeBuilding, node));
    }

    bool shouldConsiderConstructingBuildingDefn(BuildingDefn buildingDefn, AINode node)
    {
        var totalPlayerBuildings = PlayerBuildingData.NumBuildings;
        var isEarlyGame = totalPlayerBuildings < 6;

        // If we don't have materials to construct building then don't consider it
        // TODO: Should ensure that the materials are accessible; however, I'm not tracking that on a per-node basis in AIGameData
        if (!hasNecessaryResourcesToConstruct(CurrentPlayer, buildingDefn))
            return false;

        // TODO: Consider applying some strategy here to limit; e.g. if have 5 camps on a 6 node map, then don't build another camp...
        switch (buildingDefn.BuildingClass)
        {
            case BuildingClass.Camp: return true; // always consider building camps

            case BuildingClass.Gatherer:

                // Can only construct woodcutter buildings in forests, minershut in stonemine, etc
                if (node.Defn.Type != NodeType.Resource) return false;

                Debug.Assert(node.CompletedBuildingDefn != null, "must have building");
                Debug.Assert(node.CompletedBuildingDefn.GatherableResource, "must be gatherable resource building");

                break;

            case BuildingClass.Crafter:
                // Don't build if don't have the resource needed to craft
                var haveItems = false;
                foreach (var item in buildingDefn.CraftableItems)
                    if (CurrentPlayer.HaveMatsToCraftItem(item))
                        haveItems = true;
                if (!haveItems)
                    return false; // can't craft anything.  TODO: Check if player has buildings but not resources yet; can still build then

                if (isEarlyGame)
                {
                    // In the early game, don't build lumbermills before woodcutters If we have limited wood supplies.
                    int numWoodcutters = PlayerBuildingData.BuildingCounts[BuildingType.Woodcutter],
                        numLumbermills = PlayerBuildingData.BuildingCounts[BuildingType.Lumbermill];
                    if (numLumbermills >= numWoodcutters && buildingDefn.BuildingType == BuildingType.Lumbermill)// && CurrentPlayer.NumItemsOwned(ItemType.Wood) < 20)
                        return false;

                    // TODO: Add ItemType.Wood etc, and use that instead of strings
                    // TODO: Repeat above for other crafting buildings
                }
                break;

            case BuildingClass.Storage:
                // For now, assume 1 storage per 10 buildings
                if (totalPlayerBuildings < 10) return false;
                break;

            case BuildingClass.Defense:
                // don't build defenses too early
                if (isEarlyGame)
                {
                    if (node.NumHopsToClosestEnemy > 1) return false;
                }
                else
                {
                    if (node.NumEnemyNodesNearby == 0) return false;
                }

                // Only construct Defensive buildings if enemies are close
                // TODO (FUTURE)... or there is strategic value in protecting this node; e.g. chokepoint
                if (node.NumHopsToClosestAggressiveEnemy > 1) return false;

                break;

            default:
                // unrecognized building class
                Debug.Log("not handled " + buildingDefn.Id);
                break;
        }

        return true;
    }

    // Return true if the currentplayer can go from start to dest
    private bool CurrentPlayerCanTravelFromTo(AINode startNode, AINode destNode)
    {
        // TODO: Consider tracking bool for below (canTravel(source,dest)) that only changes when buildings are captured...

        if (startNode == destNode) return false;

        // Fake BFS
        // simple/stupid for the moment - must have a connection (within 3)
        for (int i = 0; i < startNode.ConnectedNodes.Count; i++)
        {
            var conn = startNode.ConnectedNodes[i];
            if (conn == destNode)
                return true;

            // See if can walk through connected node; only if it has a buliding that we own
            if (!conn.CanWalkThrough(CurrentPlayerId))
                continue;

            for (int j = 0; j < conn.ConnectedNodes.Count; j++)
            {
                var conn2 = conn.ConnectedNodes[j];
                if (conn2 == destNode)
                    return true;

                // See if can walk through connected node; only if it has a buliding that we own
                if (!conn2.CanWalkThrough(CurrentPlayerId))
                    continue;

                for (int k = 0; k < conn2.ConnectedNodes.Count; k++)
                    if (conn2.ConnectedNodes[k] == destNode)
                        return true;
            }
        }
        return false;
    }

    private void createActionsToSendWorkersFromToNode(List<AIMove> moves, AIAction action, AINode node, AINode destNode, BuildingDefn buildingToConstruct = null)
    {
        var doAdvancedAI = false;
        if (doAdvancedAI)
        {
            // Consider sending: 1/3, 1/2, 2/3, all
            moves.Add(AIMove.Get(action, node, destNode, node.NumWorkersInNode / 3, buildingToConstruct));
            if (Math.Abs(node.NumWorkersInNode / 3 - node.NumWorkersInNode / 2) > 10)// only do if big enough to matter
                moves.Add(AIMove.Get(action, node, destNode, node.NumWorkersInNode / 2, buildingToConstruct));

            if (Math.Abs(node.NumWorkersInNode / 3 - node.NumWorkersInNode * 2 / 3) > 15)// only do if big enough to matter
                moves.Add(AIMove.Get(action, node, destNode, node.NumWorkersInNode * 2 / 3, buildingToConstruct));
            moves.Add(AIMove.Get(action, node, destNode, node.NumWorkersInNode - 1, buildingToConstruct));
        }
        else
        {
            // consider sending half and all
            moves.Add(AIMove.Get(action, node, destNode, node.NumWorkersInNode - 1, buildingToConstruct));

            if (node.NumWorkersInNode > 30)// only consider sending half if have enough workers to be different than sending all
                moves.Add(AIMove.Get(action, node, destNode, node.NumWorkersInNode / 2, buildingToConstruct));
        }
    }
}