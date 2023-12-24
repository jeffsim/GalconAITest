using System.Collections.Generic;
using UnityEditor;

public enum AIActionType { SendWorkersToNode, ConstructBuildingInOwnedNode };

public class AIAction
{
    public AIActionType Type;
    public int Count;
    public NodeData SourceNode;
    public NodeData DestNode;
}

class AI_PlayerState
{

}

class AI_BuildingState
{
    public BuildingDefn buildingDefn;

    public AI_BuildingState(BuildingData building)
    {
        buildingDefn = building?.Defn;
    }
}

class AI_NodeState
{
    private NodeData nodeData;
    public List<AI_NodeState> EmptyNeighborNodes;
    public AI_BuildingState Building;
    public bool HasBuilding => Building.buildingDefn != null;

    public AI_NodeState(NodeData nodeData)
    {
        this.nodeData = nodeData;
        EmptyNeighborNodes = new List<AI_NodeState>();
        Building = new AI_BuildingState(nodeData.Building);
    }
}

class AI_TownState
{
    public AI_NodeState[] Nodes;
    public List<AI_NodeState> PlayerOwnedNodes;

    TownData townData;
    PlayerData player;

    public AI_TownState(TownData townData, PlayerData player)
    {
        this.townData = townData;
        this.player = player;

        // Initialize Node list
        Nodes = new AI_NodeState[townData.Nodes.Count];
        for (int i = 0; i < townData.Nodes.Count; i++)
            Nodes[i] = new AI_NodeState(townData.Nodes[i]);

        // Initialize Player owned nodes list; also update EmptyNeighborNodes now that all Nodes are initialized.
        PlayerOwnedNodes = new();
        for (int i = 0; i < townData.Nodes.Count; i++)
        {
            foreach (var neighborNode in townData.Nodes[i].NodeConnections)
                if (neighborNode.End.OwnedBy == null)
                    Nodes[i].EmptyNeighborNodes.Add(Nodes[townData.Nodes.IndexOf(neighborNode.End)]);
                else if (townData.Nodes[i].OwnedBy == player)
                    PlayerOwnedNodes.Add(Nodes[i]);
        }
    }
}

public class PlayerAI
{
    PlayerData player;

    public PlayerAI(PlayerData playerData)
    {
        player = playerData;
    }

    internal void Update(TownData townData)
    {
        // Recursively take actions, modifying the state data (and then restoring it) as we go.  Find the highest value action.
        var state = new AI_TownState(townData, player);
        recursivelyDetermineBestAction(state);
    }

    void recursivelyDetermineBestAction(AI_TownState state, int curDepth = 0)
    {
        // Actions player can take:
        // 1. Send workers to a node that neighbors a node we own.
        // 2. Construct a building in a node we own.
        // 3. Upgrade a building in a node we own.
        // 4. Attack a node that neighbors a node we own.
        foreach (var node in state.PlayerOwnedNodes)
        {
            foreach (var emptyNeighborNode in node.EmptyNeighborNodes)
            {
                // Try sending 10% of node's workers to emptyNeighborNode.
                var action1 = state.SendWorkersFromNodeToNode(node, emptyNeighborNode, 0.1f);
                recursivelyDetermineBestAction(state, curDepth + 1);
                state.UndoAction(action1);

                // Try sending 50% of node's workers to emptyNeighborNode.
                var action2 = state.SendWorkersFromNodeToNode(node, emptyNeighborNode, 0.5f);
                recursivelyDetermineBestAction(state, curDepth + 1);
                state.UndoAction(action2);

                // Try sending 95% of node's workers to emptyNeighborNode.
                var action3 = state.SendWorkersFromNodeToNode(node, emptyNeighborNode, 0.95f);
                recursivelyDetermineBestAction(state, curDepth + 1);
                state.UndoAction(action3);

            }

            if (!node.HasBuilding)
            {
                // Try constructing a building in node if we have resources to build it and those resources are accessible

                // Prioritize buildings that gather resource if a neighboring node is generates that resource type
                // Prioritize buildings that progress a strategy
                // Prioritize buildings that provide defense

            }
        }
    }
}
