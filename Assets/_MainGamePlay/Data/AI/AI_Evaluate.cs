public partial class AI_TownState
{
    // TODO: Add weights based on AI's personality
    internal void EvaluateScore(int stateDepth, int maxStateDepth, out float score, out DebugAIStateReasons scoreReasons)
    {
        score = 0;

        scoreReasons = null;
#if DEBUG
        if (AITestScene.Instance.DebugOutputStrategyReasons)
            scoreReasons = new();
#endif

        for (int i = 0; i < NumNodes; i++)
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
                    score += .1f; // some score for owning empty nodes.  Base this on AI personality's "desire to expand"
#if DEBUG
                    scoreReasons?.ScoresFrom_NumEmptyNodesOwned.Add(new DebugAIStateReason() { Node = node, ScoreValue = .1f });
#endif
                }
                else
                {
                    // Resource gathering buildings are useful if they can reach a resource node.
                    // These buildings are more useful the close to the resource node they are.
                    // TODO: Increase usefulness score based on how much we need the resource vs how much we have
                    if (node.CanGoGatherResources)
                    {
                        // Dictionaries are slow, and this is the innermost loop, so...
                     //   int dist = node.DistanceToClosestGatherableResourceNode(node.ResourceThisNodeCanGoGather);
                       // if (dist == 1)
                        {
                            score += 1.5f;

                            // The longer we've owned the node, the more useful it is.  Use node.TurnBuildingWasBuilt, which goes from 0 to maxStateDepth, to weight the score
                            // where turn 0 is the first turn, and maxStateDepth is the last turn
                            // building built turn 0; currently on turn 10; multiply score by 10
                            // building built turn 5; currently on turn 10; multiply score by 5
                            score += (maxStateDepth - node.TurnBuildingWasBuilt) * .1f;

#if DEBUG
                            scoreReasons?.ScoresFrom_ResourceGatherersCloseToResourceNodes.Add(new DebugAIStateReason() { Node = node, ScoreValue = 2f });
#endif

                            // The more of the resource that this building can gather we already own, the lower the utility of it
                            var resourceType = node.ResourceThisNodeCanGoGather;
                            if (PlayerTownInventory.TryGetValue(resourceType, out int resourceCount))
                            {
                                score -= resourceCount * .1f;
                            }

                            // The more we globally need the resource that this node can gather, the higher the utility of it
                            // if (GlobalResourceNeeds.TryGetValue(resourceType, out int globalResourceNeed))
                            // {
                            //     score += globalResourceNeed * .1f;
                            // }
                        }
                    }

                    // Defensive buildings are useful if...
                    if (node.CanGenerateWorkers)
                    {
                        score += .25f;
#if DEBUG
                        scoreReasons?.ScoresFrom_BuildingsThatGenerateWorkers.Add(new DebugAIStateReason() { Node = node, ScoreValue = .25f });
#endif
                    }

                    // Storage buildings are useful if...
                    // Crafting buildings are useful if...
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

        // Weight the score based on how deep we are in the state tree; the deeper we are, the less we care about the score
        // stateDepth of 1 means we are at the top of the tree, so we care about the score fully
        // stateDepth of maxStateDepth means we are at the bottom of the tree, so we care less; however we still care.  at bottom we only care .99f
        //float weight = 1 - stateDepth / maxStateDepth * 0.01f;
        //  score *= weight;

    }
}
