using System;

[Serializable]
public class Town_NodeConnectionDefn
{
    public NodeConnectionTypeDefn ConnectionType;
    public int Node1Id;
    public int Node2Id;

    public bool IsBidirectional = true;  // if false, then only goes from Node1 to Node2
}