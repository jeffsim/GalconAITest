using UnityEngine;

/// Single source of truth for "how many attackers do I need to CAPTURE this enemy node".
/// Used by AttackGenerator to size waves and by ActionUtility.ScoreAttack to gate viability;
/// these MUST agree or the generator will emit waves that the scorer immediately vetoes
/// (or vice versa, the scorer accepts dribble waves).
///
/// The formula:
///   defenders   = target.EnemyEffectiveDefense                       // raw, pre-deduct
///   regen       = ExpectedDefenderRegen(target, sourceHops)
///   captureMin  = defenders + regen + 1                              // +1 to flip ownership
///   required    = ceil(captureMin * personality.AttackOverkill)
///
/// `required` is the number of attackers that need to LAND ALIVE at the target. Existing
/// in-flight attackers count toward this -- the generator subtracts IncomingMyAttackers
/// from `required` to get the size of the FRESH wave it needs to dispatch.
public static class AttackSizing
{
    /// Approximate seconds per BFS hop a wave takes to traverse, used to scale defender
    /// regen during travel. Real travel time is `sum(segLen) / workerSpeed`, but the AI
    /// doesn't pathfind here; this constant is tuned to roughly match a 1-hop attack at
    /// gameSpeed=1 with default WorkerDefn.Speed=4 (0.25 world units/sec) over a typical
    /// ~4-unit edge. The estimate is intentionally generous: under-estimating regen is
    /// what produced the original drip-feed bug, and over-estimating just sends slightly
    /// more attackers than strictly necessary.
    public const float SecondsPerHop = 8f;

    /// Returns the total attackers (in-flight + fresh) required to CAPTURE the target.
    /// `sourceHops` is the BFS depth of the closest available source; pass 1 for a
    /// direct adjacent source, 2 for a relayed wave, etc.
    public static int RequiredAttackersToCapture(AI_NodeState target, PersonalityWeights p, int sourceHops)
    {
        int defenders = Mathf.Max(0, target.EnemyEffectiveDefense);
        int regen = ExpectedDefenderRegen(target, sourceHops);

        // +1 to actually flip ownership (kills the last defender then captures).
        int captureMin = defenders + regen + 1;
        int required = Mathf.CeilToInt(captureMin * p.AttackOverkill);
        if (required < 1) required = 1;
        return required;
    }

    /// Estimates how many extra defenders the target's worker-generator will produce
    /// during the wave's travel time. Capped at MaxWorkers headroom so a near-cap node
    /// doesn't get a fictitious infinite regen credit.
    public static int ExpectedDefenderRegen(AI_NodeState target, int sourceHops)
    {
        if (target == null || !target.CanGenerateWorkers || target.BuildingDefn == null) return 0;
        float seconds = target.BuildingDefn.SecondsPerWorkerGenerated;
        if (seconds <= 0f) return 0;

        float travelSeconds = Mathf.Max(1, sourceHops) * SecondsPerHop;
        int regenWorkers = Mathf.FloorToInt(travelSeconds / seconds);

        // Bound by remaining headroom: a node already at MaxWorkers can only over-cap by
        // 1 between regen ticks before it stops generating, so the practical regen cap
        // is `MaxWorkers - currentDefense` (clamped at 0).
        int headroom = Mathf.Max(0, target.MaxWorkers - target.EnemyEffectiveDefense);
        if (regenWorkers > headroom) regenWorkers = headroom;
        if (regenWorkers < 0) regenWorkers = 0;
        return regenWorkers;
    }
}
