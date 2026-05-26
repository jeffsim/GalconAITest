
using System;

public class BuildingData
{
    public BuildingDefn Defn;
    public int Level { get; private set; }
    public int MaxWorkers = 10;
    public int WorkersGeneratedPerTurn = 1;

    // Realtime accumulators. Each tick adds dt (scaled by GameSpeed); when an accumulator
    // crosses the configured Defn.SecondsPer* threshold, one resource / worker is produced
    // and the accumulator is reduced by that threshold (carrying remainder forward, so we
    // don't lose sub-second time).
    public float ResourceProductionAccum = 0f;
    public float WorkerGenerationAccum = 0f;

    // Next world-time this gatherer building may dispatch another worker (spread sends).
    public float NextGathererDispatchTime;

    public BuildingData(BuildingDefn defn)
    {
        Defn = defn;
        Level = 0;
        Upgrade();
    }

    public void Upgrade()
    {
        Level++;
        // NOTE: If update this then need to update elsewhere too.  grep on TODO-042
        MaxWorkers = 10 * (int)Math.Pow(2, Level - 1); // 10, 20, 40, 80, 160, ... 
        WorkersGeneratedPerTurn = 1;
    }
}
