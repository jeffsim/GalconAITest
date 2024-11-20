public struct Strategy_Node
{
    public int NodeId;
    public int OwnerId;
    public int NumWorkers;
    public int BuildingLevel;
    public BuildingType BuildingType;
    public bool IsUpgradableBuilding;
    public int NumNeighbors;
    public int[] NeighborIndices; // Changed from Strategy_Node[] to int[]

    public const int MAX_NEIGHBORS = 8;

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
            NeighborIndices = new int[MAX_NEIGHBORS],
            NumNeighbors = 0
        };
    }
}
