using System.Collections.Generic;

public class WorldState
{
    public HashSet<string> Conditions { get; private set; }
    public AI_TownState TownState { get; private set; }
    public int PlayerId { get; private set; }

    public WorldState(AI_TownState townState, int playerId)
    {
        Conditions = new HashSet<string>();
        TownState = townState;
        PlayerId = playerId;
    }

    public void UpdateWorldState()
    {
        Conditions.Clear();

        // Update Player Inventory
        foreach (var item in TownState.PlayerTownInventory)
        {
            if (item.Value > 0)
                Conditions.Add($"HasItem:{item.Key}");
        }

        // Update Nodes
        foreach (var node in TownState.Nodes)
        {
            if (node.OwnedBy == TownState.player)
            {
                Conditions.Add($"OwnsNode:{node.NodeId}");

                // Workers on Node
                if (node.NumWorkers > 0)
                    Conditions.Add($"HasWorkers:{node.NumWorkers}:{node.NodeId}");

                // Buildings
                if (node.HasBuilding)
                {
                    Conditions.Add($"HasBuilding:{node.BuildingDefn.BuildingType}:{node.NodeId}");
                }

                // Neighboring Nodes
                foreach (var neighbor in node.NeighborNodes)
                {
                    Conditions.Add($"IsNeighbor:{node.NodeId}:{neighbor.NodeId}");

                    if (neighbor.OwnedBy != TownState.player && neighbor.OwnedBy != null)
                    {
                        Conditions.Add($"EnemyNode:{neighbor.NodeId}");
                    }
                    else if (neighbor.OwnedBy == null)
                    {
                        Conditions.Add($"NeutralNode:{neighbor.NodeId}");
                    }
                }
            }
        }
    }
}
