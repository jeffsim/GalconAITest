using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class Dijkstra
{
    // attrib: https://www.videlin.eu/2016/04/28/shortest-path-in-graph-dijkstras-algorithm-c-implementation/
    public static List<int> DijkstraAlgorithm(int[,] graph, int sourceNode, int destinationNode, Func<int, bool> canEnterNodeCheck)
    {
        var n = graph.GetLength(0);

        var distance = new int[n];
        for (int i = 0; i < n; i++)
        {
            distance[i] = int.MaxValue;
        }

        distance[sourceNode] = 0;

        var used = new bool[n];
        var previous = new int?[n];

        while (true)
        {
            var minDistance = int.MaxValue;
            var minNode = 0;
            for (int i = 0; i < n; i++)
            {
                if (!used[i] && minDistance > distance[i])
                {
                    minDistance = distance[i];
                    minNode = i;
                }
            }

            if (minDistance == int.MaxValue)
            {
                break;
            }

            used[minNode] = true;

            for (int i = 0; i < n; i++)
            {
                if (graph[minNode, i] > 0)
                {
                    var shortestToMinNode = distance[minNode];
                    var distanceToNextNode = graph[minNode, i];

                    var totalDistance = shortestToMinNode + distanceToNextNode;

                    bool check1 = canEnterNodeCheck(i);
                    if (totalDistance < distance[i] && check1)
                    {
                        distance[i] = totalDistance;
                        previous[i] = minNode;
                    }
                }
            }
        }

        if (destinationNode < 0) Debug.Assert(destinationNode >= 0 && destinationNode < distance.Length, "destinationNode (" + destinationNode + ") is out of range.  Max=" + distance.Length);
        if (distance[destinationNode] == int.MaxValue)
            return null;

        var path = new LinkedList<int>();
        int? currentNode = destinationNode;
        while (currentNode != null)
        {
            path.AddFirst(currentNode.Value);
            currentNode = previous[currentNode.Value];
        }

        return path.ToList();
    }
}