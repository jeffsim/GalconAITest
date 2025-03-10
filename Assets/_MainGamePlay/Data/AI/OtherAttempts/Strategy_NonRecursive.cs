using System.Collections.Generic;
using UnityEngine;

public partial class Strategy_NonRecursive
{
    public override string ToString() => $"Strategy_NonRecursive ({Player.Name[^1]})";

    TownData SourceTownData;
    PlayerData Player;
    AIAction BestAction;
    List<AI_NodeState> PlayerNodes = new(100);
    List<AI_NodeState> EnemyNodes = new(100);
    AI_TownState Town;

    const int minWorkersInNodeBeforeConsideringSendingAnyOut = 6;

    float personalityMultiplier_UpgradeNode = 0.7f;
    float personalityMultiplier_ButtressNode = 1.0f;
    float personalityMultiplier_BuildBuilding = 1.0f;
    float personalityMultiplier_CaptureNode = 1.0f;

    const float excessWorkersScalingFactor = 1f;
    const float excessWorkersScalingFactor2 = 1f;
    const float nearbyEnemiesScalingFactor = 1f;

    // Scaling factors for Build Building
    const float buildingResourceScalingFactor = 5f; // Influence of resource availability
    const float buildingStrategicScalingFactor = 8f; // Influence of strategic importance

    // Define the normalization parameters for Upgrade Node action
    const float upgradeNodeMinScore = 10f;
    const float upgradeNodeMaxScore = 40f; // Global max score across all actions
    const float buildBuildingMinScore = 20f;
    const float buildBuildingMaxScore = 40f; // Original score range for Build Building
    const float buttressNodeMinScore = 20f;
    const float buttressNodeMaxScore = 40f; // Global max score across all actions
    const float attackNodeMinScore = 20f;
    const float attackNodeMaxScore = 40f; // Global max score across all actions
    
    const float territoryEdgeScalingFactor = 10f;
    const float insufficientWorkersScalingFactor = 10f;

    public Strategy_NonRecursive(TownData townData, PlayerData player)
    {
        SourceTownData = townData;
        Town = new(player);
        Town.InitializeStaticData(townData);

        Debug.Log("if this works can I get rid of AI_NodeState and ai_townstate and just look at realNodes?");

        this.Player = player;
    }

    public AIAction DecideAction()
    {
        Town.UpdateState(SourceTownData);

        // TODO: Set personality multipliers based on player's personality
        personalityMultiplier_UpgradeNode = 0.7f;
        personalityMultiplier_ButtressNode = 1.0f;
        personalityMultiplier_BuildBuilding = 1.0f;
        personalityMultiplier_CaptureNode = 1.0f;

        BestAction = Player.AI.GetAIAction();

        UpdateNodeDetails();

        CheckPriority_UpgradeNode();
        CheckPriority_ButtressNode();
        CheckPriority_BuildBuilding();
        CheckPriority_AttackEnemyNodes();

        return BestAction;
    }

    void UpdateNodeDetails()
    {
        PlayerNodes.Clear();
        foreach (var node in Town.Nodes)
            if (node.OwnedBy == Player)
                PlayerNodes.Add(node);

        // Calculate the number of enemies in neighboring nodes for each of our nodes
        var Nodes = Town.Nodes;
        int numNodes = Nodes.Length;
        for (int i = 0; i < numNodes; i++)
        {
            var node = Nodes[i];
            if (node.OwnedBy != Player) continue;
            node.NumEnemiesInNeighborNodes = 0;
            node.IsOnTerritoryEdge = false;
            var nnodes = node.NeighborNodes;
            var count = nnodes.Count;
            for (int n = 0; n < count; n++)
            {
                var nn = nnodes[n];
                if (nn.OwnedBy != Player)
                {
                    if (nn.OwnedBy != null)
                        node.NumEnemiesInNeighborNodes += nn.NumWorkers;
                    node.IsOnTerritoryEdge = true;
                }
            }
        }

        // we only care about enemies on the territory edge (enemies inside their terrain aren't accesisble)
        EnemyNodes.Clear();
        foreach (var node in Town.Nodes)
            if (node.OwnedBy != Player && node.OwnedBy != null)
                foreach (var neighbor in node.NeighborNodes)
                    if (neighbor.OwnedBy == Player)
                    {
                        if (!EnemyNodes.Contains(node))
                            EnemyNodes.Add(node);
                        break;
                    }
    }
}