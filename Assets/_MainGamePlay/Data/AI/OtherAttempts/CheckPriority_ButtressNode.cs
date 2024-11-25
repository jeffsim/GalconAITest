using UnityEngine;
using System.Collections.Generic;
using JetBrains.Annotations;

public partial class Strategy_NonRecursive
{
    // New constants for Buttress Node action
    const float buttressNodeMinScore = 20f;
    const float buttressNodeMaxScore = 40f; // Global max score across all actions
    const float territoryEdgeScalingFactor = 10f;
    const float insufficientWorkersScalingFactor = 10f;
    List<AI_NodeState> nodesWithExcessWorkers = new(100);

    private void CheckPriority_ButtressNode()
    {
        int playerNodesCount = PlayerNodes.Count;

        // List to store nodes that can reinforce other nodes
        nodesWithExcessWorkers.Clear();
        for (int i = 0; i < playerNodesCount; i++)
        {
            var node = PlayerNodes[i];
            if (node.NumWorkers > node.MaxWorkers || node.NumWorkers > 15) // TODO
                nodesWithExcessWorkers.Add(node);
        }
        if (nodesWithExcessWorkers.Count == 0)
            return;

        for (int i = 0; i < playerNodesCount; i++)
        {
            var toNode = PlayerNodes[i];
            float rawValue = 0f;

            // 1. Calculate value based on nearby enemies
            if (toNode.NumEnemiesInNeighborNodes > 0)
            {
                float delta = toNode.NumEnemiesInNeighborNodes - toNode.NumWorkers;
                rawValue += Mathf.Pow(delta, 2) * nearbyEnemiesScalingFactor;
            }

            // 2. Increase value if the node is on the territory edge
            if (toNode.IsOnTerritoryEdge)
            {
                rawValue += territoryEdgeScalingFactor;
            }

            // 3. Increase value if the node has significantly fewer workers than its max
            if (toNode.NumWorkers < toNode.MaxWorkers / 2)
            {
                float workersDeficit = toNode.MaxWorkers - toNode.NumWorkers;
                rawValue += Mathf.Pow(workersDeficit, 2) * insufficientWorkersScalingFactor;
            }

            // 4. Normalize the raw value
            float clampedRawValue = Mathf.Clamp(rawValue, buttressNodeMinScore, buttressNodeMaxScore);
            float normalizedValue = (clampedRawValue - buttressNodeMinScore) / (buttressNodeMaxScore - buttressNodeMinScore);
            // normalizedValue is now between 0.0 and 0.333

            // 5. Apply AI personality multiplier
            float finalValue = normalizedValue * personalityMultiplier_ButtressNode;

            // 6. Update Best Action if this action is better than the current best action
            if (finalValue > BestAction.Score)
            {
                // TODO: Ideally BFS out similar to attack.
                // Instead, just get the neighbor with the most available workers and if there is enough then pull from it
                var fromNode = getFriendlyNodeWithMostWorkers(toNode);
                if (fromNode == null) continue;
                if (fromNode.OwnedBy == Player && fromNode.NumWorkers > fromNode.MaxWorkers * 3f / 4f)
                {
                    int numSent = fromNode.NumWorkers / 2;

                    AIDebuggerEntryData debuggerEntry = null;
#if DEBUG
                    if (AITestScene.Instance.TrackDebugAIInfo)
                        debuggerEntry = AIDebugger.rootEntry.AddEntry_SendWorkersToOwnedNode(fromNode, toNode, numSent, finalValue, Player.AI.debugOutput_ActionsTried++, 0);
#endif
                    BestAction.SetTo_SendWorkersToOwnedNode(fromNode, toNode, numSent, finalValue, debuggerEntry);
                }
            }
        }
    }

    AI_NodeState getFriendlyNodeWithMostWorkers(AI_NodeState toNode)
    {
        // starting at toNode, do a BFS search to find the player-owned node with the most workers that isn't on the edge
        AI_NodeState maxNode = null;
        int maxWorkers = int.MinValue;
        Queue<AI_NodeState> queue = new();
        HashSet<AI_NodeState> visited = new();
        queue.Enqueue(toNode);
        visited.Add(toNode);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node.OwnedBy == Player && node.NumWorkers > maxWorkers && !node.IsOnTerritoryEdge)
            {
                maxNode = node;
                maxWorkers = node.NumWorkers;
            }
            foreach (var neighbor in node.NeighborNodes)
            {
                if (!visited.Contains(neighbor))
                {
                    queue.Enqueue(neighbor);
                    visited.Add(neighbor);
                }
            }
        }

        return maxNode;
    }
}
