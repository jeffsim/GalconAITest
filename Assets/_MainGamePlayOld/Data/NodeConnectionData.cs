public class NodeConnectionData
{
    public int Node1Id;
    public int Node2Id;
    public float TraversalCostMultiplier;
    public float BaseTravelCost;

    public NodeConnectionData(Town_NodeConnectionDefn conn, bool isForwardConnection)
    {
        Node1Id = isForwardConnection ? conn.Node1Id : conn.Node2Id;
        Node2Id = isForwardConnection ? conn.Node2Id : conn.Node1Id;
        TraversalCostMultiplier = conn.ConnectionType.TraversalCostMultiplier;
    }
}
