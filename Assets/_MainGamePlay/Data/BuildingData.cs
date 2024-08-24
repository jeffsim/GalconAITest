
using System;

public class BuildingData
{
    public BuildingDefn Defn;
    public int Level { get; private set; }
    public int MaxWorkers = 10;
    public int WorkersGeneratedPerTurn = 1;

    public BuildingData(BuildingDefn defn)
    {
        Defn = defn;
        Level = 0;
        Upgrade();
    }

    public void Upgrade()
    {
        Level++;
        MaxWorkers = 10 * (int)Math.Pow(2, Level - 1); // 10, 20, 40, 80, 160, ...
        WorkersGeneratedPerTurn = Level; // 1, 2, 3, 4, 5
    }
}