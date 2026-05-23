using System;

public partial class PlayerAI
{
    // Phase 1 candidate generation (non-recursive heuristic enumeration) feeds Phase 2,
    // which runs the existing simulate+recurse path on only the top-K candidates by heuristic.
    // This bounds the depth-0 branching factor without lowering MaxAIDepth.
    //
    // Recursive plies (depth >= 1) reuse the same machinery with a smaller beam width
    // (HybridBeamWidth) so subtree size becomes O(beam^depth) rather than O(branchingFactor^depth).

    struct HybridCandidate
    {
        public float HeuristicScore;
        public AI_NodeState Node;
        public int TaskIndex;
    }

    // Top-K width at depth 0. Tuned per the plan's K=5-8 guidance; every extra slot multiplies
    // the depth-0 branching factor.
    public const int HybridTopK = 8;

    // Beam width at recursive plies (depth >= 1). Per the plan, only the top 2-3 child actions
    // by heuristic pre-score are expanded.
    public const int HybridBeamWidth = 3;

    const int HybridCandidateBufferSize = HybridTopK;

    // Per-depth buffers so a recursive _Hybrid call cannot stomp the parent's iteration state.
    HybridCandidate[][] hybridCandidatesPerDepth;
    int[] hybridCandidateCountsPerDepth;

    public int GetHybridBeamWidthForDepth(int curDepth) => curDepth == 0 ? HybridTopK : HybridBeamWidth;

    public AIAction DetermineBestActionToPerform_Hybrid(int curDepth, AIDebuggerEntryData parentDebuggerEntry)
        => DetermineBestActionToPerform_Hybrid(curDepth, parentDebuggerEntry, GetHybridBeamWidthForDepth(curDepth));

    public AIAction DetermineBestActionToPerform_Hybrid(int curDepth, AIDebuggerEntryData parentDebuggerEntry, int topK)
    {
        var bestAction = GetAIAction();

        // Snapshot baseline so peer tasks at this depth can branch-and-bound against it.
        float prevBaseline = currentDepthBaselineScore;
        currentDepthBaselineScore = aiTownState.EvaluateScore(curDepth, maxDepth, out _);

        EnsureHybridCandidateCapacity(curDepth);
        var candidates = hybridCandidatesPerDepth[curDepth];
        hybridCandidateCountsPerDepth[curDepth] = 0;

        // Clamp topK to buffer size to guarantee InsertIntoTopK never overflows.
        if (topK > HybridCandidateBufferSize) topK = HybridCandidateBufferSize;
        if (topK < 1) topK = 1;

        var nodes = aiTownState.Nodes;
        int numNodes = nodes.Length;
        int numTasks = Tasks.Count;

        // Phase 1: enumerate (node, task) heuristic-only scores. No simulation, no recursion.
        for (int i = 0; i < numNodes; i++)
        {
            var node = nodes[i];
            if (node.IsVisited) continue;
            for (int t = 0; t < numTasks; t++)
            {
                float h = Tasks[t].PreviewHeuristic(node);
                if (h <= 0f) continue;
                InsertIntoTopK(curDepth, candidates, topK, h, node, t);
            }
        }

        int candidateCount = hybridCandidateCountsPerDepth[curDepth];

        // Phase 2: full simulate+recurse on top-K only, in heuristic-descending order so
        // bestAction.Score climbs early and the inner branch-and-bound prunes aggressively.
        for (int k = 0; k < candidateCount; k++)
        {
            var candidate = candidates[k];
            var node = candidate.Node;
            var task = Tasks[candidate.TaskIndex];

            node.IsVisited = true;
            bool validTask = task.TryTask(node, curDepth, debugOutput_ActionsTried, parentDebuggerEntry, bestAction.Score, out AIAction action);
            node.IsVisited = false;

            if (validTask && action.Score > bestAction.Score)
            {
                bestAction = action;
                if (parentDebuggerEntry != null)
                    parentDebuggerEntry.BestNextAction = bestAction.AIDebuggerEntry;
            }
        }

        currentDepthBaselineScore = prevBaseline;
        return bestAction.Type == AIActionType.DoNothing ? null : bestAction;
    }

    void EnsureHybridCandidateCapacity(int curDepth)
    {
        int requiredDepth = curDepth + 1;
        if (hybridCandidatesPerDepth != null && hybridCandidatesPerDepth.Length >= requiredDepth)
            return;

        int newLen = Math.Max(requiredDepth, hybridCandidatesPerDepth?.Length ?? 0);
        if (newLen < 8) newLen = 8;

        var newArr = new HybridCandidate[newLen][];
        var newCounts = new int[newLen];
        if (hybridCandidatesPerDepth != null)
        {
            Array.Copy(hybridCandidatesPerDepth, newArr, hybridCandidatesPerDepth.Length);
            Array.Copy(hybridCandidateCountsPerDepth, newCounts, hybridCandidateCountsPerDepth.Length);
        }
        for (int i = 0; i < newLen; i++)
            if (newArr[i] == null) newArr[i] = new HybridCandidate[HybridCandidateBufferSize];

        hybridCandidatesPerDepth = newArr;
        hybridCandidateCountsPerDepth = newCounts;
    }

    // Insertion-sort into a fixed-size top-K buffer (descending by heuristic score).
    // K is small (<= HybridTopK), so a linear shift is cheaper than a heap.
    void InsertIntoTopK(int curDepth, HybridCandidate[] buffer, int topK, float score, AI_NodeState node, int taskIndex)
    {
        int count = hybridCandidateCountsPerDepth[curDepth];

        int insertAt = count;
        for (int k = 0; k < count; k++)
        {
            if (score > buffer[k].HeuristicScore)
            {
                insertAt = k;
                break;
            }
        }
        if (insertAt >= topK) return;

        int shiftEnd = Math.Min(count, topK - 1);
        for (int k = shiftEnd; k > insertAt; k--)
            buffer[k] = buffer[k - 1];

        buffer[insertAt].HeuristicScore = score;
        buffer[insertAt].Node = node;
        buffer[insertAt].TaskIndex = taskIndex;
        if (count < topK) hybridCandidateCountsPerDepth[curDepth] = count + 1;
    }
}
