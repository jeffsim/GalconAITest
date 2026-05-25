using System.Collections.Generic;
using System;
using UnityEngine;

public enum AIHeuristicActionType
{
    Upgrade,
    Buttress,
    Build,
    Capture,
    Attack,
}

/// <summary>
/// Heuristic scoring and territory analysis used by the recursive search.
/// Returns bonuses scaled to be meaningful alongside EvaluateScore() results (~0-3 range).
/// </summary>
public static class AI_ActionHeuristics
{
    const float HeuristicScoreScale = 3f;

    const float nearbyEnemiesScalingFactor = 1f;
    const float buildingResourceScalingFactor = 5f;
    const float buildingStrategicScalingFactor = 8f;
    const float territoryEdgeScalingFactor = 10f;
    const float insufficientWorkersScalingFactor = 10f;

    const float upgradeNodeMinScore = 10f;
    const float upgradeNodeMaxScore = 40f;
    // buildBuildingMinScore was 20 back when GetBuildHeuristic's normalization used the buggy
    // (clamped - 10) / 30 form, which produced a floor of ~1.0 for any raw <= 20. Now that the
    // normalization uses the true min/max, 20 was clamping out every "ordinary" build candidate
    // (Camp/Outpost rawValue 8, defensive interior 8, defensive edge 12) to heuristic 0 -- so
    // the AI literally stopped building anything except high-shortage gatherers. 5 puts the
    // typical base values comfortably above the floor without saturating high-shortage cases.
    const float buildBuildingMinScore = 5f;
    const float buildBuildingMaxScore = 40f;
    const float buttressNodeMinScore = 20f;
    const float buttressNodeMaxScore = 40f;
    const float attackNodeMinScore = 20f;
    const float attackNodeMaxScore = 40f;

    // Fallback "I always want some" floor when ResourceDemand has no entry for a GoodType
    // (e.g. early game when no building requires that resource). Keeps gatherers from going
    // to zero appeal in pathological maps.
    const int resourceDemandFloor = 5;

    // Each unit of resource shortage on an enemy node we can capture adds this much raw
    // attack score. With min/max attack score = 20/40, a shortage of ~2 already maxes out
    // the bonus, so this is intentionally aggressive on critical-resource captures.
    const float attackResourceShortageScalingFactor = 5f;

    // Chokepoint multiplier scaling: a node with ChokepointScore = 1.0 has its capture /
    // attack / buttress heuristic multiplied by (1 + scale). Tuned so the structural value
    // of holding a map-level chokepoint is meaningful but doesn't completely override the
    // dynamic situation -- a non-chokepoint that's actually under attack should still beat a
    // chokepoint that's safe. Scale=1.5 means a peak chokepoint is worth 2.5x an ordinary
    // node of identical local conditions.
    public const float ChokepointCaptureScale = 1.5f;
    public const float ChokepointDefenseScale = 1.5f;
    public const float ChokepointAttackScale = 1.5f;
    public const float ChokepointGoalScale = 1.5f;

    // Returns 1 + score * scale * personalityWeight, floor 1. The personality weight makes
    // chokepoint amplification reflect HOW MUCH the AI cares about the kind of move it's
    // about to make. Defensive AIs amplify defensive choke moves (DefendFrontier, Buttress)
    // by DefenseWeight; aggressive AIs amplify offensive choke moves (Capture/Attack/
    // Construct-on-neutral) by AggressivenessWeight. Without this, a pacifist (agg=0)
    // saw the same 2.5x pull toward a distant chokepoint as an aggressor (agg=2), and a
    // defensive AI on a chokepoint frontier got the same defensive amp as a balanced one --
    // the multiplier was structurally correct ("chokes matter more") but personality-blind.
    //
    // personalityWeight defaults to 1 for backward compatibility with callers that don't
    // (or shouldn't) inject a personality term. Pass 0 to entirely suppress the choke
    // amplification for an AI that has no interest in that mode of play.
    public static float GetChokepointMultiplier(AI_NodeState node, float scale, float personalityWeight = 1f)
    {
        if (node == null) return 1f;
        float score = node.ChokepointScore;
        if (score <= 0f) return 1f;
        float weight = Mathf.Max(0f, personalityWeight);
        return 1f + score * scale * weight;
    }

    public static float GetPersonalityMultiplier(PlayerData player, AIHeuristicActionType actionType)
    {
        var aiDefn = player.AIDefn;
        if (aiDefn == null) return 1f;

        return actionType switch
        {
            AIHeuristicActionType.Upgrade => aiDefn.EconomicExpansionWeight,
            AIHeuristicActionType.Build => aiDefn.EconomicExpansionWeight,
            AIHeuristicActionType.Capture => aiDefn.TerritoryExpansionWeight,
            AIHeuristicActionType.Buttress => aiDefn.DefenseWeight,
            AIHeuristicActionType.Attack => aiDefn.AggressivenessWeight,
            _ => 1f,
        };
    }

    // How willing this AI is to upgrade a threatened node without first overloading it.
    // 1.0 = aggressive (upgrades immediately, accepts halved garrison under fire)
    // 0.0 = cautious (demands full overload buffer before upgrading under threat)
    // Derived from the ratio of AggressivenessWeight to DefenseWeight.
    public static float GetUpgradeRiskTolerance(PlayerData player)
    {
        var aiDefn = player.AIDefn;
        if (aiDefn == null) return 0.5f;
        float sum = aiDefn.AggressivenessWeight + aiDefn.DefenseWeight;
        if (sum <= 0f) return 0.5f;
        return Mathf.Clamp01(aiDefn.AggressivenessWeight / sum);
    }

    public static float ApplyHeuristicAndPersonality(float simulationScore, float heuristicBonus, PlayerData player, AIHeuristicActionType actionType)
    {
        // Personality now scales the full final score, not just the heuristic bonus. With the
        // earlier "bonus only" formula, a sufficiently good simulationScore (e.g. capturing a
        // resource-rich enemy node) would still dominate even when AggressivenessWeight = 0,
        // because simulationScore was unaffected by personality. Applying it to the full score
        // makes weights of 0/2 actually disable/double the action's appeal as the test harness
        // expects.
        float personality = GetPersonalityMultiplier(player, actionType);
        return (simulationScore + heuristicBonus) * personality;
    }

    // Goal-driven resource demand: every active strategic goal expands into the buildings
    // that would help fulfill it; each such building's construction requirements contribute
    // to demand, scaled by the goal's urgency (Value / HorizonTurns).
    //
    // This replaces the previous "sum every buildable building's reqs unconditionally"
    // formulation, which couldn't differentiate between an aggressive AI (wants Barracks ->
    // wants wood) and a peaceful one (doesn't care about Barracks). Now the same map can
    // produce different demand vectors for different personalities because the goal list
    // they generate is different.
    //
    // Capture goals collectively share a single Barracks contribution (one Barracks unlocks
    // all of them) rather than multiplying demand by goal count. EconomicTier goals each
    // contribute their own building's reqs, since each building is a distinct outcome.
    public static void UpdateResourceDemand(
        AI_TownState town,
        BuildingDefn[] buildableBuildingDefns,
        int numBuildableBuildingDefns,
        List<AIGoal> activeGoals)
    {
        var demand = town.ResourceDemand;
        demand.Clear();

        float totalCaptureUrgency = 0f;
        for (int i = 0; i < activeGoals.Count; i++)
        {
            var goal = activeGoals[i];
            float urgency = goal.Value / System.Math.Max(1, goal.HorizonTurns);

            switch (goal.Type)
            {
                case AIGoalType.EconomicTier:
                    AddBuildingReqsToDemand(demand, goal.TargetBuilding, urgency);
                    break;

                case AIGoalType.CaptureNode:
                    // Aggregate; folded into a single Barracks contribution below.
                    totalCaptureUrgency += urgency;
                    break;

                case AIGoalType.DefendFrontier:
                    // v1: defense doesn't drive resource demand directly. Buttress (the
                    // recursive task) handles it via worker shuffling. v2 could add demand
                    // for defensive buildings.
                    break;

                case AIGoalType.MaintainStockpile:
                    AddStockpileToDemand(demand, town, goal);
                    break;
            }
        }

        if (totalCaptureUrgency > 0f)
        {
            var barracksDefn = FindBuildingDefnOfType(buildableBuildingDefns, numBuildableBuildingDefns, BuildingType.Barracks);
            if (barracksDefn != null)
                AddBuildingReqsToDemand(demand, barracksDefn, totalCaptureUrgency);
        }
    }

    static void AddStockpileToDemand(Dictionary<GoodType, int> demand, AI_TownState town, AIGoal goal)
    {
        if (goal.TargetGoodType == GoodType.Unset) return;

        float urgency = goal.Value / System.Math.Max(1, goal.HorizonTurns);
        int target = town.player.AIDefn != null ? town.player.AIDefn.GetTargetStockpile(goal.TargetGoodType) : 0;
        int have = town.PlayerTownInventory.TryGetValue(goal.TargetGoodType, out int v) ? v : 0;
        int deficit = target - have;
        if (deficit <= 0) return;

        // Demand at least the stockpile target, scaled up by urgency so critical shortfalls
        // push harder toward gatherers and worker staffing.
        int contribution = Math.Max(deficit, (int)(target * urgency * 0.1f));
        demand.TryGetValue(goal.TargetGoodType, out int prev);
        demand[goal.TargetGoodType] = prev + contribution;
    }

    static void AddBuildingReqsToDemand(Dictionary<GoodType, int> demand, BuildingDefn bd, float urgency)
    {
        if (bd == null || bd.ConstructionRequirements == null) return;

        for (int r = 0; r < bd.ConstructionRequirements.Count; r++)
        {
            var req = bd.ConstructionRequirements[r];
            var goodType = req.Good.GoodType;

            // Always at least the literal cost (so even minimum-urgency goals still register
            // their building's construction footprint), scaled up linearly with urgency.
            // The 0.1 factor keeps urgency~10 producing roughly 1x req.Amount of extra demand.
            int contribution = (int)(req.Amount * urgency * 0.1f);
            if (contribution < req.Amount) contribution = req.Amount;

            demand.TryGetValue(goodType, out int prev);
            demand[goodType] = prev + contribution;
        }
    }

    static BuildingDefn FindBuildingDefnOfType(BuildingDefn[] defns, int count, BuildingType type)
    {
        for (int i = 0; i < count; i++)
            if (defns[i].BuildingType == type) return defns[i];
        return null;
    }

    // Shortage of a resource = max(0, demand - currentlyOwned). Falls back to a small floor
    // when nothing in the buildable catalog needs the resource so gatherers still register
    // some appeal.
    public static int GetResourceShortage(AI_TownState town, GoodType goodType)
    {
        if (goodType == GoodType.Unset) return 0;
        int wanted = town.ResourceDemand.TryGetValue(goodType, out int d) ? d : resourceDemandFloor;
        if (wanted < resourceDemandFloor) wanted = resourceDemandFloor;

        // Also treat PlayerAIDefn stockpile targets as implicit demand so the AI keeps
        // producing even when no construction goals are active.
        int stockpileTarget = town.player.AIDefn != null ? town.player.AIDefn.GetTargetStockpile(goodType) : 0;
        if (stockpileTarget > wanted) wanted = stockpileTarget;

        int owned = town.PlayerTownInventory.TryGetValue(goodType, out int o) ? o : 0;
        int shortage = wanted - owned;
        return shortage < 0 ? 0 : shortage;
    }

    // How many workers this node should have to meet current resource demand. Scales up
    // with shortage so 1/10 workers in a forest is not "good enough" when wood is needed.
    public static int GetDesiredWorkersForResourceNode(AI_TownState town, AI_NodeState node)
    {
        if (!node.CanBeGatheredFrom && !node.CanGoGatherResources) return 0;
        if (node.MaxWorkers <= 0) return 0;

        GoodType resource = node.CanBeGatheredFrom
            ? node.ResourceGatheredFromThisNode
            : node.ResourceThisNodeCanGoGather;
        int shortage = GetResourceShortage(town, resource);
        if (shortage <= 0) return Math.Min(1, node.NumWorkers);

        int wanted = town.ResourceDemand.TryGetValue(resource, out int d) ? d : resourceDemandFloor;
        float fillRatio = Mathf.Clamp01((float)shortage / Mathf.Max(1, wanted));
        int desired = Math.Max(1, (int)Math.Ceiling(node.MaxWorkers * fillRatio));
        return Math.Min(desired, node.MaxWorkers);
    }

    public static bool NodeProducesResource(AI_NodeState node, GoodType goodType)
    {
        if (goodType == GoodType.Unset) return false;
        if (node.CanBeGatheredFrom && node.ResourceGatheredFromThisNode == goodType) return true;
        if (node.CanGoGatherResources && node.ResourceThisNodeCanGoGather == goodType) return true;
        return false;
    }

    public static bool NeutralNeighborTouchesEnemy(AI_NodeState neutral, PlayerData player)
    {
        if (neutral.OwnedBy != null) return false;
        var neighbors = neutral.NeighborNodes;
        for (int i = 0; i < neighbors.Count; i++)
        {
            var nb = neighbors[i];
            if (nb.OwnedBy != null && nb.OwnedBy != player)
                return true;
        }
        return false;
    }

    public static int GetFrontierPressure(AI_NodeState node) =>
        node.NumEnemiesInNeighborNodes + node.NumContestedNeutralWorkersNearby;

    // How many "virtual enemies" each unit of AttackHeat translates to. The buttress and
    // frontier heuristics treat the result as if it were live enemy force adjacent to the
    // node, so this controls how aggressively past attacks bias the AI toward reinforcing
    // a chokepoint when the current snapshot of enemy neighbors looks calm.
    const float attackHeatToPressureMultiplier = 1.5f;

    // AttackHeat level above which a defender node is considered "in emergency" by
    // GetButtressSourceNode even if effective pressure is otherwise low. Tuned so a single
    // recent attacker doesn't immediately trip emergency, but a couple in quick succession
    // does. Heat decays at AttackHeatDecayPerSecond (TownData) so the threshold falls off
    // naturally when attacks stop.
    public const float AttackHeatEmergencyThreshold = 2f;

    // AttackHeat level above which an owned node is treated as a chokepoint that should
    // retain workers for defense (used in offensive willing-to-send guard).
    public const float AttackHeatChokepointThreshold = 1f;

    // Effective frontier pressure: live snapshot pressure plus a heat-driven inflation so
    // chokepoint nodes that have been attacked repeatedly (but currently look "fine" because
    // the previous wave has been killed and the next wave is in transit) still register as
    // needing defense. Also adds 1:1 the count of in-flight hostile workers targeting this
    // node so the AI can prepare for telegraphed waves BEFORE they begin landing -- AttackHeat
    // alone is post-hoc and only fires after the first attacker resolves.
    public static int GetEffectiveFrontierPressure(AI_NodeState node)
    {
        int snapshot = GetFrontierPressure(node);
        int heatBonus = node.AttackHeat > 0f
            ? (int)System.Math.Ceiling(node.AttackHeat * attackHeatToPressureMultiplier)
            : 0;
        return snapshot + heatBonus + node.IncomingHostileWorkers;
    }

    public static void UpdateTerritoryDetails(AI_TownState town, PlayerData player)
    {
        var nodes = town.Nodes;
        int numNodes = nodes.Length;
        for (int i = 0; i < numNodes; i++)
        {
            var node = nodes[i];
            node.IsOnTerritoryEdge = false;
            node.NumEnemiesInNeighborNodes = 0;
            node.NumContestedNeutralWorkersNearby = 0;
        }

        for (int i = 0; i < numNodes; i++)
        {
            var node = nodes[i];
            if (node.OwnedBy != player) continue;

            var neighbors = node.NeighborNodes;
            for (int n = 0; n < neighbors.Count; n++)
            {
                var nn = neighbors[n];
                if (nn.OwnedBy != player)
                {
                    if (nn.OwnedBy != null)
                        node.NumEnemiesInNeighborNodes += nn.NumWorkers;
                    else if (NeutralNeighborTouchesEnemy(nn, player))
                        node.NumContestedNeutralWorkersNearby += nn.NumWorkers;
                    node.IsOnTerritoryEdge = true;
                }
            }
        }

        // Neutral nodes adjacent to our territory are expansion-frontier targets for build heuristics
        for (int i = 0; i < numNodes; i++)
        {
            var node = nodes[i];
            if (node.OwnedBy != null) continue;

            var neighbors = node.NeighborNodes;
            for (int n = 0; n < neighbors.Count; n++)
            {
                if (neighbors[n].OwnedBy == player)
                {
                    node.IsOnTerritoryEdge = true;
                    break;
                }
            }
        }
    }

    public static int GetWorkersWillingToSend(AI_NodeState node, int minWorkersInNodeBeforeConsideringSendingAnyOut)
    {
        if (node.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut) return 0;

        // Predicted-threat reserve: don't drain workers off a node whose own defense will
        // need them. Uses immediate-enemy pressure (snapshot enemies + heat + IncomingHostile)
        // 1:1, EXCLUDING contested-neutral inflation -- the latter represents potential
        // threats whose right answer is "go capture them" rather than "reserve workers
        // against them and end up doing nothing". This reserve also subsumes the old
        // AttackHeat chokepoint-drain guard: a hot chokepoint already has heat folded
        // into immediate pressure, so its reservation grows automatically and a hot node
        // with no surplus over pressure naturally returns 0 here.
        int reserveForDefense = GetReservedForImmediateDefense(node, node.OwnedBy);

        // 75% cap-fill check: "don't drain a node that's still building up its reserves".
        // Only meaningful for nodes that actually have something defensive to reserve FOR.
        // For interior nodes with zero immediate enemy pressure, their workers ARE the
        // reserves -- there's no future garrison to build toward -- and gating on cap-fill
        // just leaves them sitting idle. Bug seen: Blue's interior #0 at 16/160 with zero
        // immediate pressure had 10 spare workers but the 75% check blocked it from
        // contributing to any offensive plan, locking Blue out of attacks even when its
        // frontier sources alone were just short of the target force.
        if (reserveForDefense > 0 && node.NumWorkers < node.MaxWorkers * 3f / 4f) return 0;

        int sendable = node.NumWorkers - reserveForDefense;
        if (sendable <= 0) return 0;

        // Game rule: source must retain at least 1 worker (NodeData.GetMaxSendableWorkers);
        // keep heuristic consistent with what simulation / real-game executors will actually do.
        int half = node.NumWorkers / 2;
        int cap = Math.Min(half, NodeData.GetMaxSendableWorkers(node.NumWorkers));
        return Math.Min(cap, sendable);
    }

    // When a frontier node is under real pressure, allow sources below 75% capacity to pitch in.
    // The destAttackHeat parameter lets the caller signal "destination is a chokepoint that's
    // been getting attacked even if the current snapshot looks quiet". In that case we drop
    // the source-capacity threshold from 75% to 50% so a mid-staffed interior node can still
    // reinforce a hot frontier. Emergency cases (effective pressure exceeds the destination's
    // garrison) remain fully relaxed: source can give down to its minimum garrison.
    //
    // destNeedsOverkill: caller has determined the destination's personality-aware desired
    // garrison exceeds what visible pressure alone would require (i.e., a defensive
    // personality is pulling for an overstack). Treated like destAttackHeat for source
    // threshold purposes -- a def=2 AI's interior nodes should be willing to ship below
    // 75% to help a chokepoint that the personality says is undergarrisoned, even if the
    // snapshot pressure currently looks "fine" 1:1.
    public static int GetWorkersWillingToSendForDefense(
        AI_NodeState node,
        int minWorkersInNodeBeforeConsideringSendingAnyOut,
        bool emergency,
        float destAttackHeat = 0f,
        bool destNeedsOverkill = false)
    {
        if (node.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut) return 0;
        // Game rule: source must retain at least 1 worker (NodeData.GetMaxSendableWorkers).
        // Clamp at the end so neither emergency nor normal paths can over-promise.
        int maxSendable = NodeData.GetMaxSendableWorkers(node.NumWorkers);
        if (emergency)
        {
            // Reserve the source's OWN immediate-enemy defensive need even in emergency.
            // The earlier "drain to the floor" rule let a hot frontier source ship its
            // garrison away to help another frontier just because the destination was
            // tagged emergency. The reservation uses GetReservedForImmediateDefense
            // (real enemies + heat + incoming hostile -- no contested-neutral inflation),
            // so an at-cap edge node next to a contested neutral can still ship workers to
            // a true emergency or to capture the contested neutral itself, instead of
            // hoarding 100% of its cap against a worker count on a node nobody owns.
            int reserveForDefense = Math.Max(
                minWorkersInNodeBeforeConsideringSendingAnyOut,
                GetReservedForImmediateDefense(node, node.OwnedBy));
            int sendable = node.NumWorkers - reserveForDefense;
            if (sendable <= 0) return 0;
            return Math.Min(maxSendable, sendable);
        }

        // Destination has chokepoint-level heat (or personality-driven overkill demand) but
        // isn't in emergency: relax source threshold to 50% so mid-staffed neighbors can pitch
        // in without waiting to hit 75%. Source-side cap-fill check only applies to nodes
        // that actually have a defensive baseline to build toward; interior nodes with zero
        // immediate enemy pressure have no defensive reservation, so any cap-fill threshold
        // would just leave them sitting idle while their workers could be reinforcing a
        // hot frontier (same logic as GetWorkersWillingToSend).
        bool relax = destAttackHeat >= AttackHeatChokepointThreshold || destNeedsOverkill;
        float capacityThreshold = relax ? 0.5f : 0.75f;
        bool sourceHasImmediatePressure = GetImmediateEnemyPressure(node) > 0;
        if (sourceHasImmediatePressure && node.NumWorkers < node.MaxWorkers * capacityThreshold) return 0;

        // Also reserve the source's own immediate-enemy defensive need in non-emergency.
        // The old non-emergency path applied ONLY the cap-fill check and then sent half the
        // garrison, which drained hot sources past their own pressure. With the cap-fill
        // gate at 75% / 50%, a pressured #27 at 590/640 vs 299 visible pressure would pass
        // the gate (590 >= 320) and ship 295 -- leaving 295 vs 299 pressure, i.e., the
        // source is now itself outmatched. Apply the same 1:1 raw-pressure reservation
        // used by GetWorkersWillingToSend so non-emergency drains stop at the source's
        // own visible defensive floor; emergency mode (above) explicitly bypasses this
        // when the destination is in true distress.
        int nonEmergencyReserve = GetReservedForImmediateDefense(node, node.OwnedBy);
        int nonEmergencySendable = node.NumWorkers - nonEmergencyReserve;
        if (nonEmergencySendable <= 0) return 0;
        return Math.Min(maxSendable, Math.Min(node.NumWorkers / 2, nonEmergencySendable));
    }

    // True when the destination's personality-aware total defensive deficit materially
    // exceeds what raw visible pressure alone would demand. Used by buttress tasks to signal
    // sources to relax their capacity threshold (see GetWorkersWillingToSendForDefense).
    // Uses GetTotalDefensiveDeficit so the upgrade-overload path (the user's "send workers
    // here so I can upgrade" intent) also flips the source threshold -- without this, a
    // cautious AI staring at an under-tier choke would compute a real overload deficit but
    // mid-staffed sources would still be gated to 75% capacity and ship nothing.
    public static bool DestNeedsPersonalityOverkill(AI_NodeState toNode, PlayerData player)
    {
        if (player == null || player.AIDefn == null) return false;
        int baseline = Math.Max(0, GetEffectiveFrontierPressure(toNode) - toNode.EffectiveDefenseGarrison);
        int personalityAware = GetTotalDefensiveDeficit(toNode, player);
        return personalityAware > baseline + 1;
    }

    public static int GetTargetForceWithOverkill(int threat, float overkillMultiplier)
    {
        if (threat <= 0) return 1;
        return Math.Max(1, (int)Math.Ceiling(threat * overkillMultiplier));
    }

    // Minimum workers to commit in one capture dispatch. Prevents drip-feeding 1 worker per
    // AI decision tick on safe neutrals; capped by willing so a single source still sends
    // the largest batch it can (e.g. 5) rather than failing until multi-source can muster 10.
    public const int MinCaptureWaveSize = 10;

    // Minimum batch size for a buttress dispatch from a NON-OVERFLOWING source. Worker
    // generation on an interior camp ticks NumWorkers from N to N+1 every gen tick; without
    // this guard the AI would immediately ship that 1 spare worker every tick, producing a
    // visible "drip" of 1-worker support waves that never let the source accumulate to send
    // anything substantive. Sources that are actually OVER cap are exempt -- their excess
    // would decay one per tick anyway, so dispatching even 1 to a real deficit is "free".
    // Sized small enough that mid-sized interior camps (cap 20-40) can still contribute at
    // moderate fill, large enough that 1-2 worker drips are firmly blocked.
    public const int MinButtressWaveSize = 5;

    // Realtime: a neutral already has an in-flight capture wave from this player.
    public static bool IsCaptureAlreadyCommitted(AI_NodeState toNode, PlayerData player) =>
        toNode?.RealNode != null
        && toNode.RealNode.PendingCaptureBy == player
        && toNode.RealNode.OwnedBy == null;

    public static int GetCaptureDispatchSize(int threat, float overkillMultiplier, int willing)
    {
        int threatBased = GetTargetForceWithOverkill(threat, overkillMultiplier);
        return Math.Max(threatBased, Math.Min(MinCaptureWaveSize, willing));
    }

    // Multi-source flanking on a shared gateway neutral: garrison-only sizing plus a small
    // overkill buffer is enough; full adjacent-enemy stacking made capture unplanable.
    public static int GetGatewayCaptureTargetForce(AI_NodeState target, PlayerData player, float overkillMultiplier, int numSources)
    {
        int threat = GetGatewayCaptureThreat(target, player);
        int targetForce = GetTargetForceWithOverkill(threat, overkillMultiplier);
        if (numSources >= 2 && NeutralNeighborTouchesEnemy(target, player))
        {
            int relaxed = threat + Math.Max(1, (int)Math.Ceiling(overkillMultiplier));
            targetForce = Math.Min(targetForce, relaxed);
        }
        return Math.Max(targetForce, MinCaptureWaveSize);
    }

    public static int GetAdjacentEnemyThreat(AI_NodeState node, PlayerData player)
    {
        int threat = 0;
        var neighbors = node.NeighborNodes;
        for (int n = 0; n < neighbors.Count; n++)
        {
            var neighbor = neighbors[n];
            if (neighbor.OwnedBy != null && neighbor.OwnedBy != player)
                threat += neighbor.NumWorkers;
        }
        return threat;
    }

    public static int GetNeutralCaptureThreat(AI_NodeState toNode, PlayerData player)
    {
        int threat = toNode.OwnedBy == null ? toNode.NumWorkers : 0;
        return threat + GetAdjacentEnemyThreat(toNode, player);
    }

    // For a neutral gateway we share with an enemy (e.g. #11 between Red and Blue): sizing a
    // capture only needs to beat the garrison ON the node, not every enemy worker one hop away.
    // Counting adjacent enemies made Red need 29 workers when it only had 14 and gave up on
    // capture while buttress-shuffling forever.
    public static int GetGatewayCaptureThreat(AI_NodeState toNode, PlayerData player)
    {
        if (toNode.OwnedBy != null) return GetNeutralCaptureThreat(toNode, player);
        if (!NeutralNeighborTouchesEnemy(toNode, player)) return GetNeutralCaptureThreat(toNode, player);
        foreach (var nb in toNode.NeighborNodes)
            if (nb.OwnedBy == player) return Math.Max(0, toNode.NumWorkers);
        return GetNeutralCaptureThreat(toNode, player);
    }

    // Workers this owned node should hold given current frontier pressure. Uses effective
    // pressure (snapshot + heat) so chokepoints under sustained attack are sized for what
    // they've BEEN absorbing, not just what's visible at the current instant.
    //
    // The personality-aware scaling makes "desired" reflect not just visible enemy force
    // but also the AI's stated defensive posture and the structural value of the node.
    // Without this, a DefenseWeight=2 / Aggressiveness=0 AI considered a frontier "fully
    // staffed" the moment the garrison matched visible pressure 1:1, and never overstacked
    // chokepoints despite huge interior reserves. Symmetry with the goal-value formula in
    // EnumerateDefendGoals which also scales by `defense * chokepointMult`: now the actual
    // worker-movement layer agrees with the goal/demand layer about how much it cares.
    //
    // defenseOverkill = max(1, DefenseWeight): a pacifist (def=0) still wants to cover
    // visible pressure 1:1; a defender (def=2) wants 2x; values <1 don't drop coverage
    // because letting a frontier sit understaffed is never the intended consequence of
    // "I don't prioritize defense" -- the trade-off is expressed via the buttress action's
    // personality multiplier, not by intentionally under-defending.
    public static int GetDesiredFrontierWorkers(AI_NodeState node, PlayerData player = null)
    {
        int pressure = GetEffectiveFrontierPressure(node);
        if (pressure <= 0) return 0;

        float defWeight = player != null && player.AIDefn != null ? player.AIDefn.DefenseWeight : 1f;
        float defenseOverkill = Math.Max(1f, defWeight);
        // Pass DefenseWeight to the chokepoint mult so a pacifist (def=0) doesn't get an
        // extra defensive choke amp on top of the baseline overkill of 1.0 -- their desired
        // garrison stays at raw pressure regardless of how structurally important the choke
        // looks. A defensive AI compounds the amp (defenseOverkill * defense-scaled-choke)
        // for the same reason its goal value uses both factors.
        float chokeMult = GetChokepointMultiplier(node, ChokepointDefenseScale, defWeight);
        float scale = defenseOverkill * chokeMult;

        int desired = (int)Math.Ceiling(pressure * scale);
        return Math.Min(node.MaxWorkers, Math.Max(1, desired));
    }

    // Destination-side deficit: include my in-flight friendly reinforcements so we don't
    // dispatch ANOTHER wave when help is already on the way. EffectiveDefenseGarrison is
    // NumWorkers (physical) + IncomingFriendlyWorkers (pre-existing in-flight).
    public static int GetFrontierWorkerDeficit(AI_NodeState node, PlayerData player = null) =>
        Math.Max(0, GetDesiredFrontierWorkers(node, player) - node.EffectiveDefenseGarrison);

    // Pressure that a source node must reserve workers AGAINST when deciding how many it
    // can spare for offense or buttress. Distinct from GetEffectiveFrontierPressure --
    // which includes NumContestedNeutralWorkersNearby -- because a contested neutral
    // represents a POTENTIAL threat whose resolution is "capture the neutral" or "ship
    // workers to a more pressing emergency", not "stay home and reserve workers
    // indefinitely against an attack that hasn't materialized". Counting contested
    // neutrals in source reservation locks up at-cap edge nodes that should be free to
    // act (#10 at 80/80 next to contested #0 with 64 workers ended up with desired=80,
    // reservable=80, sendable=0 -- it could neither help buttress its own #27 nor
    // capture #0, even though either move would address the situation).
    public static int GetImmediateEnemyPressure(AI_NodeState node)
    {
        int heatBonus = node.AttackHeat > 0f
            ? (int)Math.Ceiling(node.AttackHeat * attackHeatToPressureMultiplier)
            : 0;
        return node.NumEnemiesInNeighborNodes + heatBonus + node.IncomingHostileWorkers;
    }

    // Source-side reservation sizing: how many workers a node must keep on hand against
    // its OWN immediate enemy pressure before any can be sent elsewhere. Uses raw 1:1
    // visible pressure (immediate enemies + heat + incoming hostile) -- NO personality,
    // overkill, or chokepoint scaling. Those scalings live in GetDesiredFrontierWorkers
    // and express how many workers the AI *wants* on this node (for goal value, deficit
    // sizing, and buttress demand). Applying them to source RESERVATION as well locks up
    // high-def AIs entirely: a def=2 chokepoint with cap 320 ends up reserving min(320,
    // pressure*8) == 320 against any pressure >= 40, and never sends anything anywhere
    // even when its garrison massively exceeds the visible threat. Bug seen: Blue at #27
    // had 590/640 vs pressure 299 -- 291 spare workers -- but reservation was 640 and the
    // node sent nothing, leaving Blue paralyzed with 1199 total workers and zero attacks.
    // Reservation here is the bare-minimum "don't leave the node weaker than the current
    // attackers"; the "I want more" preference is expressed via goal/deficit, not by
    // hoarding source workers indefinitely.
    public static int GetReservedForImmediateDefense(AI_NodeState node, PlayerData player)
    {
        int pressure = GetImmediateEnemyPressure(node);
        if (pressure <= 0) return 0;
        return Math.Min(node.MaxWorkers, pressure);
    }

    public static bool NeedsFrontierButtress(AI_NodeState node, PlayerData player = null)
    {
        const int minDeficit = 2;
        return GetFrontierWorkerDeficit(node, player) >= minDeficit;
    }

    // Combined defensive worker need: max of frontier (visible pressure + personality scaling)
    // and upgrade-overload (need pre-upgrade headroom so the post-upgrade halving survives
    // pressure). This is the number the buttress tasks already merge by hand at dispatch
    // time; exposing it as one function lets the GOAL value and the source-threshold relax
    // signal agree with the action layer about how undersupplied a frontier really is.
    //
    // For a high-defense AI on a low-tier choke under heavy pressure, the frontier deficit
    // alone is 0 (it clamps at MaxWorkers) while the upgrade-overload deficit can be huge --
    // and the upgrade-overload number is what the AI actually needs to act on the user's
    // strategic intent ("upgrade so it can hold more workers"). Without unifying these,
    // EnumerateDefendGoals reports "deficit 0" on a node the buttress task is desperately
    // trying to reinforce, and the goal value stays at the floor (~4) while CaptureNode
    // goals dominate the urgency-weighted demand vector.
    //
    // Resource-staffing and IsUnderstaffedFrontier are intentionally excluded -- the former
    // requires AI_TownState (not always available at call sites) and isn't a defensive
    // signal in the personality sense, and the latter is fully subsumed by frontier scaling
    // for nodes large enough to trip it.
    public static int GetTotalDefensiveDeficit(AI_NodeState node, PlayerData player)
    {
        int deficit = GetFrontierWorkerDeficit(node, player);
        float riskTolerance = GetUpgradeRiskTolerance(player);
        if (NeedsUpgradeOverloadButtress(node, riskTolerance))
        {
            int overloadDesired = GetDesiredOverloadForUpgrade(node, riskTolerance);
            int overloadDeficit = overloadDesired - node.EffectiveDefenseGarrison;
            if (overloadDeficit > deficit) deficit = overloadDeficit;
        }
        return Math.Max(0, deficit);
    }

    // Destination-side capacity check: incoming friendly arrivals count toward "staffed".
    public static bool IsUnderstaffedFrontier(AI_NodeState node) =>
        node.IsOnTerritoryEdge
        && node.MaxWorkers > 0
        && node.EffectiveDefenseGarrison < node.MaxWorkers * understaffedFrontierThreshold;

    // Size a neutral capture: 1 worker on safe frontiers; enough to clear unowned garrison on
    // the target and hold against adjacent enemy garrisons on contested ones.
    public static bool TryGetCaptureWorkersToSend(AI_NodeState fromNode, AI_NodeState toNode, PlayerData player, int minWorkersInNodeBeforeConsideringSendingAnyOut, float overkillMultiplier, out int numToSend)
    {
        numToSend = 0;
        if (fromNode.NumWorkers < minWorkersInNodeBeforeConsideringSendingAnyOut) return false;

        int willing = GetWorkersWillingToSend(fromNode, minWorkersInNodeBeforeConsideringSendingAnyOut);
        if (willing <= 0) return false;

        int threat = GetNeutralCaptureThreat(toNode, player);
        int target = GetCaptureDispatchSize(threat, overkillMultiplier, willing);
        if (willing < target) return false;

        numToSend = target;
        return true;
    }

    // Maximum raw bonus overcrowding alone can contribute. Sized so a node at 200% capacity
    // lands roughly in the middle of the heuristic band (raw ~26, h ~1.6) -- enough signal
    // to rank upgrade as an attractive option without saturating to 3.0 and thereby pruning
    // away every Construct/Build alternative via branch-and-bound. Only a genuine defensive
    // imperative below (overcrowded AND threatened) should drive the heuristic to max.
    const float overcrowdingMaxRawBonus = 15f;

    // Raw bonus per neighboring target that becomes plausibly attackable after upgrading.
    // Resource/worker-generating nodes are worth more; ordinary territory captures less.
    const float upgradeAttackPotentialBonusPerTarget = 6f;
    const float upgradeAttackPotentialResourceBonus = 4f;

    // Overkill multiplier used when judging "could I beat this target with the post-upgrade
    // accumulated workforce?". Conservative (1.5x defender) so the AI doesn't upgrade based
    // on barely-marginal plans.
    const float upgradeAttackPotentialOverkillMultiplier = 1.5f;

    public static float GetUpgradeHeuristic(AI_NodeState node, float riskTolerance = 0.5f)
    {
        if (node.NumWorkers < node.MaxWorkers) return 0f;

        // Effective pressure folds in snapshot enemy garrisons + AttackHeat + IncomingHostileWorkers
        // so a telegraphed wave that has already left its origin shows up here. The old code
        // (which only consulted node.NumEnemiesInNeighborNodes) went blind exactly when it
        // mattered most -- the moment the enemy launches, the snapshot drops to 0 and the
        // upgrade looked safe seconds before the wave landed on a halved garrison.
        int effectivePressure = GetEffectiveFrontierPressure(node);
        bool defensiveImperative = node.IsOnTerritoryEdge && effectivePressure > 0;

        // Hard veto: upgrading halves NumWorkers. If post-upgrade workers can't survive the
        // predicted hostile force, the upgrade is suicidal regardless of how overcrowded or
        // structurally valuable the node is. This is the predictive guardrail the recursive
        // search needs to refuse the upgrade BEFORE it commits and a wave that's already in
        // flight lands on the emptied node. Threshold scales with risk tolerance: cautious
        // (0.0) demands surviving the full predicted force 1:1, risky (1.0) accepts up to a
        // 2:1 disadvantage and trusts buttressing to backfill.
        //
        // The veto is suppressed when the upgrade is "defensively free" -- the halving
        // doesn't materially worsen our defensive picture. Two cases qualify:
        //   (a) post-upgrade workers still meet or exceed MaxWorkers: the over-cap stockpile
        //       absorbed the entire halving cost; our long-run "real" garrison is unchanged.
        //       (#0 sitting at 295/10 is the canonical example -- halving to 147 still way
        //       above MaxWorkers=10, and the 148 lost workers would have decayed at one per
        //       gen-tick regardless.)
        //   (b) we're already outmatched pre-upgrade AND the marginal additional shortfall
        //       from halving is no worse than the existing shortfall. If we couldn't have
        //       held the node either way at full force, the upgrade's halving doesn't
        //       transition us from "can hold" to "cannot hold". (#10 at 40/40 facing 94
        //       pressure qualifies: pre-shortfall 54, post-shortfall 74, marginal 20 < 54.)
        //       The "no worse than" gate is what filters out the genuinely-suicidal case
        //       of a small node BARELY outmatched (10/10 vs pressure 13: marginal 5 >
        //       pre 3, halving would more than double our exposure).
        int postUpgradeWorkers = node.NumWorkers / 2;
        float requiredRatio = 1f - 0.5f * Mathf.Clamp01(riskTolerance);
        float requiredForce = effectivePressure * requiredRatio;
        int preShortfall = Math.Max(0, effectivePressure - node.NumWorkers);
        int postShortfall = Math.Max(0, effectivePressure - postUpgradeWorkers);
        int marginalShortfall = postShortfall - preShortfall;
        bool postUpgradeStillAtCap = postUpgradeWorkers >= node.MaxWorkers;
        bool alreadyOutmatched = defensiveImperative && node.NumWorkers < requiredForce;
        bool marginalShortfallTolerable = alreadyOutmatched && marginalShortfall <= preShortfall;
        bool upgradeIsDefensivelyFree = postUpgradeStillAtCap || marginalShortfallTolerable;

        if (defensiveImperative && !upgradeIsDefensivelyFree)
        {
            if (postUpgradeWorkers < requiredForce)
                return 0f;
        }

        float rawValue = upgradeNodeMinScore * 1.1f;

        // Overcrowding signal: quadratic in PERCENTAGE excess (not absolute count) so the
        // heuristic doesn't depend on a building's MaxWorkers scale. The previous formulation
        // added Pow(numExcessiveWorkers, 2) on top, which let a 10-cap Camp with 10 excess
        // workers saturate the heuristic at 3.0 just from being overcrowded -- which is why
        // Green's first move was always Upgrade #9: peer Construct/Build alternatives were
        // then pruned by ShouldPruneByHeuristic before they could even simulate.
        int numExcessiveWorkers = node.NumWorkers - node.MaxWorkers;
        if (numExcessiveWorkers > 0)
        {
            float percentExcessive = (float)numExcessiveWorkers / node.MaxWorkers;
            rawValue += Mathf.Pow(percentExcessive, 2) * overcrowdingMaxRawBonus;
        }

        // Outnumbered penalty scaled by personality: a cautious AI (riskTolerance~0) gets the
        // full penalty when NOT under defensive imperative and a reduced penalty even when IS
        // under imperative (it wants to wait for overload). A risky AI (riskTolerance~1) gets
        // no penalty under imperative and reduced penalty otherwise — it's willing to upgrade
        // immediately and accept the halved garrison.
        //
        // Penalty input switched from the raw NumEnemiesInNeighborNodes snapshot to
        // effectivePressure so an in-flight hostile wave (which empties the neighbor snapshot
        // the moment it launches) still penalizes the upgrade. Without this, the AI literally
        // could not see the army that was already on its way.
        //
        // Suppressed when the upgrade is "defensively free" (see veto comment): applying
        // a huge quadratic outnumbered penalty there would re-veto the upgrade through the
        // back door even though we already established the veto shouldn't fire. The
        // overcrowding/defensive-imperative bonuses above want to push the heuristic high;
        // this penalty would otherwise cancel them out and the AI would still refuse to
        // upgrade a 295/10 node facing 641 enemy force, or a 40/40 chokepoint facing 94.
        int outnumberedBy = effectivePressure - node.NumWorkers;
        if (outnumberedBy > 0 && !upgradeIsDefensivelyFree)
        {
            float penaltyScale = defensiveImperative ? (1f - riskTolerance) : 1f;
            rawValue -= outnumberedBy * outnumberedBy * nearbyEnemiesScalingFactor * penaltyScale;
        }

        if (defensiveImperative)
            rawValue += 35;

        // Forward-looking "attack potential unlocked" signal: if the current cap can't muster
        // enough force to attack a nearby enemy/neutral target but the post-upgrade cap could,
        // that target becomes the strategic reason to upgrade. Without this signal the
        // recursive search rarely discovers upgrade-then-attack chains -- the search would
        // have to spend depth on upgrade (immediately halving workers) and then several more
        // plies accumulating before the payoff is visible to EvaluateScore.
        rawValue += GetUpgradeAttackPotentialBonus(node);

        float clampedRawValue = Mathf.Clamp(rawValue, upgradeNodeMinScore, upgradeNodeMaxScore);
        float normalizedValue = (clampedRawValue - upgradeNodeMinScore) / (upgradeNodeMaxScore - upgradeNodeMinScore);
        return normalizedValue * HeuristicScoreScale;
    }

    static float GetUpgradeAttackPotentialBonus(AI_NodeState node)
    {
        int currentCap = node.MaxWorkers;
        if (currentCap <= 0) return 0f;
        int postUpgradeCap = currentCap * 2;

        float total = 0f;
        var neighbors = node.NeighborNodes;
        for (int i = 0; i < neighbors.Count; i++)
        {
            var nb = neighbors[i];
            if (nb.OwnedBy == node.OwnedBy) continue;
            int defenders = nb.NumWorkers;
            if (defenders <= 0) continue;

            // Plausible attack force = ceil(half the cap), the same heuristic used by
            // GetWorkersWillingToSend (NumWorkers / 2 once at full cap).
            int forceNow = currentCap / 2;
            int forcePostUpgrade = postUpgradeCap / 2;
            int needed = (int)Math.Ceiling(defenders * upgradeAttackPotentialOverkillMultiplier);

            // Already attackable now -> upgrade is not the unlocker for this target.
            if (forceNow >= needed) continue;
            // Even post-upgrade can't beat it -> upgrade alone wouldn't unlock it.
            if (forcePostUpgrade < needed) continue;

            float perTarget = upgradeAttackPotentialBonusPerTarget;
            if (nb.IsResourceNode || nb.CanGenerateWorkers)
                perTarget += upgradeAttackPotentialResourceBonus;
            total += perTarget;
        }
        return total;
    }

    const float resourceStaffingScalingFactor = 8f;

    // A frontier node below this fill ratio is considered dangerously understaffed
    // regardless of whether adjacent enemies are currently visible (they may have
    // already departed as in-flight attackers).
    const float understaffedFrontierThreshold = 0.25f;

    public static float GetButtressHeuristic(AI_TownState town, AI_NodeState toNode)
    {
        float rawValue = 0f;
        float riskTolerance = GetUpgradeRiskTolerance(town.player);

        // Destination-side: garrison the dest WILL have once pre-existing in-flight friendly
        // workers arrive. Lying about this (using physical NumWorkers only) caused the AI to
        // dispatch redundant waves while help was already on the way.
        int destGarrison = toNode.EffectiveDefenseGarrison;

        int frontierDeficit = GetFrontierWorkerDeficit(toNode, town.player);
        bool understaffedFrontier = IsUnderstaffedFrontier(toNode);
        bool needsUpgradeOverload = NeedsUpgradeOverloadButtress(toNode, riskTolerance);

        if (frontierDeficit <= 0 && !understaffedFrontier && !NeedsResourceStaffingButtress(town, toNode) && !needsUpgradeOverload)
            return 0f;

        if (frontierDeficit > 0)
            rawValue += frontierDeficit * frontierDeficit * nearbyEnemiesScalingFactor;

        if (toNode.IsOnTerritoryEdge && frontierDeficit > 0)
            rawValue += territoryEdgeScalingFactor;

        if (understaffedFrontier)
        {
            float capacityDeficit = toNode.MaxWorkers - destGarrison;
            rawValue += capacityDeficit * insufficientWorkersScalingFactor;
        }

        // Overload for upgrade: a frontier node at max capacity under pressure needs workers
        // beyond its current cap so that after upgrade (halves workers) it's still defensible.
        // This is a high-priority defensive action — weighted similarly to understaffed frontier.
        if (needsUpgradeOverload)
        {
            int desiredOverload = GetDesiredOverloadForUpgrade(toNode, riskTolerance);
            int overloadDeficit = desiredOverload - destGarrison;
            rawValue += overloadDeficit * insufficientWorkersScalingFactor;
            rawValue += territoryEdgeScalingFactor;
        }

        // Staff resource-producing nodes when we need more output.
        if (NeedsResourceStaffingButtress(town, toNode))
        {
            int desired = GetDesiredWorkersForResourceNode(town, toNode);
            float workerDeficit = desired - destGarrison;
            GoodType resource = toNode.CanBeGatheredFrom
                ? toNode.ResourceGatheredFromThisNode
                : toNode.ResourceThisNodeCanGoGather;
            int shortage = GetResourceShortage(town, resource);
            rawValue += 12f;
            rawValue += workerDeficit * workerDeficit * resourceStaffingScalingFactor * Mathf.Max(1f, shortage * 0.1f);
        }

        if (rawValue < buttressNodeMinScore) return 0f;

        float clampedRawValue = Mathf.Clamp(rawValue, buttressNodeMinScore, buttressNodeMaxScore);
        float normalizedValue = (clampedRawValue - buttressNodeMinScore) / (buttressNodeMaxScore - buttressNodeMinScore);
        // Chokepoint amplifier: defending a structural chokepoint matters more than defending
        // an ordinary frontier of equivalent pressure. Scaled by DefenseWeight so a pacifist
        // doesn't get a stronger pull toward holding chokes than a defender; aggressive AIs
        // see chokes as just-another-frontier here (they prefer attacking through them, not
        // garrisoning them). Applied after normalization so the multiplier composes linearly
        // with peer-action priorities rather than getting clipped by the buttressNodeMaxScore
        // ceiling.
        float defWeight = town.player != null && town.player.AIDefn != null ? town.player.AIDefn.DefenseWeight : 1f;
        return normalizedValue * HeuristicScoreScale * GetChokepointMultiplier(toNode, ChokepointDefenseScale, defWeight);
    }

    // A frontier node at capacity with an upgradeable building under pressure needs workers
    // ABOVE max so that after upgrade (which halves workers) it remains defensible. Returns
    // the desired pre-upgrade worker count, or 0 if overloading isn't needed.
    //
    // riskTolerance (0-1): a cautious AI (0) demands full overload so post-upgrade garrison
    // matches the pressure. A risky AI (1) is fine upgrading at capacity — no overload needed.
    //
    // The desired count is sized to exactly satisfy GetUpgradeHeuristic's veto:
    //   postUpgrade (== preUpgrade/2) >= pressure * requiredRatio
    // where requiredRatio = 1 - 0.5 * riskTolerance (must match the veto formula). Without
    // this lockstep, a cautious AI under heavy pressure would overload to a count too small
    // for the upgrade to ever fire -- e.g. a L1 Outpost (MaxWorkers=10) under pressure 32
    // would desire only ~20 pre-upgrade (the old MaxWorkers*2 cap), upgrade to NumWorkers=10
    // which then fails the veto (10 < 32), and the AI would sit forever drip-feeding tiny
    // support waves that bleed off via the over-cap decay tick. Pre-upgrade desired is NOT
    // capped: over-cap stacking is the entire strategic point, and the game permits it
    // (over-cap workers decay one per worker-generation tick but persist long enough for
    // the upgrade to fire on the same or next AI decision tick).
    public static int GetDesiredOverloadForUpgrade(AI_NodeState node, float riskTolerance)
    {
        if (node.NumWorkers < node.MaxWorkers) return 0;
        if (node.BuildingDefn == null || !node.BuildingDefn.CanBeUpgraded) return 0;
        if (!node.IsOnTerritoryEdge) return 0;
        int pressure = GetEffectiveFrontierPressure(node);
        if (pressure <= 0) return 0;

        // A fully risky AI doesn't need any overload — it upgrades at capacity immediately.
        if (riskTolerance >= 1f) return 0;

        // Match GetUpgradeHeuristic's veto threshold: post-upgrade workers must be at least
        // pressure * requiredRatio for the upgrade to fire.
        float requiredRatio = 1f - 0.5f * Mathf.Clamp01(riskTolerance);
        int desiredPostUpgrade = (int)Math.Ceiling(pressure * requiredRatio);
        int fullDesiredPreUpgrade = Math.Max(node.MaxWorkers + 1, desiredPostUpgrade * 2);

        // Interpolate between "just at capacity" (risky) and full overload (cautious).
        // Ensure at least MaxWorkers+2 for partial-risk AIs so post-upgrade has > 1 worker.
        int minPreUpgrade = node.MaxWorkers + 2;
        int desiredPreUpgrade = (int)Mathf.Lerp(minPreUpgrade, fullDesiredPreUpgrade, 1f - riskTolerance);
        return Math.Max(minPreUpgrade, desiredPreUpgrade);
    }

    // Destination-side: a node whose physical garrison is below desired-overload but whose
    // EffectiveDefenseGarrison (physical + in-flight friendly) already covers it doesn't need
    // ANOTHER wave dispatched its way.
    public static bool NeedsUpgradeOverloadButtress(AI_NodeState node, float riskTolerance)
    {
        int desired = GetDesiredOverloadForUpgrade(node, riskTolerance);
        return desired > 0 && node.EffectiveDefenseGarrison < desired;
    }

    // Destination-side: incoming friendly workers heading to this resource node count toward
    // "already staffed", so we don't queue redundant buttress waves while the previous one
    // is still in transit.
    public static bool NeedsResourceStaffingButtress(AI_TownState town, AI_NodeState toNode)
    {
        if (!toNode.CanBeGatheredFrom && !toNode.CanGoGatherResources) return false;
        int desired = GetDesiredWorkersForResourceNode(town, toNode);
        if (toNode.EffectiveDefenseGarrison >= desired) return false;
        GoodType resource = toNode.CanBeGatheredFrom
            ? toNode.ResourceGatheredFromThisNode
            : toNode.ResourceThisNodeCanGoGather;
        return GetResourceShortage(town, resource) > 0;
    }

    // Small post-normalization boost given to builds on empty neutral targets. Applied AFTER
    // clamping so it cannot saturate the heuristic the way the earlier in-rawValue +15 did.
    // This is the "constructing on a neutral literally claims a node" signal -- separate from
    // the building-type quality signal that the rawValue path already captures.
    const float neutralTargetBuildBonus = 0.5f;

    public static float GetBuildHeuristic(AI_TownState town, BuildingDefn buildingDefn, AI_NodeState toNode)
    {
        float rawValue = 0f;

        if (buildingDefn.IsDefensive)
        {
            if (toNode.IsOnTerritoryEdge)
                rawValue += buildingStrategicScalingFactor * 1.5f;
            else
                rawValue += buildingStrategicScalingFactor;
        }
        else if (buildingDefn.CanGatherResources)
        {
            // Goal-driven: appeal of a gatherer is proportional to how short we are on its
            // produced resource relative to what the buildable catalog demands. Replaces the
            // previous constant target of 20-of-everything which had no relationship to what
            // the player was actually trying to build.
            var gatherableResource = buildingDefn.ResourceThisNodeCanGoGather.GoodType;
            int shortage = GetResourceShortage(town, gatherableResource);
            rawValue += buildingResourceScalingFactor * shortage;
        }
        else
        {
            rawValue += buildingStrategicScalingFactor;
        }

        float clampedRawValue = Mathf.Clamp(rawValue, buildBuildingMinScore, buildBuildingMaxScore);
        float normalizedValue = (clampedRawValue - buildBuildingMinScore) / (buildBuildingMaxScore - buildBuildingMinScore);
        float result = normalizedValue * HeuristicScoreScale;

        // Territory-gain bonus for empty neutral targets. Adding here (after normalization)
        // means it bumps the final value by a fixed amount without saturating -- earlier I
        // added it to rawValue before clamp and every neutral candidate maxed out at the same
        // score, defeating top-K ordering and branch-and-bound pruning.
        if (toNode.OwnedBy == null)
            result += neutralTargetBuildBonus;

        // Chokepoint amplifier on neutral construct sites: building on an empty chokepoint
        // is worth more than building on an off-route empty node. Scaled by AggressivenessWeight
        // because grabbing a strategic chokepoint via construct-then-build is a force-projection
        // move -- a pacifist (agg=0) shouldn't be drawn to the far-off peak chokepoint over
        // the equally-reachable resource node next door. Matched to the capture scale so
        // Construct on a chokepoint and AttackToNode on a chokepoint move together for AIs
        // that DO care about chokes.
        float agg = town.player != null && town.player.AIDefn != null ? town.player.AIDefn.AggressivenessWeight : 1f;
        result *= GetChokepointMultiplier(toNode, ChokepointCaptureScale, agg);

        return result;
    }

    public static float GetCaptureResourceNodeHeuristic(AI_TownState town, AI_NodeState targetNode, int workersToSend)
    {
        return GetAttackHeuristic(town, targetNode, workersToSend);
    }

    public static float GetCaptureNeutralNodeHeuristic(AI_TownState town, AI_NodeState targetNode, int workersToSend)
    {
        float h = GetAttackHeuristic(town, targetNode, workersToSend);
        if (NeutralNeighborTouchesEnemy(targetNode, town.player))
            h += 1.5f;
        return h;
    }

    public static float GetAttackHeuristic(AI_TownState town, AI_NodeState targetNode, int totalWorkersWillingToSend)
    {
        float rawValue = attackNodeMaxScore / 2f;

        // Prefer weaker targets and attacks where we have comfortable force advantage
        float forceRatio = totalWorkersWillingToSend / (float)Mathf.Max(1, targetNode.NumWorkers);
        if (forceRatio >= 1.5f)
            rawValue += 10f;
        else if (forceRatio >= 1f)
            rawValue += 5f;

        if (targetNode.HasBuilding)
            rawValue += 5f;

        // Resource-aware bonus: if the target node either gathers a resource (e.g. enemy
        // Woodcutter) or *is* a resource source (e.g. neutral/enemy Forest), and we are
        // short on that resource relative to what the buildable catalog demands, score the
        // capture much more highly. This is what turns "capture the forest because I need
        // wood for a Barracks" from accidental into intentional.
        GoodType targetResource = GoodType.Unset;
        if (targetNode.CanGoGatherResources)
            targetResource = targetNode.ResourceThisNodeCanGoGather;
        else if (targetNode.CanBeGatheredFrom)
            targetResource = targetNode.ResourceGatheredFromThisNode;

        if (targetResource != GoodType.Unset)
        {
            int shortage = GetResourceShortage(town, targetResource);
            rawValue += shortage * attackResourceShortageScalingFactor;
        }

        float clampedRawValue = Mathf.Clamp(rawValue, attackNodeMinScore, attackNodeMaxScore);
        float normalizedValue = (clampedRawValue - attackNodeMinScore) / (attackNodeMaxScore - attackNodeMinScore);
        // Chokepoint amplifier: capturing / attacking a structural chokepoint is worth more
        // than an equivalent off-route node. Scaled by AggressivenessWeight because this
        // function is shared by all offensive moves -- AttackToNode, capture-neutral, and
        // capture-resource all call here, and all of them are about projecting force at a
        // strategically valuable position. A pacifist (agg=0) collapses the amp to 1.0 so a
        // distant peak-choke neutral looks no more attractive than an equally-reachable
        // ordinary neutral; the AI captures whatever's convenient instead of marching across
        // the map for the choke. Symmetric with GetButtressHeuristic which scales the
        // DefensiveScale by DefenseWeight for the same personality-faithfulness reason.
        float agg = town.player != null && town.player.AIDefn != null ? town.player.AIDefn.AggressivenessWeight : 1f;
        return normalizedValue * HeuristicScoreScale * GetChokepointMultiplier(targetNode, ChokepointAttackScale, agg);
    }

    public static AI_NodeState GetButtressSourceNode(AI_NodeState toNode, PlayerData player, int minWorkersInNodeBeforeConsideringSendingAnyOut)
    {
        // Use effective pressure for the emergency check so a chokepoint with high AttackHeat
        // (recently hammered, but currently quiet) is treated as an emergency and pulls workers
        // from sources below the normal 75% capacity threshold. Destination "garrison" here is
        // physical + in-flight friendly -- if help is already on the way we shouldn't escalate
        // to emergency just because the physical count looks thin.
        int destGarrison = toNode.EffectiveDefenseGarrison;
        bool emergency = GetEffectiveFrontierPressure(toNode) > destGarrison
                         || toNode.NumContestedNeutralWorkersNearby > destGarrison / 2
                         || toNode.AttackHeat >= AttackHeatEmergencyThreshold;
        // Mirror the buttress tasks: a personality-driven overkill demand at the destination
        // relaxes source threshold even outside of true emergency. Without this, the
        // single-source preview/dispatch would refuse mid-staffed sources that
        // GetWorkersWillingToSendForDefense (in the tasks) would actually accept once the
        // dest's personality-aware deficit is in play.
        bool destNeedsOverkill = DestNeedsPersonalityOverkill(toNode, player);

        AI_NodeState best = null;
        int bestWilling = 0;
        Queue<AI_NodeState> queue = new();
        HashSet<AI_NodeState> visited = new();
        queue.Enqueue(toNode);
        visited.Add(toNode);

        // Only walk through player-owned territory. A "friendly source" reachable only via
        // enemy/neutral nodes isn't usable as a buttress source -- the workers would have
        // to cross hostile ground to arrive, and realtime ResolveWorkerArrival intercepts
        // them at the first hostile intermediate. Restricting BFS expansion to friendly
        // neighbors guarantees any returned source has a fully player-owned path to toNode.
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node.OwnedBy == player && node != toNode)
            {
                // Don't drain a hotter node to help a cooler one. A node that's seen more
                // recent attacks than the destination is itself the more urgent defender.
                bool sourceHotterThanDest = node.AttackHeat >= AttackHeatChokepointThreshold
                                            && node.AttackHeat > toNode.AttackHeat;
                // Also refuse sources that are themselves an undergarrisoned frontier by
                // IMMEDIATE enemy pressure (real enemy neighbors + heat + incoming hostile),
                // even when AttackHeat hasn't risen yet. AttackHeat is post-hoc -- a
                // chokepoint can have a massive enemy stack one hop away with zero heat
                // because no attacker has resolved yet. Uses immediate-enemy pressure
                // rather than effective pressure so a node merely adjacent to a contested
                // neutral isn't falsely flagged as hot (otherwise #10 at 80/80 facing a
                // contested neutral #0 with 64 workers would be excluded as a buttress
                // source for #27 even though #10 has no actual enemy neighbors). The guard
                // only triggers when the SOURCE's own real pressure is at least as high as
                // the destination's -- a lightly-pressured frontier can still help a
                // more-pressured one.
                bool sourceIsHotFrontier =
                    GetReservedForImmediateDefense(node, player) > node.NumWorkers
                    && GetImmediateEnemyPressure(node) >= GetImmediateEnemyPressure(toNode);
                if (!sourceHotterThanDest && !sourceIsHotFrontier)
                {
                    int keepAtSource = Math.Max(minWorkersInNodeBeforeConsideringSendingAnyOut, GetReservedForImmediateDefense(node, player));
                    int excess = node.NumWorkers - keepAtSource;
                    if (excess > 0)
                    {
                        int willing = GetWorkersWillingToSendForDefense(node, minWorkersInNodeBeforeConsideringSendingAnyOut, emergency, toNode.AttackHeat, destNeedsOverkill);
                        willing = Math.Min(willing, excess);
                        if (willing > bestWilling)
                        {
                            bestWilling = willing;
                            best = node;
                        }
                    }
                }
            }
            foreach (var neighbor in node.NeighborNodes)
            {
                if (visited.Contains(neighbor)) continue;
                if (neighbor.OwnedBy != player) continue;
                visited.Add(neighbor);
                queue.Enqueue(neighbor);
            }
        }

        return best;
    }

    public static bool CanButtressFromAnySource(AI_NodeState toNode, PlayerData player, int minWorkersInNodeBeforeConsideringSendingAnyOut)
    {
        return GetButtressSourceNode(toNode, player, minWorkersInNodeBeforeConsideringSendingAnyOut) != null;
    }

    public static AI_NodeState GetFriendlyNodeWithMostWorkers(AI_NodeState toNode, PlayerData player)
    {
        AI_NodeState maxNode = null;
        int maxWorkers = int.MinValue;
        Queue<AI_NodeState> queue = new();
        HashSet<AI_NodeState> visited = new();
        queue.Enqueue(toNode);
        visited.Add(toNode);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node.OwnedBy == player && node.NumWorkers > maxWorkers && !node.IsOnTerritoryEdge)
            {
                maxNode = node;
                maxWorkers = node.NumWorkers;
            }
            foreach (var neighbor in node.NeighborNodes)
            {
                if (!visited.Contains(neighbor))
                {
                    queue.Enqueue(neighbor);
                    visited.Add(neighbor);
                }
            }
        }

        return maxNode;
    }

    public static bool CanBuildBuilding(AI_TownState town, BuildingDefn buildingDefn, AI_NodeState toNode)
    {
        if (toNode.CanBeGatheredFrom)
        {
            if (!buildingDefn.CanGatherResources)
                return false;
            if (toNode.ResourceGatheredFromThisNode != buildingDefn.ResourceThisNodeCanGoGather.GoodType)
                return false;
        }
        else if (buildingDefn.CanGatherResources)
            return false;

        if (!town.ConstructionResourcesCanBeReachedFromNode(toNode, buildingDefn.ConstructionRequirements))
            return false;

        return true;
    }

    public static bool PlayerHasExcessWorkers(AI_TownState town, PlayerData player)
    {
        for (int i = 0; i < town.NumNodes; i++)
        {
            var node = town.Nodes[i];
            if (node.OwnedBy == player && (node.NumWorkers > node.MaxWorkers || node.NumWorkers > 15))
                return true;
        }
        return false;
    }
}
