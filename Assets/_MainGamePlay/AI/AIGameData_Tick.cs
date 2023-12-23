using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

// Abridged version of full gamedata used for AI evaluation
public partial class AIGameData
{
    List<ItemDefn> allItemsWeCouldCraftRightNow = new List<ItemDefn>(10);

    public void Tick()
    {
        // Time is used to generate items and building Worker growth.  Assumes something like 10 seconds of time pass between moves/recursive calls in AI evaluation
        var deltaTime = 10; // seconds
        FakeTime += deltaTime; // seconds

        Profiler.BeginSample("start");
        allItemsWeCouldCraftRightNow.Clear();
        foreach (var node in Nodes)
            if (node.HasCompletedBuilding && node.CompletedBuildingDefn.BuildingClass == BuildingClass.Gatherer && node.Owner != null)
                foreach (var item in node.CompletedBuildingDefn.CraftableItems)
                    if (node.Owner.HaveMatsToCraftItem(item))
                        allItemsWeCouldCraftRightNow.Add(item);
        Profiler.EndSample();

        foreach (var node in Nodes)
        {
            if (!node.HasCompletedBuilding) continue;
            var nodeOwner = node.Owner;
            if (nodeOwner == null && node.Defn.Type == NodeType.Buildable)
            {
//                Debug.Log("Node " + node.Id + " has building (" + node.CompletedBuildingDefn.Id + ") but no owner.  OwnedById =" + node.OwnedById);
                continue;
            }
            switch (node.CompletedBuildingDefn.BuildingClass)
            {
                case BuildingClass.Gatherer:
                    if (nodeOwner == null) continue;

                    Profiler.BeginSample("Gatherer");
                    // it's a gatherer (woodcutter, etc).  If we have a path to a resource node, then every N ticks, generate an item per worker
                    // TODO: bake this in somehow
                    // approximate how many we can gather based on distance etc
                    float totalGatherTime = NodeData.secondsPerGather;  // * 2 because walk there and back
                    var numItemsGatheredPerWorker = deltaTime / totalGatherTime; // TODO: Per-item gatherSpeed
                    var numGathered = Math.Max(1, (int)(numItemsGatheredPerWorker * node.NumWorkersInNode));
                    nodeOwner.AddItem(node.CompletedBuildingDefn.GatherableResource.ItemType, numGathered);
                    Profiler.EndSample();
                    break;

                case BuildingClass.Crafter:
                    if (nodeOwner == null) continue;

                    // it's a crafter (blacksmith, etc).
                    if (allItemsWeCouldCraftRightNow.Count == 0)
                        break; // can't craft any more

                    Profiler.BeginSample("Crafter");

                    // Figure out how many we can craft given the materials on-hand.  Note that this incorrectly assumes that we can craft instantaneously
                    int numToCraft = (int)(node.NumWorkersInNode * deltaTime / NodeData.craftSpeed); // TODO: Per-item crafting speed

                    // Pick the item to craft randomly.  
                    // TODO: not taking priority of need into account.  See main code
                    var itemToCraft = allItemsWeCouldCraftRightNow[UnityEngine.Random.Range(0, allItemsWeCouldCraftRightNow.Count)];

                    // Look at each material we have on hand - determine the max we can build using that
                    foreach (var mat in itemToCraft.ItemsNeededToCraftItem)
                        numToCraft = Math.Min(numToCraft, nodeOwner.ItemsOwned[mat.Item.ItemType] / mat.Count);

                    if (numToCraft > 0)
                    {
                        // Consume the mateirals
                        foreach (var mat in itemToCraft.ItemsNeededToCraftItem)
                            nodeOwner.ItemsOwned[mat.Item.ItemType] -= numToCraft;

                        // Generate the items
                        nodeOwner.AddItem(itemToCraft.ItemType, numToCraft);
                    }

                    Profiler.EndSample();
                    break;
            }

            Profiler.BeginSample("end");

            // add worker growth
            if (node.CompletedBuildingDefn.GainsWorkersOverTime)
            {
                var numWorkersToAdd = (int)(deltaTime / NodeData.TimeToGainWorker);
                node.NumWorkersInNode = Math.Min(node.MaxNumWorkers, node.NumWorkersInNode + numWorkersToAdd);
            }

            // add worker bleed
            if (node.NumWorkersInNode > node.MaxNumWorkers)
            {
                var numWorkersToBleed = (int)(deltaTime / NodeData.TimeToBleedWorker);
                node.NumWorkersInNode = Math.Max(0, node.NumWorkersInNode - numWorkersToBleed);
            }
            Profiler.EndSample();
        }
    }
}