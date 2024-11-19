
public struct Strategy_Node
{
    public int NodeId;
    public int OwnerId;
    public int NumWorkers;
    public int BuildingLevel;
    public BuildingType BuildingType;
    public bool IsUpgradableBuilding;
    public int NumNeighbors;
    public Strategy_Node[] Neighbors;

    public const int MAX_NEIGHBORS = 4;

    public static Strategy_Node CreateInitialized()
    {
        return new Strategy_Node
        {
            NodeId = 0,
            OwnerId = 0,
            NumWorkers = 0,
            BuildingType = BuildingType.None,
            IsUpgradableBuilding = false,
            BuildingLevel = 0,
            Neighbors = new Strategy_Node[MAX_NEIGHBORS],
            NumNeighbors = 0
        };
    }
}