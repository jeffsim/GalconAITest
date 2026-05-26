using System.Collections.Generic;

/// Internal candidate-move record produced by generators and scored by ActionUtility.
/// Distinct from AIAction (which is the OUTPUT data sent to the realtime executor) so we
/// can hold richer reasoning here (e.g. a one-line "why" string for debug output) without
/// polluting the executor-facing contract.
///
/// Pooled per-tick by PlayerAI; recycled via Reset() at the start of each Update.
public class AICandidate
{
    public AIActionType Type;
    public AI_NodeState SourceNode;        // single-source actions; null for multi-source
    public AI_NodeState DestNode;
    public int Count;                       // single-source send count
    public BuildingDefn BuildingToConstruct;
    /// Multi-source send counts. Empty for single-source actions.
    public readonly Dictionary<AI_NodeState, int> Sources = new();

    public float Score;
    public string Reason;

    public void Reset()
    {
        Type = AIActionType.DoNothing;
        SourceNode = null;
        DestNode = null;
        Count = 0;
        BuildingToConstruct = null;
        Sources.Clear();
        Score = 0f;
        Reason = null;
    }

    public int TotalSent()
    {
        if (Sources.Count > 0)
        {
            int sum = 0;
            foreach (var kv in Sources) sum += kv.Value;
            return sum;
        }
        return Count;
    }

    /// Copy this candidate's payload into an AIAction so the executor can act on it.
    public void CopyTo(AIAction action)
    {
        action.Type = Type;
        action.Score = Score;
        action.SourceNode = SourceNode;
        action.DestNode = DestNode;
        action.Count = Count;
        action.BuildingToConstruct = BuildingToConstruct;
        action.AttackFromNodes.Clear();
        foreach (var kv in Sources)
            action.AttackFromNodes[kv.Key] = kv.Value;
    }
}
