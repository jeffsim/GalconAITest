using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

public class NodeConnectionData
{
    public bool IsForwardConnection;
    public Spline Spline;
    public SplineData<float> splinePath_Widths;
    public int Node1Id;
    public int Node2Id;
    public float TraversalCostMultiplier;

    public NodeConnectionData(Town_NodeConnectionDefn conn, bool isForwardConnection)
    {
        Node1Id = isForwardConnection ? conn.Node1Id : conn.Node2Id;
        Node2Id = isForwardConnection ? conn.Node2Id : conn.Node1Id;
        TraversalCostMultiplier = conn.ConnectionType.TraversalCostMultiplier;
        IsForwardConnection = isForwardConnection;
        Spline = conn.PathSpline;
        splinePath_Widths = new SplineData<float>(conn.PathSplineWidths);
    }
}
