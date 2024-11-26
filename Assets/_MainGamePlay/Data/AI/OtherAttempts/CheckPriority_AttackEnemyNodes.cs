using System;
using System.Collections.Generic;
using UnityEngine;

public partial class Strategy_NonRecursive
{
    Stack<Dictionary<AI_NodeState, int>> attackFromNodesPool = new();

    private void CheckPriority_AttackEnemyNodes()
    {
        /*
            find all enemy nodes that we can reach; i.e. they are adjacent to on of PlayerNodes
            foreach enemy node, determine (A) if we have enough workers to do so and (B) the value of doing so
            to calculate (A), we do a simple BFS outward ONLY through neighboring nodes that we own
                 if one of the nodes we own has workers to spare (and doesn't itself want to keep them) then they're added to the sum of workers
                 AI can stop at 3 BFS or go deeper.  when we have enoguh workers, we can stop the BFS
            to calculate (B), we need to know
                how much the AI personality values attacking enemy nodes
                how much the AI personality values owning nodes
        */

        int playerNodesCount = PlayerNodes.Count;
        for (int i = 0; i < playerNodesCount; i++)
        {
            // Determine how many (if any) workers the node is willing to send out
            var node = PlayerNodes[i];
            node.NumWorkersWillingToSend = 0;
            if (node.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut) continue;
            if (node.NumWorkers < node.MaxWorkers * 3f / 4f) continue;
            node.NumWorkersWillingToSend = node.NumWorkers / 2;
        }

        int numEnemyNodes = EnemyNodes.Count;
        for (int i = 0; i < numEnemyNodes; i++)
        {
            var enemyNode = EnemyNodes[i];

            // do we have any nodes next to this enemy node?
            var neighbors = enemyNode.NeighborNodes;
            int numNeighbors = neighbors.Count;
            for (int j = 0; j < numNeighbors; j++)
            {
                var neighbor = neighbors[j];
                if (neighbor.OwnedBy == Player)
                {
                    float rawValue = 0f;
                    // TODO: calculate raw value
                    rawValue = attackNodeMaxScore; // fuck it for now

                    // 4. Normalize the raw value
                    float clampedRawValue = Mathf.Clamp(rawValue, attackNodeMinScore, attackNodeMaxScore);
                    float normalizedValue = (clampedRawValue - attackNodeMinScore) / (attackNodeMaxScore - attackNodeMinScore);

                    // 5. Apply AI personality multiplier
                    float finalValue = normalizedValue * personalityMultiplier_CaptureNode;
                    if (finalValue > BestAction.Score)
                    {
                        // this node would be valuable to capture AND we can attack it.
                        // do a BFS outward to see if we have enough workers to attack this node
                        List<AI_NodeState> nodesToSendFrom = new();
                        int targetNumberToSend = (int)(enemyNode.NumWorkers * 1.5f); // TODO
                        int numToSend = BFS_GetPlayerNodesToSendFromStartingAtNode(neighbor, nodesToSendFrom, targetNumberToSend);

                        // don't send if not enough to win
                        if (numToSend < targetNumberToSend) continue;

                        AIDebuggerEntryData debuggerEntry = null;
                        Dictionary<AI_NodeState, int> attackFromNodes = attackFromNodesPool.Count > 0 ? attackFromNodesPool.Pop() : new Dictionary<AI_NodeState, int>();
                        attackFromNodes.Clear();
                        for (int k = 0; k < nodesToSendFrom.Count; k++)
                            attackFromNodes.Add(nodesToSendFrom[k], nodesToSendFrom[k].NumWorkersWillingToSend);
#if DEBUG
                        if (AITestScene.Instance.TrackDebugAIInfo)
                        {
                            debuggerEntry = AIDebugger.rootEntry.AddEntry_AttackToNode(
                                                                    attackFromNodes,
                                                                    enemyNode,
                                                                   null,// attackResults,
                                                                    0,
                                                                    Player.AI.debugOutput_ActionsTried++,
                                                                    0);
                        }
#endif
                        BestAction.SetTo_AttackToNode(attackFromNodes, enemyNode, null /*attackResults*/, finalValue, debuggerEntry);
                    }
                }
            }
        }
    }

    private int BFS_GetPlayerNodesToSendFromStartingAtNode(AI_NodeState playerNode, List<AI_NodeState> nodesToSendFrom, int targetNumberToSend)
    {
        int numSent = 0;
        nodesToSendFrom.Clear();

        // BFS outward from playerNode.  Every node must (a) be owned by the player and (b) have NumWorkersWillingToSend > 0
        // be sure to not visit nodes twice.  Track 'depth' and don't go more than 3 nodes deep
        foreach (var node in PlayerNodes)
            node.IsVisited = false;
        Queue<AI_NodeState> queue = new();
        queue.Enqueue(playerNode);
        playerNode.IsVisited = true;
        int depth = 0;
        while (queue.Count > 0 && depth < 4)
        {
            int numNodesInThisLayer = queue.Count;
            for (int i = 0; i < numNodesInThisLayer; i++)
            {
                var node = queue.Dequeue();
                if (node.NumWorkersWillingToSend > 0)
                {
                    nodesToSendFrom.Add(node);
                    numSent += node.NumWorkersWillingToSend;
                    if (numSent >= targetNumberToSend) return numSent;
                }

                foreach (var neighbor in node.NeighborNodes)
                {
                    if (neighbor.OwnedBy == Player && neighbor.NumWorkersWillingToSend > 0 && !neighbor.IsVisited)
                    {
                        queue.Enqueue(neighbor);
                        neighbor.IsVisited = true;
                    }
                }
            }
            depth++;
        }

        return numSent;
    }
}