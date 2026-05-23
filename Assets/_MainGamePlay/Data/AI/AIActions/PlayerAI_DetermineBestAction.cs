using System;

public partial class PlayerAI
{
    // Determine the best action that can be taken given the current aiTownState and return that action, ensuring
    // that aiTownState is fully restored to its original state before returning.
    public AIAction DetermineBestActionToPerform(int curDepth, AIDebuggerEntryData parentDebuggerEntry)
    {
        // we'll return the best action from all possible actions at this 'recursive step/turn'
        var bestAction = GetAIAction();

        // At depth >= 1 the simulated world has advanced one turn from the previous ply, so
        // owned gatherers credit resources and worker-generators add workers. This is what
        // exposes "build A -> tick -> resource produced -> build B" chains to the search.
        // (Depth 0 reflects the real current state with no advance.)
        if (curDepth > 0)
            ApplyWorldTick(curDepth);

        // Defensive reset: see DetermineBestActionToPerform_Hybrid for rationale. At the top
        // of a real-game search, all IsVisited must be false; a stuck-true flag would silently
        // starve subsequent searches.
        if (curDepth == 0)
        {
            var resetNodes = aiTownState.Nodes;
            for (int i = 0; i < resetNodes.Length; i++) resetNodes[i].IsVisited = false;
        }

        // Snapshot the pre-action EvaluateScore so peer tasks at this depth can use it as an
        // optimistic upper bound for branch-and-bound pruning. Saved/restored so deeper recursion
        // levels can each maintain their own baseline. Snapshot is taken AFTER ApplyWorldTick so
        // the baseline reflects post-tick state.
        float prevBaseline = currentDepthBaselineScore;
        currentDepthBaselineScore = aiTownState.EvaluateScore(curDepth, maxDepth, out _);

        // bestAction is currently set to 'do nothing' -- see if taking any of our available actions results in a better score
        for (int i = 0; i < aiTownState.Nodes.Length; i++)
        {
            var node = aiTownState.Nodes[i];
            if (node.IsVisited) continue; // don't revisit nodes we visited earlier in the recursion; avoid ping-ponging between nodes

            // try/finally protects IsVisited: if any TryTask throws (now or in the future),
            // the flag MUST still clear, otherwise this node is permanently skipped on
            // subsequent searches for this PlayerAI.
            node.IsVisited = true;
            try
            {
                for (int t = 0; t < Tasks.Count; t++)
                {
                    var task = Tasks[t];

                    bool validTask = task.TryTask(node, curDepth, debugOutput_ActionsTried, parentDebuggerEntry, bestAction.Score, out AIAction action);
                    if (validTask && action.Score > bestAction.Score)
                    {
                        bestAction = action;
                        if (parentDebuggerEntry != null)
                            parentDebuggerEntry.BestNextAction = bestAction.AIDebuggerEntry;
                    }
                }
            }
            finally
            {
                node.IsVisited = false;
            }
        }

        currentDepthBaselineScore = prevBaseline;
        if (curDepth > 0)
            UndoWorldTick(curDepth);
        return bestAction.Type == AIActionType.DoNothing ? null : bestAction;
    }
}
