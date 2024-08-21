using System.Collections.Generic;
using System.Linq;

public class AINode_State
{
    public int OwnerId;
    public int NodeId;
    public Dictionary<GoodType, int> Resources = new();
    public List<AINode_State> Neighbors = new();
    public bool IsUnderThreat;
    public int MilitaryStrength;
    public int NumWorkers;

    public AINode_State(NodeData node)
    {
        NodeId = node.NodeId;
        OwnerId = node.OwnedBy.Id;
        NumWorkers = node.NumWorkers;

        foreach (var inv in node.Inventory)
            Resources.Add(inv.Key, inv.Value);
    }
}
