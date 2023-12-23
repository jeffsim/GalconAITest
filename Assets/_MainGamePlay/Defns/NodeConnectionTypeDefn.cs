using UnityEngine;

[CreateAssetMenu(fileName = "NodeConnectionTypeDefn")]
public class NodeConnectionTypeDefn : BaseDefn
{
    public string DebugName;
    public float TraversalCostMultiplier = 1f;
}