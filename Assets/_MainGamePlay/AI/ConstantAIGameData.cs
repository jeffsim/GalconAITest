using System;
using System.Collections.Generic;
using UnityEngine;

// Keeps track of data that is consistent across all recursions and evaluations of a gamestate.  e.g.: the set
// of Node connections is constant (so would be here) but the owner of the Node isn't constant (changes as moves are applied in the AI evaluation).
public class ConstantAIGameData
{
    static public TownData Town;

    // static public int[,] directConnections;

    static public BuildingType[] buildingEnums = null;

    static public IntListIntDictionary NearbyNodeIds = new IntListIntDictionary();

    // stores how player x feels about player y
    static public Affinity[,] PlayerAffinities;

    // Stores the number of nodes between x and y.  Ignores walkability
    static public int[,] HopsToNode;

    // This should be called only once per Town entrance
    public static void Initialize(TownData town)
    {
        Town = town;

        buildingEnums = new BuildingType[20];
        Enum.GetValues(typeof(BuildingType)).CopyTo(buildingEnums, 0);

        // // Initialize travel costs between connected nodes.  This NxN matrix also tells us if node N1 is directly connected to node N2
        // directConnections = new int[Nodes.Count, Nodes.Count];
        // foreach (var node in Nodes)
        //     foreach (var conn in node.Connections)
        //         directConnections[node.Id, conn.Node2Id] = (int)(Vector3.Distance(node.WorldLoc, town.GetNodeById(conn.Node2Id).WorldLoc) * conn.TraversalCostMultiplier);

        // Keep track of all nodes within N hops - we'll use this to more quickly evaluate various things; e.g. how many enemies are currently nearby
        HopsToNode = new int[town.Nodes.Count, town.Nodes.Count];
        foreach (var node in town.Nodes)
            NearbyNodeIds[node.Id] = getNearbyNodeIds(node);

        PlayerAffinities = new Affinity[town.Players.Count, town.Players.Count];
        for (int i = 0; i < town.Players.Count; i++)
            for (int j = 0; j < town.Players.Count; j++)
                if (i == j)
                    PlayerAffinities[i, j] = Affinity.Likes; // everyone likes themselves
                else if (town.Players[i] == null || town.Players[j] == null || !town.Players[i].Race.Affinities.TryGetValue(town.Players[j].Race, out PlayerAffinities[i, j]))
                    PlayerAffinities[i, j] = Affinity.Neutral;
    }

    static List<NodeData> _oneHopNodes = new List<NodeData>(10);
    
    static List<int> getNearbyNodeIds(NodeData node)
    {
        List<int> nearbyNodeIds = new List<int>(100);
        _oneHopNodes.Clear();

        // Get all nodes w/in one hop
        foreach (var connNode in node.ConnectedNodes)
        {
            HopsToNode[node.Id, connNode.Id] = 1;
            nearbyNodeIds.Add(connNode.Id);
            _oneHopNodes.Add(connNode);
        }

        // Get all nodes that are two hops away
        foreach (var oneHopNode in _oneHopNodes)
            foreach (var connNode in oneHopNode.ConnectedNodes)
                if (!nearbyNodeIds.Contains(connNode.Id))
                {
                    HopsToNode[node.Id, connNode.Id] = 2;
                    nearbyNodeIds.Add(connNode.Id);
                }

        // Only doing 2 hops for now

        return nearbyNodeIds;
    }
}