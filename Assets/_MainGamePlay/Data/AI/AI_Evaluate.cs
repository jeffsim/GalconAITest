using System;
using Unity.Profiling;

public partial class AI_TownState
{
    ProfilerMarker m1 = new ProfilerMarker("1");
    ProfilerMarker m2 = new ProfilerMarker("2");
    ProfilerMarker m3 = new ProfilerMarker("3");

    public bool NodeOwnershipOrWorkersChanged = false;

    // TODO: Add weights based on AI's personality
    internal float EvaluateScore(int stateDepth, int maxStateDepth, out DebugAIStateReasons scoreReasons)
    {
        float score = 0;

        scoreReasons = null;

#if DEBUG
        if (AITestScene.Instance.DebugOutputStrategyReasons)
            scoreReasons = new();
#endif

        int numNodes = Nodes.Length;

        // temp simplified version of this function
        // for each node we own, add 1 to the score
        // for each building in a node we own, add .5 to the score
        // for each building in a node we own that is a resource gatherer, add 1 to the score
        // for each building in a node we own that is a worker generator, add .25 to the score
        // for (int i = 0; i < numNodes; i++)
        // {
        //     var node = Nodes[i];
        //     if (node.OwnedBy == player)
        //     {
        //         score += 1;
        //         if (node.HasBuilding)
        //         {
        //             score += .5f;
        //             if (node.CanGoGatherResources)
        //             {
        //                 score += 1;
        //             }
        //             if (node.CanGenerateWorkers)
        //             {
        //                 score += .25f;
        //             }
        //         }
        //     } else if (node.OwnedBy != null)
        //     {
        //         score -= .9f;
        //     }
        // }
        // return score;

        using (m1.Auto())
        {
            if (NodeOwnershipOrWorkersChanged)
            {
                for (int i = 0; i < numNodes; i++)
                {
                    var node = Nodes[i];
                    if (node.OwnedBy != player) continue;
                    node.NumEnemiesInNeighborNodes = 0;
                    var nnodes = node.NeighborNodes;
                    var count = nnodes.Count;
                    for (int n = 0; n < count; n++)
                    {
                        var nn = nnodes[n];
                        if (nn.OwnedBy != null && nn.OwnedBy != player)
                            node.NumEnemiesInNeighborNodes += nn.NumWorkers;
                    }
                }
                NodeOwnershipOrWorkersChanged = false;
            }
        }

        using (m2.Auto())
        {
            for (int i = 0; i < numNodes; i++)
            {
                var node = Nodes[i];
                if (node.OwnedBy == player)
                {
                    // Add score for each node we own
                    // TODO: subtract score for each node owned by another player
                    score += 1;
#if DEBUG
                    scoreReasons?.ScoresFrom_NodesOwned.Add(new DebugAIStateReason() { Node = node, ScoreValue = 1f });
#endif

                    // Add score for each building in a node we own that is "useful"
                    if (!node.HasBuilding)
                    {
                        // TODO: No longer possible?
                        score += .1f; // some score for owning empty nodes.  Base this on AI personality's "desire to expand"
#if DEBUG
                        scoreReasons?.ScoresFrom_NumEmptyNodesOwned.Add(new DebugAIStateReason() { Node = node, ScoreValue = .1f });
#endif
                    }
                    else
                    {
                        // upgraded buildings are more useful than non-upgraded buildings
                        // todo: temp - should be based on building type, game state, how much we need the building, etc.
                        float buildingUpgradeModifier = .15f; // set this to 'value' of upgrades. 
                        score += buildingUpgradeModifier * (node.BuildingLevel - 1);

                        // Resource gathering buildings are useful if they can reach a resource node.
                        // These buildings are more useful the close to the resource node they are.
                        // TODO: Increase usefulness score based on how much we need the resource vs how much we have
                        if (node.CanGoGatherResources)
                        {
                            var addedScore = 1.5f;

                            // The longer we've owned the node, the more useful it is.  Use node.TurnBuildingWasBuilt, which goes from 0 to maxStateDepth, to weight the score
                            // where turn 0 is the first turn, and maxStateDepth is the last turn
                            // building built turn 0; currently on turn 10; multiply score by 10
                            // building built turn 5; currently on turn 10; multiply score by 5
                            // TODO: Change turnbuildingwasbuilt into max of [4] turns ago; otherwise this becomes huge.  Can't just use min, need to subtract from curturn#
                            addedScore += (maxStateDepth - node.TurnBuildingWasBuilt + 1) * 1.5f;

#if DEBUG
                            scoreReasons?.ScoresFrom_ResourceGatherersCloseToResourceNodes.Add(new DebugAIStateReason() { Node = node, ScoreValue = 2f });
#endif

                            // The more we globally need the resource that this node can gather, the higher the utility of it
                            // if (GlobalResourceNeeds.TryGetValue(resourceType, out int globalResourceNeed))
                            // {
                            //     addedScore += globalResourceNeed * .1f;
                            // }

                            // The more of the resource that this building can gather we already own, the lower the utility of it
                            var resourceType = node.ResourceThisNodeCanGoGather;
                            if (PlayerTownInventory.TryGetValue(resourceType, out int resourceCount))
                            {
                                addedScore = Math.Max(0, addedScore - resourceCount * .1f);
                            }

                            score += addedScore;
                        }

                        // Defensive buildings are useful if...
                        if (node.CanGenerateWorkers)
                        {
                            score += .125f;
#if DEBUG
                            scoreReasons?.ScoresFrom_BuildingsThatGenerateWorkers.Add(new DebugAIStateReason() { Node = node, ScoreValue = .25f });
#endif
                        }

                        // Storage buildings are useful if...
                        // Crafting buildings are useful if...


                        // TODO: Track the below in aitownstate - only update it when a building's owner or numworkers changes
                        // was workgin on ^^

                        // If player-owned building has an enemy-owned node nearby, it's more useful to have more workers in it
                        if (node.NumEnemiesInNeighborNodes > node.NumWorkers)
                        {
                            float scoreValue = (node.NumWorkers - node.NumEnemiesInNeighborNodes) * .5f;
                            score += scoreValue;
#if DEBUG
                            scoreReasons?.ScoresFrom_BuildingsNearEnemyNodes.Add(new DebugAIStateReason() { Node = node, ScoreValue = score });
#endif
                        }
                    }
                }
                else if (node.OwnedBy != null)
                {
                    // Subtract score for each node owned by another player
                    //            score -= .9f; // todo: weight this based on player's personality
#if DEBUG
                    //          scoreReasons?.ScoresFrom_EnemyOwnedNodes.Add(new DebugAIStateReason() { Node = node, ScoreValue = -.9f });
#endif
                }
            }
        }

        // Weight the score based on how deep we are in the state tree; the deeper we are, the less we care about the score
        // stateDepth of 1 means we are at the top of the tree, so we care about the score fully
        // stateDepth of maxStateDepth means we are at the bottom of the tree, so we care less; however we still care.  at bottom we only care .99f
        //float weight = 1 - stateDepth / maxStateDepth * 0.01f;
        //  score *= weight;
        return score;
    }
}
