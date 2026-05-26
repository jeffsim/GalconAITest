using UnityEngine;

/// Utility-scoring entry points for the five action families. Each scorer is a pure
/// function of (worldView, analysis, candidate, personality) -- no state machines, no
/// recursive lookahead, no shared mutable scratch.
///
/// Score convention:
///   > 0  : a candidate worth executing this tick (higher = better)
///   <= 0 : do not execute (effectively vetoed)
///
/// All scorers follow the same shape:
///   1. baseValue   = intrinsic value of the destination
///   2. multiplier  = (chokepoint * personality-for-family)
///   3. risk        = workers committed below a safe threshold, scaled by Caution
///   4. forward     = optional one-step "what does this enable?" boost (build/upgrade only)
///   5. Score       = (baseValue + forward) * multiplier - risk
///
/// Magic numbers are concentrated here (not scattered across 7 task files + 1400-line
/// heuristics file). Each comes with a one-line comment so future tuning is local.
public static class ActionUtility
{
    // ------------------------------------------------------------
    // Constants (tuning knobs, all in one place).
    // ------------------------------------------------------------

    // Per-action-family base "intrinsic" value of doing something useful at all.
    const float CaptureBase = 10f;
    const float AttackBase = 12f;
    const float ReinforceBase = 15f;
    const float BuildBase = 8f;
    const float UpgradeBase = 4f;

    // Chokepoint amplification: multiplied by ChokepointScore (in [0, 1]) and the action's
    // personality dial, then added to the base multiplier. So a peak chokepoint with
    // Aggression=1 multiplies attack score by ~2.5x; a non-chokepoint multiplies by ~1.0x.
    const float ChokepointAttackAmp = 1.5f;
    const float ChokepointCaptureAmp = 1.5f;
    const float ChokepointDefenseAmp = 1.5f;

    // Forward-lookahead discount: "what would I do AFTER this build/upgrade?" contributes
    // at this fraction of its own scored value -- never more than the present-tick
    // payoff itself, so present moves are still preferred when tied.
    const float ForwardLookaheadDiscount = 0.5f;

    // Risk penalty: 1 worker committed below a safe-source threshold costs this much score
    // per Caution unit. Caution=0 ignores risk, Caution=2 doubles it.
    const float RiskPerOverSentWorker = 0.5f;

    // (Frontier-upgrade bonus was removed: empirically it made the AI upgrade EVERY at-cap
    // frontier node even when the node was about to fall to enemy pressure. Upgrades on the
    // frontier now compete on their merits via the forward-lookahead unlock score, and they
    // pay a risk penalty whenever the halved post-upgrade garrison falls below pressure.)

    // Building-type "missing tier" bonus added when this build would fill a building type
    // the player does not yet own. Stacks with base build value.
    const float MissingTierBonus = 6f;

    // ============================================================================
    // Attack
    // ============================================================================
    public static void ScoreAttack(AICandidate c, AIWorldView view, StrategicAnalysis analysis, PersonalityWeights p)
    {
        var target = c.DestNode;
        int sent = c.TotalSent();

        // Target value: territory + chokepoint + their building (if any).
        float baseValue = AttackBase + ValueOfEnemyNode(target);
        float chokeMult = 1f + target.ChokepointScore * ChokepointAttackAmp * p.Aggression;

        // Force margin: do we have meaningfully MORE than required? Below 1.0 = drip-feed,
        // veto. At 1.0 = bare minimum win. Above 1.5 = comfortable. We don't reward
        // huge overkill (the wasted workers belong elsewhere).
        int requiredForce = Mathf.CeilToInt(target.NumWorkers * p.AttackOverkill);
        if (sent < requiredForce || sent <= 0)
        {
            c.Score = 0f;
            c.Reason = $"attack #{target.NodeId} viability: sent {sent} < req {requiredForce}";
            return;
        }
        float marginBonus = Mathf.Min(2f, sent / (float)Mathf.Max(1, requiredForce)) - 1f;

        float risk = ComputeRiskFromSources(c, analysis, p);

        c.Score = (baseValue + marginBonus * 4f) * p.Aggression * chokeMult - risk;
        c.Reason = $"attack #{target.NodeId} base={baseValue:F1} choke={chokeMult:F2} agg={p.Aggression:F2} risk={risk:F1}";
    }

    static float ValueOfEnemyNode(AI_NodeState target)
    {
        float v = 1f;
        if (target.HasBuilding && target.BuildingDefn != null)
        {
            // Capturing a productive building is more valuable than capturing an empty owned node.
            if (target.CanGoGatherResources) v += 3f;
            if (target.CanGenerateWorkers) v += 4f;
            if (target.CanBeGatheredFrom) v += 2f;
            v += target.BuildingLevel * 0.5f;
        }
        return v;
    }

    // ============================================================================
    // Capture (single OR multi source; same scorer either way)
    // ============================================================================
    public static void ScoreCapture(AICandidate c, AIWorldView view, StrategicAnalysis analysis, PersonalityWeights p)
    {
        var target = c.DestNode;
        int sent = c.TotalSent();

        float baseValue = CaptureBase + ValueOfNeutralTarget(target, c.BuildingToConstruct);

        // Resource shortage bonus: capturing a node we'll gather a shortage resource from
        // is more valuable than the same capture when we have plenty.
        if (target.CanBeGatheredFrom)
        {
            int shortage = analysis.GetResourceShortage(target.ResourceGatheredFromThisNode);
            baseValue += Mathf.Min(8f, shortage * 0.3f);
        }

        // Contested-neutral bonus -- a chokepoint between us and them is worth more.
        float chokeMult = 1f + target.ChokepointScore * ChokepointCaptureAmp * p.Expansion;
        if (target.NumContestedNeutralWorkersNearby > 0 && c.BuildingToConstruct != null)
            baseValue += 2f;

        if (sent <= 0)
        {
            c.Score = 0f;
            return;
        }

        float risk = ComputeRiskFromSources(c, analysis, p);
        c.Score = baseValue * p.Expansion * chokeMult - risk;
        c.Reason = $"capture #{target.NodeId} base={baseValue:F1} exp={p.Expansion:F2} choke={chokeMult:F2}";
    }

    static float ValueOfNeutralTarget(AI_NodeState target, BuildingDefn willBuild)
    {
        float v = 0f;
        if (target.CanBeGatheredFrom) v += 3f;
        if (willBuild != null)
        {
            if (willBuild.CanGenerateWorkers) v += 4f;
            if (willBuild.CanGatherResources) v += 3f;
        }
        return v;
    }

    // ============================================================================
    // Reinforce (single OR multi source; same scorer)
    // ============================================================================
    public static void ScoreReinforce(AICandidate c, AIWorldView view, StrategicAnalysis analysis, PersonalityWeights p)
    {
        var target = c.DestNode;
        int deficit = analysis.DefensiveDeficit[target.Index];
        int sent = c.TotalSent();

        if (deficit <= 0 || sent <= 0)
        {
            c.Score = 0f;
            return;
        }

        // Reinforcement value scales with how much of the deficit we actually cover.
        float coverage = Mathf.Min(1f, sent / (float)deficit);
        float baseValue = ReinforceBase + deficit * 2f * coverage;

        // Chokepoint defense amp: defending a chokepoint is structurally more valuable.
        float chokeMult = 1f + target.ChokepointScore * ChokepointDefenseAmp * p.Caution;

        // Reinforce is the "defensive" family. Caution drives it directly. We deliberately do
        // NOT scale by (2 - Aggression) any more: a high-Aggression AI still needs to defend
        // critical nodes -- the difference shows up in the attack/reinforce TRADEOFF (attack
        // gets a larger boost from Aggression) rather than in defense being suppressed.
        float defensePersonality = Mathf.Max(0.1f, p.Caution);

        float risk = ComputeRiskFromSources(c, analysis, p);
        c.Score = baseValue * defensePersonality * chokeMult - risk;
        c.Reason = $"reinforce #{target.NodeId} deficit={deficit} sent={sent} cov={coverage:F2}";
    }

    // ============================================================================
    // Build
    // ============================================================================
    public static void ScoreBuild(AICandidate c, AIWorldView view, StrategicAnalysis analysis, PersonalityWeights p)
    {
        var target = c.DestNode;
        var source = c.SourceNode;
        var building = c.BuildingToConstruct;
        if (building == null) { c.Score = 0f; return; }

        float baseValue = BuildBase;
        if (building.CanGenerateWorkers) baseValue += 5f;
        if (building.CanGatherResources)
        {
            // Gatherer buildings are valuable in proportion to shortage of their good.
            if (building.ResourceThisNodeCanGoGather != null)
            {
                int shortage = analysis.GetResourceShortage(building.ResourceThisNodeCanGoGather.GoodType);
                baseValue += Mathf.Min(10f, shortage * 0.5f);
            }
        }

        // Missing-tier boost: filling out our building portfolio is worth real points.
        if (analysis.IsBuildingTypeMissing(building.BuildingType))
            baseValue += MissingTierBonus;

        // Forward-lookahead: if this is a Barracks adjacent to an enemy, it enables an
        // attack we couldn't otherwise launch. Reward that future option.
        float forward = ForwardLookupForBuild(building, target, view, analysis, p);

        // Chokepoint capture amp on the target site.
        float chokeMult = 1f + target.ChokepointScore * ChokepointCaptureAmp * p.Expansion;

        // Build/expansion is the slow-payoff "Tempo" family. Tempo>1 means we lean toward
        // long-term investment. Expansion controls "captures vs internal".
        float personality = p.Expansion * (0.5f + 0.5f * p.Tempo);

        float risk = source != null
            ? Mathf.Max(0f, (c.Count - analysis.SafeToSendFrom[source.Index])) * RiskPerOverSentWorker * p.Caution
            : 0f;

        c.Score = (baseValue + forward * ForwardLookaheadDiscount) * personality * chokeMult - risk;
        c.Reason = $"build {building.Id} on #{target.NodeId} base={baseValue:F1} fwd={forward:F1} pers={personality:F2}";
    }

    static float ForwardLookupForBuild(BuildingDefn building, AI_NodeState target, AIWorldView view, StrategicAnalysis analysis, PersonalityWeights p)
    {
        // One-step "what does owning this enable?" lookup. Capped, no recursion, no search.
        // Only meaningful for buildings that change what we can DO from this node (workers,
        // resource throughput). For gatherers we already scored shortage above.
        if (!building.CanGenerateWorkers) return 0f;

        // Worker generator on a frontier site: this is the classic "build Barracks then
        // attack next turn" chain. Reward by the best attack target reachable in one hop.
        float bestEnemyValue = 0f;
        for (int k = 0; k < target.NumNeighbors; k++)
        {
            var nb = target.NeighborNodes[k];
            if (nb.OwnedBy == null || nb.OwnedBy == view.Player) continue;
            float v = ValueOfEnemyNode(nb);
            if (v > bestEnemyValue) bestEnemyValue = v;
        }
        return bestEnemyValue * p.Aggression;
    }

    // ============================================================================
    // Upgrade
    // ============================================================================
    public static void ScoreUpgrade(AICandidate c, AIWorldView view, StrategicAnalysis analysis, PersonalityWeights p)
    {
        var node = c.SourceNode;
        if (node == null || node.BuildingDefn == null) { c.Score = 0f; return; }
        if (!node.BuildingDefn.CanBeUpgraded) { c.Score = 0f; return; }
        if (node.NumWorkers < node.MaxWorkers) { c.Score = 0f; return; }

        float baseValue = UpgradeBase + node.BuildingLevel * 1.5f;

        // Value by what the upgraded building does.
        if (node.CanGenerateWorkers) baseValue += 5f;
        if (node.CanGoGatherResources) baseValue += 3f;

        // Surplus-workers bonus. The game decays NumWorkers by 1 per turn while NumWorkers >
        // MaxWorkers (see TownData.Debug_WorldTurn), so an over-cap node is BLEEDING economy.
        // Each unit of surplus is "almost wasted"; doubling MaxWorkers via upgrade rescues it.
        // Cap so a once-grown 150/10 node doesn't single-handedly dominate scoring forever.
        int surplus = node.NumWorkers - node.MaxWorkers;
        if (surplus > 0)
            baseValue += Mathf.Min(15f, surplus * 0.5f);

        // Forward-lookahead: if upgrading would let us hit a previously-too-strong neighbor.
        float forward = ForwardLookupForUpgrade(node, view, p);

        // Risk: don't upgrade a frontier node if doing so leaves us under enemy pressure
        // (NumWorkers halves on upgrade). Two-tier check:
        //   - If post-upgrade workers would fall meaningfully BELOW pressure, hard-veto.
        //   - Otherwise apply a graduated risk penalty proportional to deficit + Caution.
        int postUpgradeWorkers = node.NumWorkers / 2;
        int pressure = analysis.FrontierPressure[node.Index];
        if (pressure > 0 && postUpgradeWorkers < pressure)
        {
            // Veto outright when the gap is large enough to lose the node.
            if (pressure - postUpgradeWorkers >= 3 && p.Caution > 0.5f)
            {
                c.Score = 0f;
                c.Reason = $"upgrade #{node.NodeId} VETOED (post={postUpgradeWorkers} < pressure={pressure})";
                return;
            }
        }
        float risk = 0f;
        if (postUpgradeWorkers < pressure)
            risk = (pressure - postUpgradeWorkers) * RiskPerOverSentWorker * p.Caution * 2f;

        // Upgrade is the canonical "Tempo" family. The personality multiplier is deliberately
        // muted (default ~0.8x) so an at-cap node doesn't auto-win every tick over more
        // pressing tactical needs (defense, attack on a weak frontier).
        float personality = 0.4f + 0.4f * p.Tempo;

        c.Score = (baseValue + forward * ForwardLookaheadDiscount) * personality - risk;
        c.Reason = $"upgrade #{node.NodeId} base={baseValue:F1} fwd={forward:F1} tempo={p.Tempo:F2} risk={risk:F1}";
    }

    static float ForwardLookupForUpgrade(AI_NodeState node, AIWorldView view, PersonalityWeights p)
    {
        // Look at enemy neighbors that this node currently can't beat. If post-upgrade
        // maxWorkers would let us field a force that beats one of them, that's real value.
        int postUpgradeMax = node.MaxWorkers * 2; // upgrade doubles MaxWorkers
        float bestUnlock = 0f;
        for (int k = 0; k < node.NumNeighbors; k++)
        {
            var nb = node.NeighborNodes[k];
            if (nb.OwnedBy == null || nb.OwnedBy == view.Player) continue;
            // We can't beat them now but could if we had more workers.
            if (node.NumWorkers <= nb.NumWorkers && postUpgradeMax > nb.NumWorkers + 2)
            {
                float v = ValueOfEnemyNode(nb);
                if (v > bestUnlock) bestUnlock = v;
            }
        }
        return bestUnlock * p.Aggression;
    }

    // ============================================================================
    // Shared risk helper
    // ============================================================================
    static float ComputeRiskFromSources(AICandidate c, StrategicAnalysis analysis, PersonalityWeights p)
    {
        float risk = 0f;
        if (c.Sources.Count > 0)
        {
            foreach (var kv in c.Sources)
            {
                int safe = analysis.SafeToSendFrom[kv.Key.Index];
                int over = kv.Value - safe;
                if (over > 0) risk += over * RiskPerOverSentWorker * p.Caution;
            }
        }
        else if (c.SourceNode != null)
        {
            int safe = analysis.SafeToSendFrom[c.SourceNode.Index];
            int over = c.Count - safe;
            if (over > 0) risk = over * RiskPerOverSentWorker * p.Caution;
        }
        return risk;
    }
}
