using System.Collections.Generic;

public class PlayerAIData
{
    public PlayerData Player;
    public TownData Town;

    public float NextAIUpdateTime;

    public static int TotalMoves = 0;
    static public bool warmedPool = false;

    public PlayerAIData(PlayerData player, TownData town)
    {
        Player = player;
        Town = town;
    }
    
    public void Update()
    {
        var evaluator = new Algorithm_SimpleRecurse();
        // var evaluator = new Algorithm_ABNegaMax();

        var board = AIGameData.Get(Town, Player.Id);
        TotalMoves = 0;
        var bestNextMove = evaluator.GetBestMove(board, Town.Defn.EnemyIntelligence);
        if (bestNextMove != null)
        {
            bestNextMove.Apply(Town, Player);
            bestNextMove.ReturnToPool();
        }
        board.ReturnToPool();
    }

    private int NumEnemyAdjacentNodes(NodeData node)
    {
        int count = 0;
        foreach (var conn in node.ConnectedNodes)
            if (nodeIsControlledByEnemy(conn))
                count++;
        return count;
    }

    private bool nodeIsControlledByEnemy(NodeData conn)
    {
        return conn.OwnedBy != null && conn.OwnedBy.Hates(Player);
    }

    private bool nodeHasNeighboringBuilding(NodeData node, BuildingDefn forest)
    {
        foreach (var conn in node.ConnectedNodes)
            if (conn.HasBuildingOfType(forest))
                return true;
        return false;
    }

    public bool TryToBuildInNeighboringNode(NodeData sourceNode, NodeData targetNode, BuildingDefn buildingDefn)
    {
        if (Town.BuildingResourcesAreAvailable(buildingDefn, Player))
        {
            targetNode.TrackIntentToConstructBuilding(buildingDefn, Player);
            var path = Town.GetNodePath(sourceNode, targetNode); // todo: overkill since it's a connecting node
            Town.SendWorkersToNode(sourceNode, path, sourceNode.NumWorkers / 2);
            return true;
        }
        return false;
    }

    private bool playerHasBuilding(List<NodeData> nodes, BuildingDefn buildingDefn)
    {
        foreach (var node in nodes)
            if (node.BuildingInNode != null && node.BuildingInNode.DefnId == buildingDefn.Id)
                return true;
        return false;
    }
}
