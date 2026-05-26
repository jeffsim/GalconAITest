using System.Collections.Generic;
using System.Text;

/// Flat per-tick log of what the AI considered and chose. Replaces the old recursive
/// AIDebuggerEntryData tree -- single allocation, no parent/child pointers, no per-branch
/// pool. Consumed by AITestScene_AIDebugDump / AITestScene_SimulationDump and on-map
/// arrows.
///
/// One AIDecisionRecord per PlayerAI; overwritten in-place each tick (no history). The
/// last-N executed actions are stored separately in PlayerAI.RecentExecutedActions so the
/// dump can show "what the AI has been doing over time" without keeping decision records.
public class AIDecisionRecord
{
    public int Tick;
    public float WorldTime;
    public PlayerData Player;

    /// One-line description of the chosen action (or "DoNothing" / why nothing fired).
    public string ChosenDescription;
    public float ChosenScore;

    /// Top-N candidates considered this tick, sorted by score descending. Used by the
    /// AI debug dump to show "here's what came close to winning, and why."
    public readonly List<Snapshot> TopCandidates = new();

    /// Counts of candidates emitted by each generator family, for at-a-glance health checks.
    public readonly Dictionary<AIActionType, int> CandidateCountsByType = new();

    public int TotalCandidatesEvaluated;

    public const int MaxTopCandidates = 10;

    public class Snapshot
    {
        public AIActionType Type;
        public int DestNodeId;
        public string Description;
        public float Score;
    }

    public void BeginTick(PlayerData player, int tick, float worldTime)
    {
        Player = player;
        Tick = tick;
        WorldTime = worldTime;
        TopCandidates.Clear();
        CandidateCountsByType.Clear();
        ChosenDescription = null;
        ChosenScore = 0f;
        TotalCandidatesEvaluated = 0;
    }

    public void RecordEvaluated(AICandidate c)
    {
        TotalCandidatesEvaluated++;
        if (!CandidateCountsByType.ContainsKey(c.Type))
            CandidateCountsByType[c.Type] = 0;
        CandidateCountsByType[c.Type]++;

        // Maintain a top-N by score, insertion-sorted.
        InsertTopCandidate(c);
    }

    void InsertTopCandidate(AICandidate c)
    {
        int destId = c.DestNode != null ? c.DestNode.NodeId : -1;
        int insertAt = TopCandidates.Count;
        for (int i = 0; i < TopCandidates.Count; i++)
        {
            if (c.Score > TopCandidates[i].Score)
            {
                insertAt = i;
                break;
            }
        }
        if (insertAt >= MaxTopCandidates) return;
        var snap = new Snapshot
        {
            Type = c.Type,
            DestNodeId = destId,
            Description = c.Reason ?? c.Type.ToString(),
            Score = c.Score,
        };
        TopCandidates.Insert(insertAt, snap);
        if (TopCandidates.Count > MaxTopCandidates)
            TopCandidates.RemoveAt(TopCandidates.Count - 1);
    }

    public void RecordChosen(AICandidate c)
    {
        if (c == null)
        {
            ChosenDescription = "DoNothing (no viable candidates)";
            ChosenScore = 0f;
            return;
        }
        ChosenDescription = c.Reason ?? c.Type.ToString();
        ChosenScore = c.Score;
    }

    public void AppendDump(StringBuilder sb)
    {
        sb.AppendLine($"  decision (tick={Tick} t={WorldTime:F2}): {ChosenDescription} score={ChosenScore:F2}");
        sb.Append("  candidates by type:");
        if (CandidateCountsByType.Count == 0)
            sb.Append(" (none)");
        else
            foreach (var kv in CandidateCountsByType)
                sb.Append($" {kv.Key}={kv.Value}");
        sb.AppendLine();
        sb.AppendLine($"  top {TopCandidates.Count} of {TotalCandidatesEvaluated} considered:");
        for (int i = 0; i < TopCandidates.Count; i++)
        {
            var s = TopCandidates[i];
            sb.AppendLine($"    [{i + 1}] {s.Score:F2}  {s.Description}");
        }
    }
}
