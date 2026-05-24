using System;
using Unity.Profiling;
using UnityEngine;

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
        int numNodes = Nodes.Length;

#if DEBUG
        if (AITestScene.Instance.DebugOutputStrategyReasons) scoreReasons = new();
#endif

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
                        // Upgrade value. The flat 0.15/level was too small for the recursive
                        // search to recognize upgrade-then-X chains; the immediate cost of
                        // halving workers outweighed it by EvaluateScore's standards. The
                        // context-aware terms below let the search "see" why the higher cap
                        // matters before it ever has to fill it.
                        int levelsAboveStarting = node.BuildingLevel - 1;
                        if (levelsAboveStarting > 0)
                        {
                            score += 0.15f * levelsAboveStarting;

                            // Frontier nodes: bigger cap means better force projection. The
                            // bonus is per level (not per MaxWorkers) so it doesn't double up
                            // with the resource-throughput term below for gatherers that also
                            // happen to be on the frontier.
                            if (node.IsOnTerritoryEdge)
                                score += 0.5f * levelsAboveStarting;

                            // Resource gatherers/sources: higher cap raises eventual throughput,
                            // weighted by the shortage of the produced good. Search sees value
                            // immediately even though current NumWorkers was halved by the upgrade.
                            if (node.CanGoGatherResources || node.CanBeGatheredFrom)
                            {
                                GoodType resource = node.CanBeGatheredFrom
                                    ? node.ResourceGatheredFromThisNode
                                    : node.ResourceThisNodeCanGoGather;
                                int shortage = AI_ActionHeuristics.GetResourceShortage(this, resource);
                                if (shortage > 0)
                                    score += 0.05f * shortage * levelsAboveStarting;
                            }

                            // Worker generators: higher cap means more useful workers can
                            // accumulate here, which is the entire reason to ever upgrade a
                            // Camp/Outpost. Scaled by level so each step is meaningful.
                            if (node.CanGenerateWorkers)
                                score += 0.4f * levelsAboveStarting;
                        }

                        // Discourage having too many workers in a building
                        if (node.NumWorkers > node.MaxWorkers * 2f)
                            score -= .5f * (node.NumWorkers - node.MaxWorkers / 2f);

                        // Resource gathering buildings are useful if they can reach a resource node.
                        // These buildings are more useful the close to the resource node they are.
                        // TODO: Increase usefulness score based on how much we need the resource vs how much we have
                        if (node.CanGoGatherResources)
                        {
                            var addedScore = 1.5f;

                            // The longer we've owned the node, the more useful it is.
                            addedScore += (maxStateDepth - node.TurnBuildingWasBuilt + 1) * 1.5f;

                            var resourceType = node.ResourceThisNodeCanGoGather;
                            int numResource = PlayerTownInventory[resourceType];
                            addedScore = Math.Max(0, addedScore - numResource * .1f);

                            // Reward staffing gatherers: output scales with workers.
                            if (node.BuildingDefn != null && node.NumWorkers > 0)
                            {
                                float rate = ResourceProduction.GetResourcesPerSecond(node.BuildingDefn, node.NumWorkers);
                                addedScore += rate * 0.5f;
                            }

                            score += addedScore;
                        }

                        // Owned resource nodes (forest/stone): value scales with workers assigned.
                        if (node.CanBeGatheredFrom && node.BuildingDefn != null && node.NumWorkers > 0)
                        {
                            float rate = ResourceProduction.GetResourcesPerSecond(node.BuildingDefn, node.NumWorkers);
                            score += rate * 0.75f;
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
                            float scoreValue = Mathf.Max((node.NumWorkers - node.NumEnemiesInNeighborNodes) * .15f, - 1);
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
                    score -= .1f; // todo: weight this based on player's personality
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
