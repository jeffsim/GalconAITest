using System;
using System.Collections.Generic;
using UnityEngine;

public enum AIAction
{
    None,
    SendWorkersToNode,
    ConstructBuilding,
    UpgradeBuilding,
    DestroyBuilding
};

public class AIMove
{
    public int Score = int.MinValue;
    public AIAction AIAction;

    public int SourceNodeId;
    public int TargetNodeId;
    public AIGameData GameData;

    public BuildingDefn BuildingToConstruct;
    public int NumWorkersToMove;

    internal int GetHashValue()
    {
        var buildingIndex = BuildingToConstruct == null ? 99 : BuildingToConstruct.Index;
        return (int)AIAction * 5 +                 // 5 Actions
                    SourceNodeId * (5 * 50) +           // 50 Nodes
                    TargetNodeId * (5 * 50 * 50) +         // 50 Nodes
                    NumWorkersToMove * (5 * 50 * 50 * 100) +    // Move up to 100 Workers
                    buildingIndex * (5 * 50 * 50 * 100 * 100);    // 100 buildings
    }


    #region Pooling
    static Queue<AIMove> _pool = new Queue<AIMove>(Settings.PoolSizes);
    static public void WarmUpPool()
    {
        for (int i = 0; i < Settings.PoolSizes; i++)
            _pool.Enqueue(new AIMove());
    }

    static AIMove Get() => _pool.Count > 0 ? _pool.Dequeue() : new AIMove();
    static public AIMove Get(int score)
    {
        var move = Get(AIAction.None, null);
        move.Score = score;
        return move;
    }

    static public AIMove Get(AIAction action, AINode sourceNode, AINode targetNode = null, int numWorkersToSend = 0, BuildingDefn buildingToConstruct = null)
    {
        return Get().Initialize(action, sourceNode, targetNode, numWorkersToSend, buildingToConstruct);
    }

    public void ReturnToPool() => _pool.Enqueue(this);
    static public void ResetPool() => _pool.Clear();
    #endregion

    /// <summary>
    /// Initializes the AIMove with the specific data; this is called many times during an AI evaluation
    /// </summary>
    private AIMove Initialize(AIAction action, AINode sourceNode, AINode targetNode = null, int numWorkersToSend = 0, BuildingDefn buildingToConstruct = null)
    {
        AIAction = action;
        Score = 0;
        SourceNodeId = sourceNode != null ? sourceNode.Id : 0;
        TargetNodeId = targetNode != null ? targetNode.Id : -1;
        NumWorkersToMove = numWorkersToSend;
        BuildingToConstruct = buildingToConstruct;

        return this;
    }

    internal bool IsWaitAction()
    {
        return AIAction == AIAction.None;
    }

    internal bool IsConstructAction(string buildingDefnId = null)
    {
        if (AIAction != AIAction.ConstructBuilding)
            return false;
        if (buildingDefnId == null)
            return true;
        return BuildingToConstruct.Id == buildingDefnId;
    }

    internal void Apply(AIGameData gameData)
    {
        GameData = gameData;
        AINode SourceNode = GameData.Nodes[SourceNodeId];
        AINode TargetNode = TargetNodeId >= 0 ? GameData.Nodes[TargetNodeId] : null;

        switch (AIAction)
        {
            case AIAction.None:
                break;
            case AIAction.SendWorkersToNode: // could be attack or reinforce
                SourceNode.NumWorkersInNode -= NumWorkersToMove;
                if (TargetNode.OwnedById == gameData.CurrentPlayerId)
                {
                    // reinforce
                    TargetNode.NumWorkersInNode += NumWorkersToMove;
                }
                else if (TargetNode.OwnedById == 0)
                {
                    // empty node - don't think should happen
                    //   Debug.Log("Hm, sent " + NumWorkersToMove + " workers from " + SourceNode.Id + " to " + TargetNode.Id + " but target node is empty");
                    TargetNode.NumWorkersInNode += NumWorkersToMove;
                }
                else
                {
                    // node owned by another player.
                    var targetNodeOwnerId = TargetNode.OwnedById;
                    var affinity = ConstantAIGameData.PlayerAffinities[GameData.CurrentPlayer.Id, targetNodeOwnerId];
                    if (affinity == Affinity.Hates || affinity == Affinity.DoesntHateButWantsTheirNodes)
                    {
                        // Attacking enemy node
                        // determine who wins.  rough estimate
                        var totalAttackPower = NumWorkersToMove * SourceNode.WorkerAttackDamage;
                        var totalDefensePower = TargetNode.NumWorkersInNode * TargetNode.WorkerDefensePower;
                        if (totalAttackPower > totalDefensePower)
                        {
                            // Conquered node
                            TargetNode.SetOwner(gameData.CurrentPlayer);
                            TargetNode.NumWorkersInNode = NumWorkersToMove - TargetNode.NumWorkersInNode;
                            // GameData.updateEnemyProximities();

                            BuildingDefn buildingDefn = null;
                            if (TargetNode.HasCompletedBuilding)
                                buildingDefn = TargetNode.CompletedBuildingDefn;
                            else
                            {
                                var pendingBuildingDefnId = TargetNode.GetPendingConstructionByPlayer(gameData.CurrentPlayerId);
                                if (pendingBuildingDefnId == null)
                                    pendingBuildingDefnId = TargetNode.GetPendingConstructionByPlayer(targetNodeOwnerId);
                                if (pendingBuildingDefnId != null)
                                    buildingDefn = GameDefns.Instance.BuildingDefns[pendingBuildingDefnId];
                            }

                            if (buildingDefn != null)
                            {
                                GameData.PlayerBuildingData.DeductBuildingCountForPlayer(buildingDefn, targetNodeOwnerId, GameData);
                                GameData.PlayerBuildingData.AddBuildingCountForPlayer(buildingDefn, gameData.CurrentPlayerId, GameData);
                            }
                            GameData.UpdateNearbyEnemies();
                        }
                        else
                            TargetNode.NumWorkersInNode -= NumWorkersToMove;
                    }
                }
                break;

            case AIAction.ConstructBuilding:
                TargetNode.ConstructBuilding(BuildingToConstruct, NumWorkersToMove, SourceNode);
                GameData.UpdatePlayerItemsOnBuildingConstruction(gameData.CurrentPlayer, BuildingToConstruct);
                // GameData.updateEnemyProximities();
                GameData.PlayerBuildingData.AddBuildingCountForPlayer(TargetNode.CompletedBuildingDefn, gameData.CurrentPlayerId, GameData);
                GameData.UpdateNearbyEnemies();
                break;

            case AIAction.UpgradeBuilding:
                SourceNode.UpgradeBuilding();
                break;

            case AIAction.DestroyBuilding:
                GameData.PlayerBuildingData.DeductBuildingCountForPlayer(SourceNode.CompletedBuildingDefn, gameData.CurrentPlayerId, GameData);
                SourceNode.DestroyBuilding();
                // GameData.updateEnemyProximities();
                break;
        }
    }

    internal void Apply(TownData town, PlayerData playerMakingMove)
    {
        // This Move is the optimal next move for the specified player - however, the player
        // may not be ready yet to make this move.  So: ensure the move is makeable below
        var townSourceNode = town.GetNodeById(SourceNodeId);
        var townTargetNode = TargetNodeId != -1 ? town.GetNodeById(TargetNodeId) : null;

        switch (AIAction)
        {
            case AIAction.None:
                break;

            case AIAction.SendWorkersToNode:
                if (NumWorkersToMove >= townSourceNode.Workers.Count + 5) // -5 to have some leeway
                    break; // not ready to do it yet
                var path1 = town.GetNodePath(townSourceNode, townTargetNode);
                if (path1.Count == 0)
                    return; // can't get there yet; e.g. haven't captured interim node it's fine

                // ensure we don't send ALL workers - keep at least one behind
                var numToMove = Math.Min(NumWorkersToMove, townSourceNode.NumWorkers - 1);
                town.SendWorkersToNode(townSourceNode, path1, numToMove, false);
                break;

            case AIAction.ConstructBuilding:
                // Something may have happened since the AI evaluated this move, so check that the building is still there and owned
                if (townTargetNode.HasBuilding)
                    break; // someone else completed construction

                if (townSourceNode.Workers.Count < NumWorkersToMove ||
                    !town.BuildingResourcesAreAvailable(BuildingToConstruct, playerMakingMove))
                    break; // not ready to do it yet

                var path = town.GetNodePath(townSourceNode, townTargetNode);
                if (path.Count == 0)
                    break; // can't get there yet; e.g. haven't captured interim node it's fine

                // if here, then we can make the move
                townTargetNode.TrackIntentToConstructBuilding(BuildingToConstruct, playerMakingMove);
                town.SendWorkersToNode(townSourceNode, path, NumWorkersToMove);
                break;

            case AIAction.UpgradeBuilding:
                // Something may have happened since the AI evaluated this move, so check that the building is still there and owned
                if (!townSourceNode.HasBuilding || townSourceNode.OwnedBy == null || townSourceNode.OwnedBy.Id != playerMakingMove.Id)
                    break; // not ready yet
                townSourceNode.Upgrade();
                break;

            case AIAction.DestroyBuilding:
                // Something may have happened since the AI evaluated this move, so check that the building is still there and owned
                if (!townSourceNode.HasBuilding || townSourceNode.HasBuildingUnderConstruction || townSourceNode.OwnedBy == null || townSourceNode.OwnedBy.Id != playerMakingMove.Id)
                    break; // not ready yet
                Debug.Log("Destroying " + townSourceNode.BuildingInNode.DefnId + " in " + townSourceNode.Id + " by " + playerMakingMove.Id);
                townSourceNode.DestroyBuilding();
                break;
        }
    }
}
