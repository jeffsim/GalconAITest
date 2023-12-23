using Sirenix.OdinInspector;
using UnityEngine;

public enum NodeType
{
    // Player can construct buildings on top of this node
    Buildable,

    // This node contains a gatherable resource - wood, stone, etc.  Player cannot construct buildings on
    // top of it; workers in adjacent nodes w/ appropriate gathering buildings will gather
    Resource,
}

[CreateAssetMenu(fileName = "NodeDefn")]
public class NodeDefn : BaseDefn
{
    public NodeType Type;

    [ShowIf("Type", NodeType.Resource)]
    public ItemType ResourceGenerated;

    // If true, then it's possible to construct buildings on Nodes with this NodeDefn.
    // For now, all nodes support it, but I could add it in the future (not sure how though)
    public bool SupportsBuildingOn = true;

    // ================================================================================
    // Building
    // ================================================================================

    // Resource nodes always have a building pre-built (e.g. a Forest)
    // So do e.g. Special Defensive tower nodes etc
    public BuildingDefn BuildingInNode;
}