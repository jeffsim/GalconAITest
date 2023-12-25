public enum AIActionType { DoNothing, SendWorkersToNode, ConstructBuildingInOwnedNode };

public class AIAction
{
    public float Score;
    public AIActionType Type = AIActionType.DoNothing;
    public int Count;
    public AI_NodeState SourceNode;
    public AI_NodeState DestNode;
    public BuildingDefn BuildingToConstruct;

#if DEBUG
    // Keep track of the optimal actions to perform after this one; only used for debugging
    public AIAction NextAction;
    // public List<AIAction> NextActions = new List<AIAction>();
#endif
}
