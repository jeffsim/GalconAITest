using System;

/// <summary>
/// Shared resource production math for realtime ticks, step-mode turns, and AI simulation.
/// Production scales linearly with the number of workers assigned to the node.
/// </summary>
public static class ResourceProduction
{
    public static bool ProducesResources(BuildingDefn defn) =>
        defn != null && (defn.CanGatherResources || defn.CanBeGatheredFrom);

    public static GoodType GetProducedGoodType(BuildingDefn defn)
    {
        if (defn == null) return GoodType.Unset;
        if (defn.CanGatherResources && defn.ResourceThisNodeCanGoGather != null)
            return defn.ResourceThisNodeCanGoGather.GoodType;
        if (defn.CanBeGatheredFrom && defn.ResourceGatheredFromThisNode != null)
            return defn.ResourceGatheredFromThisNode.GoodType;
        return GoodType.Unset;
    }

    public static float GetResourcesPerSecond(BuildingDefn defn, int numWorkers)
    {
        if (defn == null || numWorkers <= 0) return 0f;
        return defn.ResourcesPerSecondPerWorker * numWorkers;
    }

    public static int GetProducedPerTurn(BuildingDefn defn, int numWorkers)
    {
        if (defn == null || numWorkers <= 0) return 0;
        if (defn.CanGatherResources)
            return defn.ResourceProducedPerWorkerPerTurn * numWorkers;
        if (defn.CanBeGatheredFrom)
            return defn.ResourceProducedPerWorkerPerTurn * numWorkers;
        return 0;
    }

    // Realtime: accumulate fractional production and emit whole units.
    public static int Tick(float deltaSeconds, ref float accum, BuildingDefn defn, int numWorkers)
    {
        float rate = GetResourcesPerSecond(defn, numWorkers);
        if (rate <= 0f || deltaSeconds <= 0f) return 0;

        accum += deltaSeconds * rate;
        int produced = 0;
        while (accum >= 1f)
        {
            accum -= 1f;
            produced++;
        }
        return produced;
    }

    public static void CreditInventory(SerializedDictionary<GoodType, int> inventory, GoodType goodType, int amount)
    {
        if (amount <= 0 || goodType == GoodType.Unset) return;
        if (!inventory.ContainsKey(goodType))
            inventory[goodType] = 0;
        inventory[goodType] += amount;
    }
}
