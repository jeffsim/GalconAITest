using System;
using UnityEngine.Profiling;

public partial class AIGameData
{
    static BuildingScoreDictionary personality_BuildingScores;
    BuildingClassCountDictionary numOfOwnedBuilding = new BuildingClassCountDictionary();

    // SmallIntDictionary numOfOwnedResourceGatherers = new SmallIntDictionary(); // itemdefn, count

    internal int evaluate()
    {
        var personality_numNodes = 1.0f; // likes having nodes
        var personality_numWorkers = 0.1f; // likes having workers
        var personality_itemValue = 0.1f; // likes having items
        var personality_needValue = -.2f; // dislikes needing items

        // Not using dictionary because perf; could use smallintdirectionary
        var numWoodcutters = 0;
        var numStoneMiners = 0;
        var numLumbermills = 0;

        if (personality_BuildingScores == null)
        {
            // These are constant for the game; no need to set every time
            personality_BuildingScores = new BuildingScoreDictionary();
            personality_BuildingScores[BuildingClass.Camp] = 2.0f;
            personality_BuildingScores[BuildingClass.Crafter] = 2.0f;
            personality_BuildingScores[BuildingClass.Gatherer] = 2.0f;
            personality_BuildingScores[BuildingClass.Storage] = 0.1f;
            personality_BuildingScores[BuildingClass.Defense] = 2f;
        }

        float score = 0;
        int totalBuildingsOwned = 0, totalEnemyBuildingsOwned = 0;
        numOfOwnedBuilding.Reset();

        foreach (var node in Nodes)
        {
            if (node.OwnedById == CurrentPlayerId)
            {
                Profiler.BeginSample("evaluate.Check2");
                totalBuildingsOwned++;
                score += 1 * personality_numNodes;                          // good to own nodes
                score += node.NumWorkersInNode * personality_numWorkers;    // likes having workers

                if (node.HasCompletedBuilding)
                {
                    var buildingClass = node.CompletedBuildingDefn.BuildingClass;
                    score += personality_BuildingScores[buildingClass];

                    numOfOwnedBuilding[node.CompletedBuildingDefn.BuildingClass]++;

                    if (buildingClass == BuildingClass.Gatherer)
                    {
                        // Forest/Woodcutter, etc

                        // good to own resource nodes!  especially if we need what they produce...
                        var itemGenerated = node.CompletedBuildingDefn.GatherableResource.ItemType;
                        // var itemGenerated = node.Defn.ResourceGenerated;
                        score += 200;

                        var numNeeded = Math.Max(0, CurrentPlayer.ItemsNeeded[itemGenerated] - CurrentPlayer.ItemsOwned[itemGenerated]);
                        if (numNeeded == 0 && CurrentPlayer.ItemsOwned[itemGenerated] == 0)
                        {
                            // Fake some early-game predictive needs
                            if (itemGenerated == ItemType.Wood) numNeeded = 6;
                            if (itemGenerated == ItemType.Stone) numNeeded = 3;
                        }
                        score += 100 * numNeeded;

                        if (node.Defn.ResourceGenerated == ItemType.Wood) numWoodcutters++;
                        if (node.Defn.ResourceGenerated == ItemType.Stone) numStoneMiners++;
                    }
                    else if (buildingClass == BuildingClass.Crafter)
                    {
                        // Don't have crafting buildings if can't craft the items
                        var haveItems = false;
                        foreach (var item in node.CompletedBuildingDefn.CraftableItems)
                            if (this.CurrentPlayer.HaveMatsToCraftItem(item))
                                haveItems = true;
                        score += haveItems ? 100 : -500;

                        // don't need these early on
                        if (node.CompletedBuildingDefn.BuildingType == BuildingType.CoinMinter) score -= 500;
                        if (node.CompletedBuildingDefn.BuildingType == BuildingType.Lumbermill) numLumbermills++;
                    }
                    else if (buildingClass == BuildingClass.Defense)
                    {
                        if (node.NumHopsToClosestEnemy == 1)
                            score += 1000 * node.NumEnemyNodesNearby; // increase this to make more aggressive
                        else if (node.NumEnemyNodesNearby > 0)
                            score += 400;
                    }
                    else if (buildingClass == BuildingClass.Camp)
                    {
                        // Add value to upgrading
                        // score += (node.Level - 1) * 100;
                    }

                    // Value protecting when nearby enemies
                    if (node.NumHopsToClosestEnemy == 1)
                        score += 50 * (node.NumEnemyNodesNearby * 20 - node.NumWorkersInNode); // assuming 20 workers per enemynode; ideally look at real number. todo
                    else if (node.NumEnemyNodesNearby > 0)
                        score += 10 * (node.NumEnemyNodesNearby * 10 - node.NumWorkersInNode); // assuming 20 workers per enemynode; ideally look at real number. todo

                    // Add value to upgrading
                    score += (node.Level - 1) * 50;

                    // prefer buttressing when there are no enemies nearby
                    if (node.NumEnemyNodesNearby > 0)
                        score += 10 * node.NumEnemyNodesNearby * node.NumWorkersInNode;
                }

                Profiler.EndSample();
            }
            else if (node.OwnedById == 0)
            {
            }
            else
            {
                // owned by another player
                if (ConstantAIGameData.PlayerAffinities[CurrentPlayer.Id, node.OwnedById] == Affinity.Hates)
                    totalEnemyBuildingsOwned++;
            }
        }

        if (totalBuildingsOwned == 0)
            return -1000000;// lose

        if (totalEnemyBuildingsOwned == 0)
            return 1000000; // win

        // lose points for every enemy building
        score -= 200 * totalEnemyBuildingsOwned;

        // Items and Needs
        for (int i = 0; i < SmallItemCountDictionary.Count; i++)
            score += CurrentPlayer.ItemsOwned.Values[i] * personality_itemValue;     // TODO: Make this per-item; gold >> wood
        for (int i = 0; i < SmallItemCountDictionary.Count; i++)
            score += CurrentPlayer.ItemsNeeded.Values[i] * personality_needValue;    // TODO: Make this per-item; gold >> wood

        // = Higher level strategy -- Buildings
        if (numOfOwnedBuilding[BuildingClass.Camp] == 0)
            score -= 1000;              // == Must have at least 1 camp
        else
            score += numOfOwnedBuilding[BuildingClass.Camp] * 100;

        // early game; establish basic supply chain
        var isEarlyGame = totalBuildingsOwned < 6;
        if (isEarlyGame)
        {
            // Prioritize building basic wood and stone economy first
            if (numWoodcutters == 0) score -= 1000;     // REALLY want at least one woodcutter
            else if (numWoodcutters == 1) score -= 400; // Ideally would have at least two woodcutters
            if (numStoneMiners < 2) score -= 100;
            if (numLumbermills < 2) score -= 100;
        }

        // mid game; establish defenses
        var isMidGame = !isEarlyGame && totalBuildingsOwned < 12;
        if (isMidGame)
        {
            // if (numOfOwnedResourceGatherers["stone"] < 2) score -= 100;
            if (numWoodcutters < 3) score -= 400;
            if (numStoneMiners < 3) score -= 100;
        }

        return (int)score;
    }
}