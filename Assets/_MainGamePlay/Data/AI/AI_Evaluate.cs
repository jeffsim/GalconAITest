public partial class AI_TownState
{
    // TODO: Add weights based on AI's personality
    internal void EvaluateScore(int stateDepth, int maxStateDepth, out float score, out DebugAIStateReasons scoreReasons)
    {
        score = 0;

        scoreReasons = null;
#if DEBUG
        if (GameMgr.Instance.DebugOutputStrategyFull)
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
                        int dist = node.DistanceToClosestGatherableResourceNode;
                        if (dist == 1)
                        {
                            score += 2f;
#if DEBUG
                            scoreReasons?.ScoresFrom_ResourceGatherersCloseToResourceNodes.Add(new DebugAIStateReason() { Node = node, ScoreValue = 2f });
#endif
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
                score -= .9f; // todo: weight this based on player's personality
#if DEBUG
                scoreReasons?.ScoresFrom_EnemyOwnedNodes.Add(new DebugAIStateReason() { Node = node, ScoreValue = -.9f });
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
