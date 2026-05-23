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

    // Beam width at recursive plies (depth >= 1). At maxDepth=11 the total branches are
    // O(HybridTopK * HybridBeamWidth^(maxDepth-1)). Beam 3 -> 472K leaves; Beam 2 -> 8K leaves.
    // Beam was 3 originally; reduced after GetUpgradeHeuristic stopped saturating to 3.0,
    // because the previous saturation was implicitly doing most of the pruning -- peer
    // Construct/Build/Attack candidates couldn't beat Upgrade's optimistic bound and got
    // skipped by ShouldPruneByHeuristic. With heuristics now closer together, beam 3 expands
    // the full tree and the action pool blows up (>100K AIActions per Update).
    public const int HybridBeamWidth = 2;

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

        // Per-ply world tick at depth >= 1 (see DetermineBestActionToPerform for rationale).
        // Both search drivers must apply the tick consistently because GetActionScore can
        // route the recursion through either function depending on EnableHybridSearch.
        if (curDepth > 0)
            ApplyWorldTick(curDepth);

        // Defensive reset: at the top of a real-game search, all IsVisited should be false.
        // If a prior search threw an exception inside the simulate+recurse loop below, the
        // try/finally guards us, but for years' worth of code stretching back through this
        // file we treat this as a belt-and-suspenders zero-cost cleanup.
        if (curDepth == 0)
        {
            var resetNodes = aiTownState.Nodes;
            for (int i = 0; i < resetNodes.Length; i++) resetNodes[i].IsVisited = false;
        }

        // Snapshot baseline so peer tasks at this depth can branch-and-bound against it.
        // Snapshot is taken AFTER ApplyWorldTick so the baseline reflects post-tick state.
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
        // PreviewHeuristic returns personality-adjusted values so the top-K reflects what each
        // candidate would actually score, not just its raw situational fit.
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

            // try/finally protects IsVisited: if TryTask (or anything it recurses into) throws,
            // we MUST still clear the flag, otherwise this node would be skipped on every
            // subsequent search for this PlayerAI -- which manifests as "the AI just sits
            // there" with actionsTried=0 because Phase 1 keeps finding zero candidates.
            node.IsVisited = true;
            bool validTask;
            AIAction action;
            try
            {
                validTask = task.TryTask(node, curDepth, debugOutput_ActionsTried, parentDebuggerEntry, bestAction.Score, out action);
            }
            finally
            {
                node.IsVisited = false;
            }

            if (validTask && action.Score > bestAction.Score)
            {
                bestAction = action;
                if (parentDebuggerEntry != null)
                    parentDebuggerEntry.BestNextAction = bestAction.AIDebuggerEntry;
            }
        }

        currentDepthBaselineScore = prevBaseline;
        if (curDepth > 0)
            UndoWorldTick(curDepth);
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
