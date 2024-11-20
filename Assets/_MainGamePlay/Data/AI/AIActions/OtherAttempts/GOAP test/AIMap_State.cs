using System;
using System.Collections.Generic;
using System.Linq;

public class AIMap_State
{
    public List<AINode_State> AllNodes = new();

    public AIMap_State(TownData townData)
    {
        // Populate AllNodes from townData
        foreach (var node in townData.Nodes)
            AllNodes.Add(new AINode_State(node));

        foreach (var aiNode in AllNodes)
        {
            // connect neighbors
            var townNode = townData.Nodes.First(n => n.NodeId == aiNode.NodeId);
            foreach (var conn in townNode.NodeConnections)
            {
                var endNode = AllNodes.First(n => n.NodeId == conn.End.NodeId);
                aiNode.Neighbors.Add(endNode);
                if (conn.IsBidirectional)
                    endNode.Neighbors.Add(aiNode);
            }
        }

    }

    // public AINode_State FindClosestResourceNode(int playerId, ResourceType resourceType)
    // {
    //     // Find the nearest node with the resource, avoiding enemy-controlled nodes
    //     return AllNodes.Where(n => n.Resources.ContainsKey(resourceType) && n.OwnerId == playerId)
    //                    .OrderBy(n => n.DistanceTo(playerId)) // Assuming DistanceTo is a method to calculate distance
    //                    .FirstOrDefault();
    // }

    public float CalculatePathCost(int playerId, AINode_State targetNode)
    {
        // Placeholder for pathfinding logic that computes cost to reach a node
        return targetNode.Neighbors.Where(n => n.OwnerId != playerId).Sum(n => n.MilitaryStrength * 1.2f);
    }

    public float GetCriticalityOfResource(int playerId, GoodType resourceType)
    {
        // Placeholder for a method that evaluates how critical a resource is
        return AllNodes.Where(n => n.OwnerId == playerId && n.Resources.ContainsKey(resourceType))
                       .Sum(n => 100 - n.Resources[resourceType]); // Hypothetical criticality assessment
    }

    public List<AINode_State> GetPlayerNodes(int playerId)
    {
        return AllNodes.Where(node => node.OwnerId == playerId).ToList();
    }
}
