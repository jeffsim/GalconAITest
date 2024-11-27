using System;
using System.Collections.Generic;
using UnityEngine;

public partial class Strategy_NonRecursive
{
    Stack<Dictionary<AI_NodeState, int>> attackFromNodesPool = new();

    private void CheckPriority_AttackEnemyNodes()
    {
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

                    // 4. Normalize the raw value
                    float clampedRawValue = Mathf.Clamp(rawValue, attackNodeMinScore, attackNodeMaxScore);
                    float normalizedValue = (clampedRawValue - attackNodeMinScore) / (attackNodeMaxScore - attackNodeMinScore);

                    bool fuckItAttackAnyways = /*UnityEngine.Random.value < 0.25f && */neighbor.NumWorkers > neighbor.MaxWorkers * 0.5f;
                    if (fuckItAttackAnyways)
                        normalizedValue = attackNodeMaxScore;
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
                        if (numToSend < targetNumberToSend)
                        {
                            // Occassionally randomly be okay sending fewer from node. TODO: smarter heuristic
                            if (fuckItAttackAnyways && neighbor.NumWorkers > neighbor.MaxWorkers * 0.5f)
                            {
                                nodesToSendFrom.Clear();
                                nodesToSendFrom.Add(neighbor);
                                neighbor.NumWorkersWillingToSend = neighbor.NumWorkers / 2;
                            }
                            else
                                continue;
                        }
                        else
                            continue;

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