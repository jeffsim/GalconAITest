// using System;
// using System.Collections.Generic;

// public struct QueueItem
// {
//     public AI_NodeState Node;
//     public int Depth;

//     public QueueItem(AI_NodeState node, int depth)
//     {
//         Node = node;
//         Depth = depth;
//     }
// }

// public class AIProcessor
// {
//     private readonly bool[] _visited;
//     private readonly QueueItem[] _queueArray;
//     private int _queueHead;
//     private int _queueTail;

//     private readonly int _maxDepth = 2;
//     private readonly int _workerThreshold = 10;
//     private readonly PlayerData _player;

//     // Assuming a maximum number of nodes; adjust as necessary
//     private readonly int _maxNodes;

//     // Constructor to initialize preallocated structures
//     public AIProcessor(PlayerData player, int maxNodes)
//     {
//         _player = player;
//         _maxNodes = maxNodes;
//         _visited = new bool[_maxNodes];
//         _queueArray = new QueueItem[_maxNodes];
//         _queueHead = 0;
//         _queueTail = 0;
//     }

//     /// <summary>
//     /// Retrieves deep neighbors with potential attackers without any runtime allocations.
//     /// Optimized for performance in inner loops.
//     /// </summary>
//     /// <param name="toNode">The starting node.</param>
//     /// <param name="result">The list to store the resulting nodes.</param>
//     public void GetDeepNeighborsWithPotentialAttackers(AI_NodeState toNode, List<AI_NodeState> result)
//     {
//         // Ensure result list is cleared and preallocated outside
//         result.Clear();

//         // Reset visited array without allocations
//         Array.Clear(_visited, 0, _maxNodes);

//         // Initialize the queue indices
//         _queueHead = 0;
//         _queueTail = 0;

//         // Enqueue the starting node without allocations
//         _visited[toNode.NodeId] = true; // Assuming AI_NodeState has a unique NodeId property
//         _queueArray[_queueTail++] = new QueueItem(toNode, 0);

//         // Perform BFS up to the specified depth
//         while (_queueHead < _queueTail)
//         {
//             // Dequeue the next item
//             ref readonly QueueItem current = ref _queueArray[_queueHead++];

//             if (current.Depth >= _maxDepth)
//                 continue;

//             AI_NodeState currentNode = current.Node;
//             int currentDepth = current.Depth;

//             // Cache frequently accessed properties to local variables
//             var neighbors = currentNode.NeighborNodes;
//             int neighborCount = neighbors.Count;

//             for (int i = 0; i < neighborCount; i++)
//             {
//                 AI_NodeState neighbor = neighbors[i];

//                 // Assuming neighbor.NodeId is within [0, _maxNodes)
//                 if (!_visited[neighbor.NodeId] && neighbor.OwnedBy == _player)
//                 {
//                     _visited[neighbor.NodeId] = true;

//                     if (neighbor.NumWorkers > _workerThreshold)
//                     {
//                         result.Add(neighbor);
//                     }

//                     // Enqueue the neighbor if within bounds
//                     if (_queueTail < _maxNodes)
//                     {
//                         _queueArray[_queueTail++] = new QueueItem(neighbor, currentDepth + 1);
//                     }
//                     // Else, queue is full; handle overflow if necessary
//                 }
//             }
//         }
//     }
// }
