using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class Strategy_NonRecursive
{
    TownData SourceTownData;
    PlayerData Player;
    AIAction BestAction;
    List<AI_NodeState> PlayerNodes = new(100);
    List<AI_NodeState> EnemyNodes = new(100);
    AI_TownState Town;

    float personalityMultiplier_UpgradeNode = 0.7f;
    float personalityMultiplier_ButtressNode = 1.0f;
    float personalityMultiplier_BuildBuilding = 1.0f;
    float personalityMultiplier_CaptureNode = 1.0f;

    // Scaling factors for Build Building
    const float buildingResourceScalingFactor = 5f; // Influence of resource availability
    const float buildingStrategicScalingFactor = 8f; // Influence of strategic importance

    const int minWorkersInNodeBeforeConsideringSendingAnyOut = 10;

    // Normalization parameters for Build Building
    const float buildBuildingMinScore = 20f;
    const float buildBuildingMaxScore = 30f; // Original score range for Build Building

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

        EnemyNodes.Clear();
        foreach (var node in Town.Nodes)
            if (node.OwnedBy != Player)
                EnemyNodes.Add(node);

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
    }
}