using System.Collections.Generic;
using System.Diagnostics;
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

    // ============================================================================
    // Per-tick timings (Phase 0 of the map-preprocessing plan). Surfaced in dumps so
    // every later optimization can be compared against a number, not a vibe. All
    // values are wall-clock microseconds for THIS tick; rolling stats live in
    // TimingHistory below.
    // ============================================================================
    public long TickRefreshUs;        // worldView.Refresh + analysis.Compute
    public long TickGenerateUs;       // sum over all generators (also split per-type)
    public long TickSelectUs;         // candidate loop + best-pick + RecordChosen
    public long TickTotalUs;          // Update() entry to exit
    public readonly Dictionary<string, long> GeneratorTimingsUs = new();

    /// Rolling stats over the last N ticks. Cheap O(1) ring-buffer; reset only when
    /// PlayerAI is constructed. Surfaced as min/avg/max in the dump.
    public readonly TimingHistory History = new();

    public class TimingHistory
    {
        public const int Capacity = 64;
        readonly long[] totalUs = new long[Capacity];
        int count;
        int next;
        public long MaxObservedUs;

        public void Record(long us)
        {
            totalUs[next] = us;
            next = (next + 1) % Capacity;
            if (count < Capacity) count++;
            if (us > MaxObservedUs) MaxObservedUs = us;
        }

        public (long avgUs, long maxRecentUs, int samples) Stats()
        {
            if (count == 0) return (0, 0, 0);
            long sum = 0;
            long maxRecent = 0;
            for (int i = 0; i < count; i++)
            {
                sum += totalUs[i];
                if (totalUs[i] > maxRecent) maxRecent = totalUs[i];
            }
            return (sum / count, maxRecent, count);
        }
    }

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
        TickRefreshUs = 0;
        TickGenerateUs = 0;
        TickSelectUs = 0;
        TickTotalUs = 0;
        GeneratorTimingsUs.Clear();
    }

    public void RecordGeneratorTiming(string generatorName, long elapsedUs)
    {
        if (GeneratorTimingsUs.TryGetValue(generatorName, out long cur))
            GeneratorTimingsUs[generatorName] = cur + elapsedUs;
        else
            GeneratorTimingsUs[generatorName] = elapsedUs;
        TickGenerateUs += elapsedUs;
    }

    public void FinalizeTickTiming(long totalUs)
    {
        TickTotalUs = totalUs;
        History.Record(totalUs);
    }

    /// Converts a Stopwatch.GetTimestamp() delta into microseconds without the
    /// double-precision round-trip of Stopwatch.ElapsedTicks.
    public static long TicksToMicroseconds(long ticks)
    {
        // Stopwatch.Frequency is constant after process start, so the multiplication
        // order avoids precision loss for tiny intervals.
        return (ticks * 1_000_000L) / Stopwatch.Frequency;
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
        AppendTimingLine(sb);
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

    void AppendTimingLine(StringBuilder sb)
    {
        var (avg, maxRecent, samples) = History.Stats();
        sb.Append($"  timing(us): total={TickTotalUs} refresh={TickRefreshUs} gen={TickGenerateUs} sel={TickSelectUs}");
        if (samples > 0)
            sb.Append($" | rolling avg={avg} max={maxRecent} (n={samples}) seenMax={History.MaxObservedUs}");
        sb.AppendLine();
        if (GeneratorTimingsUs.Count > 0)
        {
            sb.Append("  gen breakdown(us):");
            foreach (var kv in GeneratorTimingsUs)
                sb.Append($" {kv.Key}={kv.Value}");
            sb.AppendLine();
        }
    }
}
