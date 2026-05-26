using System.Collections.Generic;
using UnityEngine;

/// Per-player AI brain. Single-pass utility evaluator. Each tick:
///
///   1. Refresh worldView from TownData (mirrors nodes, projects in-flight workers).
///   2. Compute strategicAnalysis (edges, pressure, deficits, demand) in one pre-pass.
///   3. Each generator emits scored candidates into candidatePool.
///   4. Pick the highest-scoring candidate.
///   5. Copy its payload into BestNextActionToTake for the executor.
///   6. Record the decision into AIDecisionRecord for debug/dump.
///
/// There is no recursion, no simulate/undo, no per-tick mutation of search state, no
/// action pool of 25,000. Worst case is O(N_nodes * N_buildings) for the build generator.
public class PlayerAI
{
    public override string ToString()
    {
        if (BestNextActionToTake == null) return "null BestNextActionToTake";
        return BestNextActionToTake.ToString();
    }

    // ============================================================================
    // Public surface used by the rest of the game
    // ============================================================================

    public AIAction BestNextActionToTake = new();
    public AIAction LastActionToTake = new();

    /// History of executed actions, formatted at record-time. Capped to keep memory bounded;
    /// consumed by AITestScene_SimulationDump to surface ping-pong oscillations.
    public const int MaxRecentExecutedActions = 100;
    public readonly List<string> RecentExecutedActions = new();

    /// Realtime: world-time at which this AI is scheduled to make its next decision.
    /// Negative sentinel forces an immediate first decision.
    public float NextRealtimeDecisionTime = -1f;

    /// Flat per-tick decision record. Replaces the old recursive AIDebuggerEntryData tree.
    /// Always populated -- there is no #if DEBUG gating any more.
    public readonly AIDecisionRecord DecisionRecord = new();

    // ============================================================================
    // Internals
    // ============================================================================

    PlayerData player;
    AIWorldView worldView;
    StrategicAnalysis analysis = new();
    PersonalityWeights personality;

    readonly List<IActionGenerator> generators = new();

    // Per-tick candidate pool. Re-used across ticks; index reset at the start of each.
    readonly List<AICandidate> candidatePool = new();
    readonly List<AICandidate> candidatesThisTick = new();
    int candidatePoolIndex;

    // Decision cache so we don't re-search in step mode every frame if nothing changed.
    int lastSearchedWorldRevision = -1;

    int tickCount;

    public PlayerAI(PlayerData playerData)
    {
        player = playerData;
        worldView = new AIWorldView(player);
        personality = PersonalityWeights.From(player.AIDefn);

        generators.Add(new AttackGenerator());
        generators.Add(new CaptureGenerator());
        generators.Add(new ReinforceGenerator());
        generators.Add(new BuildGenerator());
        generators.Add(new UpgradeGenerator());
    }

    public void InitializeStaticData(TownData townData)
    {
        worldView.InitializeStatic(townData);
    }

    // ============================================================================
    // Realtime / step-mode driver
    // ============================================================================

    public void Update(TownData townData)
    {
        // Decision cache: in step mode the world may not change between AITestScene.Update
        // ticks. Skip work when WorldRevision is unchanged. Realtime path calls
        // InvalidateDecisionCache() explicitly before its scheduled tick.
        if (lastSearchedWorldRevision == townData.WorldRevision && BestNextActionToTake.Type != AIActionType.DoNothing)
            return;

        // Re-read personality each tick in case an inspector edit happened mid-play.
        personality = PersonalityWeights.From(player.AIDefn);

        worldView.Refresh(townData);
        analysis.Compute(worldView, player, personality);

        DecisionRecord.BeginTick(player, ++tickCount, townData.WorldTime);

        ResetCandidatePool();
        candidatesThisTick.Clear();
        for (int g = 0; g < generators.Count; g++)
            generators[g].Generate(worldView, analysis, personality, this, candidatesThisTick);

        // Record every candidate considered for debug; pick the best for execution.
        AICandidate best = null;
        for (int i = 0; i < candidatesThisTick.Count; i++)
        {
            var c = candidatesThisTick[i];
            DecisionRecord.RecordEvaluated(c);
            if (c.Score <= 0f) continue;
            if (best == null || c.Score > best.Score) best = c;
        }

        if (best == null)
        {
            BestNextActionToTake.SetToNothing();
        }
        else
        {
            best.CopyTo(BestNextActionToTake);
            RememberLastAction(BestNextActionToTake);
        }
        DecisionRecord.RecordChosen(best);

        lastSearchedWorldRevision = townData.WorldRevision;
    }

    public void InvalidateDecisionCache()
    {
        lastSearchedWorldRevision = -1;
    }

    public void ScheduleNextRealtimeDecision(float currentWorldTime)
    {
        var defn = player.AIDefn;
        float interval = defn != null ? defn.DecisionIntervalSeconds : 1f;
        float variance = defn != null ? defn.DecisionVarianceSeconds : 0.5f;
        float jitter = Random.Range(-variance, variance);
        float delta = Mathf.Max(0.05f, interval + jitter);
        NextRealtimeDecisionTime = currentWorldTime + delta;
    }

    // ============================================================================
    // Candidate pool (zero-alloc across ticks)
    // ============================================================================

    public AICandidate AcquireCandidate()
    {
        if (candidatePoolIndex < candidatePool.Count)
        {
            var c = candidatePool[candidatePoolIndex++];
            c.Reset();
            return c;
        }
        var fresh = new AICandidate();
        candidatePool.Add(fresh);
        candidatePoolIndex++;
        return fresh;
    }

    public void ReleaseCandidate(AICandidate c)
    {
        // Decrement only if this is the most recently acquired -- generators always release
        // candidates immediately after acquiring them when the score zeros out, so this is
        // the common case and lets us actually free the slot. Out-of-order releases just
        // leak the slot for one tick (it'll be reset next tick by ResetCandidatePool).
        if (candidatePoolIndex > 0 && candidatePool[candidatePoolIndex - 1] == c)
            candidatePoolIndex--;
    }

    void ResetCandidatePool()
    {
        candidatePoolIndex = 0;
    }

    // ============================================================================
    // Last-action tracking (consumed by map arrows + dump history)
    // ============================================================================

    public void RememberLastAction(AIAction action)
    {
        if (action == null || action.Type == AIActionType.DoNothing) return;
        LastActionToTake.CopyFrom(action);
    }

    public void RecordExecutedAction(AIAction action, float worldTime)
    {
        if (action == null || action.Type == AIActionType.DoNothing) return;
        RecentExecutedActions.Add($"t={worldTime:F2} {FormatActionForHistory(action)}");
        if (RecentExecutedActions.Count > MaxRecentExecutedActions)
            RecentExecutedActions.RemoveRange(0, RecentExecutedActions.Count - MaxRecentExecutedActions);
    }

    static string FormatActionForHistory(AIAction action)
    {
        switch (action.Type)
        {
            case AIActionType.SendWorkersToOwnedNode:
                return $"Support {action.Count} #{action.SourceNode?.NodeId} -> #{action.DestNode?.NodeId}";
            case AIActionType.SendMultiSourceWorkersToOwnedNode:
                return $"Multi-source support #{action.DestNode?.NodeId} {FormatSources(action)}";
            case AIActionType.ConstructBuildingInEmptyNode:
                return $"Build {action.BuildingToConstruct?.Id} send {action.Count} #{action.SourceNode?.NodeId} -> #{action.DestNode?.NodeId}";
            case AIActionType.CaptureNeutralResourceNode:
                return $"Capture resource #{action.DestNode?.NodeId} send {action.Count} from #{action.SourceNode?.NodeId}";
            case AIActionType.CaptureNeutralNode:
                return $"Build {action.BuildingToConstruct?.Id} on #{action.DestNode?.NodeId} {FormatSources(action)}";
            case AIActionType.UpgradeBuilding:
                return $"Upgrade #{action.SourceNode?.NodeId}";
            case AIActionType.AttackToNode:
                return $"Attack #{action.DestNode?.NodeId} {FormatSources(action)}";
            default:
                return action.Type.ToString();
        }
    }

    static string FormatSources(AIAction action)
    {
        if (action.AttackFromNodes == null || action.AttackFromNodes.Count == 0) return "from ?";
        var sb = new System.Text.StringBuilder();
        sb.Append("from ");
        bool first = true;
        foreach (var kvp in action.AttackFromNodes)
        {
            if (!first) sb.Append(", ");
            sb.Append($"#{kvp.Key.NodeId}({kvp.Value})");
            first = false;
        }
        return sb.ToString();
    }

    public AIAction GetActionForArrowDisplay()
    {
        if (BestNextActionToTake != null && BestNextActionToTake.Type != AIActionType.DoNothing)
            return BestNextActionToTake;
        if (LastActionToTake != null && LastActionToTake.Type != AIActionType.DoNothing)
            return LastActionToTake;
        return null;
    }

    // ============================================================================
    // Testability hooks (used by AIRegressionTests)
    // ============================================================================

    /// Direct access to the WorldView for tests that need to inspect mirror state.
    public AIWorldView GetWorldView() => worldView;
    public StrategicAnalysis GetAnalysis() => analysis;
    public PersonalityWeights GetPersonality() => personality;
}
