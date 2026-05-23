using System;
using System.Collections.Generic;

public partial class PlayerAI
{
    // Per-ply world tick: at depth >= 1 of the search, advance owned-node production by one
    // turn before enumerating actions, then undo before returning. This is what lets the
    // search compose multi-step build chains like "build a Woodcutter at depth 0 -> tick at
    // depth 1 produces wood -> Barracks becomes affordable at depth 2".
    //
    // Mirrors TownData.Debug_WorldTurn for the player-owned subset:
    //   - resource-gathering buildings credit the player inventory by the building's
    //     ResourceProducedPerTurn (single source of truth shared with the real tick)
    //   - worker-generating buildings add WorkersGeneratedPerTurn workers, capped at MaxWorkers
    //
    // Other players' production is not simulated; this is a single-agent lookahead, and
    // adversaries don't act inside the search either.

    struct WorldTickState
    {
        public int[] WorkersAddedPerNode;
        public Dictionary<GoodType, int> ResourcesAdded;
    }

    // Per-depth state so deep recursion does not stomp shallower plies' undo data.
    WorldTickState[] tickStatePerDepth;

    public void ApplyWorldTick(int curDepth)
    {
        EnsureTickCapacity(curDepth);
        var state = tickStatePerDepth[curDepth];
        var workersAdded = state.WorkersAddedPerNode;
        var resourcesAdded = state.ResourcesAdded;

        for (int i = 0; i < workersAdded.Length; i++)
            workersAdded[i] = 0;
        resourcesAdded.Clear();

        var nodes = aiTownState.Nodes;
        int numNodes = aiTownState.NumNodes;

        for (int i = 0; i < numNodes; i++)
        {
            var node = nodes[i];
            if (node.OwnedBy != player) continue;
            if (!node.HasBuilding) continue;

            if (node.CanGoGatherResources)
            {
                // Read the per-building yield rather than a hardcoded constant so the AI's
                // multi-step planning agrees with the real Debug_WorldTurn yield.
                int produced = node.BuildingDefn != null ? node.BuildingDefn.ResourceProducedPerTurn : 0;
                if (produced > 0)
                {
                    var goodType = node.ResourceThisNodeCanGoGather;
                    if (aiTownState.PlayerTownInventory.TryGetValue(goodType, out int prevInv))
                        aiTownState.PlayerTownInventory[goodType] = prevInv + produced;
                    else
                        aiTownState.PlayerTownInventory[goodType] = produced;

                    resourcesAdded.TryGetValue(goodType, out int prevDelta);
                    resourcesAdded[goodType] = prevDelta + produced;
                }
            }

            if (node.CanGenerateWorkers && node.WorkersGeneratedPerTurn > 0)
            {
                int newWorkers = Math.Min(node.MaxWorkers, node.NumWorkers + node.WorkersGeneratedPerTurn);
                int delta = newWorkers - node.NumWorkers;
                if (delta != 0)
                {
                    node.NumWorkers = newWorkers;
                    workersAdded[i] = delta;
                }
            }
        }
    }

    public void UndoWorldTick(int curDepth)
    {
        if (tickStatePerDepth == null || curDepth >= tickStatePerDepth.Length) return;
        var state = tickStatePerDepth[curDepth];
        var workersAdded = state.WorkersAddedPerNode;
        var resourcesAdded = state.ResourcesAdded;

        var nodes = aiTownState.Nodes;
        int numNodes = aiTownState.NumNodes;

        for (int i = 0; i < numNodes; i++)
        {
            int delta = workersAdded[i];
            if (delta != 0)
                nodes[i].NumWorkers -= delta;
        }
        foreach (var kvp in resourcesAdded)
            aiTownState.PlayerTownInventory[kvp.Key] -= kvp.Value;
    }

    void EnsureTickCapacity(int curDepth)
    {
        int requiredDepth = curDepth + 1;
        int currentLen = tickStatePerDepth?.Length ?? 0;
        if (currentLen >= requiredDepth)
        {
            // Buffer slot for this depth may have been allocated when NumNodes was smaller.
            // Rare but cheap to re-check.
            if (tickStatePerDepth[curDepth].WorkersAddedPerNode.Length < aiTownState.NumNodes)
                tickStatePerDepth[curDepth].WorkersAddedPerNode = new int[aiTownState.NumNodes];
            return;
        }

        int newLen = Math.Max(requiredDepth, currentLen);
        if (newLen < 8) newLen = 8;

        var newArr = new WorldTickState[newLen];
        if (tickStatePerDepth != null)
            Array.Copy(tickStatePerDepth, newArr, currentLen);
        for (int i = 0; i < newLen; i++)
        {
            if (newArr[i].WorkersAddedPerNode == null || newArr[i].WorkersAddedPerNode.Length < aiTownState.NumNodes)
                newArr[i].WorkersAddedPerNode = new int[aiTownState.NumNodes];
            if (newArr[i].ResourcesAdded == null)
                newArr[i].ResourcesAdded = new Dictionary<GoodType, int>();
        }
        tickStatePerDepth = newArr;
    }
}
