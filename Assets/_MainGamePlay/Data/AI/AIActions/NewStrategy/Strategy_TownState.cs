public struct Strategy_TownState
{
    private const int MAX_TOWN_NODES = 30;
    private const int MAX_CONSTRUCTIBLE_BUILDINGS = 30;
    public Strategy_Node[] Nodes;
    public BuildingDefn[] ConstructibleBuildings;
    public int NumConstructibleBuildings;
    
    public ulong NodesVisited;

    public void Initialize()
    {
        Nodes = new Strategy_Node[MAX_TOWN_NODES];
        for (int i = 0; i < MAX_TOWN_NODES; i++)
            Nodes[i] = Strategy_Node.CreateInitialized();

        ConstructibleBuildings = new BuildingDefn[MAX_CONSTRUCTIBLE_BUILDINGS];
        NumConstructibleBuildings = 0;
    }
}
