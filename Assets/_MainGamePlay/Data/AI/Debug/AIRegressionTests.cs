using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Headless regression tests for the AI decision pipeline. Verifies each "preserved"
/// decision factor (frontier defense, in-flight projection, chokepoints, attack heat,
/// resource shortage, multi-source allocation, etc.) by constructing tiny synthetic
/// worlds and asserting on PlayerAI.BestNextActionToTake.
///
/// No UI -- the entire RunAll() returns inside a single frame, designed for budget &lt;1s on
/// modest hardware. Trigger from a button in AITestScene; results are returned as a list
/// of pass/fail records and also written to Debug.Log.
/// </summary>
public static class AIRegressionTests
{
    public class TestResult
    {
        public string Name;
        public bool Passed;
        public string Message;
        public override string ToString() => (Passed ? "[PASS] " : "[FAIL] ") + Name + (Passed || string.IsNullOrEmpty(Message) ? "" : ": " + Message);
    }

    public static List<TestResult> RunAll()
    {
        var results = new List<TestResult>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        Run(results, "frontier defense reinforces", Test_FrontierDefense);
        Run(results, "no drip-feed below min wave", Test_NoDripBelowMinWave);
        Run(results, "source workers preserved (>=1)", Test_SourceWorkersPreserved);
        Run(results, "attack heat surfaces as pressure", Test_AttackHeatPressure);
        Run(results, "chokepoint preference", Test_ChokepointPreference);
        Run(results, "multi-source attack accumulates", Test_MultiSourceAttack);
        Run(results, "single-source attack preferred when sufficient", Test_SingleSourcePreferred);
        Run(results, "resource shortage drives capture", Test_ResourceShortage);
        Run(results, "build affordability gate", Test_BuildAffordability);
        Run(results, "upgrade gated by frontier pressure", Test_UpgradeGated);
        Run(results, "upgrade allowed in safe interior", Test_UpgradeAllowed);
        Run(results, "in-flight projection prevents double-send", Test_InFlightProjection);
        Run(results, "attack-already-sufficient suppresses pile-on", Test_AttackAlreadySufficient);
        Run(results, "do-nothing fallback when isolated", Test_DoNothingFallback);
        Run(results, "personality: aggression scales attacks", Test_AggressionScaling);
        Run(results, "personality: caution scales overkill", Test_CautionScaling);
        Run(results, "personality: expansion=0 disables captures", Test_ExpansionDisablesCaptures);

        // Regression coverage for the bug-fix batch (owned-path, surplus upgrade, gatherer
        // adjacency, pending-capture reinforce):
        Run(results, "no send across enemy territory", Test_NoSendAcrossEnemyTerritory);
        Run(results, "high surplus drives upgrade", Test_HighSurplusUpgrades);
        Run(results, "surplus upgrade beats pressure veto", Test_HighSurplusUpgradesUnderHighPressure);
        Run(results, "no gatherer build without adjacent resource", Test_GathererNeedsAdjacentResource);
        Run(results, "adjacency mask recognizes adjacent resource", Test_AdjacencyMaskRecognizesAdjacentResource);
        Run(results, "distance matrix symmetric and correct", Test_DistanceMatrixSymmetricAndCorrect);
        Run(results, "defend articulation point", Test_DefendArticulationPoint);
        Run(results, "attack articulation point preferred over leaf", Test_AttackArticulationPoint);
        Run(results, "prefer uncontested expansion (race margin)", Test_PreferUncontestedExpansion);
        Run(results, "same-region reinforce first (Phase 6)", Test_SameRegionReinforceFirst);
        Run(results, "cross-bridge reinforce when dry (Phase 6)", Test_CrossBridgeReinforceWhenDry);
        Run(results, "barracks prefers hub over corridor (Phase 7)", Test_BarracksPrefersHubOverCorridor);
        Run(results, "no reinforce on neutral with capture in flight", Test_NoReinforceOnPendingCapture);

        // Attack sizing (the drip-feed fix). These exercise the rules that the previous
        // version of AttackGenerator was violating: capture-flip (+1), travel-time regen,
        // and anti-dribble (no partial waves at a regenerating defender).
        Run(results, "attack must exceed defenders to capture", Test_AttackSizesForCaptureFlip);
        Run(results, "attack accounts for defender regen during travel", Test_AttackAccountsForRegen);
        Run(results, "no dribble against a regenerating defender", Test_NoDribbleAttackOnBarracks);
        Run(results, "attack credits already-in-flight wave", Test_AttackCreditsInFlight);

        sw.Stop();
        bool allPassed = true;
        foreach (var r in results) if (!r.Passed) { allPassed = false; break; }
        results.Add(new TestResult
        {
            Name = $"=== completed in {sw.ElapsedMilliseconds} ms, {(allPassed ? "ALL PASS" : "FAILURES PRESENT")} ===",
            Passed = allPassed
        });
        return results;
    }

    static void Run(List<TestResult> results, string name, Action body)
    {
        try
        {
            body();
            results.Add(new TestResult { Name = name, Passed = true });
        }
        catch (Exception e)
        {
            results.Add(new TestResult { Name = name, Passed = false, Message = e.Message });
        }
    }

    // ============================================================================
    // Individual tests
    // ============================================================================

    // 1) Frontier defense: an owned frontier node with 1 worker next to 8 enemies should
    //    receive reinforcements from a friendly interior node with plenty of workers.
    static void Test_FrontierDefense()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 20, hasGenerator: true);
        w.AddNode(2, owner: 1, workers: 1,  hasGenerator: true); // frontier under threat
        w.AddNode(3, owner: 2, workers: 8,  hasGenerator: true); // enemy
        w.Connect(1, 2);
        w.Connect(2, 3);
        w.Build();

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;

        AssertTrue(act.Type == AIActionType.SendWorkersToOwnedNode || act.Type == AIActionType.SendMultiSourceWorkersToOwnedNode || act.Type == AIActionType.AttackToNode,
            $"expected reinforce or attack, got {act.Type}");
        if (act.Type == AIActionType.SendWorkersToOwnedNode || act.Type == AIActionType.SendMultiSourceWorkersToOwnedNode)
        {
            AssertEq(2, act.DestNode.NodeId, "reinforce target should be the frontier node");
        }
    }

    // 2) Min wave threshold: when only 4 workers are spare, an "8 deficit" reinforce should
    //    not be sent (below MinReinforceWave==5).
    static void Test_NoDripBelowMinWave()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 5, hasGenerator: true);  // 4 sendable (after MinReserve)
        w.AddNode(2, owner: 1, workers: 1, hasGenerator: true);  // frontier, 8 deficit
        w.AddNode(3, owner: 2, workers: 8, hasGenerator: true);
        w.Connect(1, 2);
        w.Connect(2, 3);
        w.Build();

        // Set Caution high so MinReserveAtSource = 4, leaving only ~1 sendable; the wave
        // would then be below MinReinforceWave and the reinforce candidate should be vetoed.
        var defn = w.GetPlayer(1).AIDefn;
        defn.Caution = 2f;

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;

        // Either DoNothing or a non-reinforce action (e.g. upgrade) -- but NOT a drip-feed
        // reinforce of the frontier.
        if (act.Type == AIActionType.SendWorkersToOwnedNode || act.Type == AIActionType.SendMultiSourceWorkersToOwnedNode)
        {
            int totalSent = act.Type == AIActionType.SendWorkersToOwnedNode ? act.Count : SumValues(act.AttackFromNodes);
            AssertTrue(totalSent >= StrategicAnalysis.MinReinforceWave,
                $"reinforce wave {totalSent} < MinReinforceWave ({StrategicAnalysis.MinReinforceWave})");
        }
    }

    // 3) Source workers preserved: a candidate emitted by any generator must never propose
    //    sending so many workers that the source would be left with <1.
    static void Test_SourceWorkersPreserved()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 5, hasGenerator: true);
        w.AddNode(2, owner: 2, workers: 1, hasGenerator: true);
        w.Connect(1, 2);
        w.Build();

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;

        if (act.SourceNode != null && act.Count > 0)
            AssertTrue(act.Count <= NodeData.GetMaxSendableWorkers(act.SourceNode.NumWorkers),
                $"Count {act.Count} would drain source #{act.SourceNode.NodeId} (workers={act.SourceNode.NumWorkers})");

        foreach (var kv in act.AttackFromNodes)
            AssertTrue(kv.Value <= NodeData.GetMaxSendableWorkers(kv.Key.NumWorkers),
                $"multi-send {kv.Value} would drain source #{kv.Key.NodeId} (workers={kv.Key.NumWorkers})");
    }

    // 4) AttackHeat surfaces as defensive pressure: a node with no visible enemies but high
    //    AttackHeat should still report DefensiveDeficit > 0.
    static void Test_AttackHeatPressure()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 3, hasGenerator: true);
        w.AddNode(2, owner: 0, workers: 0); // neutral, no enemies visible
        w.Connect(1, 2);
        w.Build();
        w.SetAttackHeat(1, 4f); // heavy recent pressure memory
        w.Town.WorldRevision++;

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);

        var analysis = p1.AI.GetAnalysis();
        // StrategicAnalysis arrays are indexed by the AI_NodeState's Index field, NOT by
        // NodeId. Look up the mirror for node 1 and use its Index.
        int idxNode1 = IndexOfNode(p1.AI.GetWorldView(), nodeId: 1);
        AssertGreater(analysis.FrontierPressure[idxNode1], 0,
            "AttackHeat should produce non-zero frontier pressure");
    }

    static int IndexOfNode(AIWorldView view, int nodeId)
    {
        for (int i = 0; i < view.NumNodes; i++)
            if (view.Nodes[i].NodeId == nodeId) return view.Nodes[i].Index;
        throw new Exception($"node {nodeId} not in view");
    }

    // 5) Chokepoint preference: between two equivalent enemy targets, the chokepoint is the
    //    one the AI attacks.
    static void Test_ChokepointPreference()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 30, hasGenerator: true);
        w.AddNode(2, owner: 2, workers: 4,  hasGenerator: true);
        w.AddNode(3, owner: 2, workers: 4,  hasGenerator: true);
        w.Connect(1, 2);
        w.Connect(1, 3);
        w.Build();
        w.SetChokepoint(2, 1.0f); // node 2 is the chokepoint
        w.SetChokepoint(3, 0.0f);
        w.Town.WorldRevision++;

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;

        AssertEq(AIActionType.AttackToNode, act.Type, $"expected attack, got {act.Type}");
        AssertEq(2, act.DestNode.NodeId, "chokepoint target should win over equal-value non-chokepoint");
    }

    // 6) Multi-source attack: when no single source can muster enough force alone, the AI
    //    should accumulate workers from multiple sources via BFS. We use a 4-node layout
    //    with two interior sources (#1, #2) feeding through a frontier relay (#3) into the
    //    enemy (#4). The relay (#3) is drained by its own defensive reserve so it cannot
    //    contribute, forcing the wave to be assembled from the depth-2 interior nodes.
    //    Capture-flip + travel-regen-sized attack requires ~14 workers landed; each
    //    interior source individually has only 9 safe to send, so multi-source is forced.
    static void Test_MultiSourceAttack()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 12, hasGenerator: true); // interior source (safe ~9)
        w.AddNode(2, owner: 1, workers: 12, hasGenerator: true); // interior source (safe ~9)
        w.AddNode(3, owner: 1, workers: 5, hasGenerator: true); // frontier relay (no safe spare)
        w.AddNode(4, owner: 2, workers: 6, hasGenerator: true); // enemy (capture-flip+regen sized > single source can muster)
        w.Connect(1, 3);
        w.Connect(2, 3);
        w.Connect(3, 4);
        w.Build();

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;

        AssertEq(AIActionType.AttackToNode, act.Type, $"expected attack, got {act.Type}");
        AssertEq(4, act.DestNode.NodeId, "should target the enemy node");
        AssertGreater(act.AttackFromNodes.Count, 1, "multi-source attack should use >1 sources");
    }

    // 7) Single-source attack preferred: when one source has enough by itself, the action
    //    can be either single- or multi-source as long as the chosen sources are valid --
    //    we just verify the AI doesn't pick something pathological.
    static void Test_SingleSourcePreferred()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 30, hasGenerator: true);
        w.AddNode(2, owner: 2, workers: 4,  hasGenerator: true);
        w.Connect(1, 2);
        w.Build();

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;
        AssertEq(AIActionType.AttackToNode, act.Type, $"expected attack, got {act.Type}");
        AssertEq(1, act.AttackFromNodes.Count, "should attack from exactly one source");
    }

    // 8) Resource shortage drives gatherer build: when wood inventory is zero and target
    //    stockpile is high, the AI should build a gatherer on an empty node beside a Forest.
    static void Test_ResourceShortage()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 20, hasGenerator: true);
        w.AddNode(2, owner: 0, workers: 0);                    // empty build site
        w.AddForestNeutral(3);                                 // Wood deposit (not captured)
        w.Connect(1, 2);
        w.Connect(2, 3);
        w.Build();

        var defn = w.GetPlayer(1).AIDefn;
        defn.TargetWoodStockpile = 30;
        defn.TargetStoneStockpile = 0;
        defn.Expansion = 2f;

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;

        bool isGathererBuild =
            (act.Type == AIActionType.ConstructBuildingInEmptyNode || act.Type == AIActionType.CaptureNeutralNode)
            && act.BuildingToConstruct != null
            && act.BuildingToConstruct.CanGatherResources
            && act.BuildingToConstruct.ResourceThisNodeCanGoGather != null
            && act.BuildingToConstruct.ResourceThisNodeCanGoGather.GoodType == GoodType.Wood;
        AssertTrue(isGathererBuild, $"expected wood gatherer build beside forest, got {act.Type}");
        AssertEq(2, act.DestNode.NodeId, "gatherer should be built on empty #2 beside forest #3");
    }

    // 9) Build affordability: if the player has no resources, build candidates that require
    //    resources must be vetoed; the AI should pick something else or do nothing.
    static void Test_BuildAffordability()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 20, hasGenerator: true);
        w.AddNode(2, owner: 0, workers: 0);                     // empty neutral
        w.Connect(1, 2);
        w.Build();

        // Player has zero of everything. If GameDefns is missing, BuildGenerator emits no
        // candidates at all (early return). Either way, no Construct action should fire.
        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;

        if (act.Type == AIActionType.ConstructBuildingInEmptyNode || act.Type == AIActionType.CaptureNeutralNode)
        {
            // If we did emit a build candidate, its requirements must already be satisfied
            // by inventory (i.e. the building has no requirements).
            var bd = act.BuildingToConstruct;
            AssertTrue(bd != null, "build action must carry a BuildingToConstruct");
            foreach (var req in bd.ConstructionRequirements)
                AssertTrue(p1.AI.GetWorldView().GetInventory(req.Good.GoodType) >= req.Amount,
                    $"build proposed {bd.Id} needing {req.Amount} {req.Good.GoodType} with only {p1.AI.GetWorldView().GetInventory(req.Good.GoodType)} on hand");
        }
    }

    // 10) Upgrade gated by frontier pressure: a frontier node at MaxWorkers under heavy enemy
    //     pressure should NOT be upgraded (post-upgrade NumWorkers halves; that would expose
    //     it to capture).
    static void Test_UpgradeGated()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 10, hasGenerator: true, upgradable: true);
        w.AddNode(2, owner: 2, workers: 20, hasGenerator: true); // huge enemy pressure
        w.Connect(1, 2);
        w.Build();

        var defn = w.GetPlayer(1).AIDefn;
        defn.Caution = 1.5f;

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;
        AssertTrue(act.Type != AIActionType.UpgradeBuilding,
            "should not upgrade a frontier node under heavy enemy pressure");
    }

    // 11) Upgrade allowed: an interior node at MaxWorkers with no pressure should be a
    //     viable upgrade candidate -- among the proposed candidates, at least one Upgrade
    //     scores > 0 for that node.
    static void Test_UpgradeAllowed()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 10, hasGenerator: true, upgradable: true);
        w.AddNode(2, owner: 1, workers: 10, hasGenerator: true); // also friendly, no enemies
        w.Connect(1, 2);
        w.Build();

        var defn = w.GetPlayer(1).AIDefn;
        defn.Tempo = 2f; // strongly favours upgrades over force

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);

        bool sawUpgradeCandidate = false;
        foreach (var snap in p1.AI.DecisionRecord.TopCandidates)
            if (snap.Type == AIActionType.UpgradeBuilding) { sawUpgradeCandidate = true; break; }
        AssertTrue(sawUpgradeCandidate, "interior MaxWorkers node should produce a viable Upgrade candidate");
    }

    // 12) In-flight projection: if a worker is already in flight toward a neutral node that
    //     we plan to capture, the mirror should treat it as "ours pending" and the AI should
    //     not propose to capture the same neutral again.
    static void Test_InFlightProjection()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 20, hasGenerator: true);
        w.AddNode(2, owner: 0, workers: 0);
        w.Connect(1, 2);
        w.Build();

        // Set the neutral as "pending capture" by P1 -- simulates an in-flight wave that has
        // already committed to this target.
        var neutral = w.NodesById[2];
        neutral.PendingCaptureBy = w.GetPlayer(1);
        neutral.IncomingByPlayer[w.GetPlayer(1)] = 5;
        // Bump revision so AI is forced to re-evaluate with the new world state.
        w.Town.WorldRevision++;

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;

        AssertTrue(
            (act.Type != AIActionType.CaptureNeutralNode && act.Type != AIActionType.ConstructBuildingInEmptyNode)
            || act.DestNode.NodeId != 2,
            "should not propose a second capture of the same neutral while one is in flight");
    }

    // 13) AttackAlreadySufficient: an enemy node whose effective defense is already beaten by
    //     in-flight attackers should NOT receive another attack from this player.
    static void Test_AttackAlreadySufficient()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 30, hasGenerator: true);
        w.AddNode(2, owner: 2, workers: 3,  hasGenerator: true);
        w.Connect(1, 2);
        w.Build();

        // Simulate 10 attackers already in flight from P1 to enemy node 2 -- more than
        // enough to win. AI_NodeState.Refresh will set AttackAlreadySufficient=true.
        var enemy = w.NodesById[2];
        enemy.IncomingByPlayer[w.GetPlayer(1)] = 10;
        w.Town.WorldRevision++;

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;
        AssertTrue(act.Type != AIActionType.AttackToNode || act.DestNode.NodeId != 2,
            "should not attack a node our in-flight workers already beat");
    }

    // 14) Do-nothing fallback: a player owning a single node with no neighbors and no work
    //     to do should produce a DoNothing decision (not crash, not propose junk).
    static void Test_DoNothingFallback()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 5, hasGenerator: true);
        w.Build();
        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        AssertEq(AIActionType.DoNothing, p1.AI.BestNextActionToTake.Type,
            "no neighbors, no enemies, nothing to do");
    }

    // 15) Aggression dial: doubling Aggression should produce a strictly higher attack score
    //     against the same target. Verifies the personality dial is wired into ScoreAttack.
    static void Test_AggressionScaling()
    {
        float lowScore = RunAttackScore(aggression: 0.5f);
        float highScore = RunAttackScore(aggression: 2.0f);
        AssertGreater(highScore, lowScore,
            $"high aggression score ({highScore:F2}) should exceed low ({lowScore:F2})");
    }

    static float RunAttackScore(float aggression)
    {
        // Source needs enough workers to mount the new capture-flip + travel-regen sized
        // attack. With Caution=1 (AttackOverkill=1.25), enemy at 2/10 with 1-hop travel,
        // regen ~= floor(1*8/2) capped at headroom 8 -> 4 expected regen during travel.
        // Required = ceil((2+4+1)*1.25) = 9 attackers. Source MinReserve(Caution=1) = 3
        // and frontier pressure from 2 enemy workers = ceil(2*1.25) = 3, so source needs
        // workers >= 9 + 3 + 3 = 15 to actually be able to dispatch the wave. Use 18 for
        // headroom (the Aggression-scaling assertion is independent of exact margin).
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 18, hasGenerator: true);
        w.AddNode(2, owner: 2, workers: 2, hasGenerator: true);
        w.Connect(1, 2);
        w.Build();
        w.GetPlayer(1).AIDefn.Aggression = aggression;
        w.GetPlayer(1).AI.InvalidateDecisionCache();
        w.GetPlayer(1).AI.Update(w.Town);
        var act = w.GetPlayer(1).AI.BestNextActionToTake;
        return act.Type == AIActionType.AttackToNode ? act.Score : 0f;
    }

    // 16) Caution dial: higher Caution should require more attackers (higher AttackOverkill).
    static void Test_CautionScaling()
    {
        var lowCaut = new PersonalityWeights(1f, 1f, 0f, 1f);
        var highCaut = new PersonalityWeights(1f, 1f, 2f, 1f);
        AssertGreater(highCaut.AttackOverkill, lowCaut.AttackOverkill,
            "AttackOverkill should rise with Caution");
        AssertEq(1f, lowCaut.AttackOverkill, "Caution=0 should give exactly 1.0x overkill");
    }

    // 17) Expansion=0 should suppress capture candidates entirely (score multiplied by 0).
    static void Test_ExpansionDisablesCaptures()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 20, hasGenerator: true);
        w.AddNode(2, owner: 0, workers: 0);
        w.Connect(1, 2);
        w.Build();

        var defn = w.GetPlayer(1).AIDefn;
        defn.Expansion = 0f;
        defn.TargetWoodStockpile = 0;
        defn.TargetStoneStockpile = 0;
        defn.Aggression = 0f; // no attack to compete with, no aggression either
        defn.Tempo = 0f;      // no upgrade pull

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;
        AssertTrue(act.Type != AIActionType.CaptureNeutralNode && act.Type != AIActionType.ConstructBuildingInEmptyNode,
            "Expansion=0 should suppress capture candidates");
    }

    // ============================================================================
    // Regression coverage for the post-rewrite bug batch
    // ============================================================================

    // 18) Owned-path requirement: two owned nodes #1 and #3 separated by an enemy #2.
    //     The AI must NOT propose a SendWorkers reinforce to #3 from #1 -- workers would have
    //     to physically route through enemy territory and get killed mid-transit.
    static void Test_NoSendAcrossEnemyTerritory()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 15, hasGenerator: true);
        w.AddNode(2, owner: 2, workers: 5,  hasGenerator: true); // enemy wedge
        w.AddNode(3, owner: 1, workers: 1,  hasGenerator: true); // isolated friendly island
        w.Connect(1, 2);
        w.Connect(2, 3);
        w.Build();

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;

        bool reinforcingIsland =
            (act.Type == AIActionType.SendWorkersToOwnedNode || act.Type == AIActionType.SendMultiSourceWorkersToOwnedNode)
            && act.DestNode != null && act.DestNode.NodeId == 3;
        AssertTrue(!reinforcingIsland,
            "should not reinforce #3 across enemy territory (no owned path from #1 to #3)");

        // Also scan candidates: no reinforce candidate should ever target #3.
        foreach (var snap in p1.AI.DecisionRecord.TopCandidates)
        {
            bool isReinforce = snap.Type == AIActionType.SendWorkersToOwnedNode
                            || snap.Type == AIActionType.SendMultiSourceWorkersToOwnedNode;
            AssertTrue(!(isReinforce && snap.DestNodeId == 3),
                $"reinforce candidate targets #3 but no owned path exists: {snap.Description}");
        }
    }

    // 19) Surplus workers drive upgrade: a generator node sitting at 150/10 workers is
    //     hemorrhaging economy (TickBuildingProduction decays 1 per spawn while over-cap).
    //     The AI must recognise this and choose UpgradeBuilding rather than DoNothing.
    static void Test_HighSurplusUpgrades()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 150, hasGenerator: true);
        // No other nodes: isolate the upgrade decision so no Attack/Capture can outscore it.
        w.Build();

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;

        AssertEq(AIActionType.UpgradeBuilding, act.Type,
            $"150/10 surplus node should upgrade; got {act.Type}");
        AssertEq(1, act.SourceNode.NodeId, "upgrade should target the over-cap node #1");
    }

    // 19b) Surplus upgrade still fires under high enemy pressure. Live-play repro: Red's
    //      #27 sat at 54/10 with three enemy neighbors totalling 161 workers. The old
    //      defensive veto refused to upgrade because post-halve workers (27) << pressure
    //      (161), but the surplus (44 workers) was bleeding off via over-cap decay so
    //      refusing was strictly worse than upgrading. With surplus >= MaxWorkers, the
    //      veto must stand down and the upgrade should be chosen (no reinforce viable
    //      because all enemy-adjacent owned nodes are themselves drained).
    static void Test_HighSurplusUpgradesUnderHighPressure()
    {
        var w = new TestWorld();
        // Owned node A (#1): 54 workers, max 10 -> 44 surplus. Frontier with 3 enemies.
        w.AddNode(1, owner: 1, workers: 54, hasGenerator: true, upgradable: true);
        // Three enemy neighbors with combined 161 workers. Each is itself near-cap so
        // they aren't viable attack targets (and their high garrison drives pressure).
        w.AddNode(2, owner: 2, workers: 62, hasGenerator: true);
        w.AddNode(3, owner: 2, workers: 61, hasGenerator: true);
        w.AddNode(4, owner: 2, workers: 38, hasGenerator: true);
        // Lone friendly interior #5 with no spare workers, so reinforce can't muster a wave.
        w.AddNode(5, owner: 1, workers: 1, hasGenerator: true);
        w.Connect(1, 2);
        w.Connect(1, 3);
        w.Connect(1, 4);
        w.Connect(1, 5);
        w.Build();

        // Match Red's personality from the live dump.
        var defn = w.GetPlayer(1).AIDefn;
        defn.Aggression = 1.2f;
        defn.Expansion = 1.0f;
        defn.Caution = 0.9f;
        defn.Tempo = 0.9f;

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;

        AssertEq(AIActionType.UpgradeBuilding, act.Type,
            $"high-surplus frontier node should upgrade despite enemy pressure; got {act.Type}");
        AssertEq(1, act.SourceNode.NodeId, "upgrade should target #1 (the 54/10 over-cap node)");
    }

    // 20) Gatherer adjacency: a StoneMiner/Woodcutter/LumberjackHut only produces resources
    //     when next to a matching deposit. The AI must not propose constructing a gatherer
    //     on a node that has no adjacent matching resource (it would gather nothing -- pure
    //     economy waste). Verified against the live BuildingDefns loaded via GameDefns.
    static void Test_GathererNeedsAdjacentResource()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 20, hasGenerator: true);
        w.AddNode(2, owner: 0, workers: 0); // empty grass neutral, no resource adjacency
        w.Connect(1, 2);
        w.Build();

        // Crank shortage so the AI is highly motivated to build a gatherer if the filter
        // missed it -- with no shortage pressure the test passes trivially.
        var defn = w.GetPlayer(1).AIDefn;
        defn.Expansion = 2f;
        defn.TargetWoodStockpile = 100;
        defn.TargetStoneStockpile = 100;

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;

        // If the chosen action is build/capture-empty, the building cannot be a gatherer
        // (because #2 has no adjacent resource of any kind).
        if ((act.Type == AIActionType.ConstructBuildingInEmptyNode || act.Type == AIActionType.CaptureNeutralNode)
            && act.BuildingToConstruct != null
            && act.BuildingToConstruct.CanGatherResources)
        {
            throw new Exception(
                $"AI proposed gatherer {act.BuildingToConstruct.Id} on a node with no matching adjacent resource");
        }
    }

    // 20b) Resource-adjacency bitmask (Phase 1 of the map preprocessing plan): direct test
    //      of the precomputed AdjacentResourceMask / LocalGatherableMask + the bit-test
    //      HasMatchingAdjacentResource helper, independent of the generator wiring above.
    //      Verifies a Forest-adjacent node accepts a Woodcutter and rejects a StoneMiner,
    //      catching regressions in either the mask population or the lookup helper.
    static void Test_AdjacencyMaskRecognizesAdjacentResource()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 5, hasGenerator: true);
        w.AddForestNeutral(2); // gatherable Wood next to #1
        w.AddNode(3, owner: 0, workers: 0); // isolated empty grass, no resource adjacency
        w.Connect(1, 2);
        w.Build();

        var p1 = w.GetPlayer(1);
        var view = p1.AI.GetWorldView();
        AI_NodeState mirror1 = null, mirror2 = null, mirror3 = null;
        for (int i = 0; i < view.NumNodes; i++)
        {
            if (view.Nodes[i].NodeId == 1) mirror1 = view.Nodes[i];
            else if (view.Nodes[i].NodeId == 2) mirror2 = view.Nodes[i];
            else if (view.Nodes[i].NodeId == 3) mirror3 = view.Nodes[i];
        }
        AssertTrue(mirror1 != null && mirror2 != null && mirror3 != null, "test mirrors should exist");

        uint woodBit = MapTopologyAnalysis.MaskFor(GoodType.Wood);
        uint stoneBit = MapTopologyAnalysis.MaskFor(GoodType.Stone);

        // The forest neutral itself yields Wood as its local resource.
        AssertTrue((mirror2.LocalGatherableMask & woodBit) != 0,
            "forest neutral #2 should have Wood bit in LocalGatherableMask");

        // #1 (forest-adjacent) reports the Wood bit in its adjacency mask, NOT the Stone bit.
        AssertTrue((mirror1.AdjacentResourceMask & woodBit) != 0,
            "#1 (adjacent to forest #2) should have Wood bit in AdjacentResourceMask");
        AssertTrue((mirror1.AdjacentResourceMask & stoneBit) == 0,
            "#1 should NOT have Stone bit in AdjacentResourceMask (no stone neighbor)");

        // #3 has no neighbors at all, so the mask is 0.
        AssertEq(0u, mirror3.AdjacentResourceMask,
            "isolated #3 should have empty AdjacentResourceMask");

        // Concrete validation of the helper: a real Forest BuildingDefn is accepted on a
        // forest-adjacent node, and a StoneMiner-like defn (Stone gatherer) is rejected.
        BuildingDefn woodcutter = null, stoneMiner = null;
        if (GameDefns.Instance != null)
        {
            foreach (var bd in GameDefns.Instance.BuildingDefns.Values)
            {
                if (!bd.CanGatherResources || bd.ResourceThisNodeCanGoGather == null) continue;
                if (bd.ResourceThisNodeCanGoGather.GoodType == GoodType.Wood && woodcutter == null) woodcutter = bd;
                if (bd.ResourceThisNodeCanGoGather.GoodType == GoodType.Stone && stoneMiner == null) stoneMiner = bd;
            }
        }
        if (woodcutter != null)
            AssertTrue(MapTopologyAnalysis.HasMatchingAdjacentResource(mirror1, woodcutter),
                "Woodcutter should be accepted next to forest");
        if (stoneMiner != null)
            AssertTrue(!MapTopologyAnalysis.HasMatchingAdjacentResource(mirror1, stoneMiner),
                "StoneMiner should be rejected when no Stone is adjacent");
    }

    // 20c) All-pairs distance matrix + Role classification (Phase 2). Builds a tiny graph
    //      with known structure and asserts each invariant: dist[i][i] == 0, symmetry,
    //      a hand-checked shortest path, and the degree-bucket NodeRole labelling.
    static void Test_DistanceMatrixSymmetricAndCorrect()
    {
        // Layout:           #4
        //                    |
        //          #1 -- #2 -- #3        (#3 also connects to #4 -- branch)
        // Degrees:  1     2    3   1
        // Distances from #1: self=0, #2=1, #3=2, #4=3.
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 1, hasGenerator: true);
        w.AddNode(2, owner: 1, workers: 1, hasGenerator: true);
        w.AddNode(3, owner: 1, workers: 1, hasGenerator: true);
        w.AddNode(4, owner: 1, workers: 1, hasGenerator: true);
        w.Connect(1, 2);
        w.Connect(2, 3);
        w.Connect(3, 4);
        w.Build();

        var view = w.GetPlayer(1).AI.GetWorldView();
        AI_NodeState m1 = null, m2 = null, m3 = null, m4 = null;
        for (int i = 0; i < view.NumNodes; i++)
        {
            var node = view.Nodes[i];
            if (node.NodeId == 1) m1 = node;
            else if (node.NodeId == 2) m2 = node;
            else if (node.NodeId == 3) m3 = node;
            else if (node.NodeId == 4) m4 = node;
        }
        AssertTrue(m1 != null && m2 != null && m3 != null && m4 != null, "all mirrors should exist");

        // Self-distance is zero.
        AssertEq(0, m1.DistanceTo[m1.Index], "dist[#1][#1] should be 0");
        AssertEq(0, m4.DistanceTo[m4.Index], "dist[#4][#4] should be 0");

        // Hand-checked distances on a 4-node chain.
        AssertEq(1, m1.DistanceTo[m2.Index], "dist[#1][#2] should be 1");
        AssertEq(2, m1.DistanceTo[m3.Index], "dist[#1][#3] should be 2");
        AssertEq(3, m1.DistanceTo[m4.Index], "dist[#1][#4] should be 3");

        // Symmetry on the bidirectional graph.
        AssertEq(m1.DistanceTo[m3.Index], m3.DistanceTo[m1.Index], "distance should be symmetric (#1<->#3)");
        AssertEq(m2.DistanceTo[m4.Index], m4.DistanceTo[m2.Index], "distance should be symmetric (#2<->#4)");

        // Degree + role classification.
        AssertEq(1, m1.Degree, "#1 should have degree 1");
        AssertEq(2, m2.Degree, "#2 should have degree 2");
        AssertEq(2, m3.Degree, "#3 should have degree 2 in this chain");
        AssertEq(1, m4.Degree, "#4 should have degree 1");
        AssertEq(NodeRole.Leaf, m1.Role, "#1 (deg 1) should be Leaf");
        AssertEq(NodeRole.Corridor, m2.Role, "#2 (deg 2) should be Corridor");
        AssertEq(NodeRole.Leaf, m4.Role, "#4 (deg 1) should be Leaf");
    }

    // 20d) Articulation-point defense (Phase 3 of the preprocessing plan). Builds a
    //      triangle plus a tail (#4 attached only via #2) -- the unique articulation
    //      point is #2. With identical pressure on #1 and #2, the AI must prefer to
    //      reinforce the articulation point because losing it amputates the tail.
    static void Test_DefendArticulationPoint()
    {
        // Graph:
        //   #1 - #2 - #3 - #1 (triangle), #4 - #2 (tail), #5 - #1 / #5 - #2 (enemy P2)
        // Articulation: #2 (its removal isolates #4 from {#1, #3}).
        // #1 and #2 both touch enemy #5 with the same garrison and same own-workers, so
        // their DefensiveDeficits are identical. The only differentiator is articulation.
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 5, hasGenerator: true);
        w.AddNode(2, owner: 1, workers: 5, hasGenerator: true);
        w.AddNode(3, owner: 1, workers: 12, hasGenerator: true); // interior source with safe spare
        w.AddNode(4, owner: 1, workers: 1, hasGenerator: true);  // the tail behind #2
        w.AddNode(5, owner: 2, workers: 8, hasGenerator: true);  // enemy pressuring #1 and #2
        w.Connect(1, 2);
        w.Connect(2, 3);
        w.Connect(1, 3);
        w.Connect(2, 4);
        w.Connect(1, 5);
        w.Connect(2, 5);
        w.Build();

        var view = w.GetPlayer(1).AI.GetWorldView();
        AI_NodeState m1 = null, m2 = null, m3 = null, m4 = null;
        for (int i = 0; i < view.NumNodes; i++)
        {
            var node = view.Nodes[i];
            if (node.NodeId == 1) m1 = node;
            else if (node.NodeId == 2) m2 = node;
            else if (node.NodeId == 3) m3 = node;
            else if (node.NodeId == 4) m4 = node;
        }

        // Articulation flags: only #2 should be flagged. #1/#3 are in a cycle, #4 is a leaf.
        AssertTrue(m2.IsArticulationPoint, "#2 should be articulation (tail #4 hangs off it)");
        AssertTrue(!m1.IsArticulationPoint, "#1 should NOT be articulation (cycle keeps graph connected without it)");
        AssertTrue(!m3.IsArticulationPoint, "#3 should NOT be articulation (cycle)");
        AssertTrue(!m4.IsArticulationPoint, "#4 (leaf) should NOT be articulation");

        // Tune personality so reinforce is the dominant decision (suppress upgrade tempo
        // and attack aggression so we are clearly comparing reinforce vs reinforce).
        var defn = w.GetPlayer(1).AIDefn;
        defn.Caution = 1f;
        defn.Aggression = 0.2f;
        defn.Expansion = 0.2f;
        defn.Tempo = 0f;

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;

        AssertTrue(
            act.Type == AIActionType.SendWorkersToOwnedNode
                || act.Type == AIActionType.SendMultiSourceWorkersToOwnedNode,
            $"expected a reinforce action, got {act.Type}");
        AssertEq(2, act.DestNode != null ? act.DestNode.NodeId : -1,
            $"AI should reinforce articulation #2 over non-articulation #1; chose #{act.DestNode?.NodeId}");
    }

    // 20e) Attack prefers an enemy articulation point over a same-strength leaf. The
    //      bonus must be strong enough to outscore an equally-weak alternative target
    //      when the source can mount either wave.
    static void Test_AttackArticulationPoint()
    {
        // Layout:
        //   #1 (P1, lots of workers) -- #2 (P2, 1w, articulation) -- #3 (P2, 1w, leaf)
        //                          \-- #4 (P2, 1w, leaf, directly off #1)
        // Articulation analysis (all P2-owned, but topology is what matters):
        //   - #2: removing disconnects #3 from {#1, #4}. ARTICULATION.
        //   - #4: removing leaves {#1, #2, #3} connected. NOT articulation.
        var w = new TestWorld();
        // #1 needs enough workers for safe-to-send to cover the regen-padded wave:
        //   pressure=2 (from #2, #4), MinReserve(Caution=0)=1, so safe = N - 3.
        //   required vs an L1 generator at 1 hop = ceil((1 + 4regen + 1) * 1.0) = 6.
        // 12 workers -> safe=9, comfortably above 6 for either target.
        w.AddNode(1, owner: 1, workers: 12, hasGenerator: true);
        w.AddNode(2, owner: 2, workers: 1, hasGenerator: true);
        w.AddNode(3, owner: 2, workers: 1, hasGenerator: true);
        w.AddNode(4, owner: 2, workers: 1, hasGenerator: true);
        w.Connect(1, 2);
        w.Connect(2, 3);
        w.Connect(1, 4);
        w.Build();

        var view = w.GetPlayer(1).AI.GetWorldView();
        AI_NodeState m2 = null, m4 = null;
        for (int i = 0; i < view.NumNodes; i++)
        {
            if (view.Nodes[i].NodeId == 2) m2 = view.Nodes[i];
            else if (view.Nodes[i].NodeId == 4) m4 = view.Nodes[i];
        }
        AssertTrue(m2.IsArticulationPoint, "#2 should be articulation (#3 hangs behind it)");
        AssertTrue(!m4.IsArticulationPoint, "#4 (leaf) should NOT be articulation");

        // Push personality hard toward attack and away from defense / upgrade so the AI
        // genuinely chooses BETWEEN the two attack targets.
        var defn = w.GetPlayer(1).AIDefn;
        defn.Aggression = 1.5f;
        defn.Caution = 0f;
        defn.Expansion = 0.2f;
        defn.Tempo = 0f;

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;

        AssertEq(AIActionType.AttackToNode, act.Type, $"expected an attack, got {act.Type}");
        AssertEq(2, act.DestNode != null ? act.DestNode.NodeId : -1,
            $"AI should attack articulation #2 over leaf #4; chose #{act.DestNode?.NodeId}");
    }

    // 20f) Race-margin / uncontested-expansion bias (Phase 4 of the preprocessing plan).
    //      Two empty build sites beside forests: #2 is a race tie with P2; #3 is uncontested.
    //      With Aggression=0 the AI must prefer building on #3 over #2.
    static void Test_PreferUncontestedExpansion()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 20, hasGenerator: true);
        w.AddNode(2, owner: 0, workers: 0);
        w.AddForestNeutral(4);
        w.AddNode(3, owner: 0, workers: 0);
        w.AddForestNeutral(6);
        w.AddNode(5, owner: 2, workers: 1, hasGenerator: true);
        w.Connect(1, 2);
        w.Connect(2, 4);
        w.Connect(5, 4);
        w.Connect(1, 3);
        w.Connect(3, 6);
        w.Build();

        var view = w.GetPlayer(1).AI.GetWorldView();
        AI_NodeState m2 = null, m3 = null;
        for (int i = 0; i < view.NumNodes; i++)
        {
            if (view.Nodes[i].NodeId == 2) m2 = view.Nodes[i];
            else if (view.Nodes[i].NodeId == 3) m3 = view.Nodes[i];
        }
        AssertEq(0, m2.GetRaceMargin(1), "#2 should be a race tie (both spawns are 1 hop away)");
        AssertGreater(m3.GetRaceMargin(1), 0, "#3 should be uncontested (P2 spawn is >1 hop away)");

        var defn = w.GetPlayer(1).AIDefn;
        defn.Aggression = 0f;
        defn.Expansion = 2f;
        defn.Caution = 0f;
        defn.Tempo = 0f;
        defn.TargetWoodStockpile = 0;
        defn.TargetStoneStockpile = 0;

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;

        bool isCaptureOrBuild =
            act.Type == AIActionType.CaptureNeutralNode || act.Type == AIActionType.ConstructBuildingInEmptyNode;
        AssertTrue(isCaptureOrBuild, $"expected a neutral capture/build, got {act.Type}");
        AssertEq(3, act.DestNode != null ? act.DestNode.NodeId : -1,
            $"AI should prefer uncontested build site #3 over contested #2; chose #{act.DestNode?.NodeId}");
    }

    // 20g) Same-region reinforcement preference (Phase 6). Two triangles joined by a
    //      single bridge form two distinct 2-edge-connected regions. The target #3 sits
    //      in region A and has an enemy pressing it; #4 (across the bridge in region B)
    //      has the largest spare garrison, but draining it would leave region B exposed.
    //      The AI must prefer to pull from an in-region defender (#1 or #2) even though
    //      #4 has more workers available.
    static void Test_SameRegionReinforceFirst()
    {
        var w = new TestWorld();
        // Triangle A (region A): #1, #2, #3
        w.AddNode(1, owner: 1, workers: 15, hasGenerator: true);
        w.AddNode(2, owner: 1, workers: 15, hasGenerator: true);
        w.AddNode(3, owner: 1, workers: 1, hasGenerator: true); // target, under pressure
        // Triangle B (region B): #4, #5, #6
        w.AddNode(4, owner: 1, workers: 30, hasGenerator: true); // bridge endpoint w/ largest reserve
        w.AddNode(5, owner: 1, workers: 5, hasGenerator: true);
        w.AddNode(6, owner: 1, workers: 5, hasGenerator: true);
        // Enemy adjacent to #3
        w.AddNode(7, owner: 2, workers: 8, hasGenerator: true);

        w.Connect(1, 2); w.Connect(2, 3); w.Connect(1, 3); // triangle A
        w.Connect(4, 5); w.Connect(5, 6); w.Connect(4, 6); // triangle B
        w.Connect(3, 4); // bridge
        w.Connect(3, 7); // enemy
        w.Build();

        // Region invariants must hold before we test the AI's choice.
        var view = w.GetPlayer(1).AI.GetWorldView();
        AI_NodeState m1 = null, m2 = null, m3 = null, m4 = null, m5 = null, m6 = null;
        for (int i = 0; i < view.NumNodes; i++)
        {
            var n = view.Nodes[i];
            if (n.NodeId == 1) m1 = n;
            else if (n.NodeId == 2) m2 = n;
            else if (n.NodeId == 3) m3 = n;
            else if (n.NodeId == 4) m4 = n;
            else if (n.NodeId == 5) m5 = n;
            else if (n.NodeId == 6) m6 = n;
        }
        AssertEq(m1.RegionId, m2.RegionId, "#1 and #2 should share region A");
        AssertEq(m2.RegionId, m3.RegionId, "#3 should share region A");
        AssertEq(m4.RegionId, m5.RegionId, "#4 and #5 should share region B");
        AssertEq(m5.RegionId, m6.RegionId, "#6 should share region B");
        AssertTrue(m3.RegionId != m4.RegionId, "region A and region B should be distinct");

        // Tune personality so reinforce dominates and we are comparing reinforce-vs-reinforce
        // sources, not reinforce-vs-attack.
        var defn = w.GetPlayer(1).AIDefn;
        defn.Caution = 1f;
        defn.Aggression = 0.2f;
        defn.Expansion = 0f;
        defn.Tempo = 0f;

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;

        AssertTrue(act.Type == AIActionType.SendWorkersToOwnedNode
                || act.Type == AIActionType.SendMultiSourceWorkersToOwnedNode,
            $"expected reinforce, got {act.Type}");
        AssertEq(3, act.DestNode != null ? act.DestNode.NodeId : -1,
            "reinforce should target the pressured #3");

        // The chosen source(s) must live in #3's region, NOT cross the bridge to #4.
        if (act.Type == AIActionType.SendWorkersToOwnedNode)
        {
            AssertTrue(act.SourceNode.RegionId == m3.RegionId,
                $"single-source should be in-region; #{act.SourceNode.NodeId} (region {act.SourceNode.RegionId}) is not in #3's region ({m3.RegionId})");
        }
        else
        {
            foreach (var kv in act.AttackFromNodes)
                AssertTrue(kv.Key.RegionId == m3.RegionId,
                    $"multi-source should be in-region; #{kv.Key.NodeId} (region {kv.Key.RegionId}) crosses a bridge");
        }
    }

    // 20h) Cross-bridge reinforcement is allowed when same-region is dry (Phase 6). Same
    //      layout as Test_SameRegionReinforceFirst but the in-region defenders are stripped
    //      to 1 worker each (no safe-to-send). The wave MUST then spill across the bridge
    //      to #4 -- the alternative is dying because we held a same-region preference too
    //      hard.
    static void Test_CrossBridgeReinforceWhenDry()
    {
        var w = new TestWorld();
        // Triangle A: in-region defenders are bare (no safe spare).
        w.AddNode(1, owner: 1, workers: 1, hasGenerator: true);
        w.AddNode(2, owner: 1, workers: 1, hasGenerator: true);
        w.AddNode(3, owner: 1, workers: 1, hasGenerator: true); // target
        // Triangle B: across the bridge, plenty of workers.
        w.AddNode(4, owner: 1, workers: 30, hasGenerator: true);
        w.AddNode(5, owner: 1, workers: 5, hasGenerator: true);
        w.AddNode(6, owner: 1, workers: 5, hasGenerator: true);
        w.AddNode(7, owner: 2, workers: 8, hasGenerator: true);

        w.Connect(1, 2); w.Connect(2, 3); w.Connect(1, 3);
        w.Connect(4, 5); w.Connect(5, 6); w.Connect(4, 6);
        w.Connect(3, 4);
        w.Connect(3, 7);
        w.Build();

        var defn = w.GetPlayer(1).AIDefn;
        defn.Caution = 1f;
        defn.Aggression = 0.2f;
        defn.Expansion = 0f;
        defn.Tempo = 0f;

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;

        // The AI may pick reinforce OR fall back to do-nothing depending on score thresholds.
        // What it absolutely must NOT do is reinforce #3 from #1 or #2 (they have nothing
        // to send) -- and if it DOES reinforce, the only viable source is across the bridge.
        if (act.Type == AIActionType.SendWorkersToOwnedNode
            || act.Type == AIActionType.SendMultiSourceWorkersToOwnedNode)
        {
            AssertEq(3, act.DestNode.NodeId, "reinforce should target #3");
            if (act.Type == AIActionType.SendWorkersToOwnedNode)
                AssertEq(4, act.SourceNode.NodeId,
                    $"with same-region dry, must source from cross-bridge #4; got #{act.SourceNode.NodeId}");
            else
                foreach (var kv in act.AttackFromNodes)
                    AssertTrue(kv.Key.NodeId == 4 || kv.Key.NodeId == 5 || kv.Key.NodeId == 6,
                        $"multi-source must use the dry side's region (#4/#5/#6); got #{kv.Key.NodeId}");
        }
    }

    // 20i) Build-site quality (Phase 7). Two empty neutrals are equally close to source
    //      #1; #2 is a Corridor (degree 2), #3 is a Hub (degree 4). A worker-generator
    //      build (Barracks-like) must score strictly higher on the Hub site so the AI
    //      prefers it as a launchpad. We hand-construct AICandidates and call ScoreBuild
    //      directly to isolate the site-quality bonus from BuildGenerator's filtering
    //      and the GameDefns-driven affordability checks.
    static void Test_BarracksPrefersHubOverCorridor()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 30, hasGenerator: true);   // source
        w.AddNode(2, owner: 0, workers: 0);                         // Corridor candidate
        w.AddNode(3, owner: 0, workers: 0);                         // Hub candidate
        w.AddNode(4, owner: 1, workers: 5, hasGenerator: true);
        w.AddNode(5, owner: 1, workers: 5, hasGenerator: true);
        w.AddNode(6, owner: 1, workers: 5, hasGenerator: true);
        w.AddNode(7, owner: 1, workers: 5, hasGenerator: true);

        w.Connect(1, 2);
        w.Connect(2, 7); // #2 degree 2 -> Corridor
        w.Connect(1, 3);
        w.Connect(3, 4);
        w.Connect(3, 5);
        w.Connect(3, 6); // #3 degree 4 -> Hub
        w.Build();

        var view = w.GetPlayer(1).AI.GetWorldView();
        AI_NodeState mSource = null, mCorridor = null, mHub = null;
        for (int i = 0; i < view.NumNodes; i++)
        {
            var n = view.Nodes[i];
            if (n.NodeId == 1) mSource = n;
            else if (n.NodeId == 2) mCorridor = n;
            else if (n.NodeId == 3) mHub = n;
        }
        AssertEq(NodeRole.Corridor, mCorridor.Role, "#2 (deg 2) should classify as Corridor");
        AssertEq(NodeRole.Hub, mHub.Role, "#3 (deg 4) should classify as Hub");

        // Drive the AI so analysis is populated (we'll reuse its StrategicAnalysis below).
        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var analysis = p1.AI.GetAnalysis();

        // Hand-rolled Barracks defn -- ScoreBuild only cares about the CanGenerateWorkers
        // flag and the BuildingType for missing-tier detection. Defaulting to a unique
        // BuildingType (None) keeps the MissingTierBonus contribution equal for both
        // candidates so the only score delta is Phase 7's site-quality bonus.
        var barracksDefn = ScriptableObject.CreateInstance<BuildingDefn>();
        barracksDefn.Id = "TestBarracksForSiteQuality";
        barracksDefn.Name = "TestBarracksForSiteQuality";
        barracksDefn.IsEnabled = true;
        barracksDefn.CanBeBuiltByPlayer = true;
        barracksDefn.CanGenerateWorkers = true;
        barracksDefn.BuildingType = BuildingType.None; // same for both candidates -> wash
        barracksDefn.ConstructionRequirements = new List<Good_CraftingRequirements>();

        var weights = new PersonalityWeights(1f, 1f, 1f, 1f);

        var corridorCandidate = new AICandidate
        {
            Type = AIActionType.ConstructBuildingInEmptyNode,
            SourceNode = mSource,
            DestNode = mCorridor,
            Count = 3,
            BuildingToConstruct = barracksDefn,
        };
        ActionUtility.ScoreBuild(corridorCandidate, view, analysis, weights);

        var hubCandidate = new AICandidate
        {
            Type = AIActionType.ConstructBuildingInEmptyNode,
            SourceNode = mSource,
            DestNode = mHub,
            Count = 3,
            BuildingToConstruct = barracksDefn,
        };
        ActionUtility.ScoreBuild(hubCandidate, view, analysis, weights);

        AssertGreater(hubCandidate.Score, corridorCandidate.Score,
            $"Barracks on Hub ({hubCandidate.Score:F2}) should beat Corridor ({corridorCandidate.Score:F2})");
    }

    // 21) Pending-capture neutral must not be reinforced. Before the fix, AI_NodeState.Refresh
    //     overwrote OwnedBy = PendingCaptureBy, making the neutral look "already mine" to
    //     ReinforceGenerator -- which then sent workers with WorkerIntent.Reinforce. Those
    //     workers arrive at a still-neutral node and die (ResolveWorkerArrival only allows
    //     CaptureAndConstruct intents to flip ownership of an empty neutral). The new Refresh
    //     uses a PendingMyCapture flag instead so the world looks like the real game state.
    static void Test_NoReinforceOnPendingCapture()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 20, hasGenerator: true);
        w.AddNode(2, owner: 0, workers: 0);
        w.Connect(1, 2);
        w.Build();

        var neutral = w.NodesById[2];
        neutral.PendingCaptureBy = w.GetPlayer(1);
        neutral.IncomingByPlayer[w.GetPlayer(1)] = 5; // 5 workers already in flight
        w.Town.WorldRevision++;

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;

        bool reinforcingPendingNeutral =
            (act.Type == AIActionType.SendWorkersToOwnedNode || act.Type == AIActionType.SendMultiSourceWorkersToOwnedNode)
            && act.DestNode != null && act.DestNode.NodeId == 2;
        AssertTrue(!reinforcingPendingNeutral,
            "should not reinforce a still-neutral node with a capture wave in flight (workers would die)");

        // Also: no second Capture proposal against the same neutral.
        bool reCapturing =
            (act.Type == AIActionType.CaptureNeutralNode || act.Type == AIActionType.ConstructBuildingInEmptyNode)
            && act.DestNode != null && act.DestNode.NodeId == 2;
        AssertTrue(!reCapturing,
            "should not double-dispatch capture while one is already in flight");
    }

    // ============================================================================
    // Attack sizing (drip-feed regression) coverage
    // ============================================================================

    // 22) Capture-flip rule: an enemy node with N defenders requires STRICTLY more than N
    //     attackers landed alive (last attacker flips ownership; the previous N each kill
    //     one defender and die). The previous bug sized attacks at exactly N and so could
    //     never actually capture even when math otherwise checked out.
    static void Test_AttackSizesForCaptureFlip()
    {
        var w = new TestWorld();
        // Source workers sized so safe-to-send is just enough for the attack but does NOT
        // sit far over MaxWorkers (large surplus would let UpgradeBuilding outscore the
        // attack via the over-cap bonus, hiding the property under test).
        // Caution=0 -> AttackOverkill=1.0 (bare minimum); MinReserve=1; pressure from
        // 5 enemy workers = ceil(5*1.0)=5; required to capture = ceil((5+0+1)*1.0)=6.
        // workers=12 -> safe=12-1-5=6 (exactly enough).
        w.AddNode(1, owner: 1, workers: 12, hasGenerator: true);
        w.AddNode(2, owner: 2, workers: 5, hasGenerator: false); // no generator -> regen=0
        w.Connect(1, 2);
        w.Build();

        var defn = w.GetPlayer(1).AIDefn;
        defn.Caution = 0f;       // overkill = 1.0 (bare minimum)
        defn.Aggression = 1.5f;  // make sure attack outscores other choices
        defn.Tempo = 0f;         // suppress UpgradeBuilding so it doesn't shadow the attack

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;

        AssertEq(AIActionType.AttackToNode, act.Type, $"expected attack, got {act.Type}");
        int sent = SumValues(act.AttackFromNodes);
        AssertGreater(sent, 5,
            $"attack must send STRICTLY more than 5 defenders (sent {sent} would only reduce to 0, not capture)");
    }

    // 23) Travel-regen rule: an enemy node with a worker generator regenerates defenders
    //     while the wave travels. The wave size must include an expected-regen term so a
    //     2-hop attack against a Barracks isn't dead on arrival.
    static void Test_AttackAccountsForRegen()
    {
        // Two layouts, identical except enemy generator presence. The wave size against
        // the regenerating enemy must be strictly larger than against a non-regenerating
        // enemy with the same defender count.
        int sentWithRegen = MeasureAttackSize(enemyGenerates: true);
        int sentWithoutRegen = MeasureAttackSize(enemyGenerates: false);
        AssertGreater(sentWithRegen, sentWithoutRegen,
            $"attack vs regenerating defender ({sentWithRegen}) should be larger than vs static defender ({sentWithoutRegen})");
    }

    static int MeasureAttackSize(bool enemyGenerates)
    {
        var w = new TestWorld();
        // Sized so safe-to-send covers the regenerating-defender wave (~11) but the
        // source isn't far over MaxWorkers (avoids UpgradeBuilding's surplus bonus
        // outscoring the attack we want to measure).
        w.AddNode(1, owner: 1, workers: 18, hasGenerator: true);
        w.AddNode(2, owner: 2, workers: 6, hasGenerator: enemyGenerates);
        w.Connect(1, 2);
        w.Build();
        var defn = w.GetPlayer(1).AIDefn;
        defn.Caution = 0f; // isolate regen contribution from overkill
        defn.Aggression = 1.5f;
        defn.Tempo = 0f;   // suppress UpgradeBuilding

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;
        if (act.Type != AIActionType.AttackToNode)
            throw new Exception($"expected AttackToNode (regen={enemyGenerates}), got {act.Type}");
        return SumValues(act.AttackFromNodes);
    }

    // 24) Anti-dribble: the original drip-feed bug was the AI sending 2 attackers every
    //     few seconds at an 8-defender Barracks regenerating 2 workers per 4 seconds. Net
    //     defender progress: zero. Now if the available source(s) cannot muster the full
    //     capture-flip+regen wave, NO attack is emitted -- DoNothing or a different action
    //     is preferred over a doomed partial wave.
    static void Test_NoDribbleAttackOnBarracks()
    {
        var w = new TestWorld();
        // Source with only enough spare workers to send a TINY wave (~2 workers) -- nowhere
        // near the new sizing for an 8-defender regenerating Barracks.
        w.AddNode(1, owner: 1, workers: 5, hasGenerator: true);
        w.AddNode(2, owner: 2, workers: 8, hasGenerator: true); // Barracks-like (regens)
        w.Connect(1, 2);
        w.Build();
        var defn = w.GetPlayer(1).AIDefn;
        defn.Caution = 0.6f;     // matches the live-play repro
        defn.Aggression = 1.5f;
        defn.Tempo = 0.7f;

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;

        AssertTrue(act.Type != AIActionType.AttackToNode,
            $"should not emit a dribble attack against a regenerating defender (got {act.Type} sent={SumValues(act.AttackFromNodes)})");
    }

    // 25) In-flight credit: when a previous wave is already in flight at the target, the
    //     fresh wave should be sized to FILL THE GAP, not to ignore the in-flight workers
    //     (which would over-send) and not to re-target a remaining-defender count
    //     (which was the old bug). Concretely: 8 defenders + 5 already in flight should
    //     emit a fresh wave large enough that fresh + 5 >= captureFloor, and AttackAlready-
    //     Sufficient must STILL be false (5 in flight < 8+1 capture floor).
    static void Test_AttackCreditsInFlight()
    {
        var w = new TestWorld();
        // Source workers sized to mount the FRESH portion of the wave (4) without
        // sitting far over MaxWorkers -- otherwise UpgradeBuilding's surplus bonus
        // shadows the attack we're testing. workers=12 -> safe=12-1-3=8 (pressure
        // sees post-deducted enemy NumWorkers max(1, 8-5)=3).
        w.AddNode(1, owner: 1, workers: 12, hasGenerator: true);
        w.AddNode(2, owner: 2, workers: 8, hasGenerator: false);
        w.Connect(1, 2);
        w.Build();

        // Pre-load 5 attackers in flight from P1 to enemy #2.
        var enemy = w.NodesById[2];
        enemy.IncomingByPlayer[w.GetPlayer(1)] = 5;
        w.Town.WorldRevision++;

        var defn = w.GetPlayer(1).AIDefn;
        defn.Caution = 0f;
        defn.Aggression = 1.5f;
        defn.Tempo = 0f; // suppress UpgradeBuilding so it doesn't outscore the attack

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;

        AssertEq(AIActionType.AttackToNode, act.Type,
            $"5 in flight + 8 defender + 1 flip = need 4+ more, source has plenty; expected attack, got {act.Type}");
        int fresh = SumValues(act.AttackFromNodes);
        AssertTrue(fresh + 5 > 8,
            $"committed force ({fresh} fresh + 5 in flight) must exceed defenders (8) for capture-flip");
        // And NOT over-send: with no regen, the math says fresh = (8+1)-5 = 4. We allow a
        // small margin for any overkill rounding, but reject e.g. an 8-worker fresh wave
        // that ignores the in-flight credit.
        AssertTrue(fresh <= 8,
            $"fresh wave ({fresh}) should not double-count vs the 5 already in flight");
    }

    // ============================================================================
    // Assertion helpers
    // ============================================================================
    static void AssertTrue(bool cond, string msg)
    {
        if (!cond) throw new Exception(msg);
    }
    static void AssertEq<T>(T expected, T actual, string msg)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{msg}: expected {expected}, got {actual}");
    }
    static void AssertGreater(float a, float b, string msg)
    {
        if (!(a > b)) throw new Exception($"{msg}: {a} not > {b}");
    }

    static int SumValues(Dictionary<AI_NodeState, int> dict)
    {
        int s = 0;
        foreach (var kv in dict) s += kv.Value;
        return s;
    }

    // ============================================================================
    // Test world builder
    // ============================================================================
    /// In-memory scaffolding for a tiny TownData + PlayerAI graph. ScriptableObject
    /// instances are constructed via CreateInstance, so this works as long as Unity is
    /// alive (which is true when invoked from a button in the existing AITestScene).
    class TestWorld
    {
        public TownData Town;
        public readonly Dictionary<int, NodeData> NodesById = new();
        public readonly Dictionary<int, NodeDefn> DefnsById = new();
        TownDefn townDefn;
        WorkerDefn workerDefn;
        PlayerAIDefn[] aiDefns = new PlayerAIDefn[3];

        BuildingDefn defaultGeneratorDefn;
        BuildingDefn forestDefn;

        public TestWorld()
        {
            townDefn = ScriptableObject.CreateInstance<TownDefn>();
            workerDefn = ScriptableObject.CreateInstance<WorkerDefn>();
            workerDefn.Name = "TestWorker";
            for (int i = 0; i < 3; i++)
            {
                aiDefns[i] = ScriptableObject.CreateInstance<PlayerAIDefn>();
                aiDefns[i].Name = $"Player {i + 1}";
                aiDefns[i].Aggression = 1f;
                aiDefns[i].Expansion = 1f;
                aiDefns[i].Caution = 1f;
                aiDefns[i].Tempo = 1f;
                aiDefns[i].TargetWoodStockpile = 0;
                aiDefns[i].TargetStoneStockpile = 0;
                aiDefns[i].DecisionIntervalSeconds = 1f;
                aiDefns[i].DecisionVarianceSeconds = 0f;
            }

            defaultGeneratorDefn = MakeBuildingDefn("TestGenerator", canGenerateWorkers: true, canBeUpgraded: true);
            forestDefn = MakeBuildingDefn("TestForest", canBeGatheredFrom: true, gatheredGood: GoodType.Wood);
        }

        public PlayerData GetPlayer(int id) => Town.Players[id];

        public void AddNode(int id, int owner = 0, int workers = 0, bool hasGenerator = false, bool upgradable = false)
        {
            var defn = new NodeDefn
            {
                NodeId = id,
                OwnedByPlayerId = owner,
                NumStartingWorkers = workers,
                WorldLoc = new Vector3(id, 0, 0),
                StartingBuilding = hasGenerator ? defaultGeneratorDefn : null,
            };
            townDefn.Nodes.Add(defn);
            DefnsById[id] = defn;
        }

        public void AddForestNeutral(int id)
        {
            var defn = new NodeDefn
            {
                NodeId = id,
                OwnedByPlayerId = 0,
                NumStartingWorkers = 0,
                WorldLoc = new Vector3(id, 0, 0),
                StartingBuilding = forestDefn,
            };
            townDefn.Nodes.Add(defn);
            DefnsById[id] = defn;
        }

        public void Connect(int a, int b)
        {
            townDefn.NodeConnections.Add(new NodeConnectionDefn
            {
                Nodes = new Vector2Int(a, b),
                IsBidirectional = true,
            });
        }

        public void Build()
        {
            Town = new TownData(townDefn, workerDefn, aiDefns);
            foreach (var n in Town.Nodes) NodesById[n.NodeId] = n;
        }

        /// Override chokepoint score post-build. ChokepointAnalysis runs once in the
        /// TownData ctor and scores nodes between two "camps" (initially-owned nodes).
        /// For tests that hand-craft chokepoint topology we override the score AFTER
        /// build, on both the live NodeData and every PlayerAI's mirror.
        public void SetChokepoint(int nodeId, float score)
        {
            NodesById[nodeId].ChokepointScore = score;
            foreach (var p in Town.Players)
            {
                if (p?.AI == null) continue;
                var view = p.AI.GetWorldView();
                if (view?.Nodes == null) continue;
                for (int i = 0; i < view.NumNodes; i++)
                    if (view.Nodes[i].NodeId == nodeId)
                        view.Nodes[i].ChokepointScore = score;
            }
        }

        public void SetAttackHeat(int nodeId, float heat)
        {
            NodesById[nodeId].AttackHeat = heat;
        }

        static BuildingDefn MakeBuildingDefn(string id, bool canGenerateWorkers = false, bool canBeUpgraded = false, bool canBeGatheredFrom = false, GoodType gatheredGood = GoodType.Unset)
        {
            var bd = ScriptableObject.CreateInstance<BuildingDefn>();
            bd.Id = id;
            bd.Name = id;
            bd.IsEnabled = true;
            bd.CanBeBuiltByPlayer = !canBeGatheredFrom;
            bd.CanGenerateWorkers = canGenerateWorkers;
            bd.CanBeUpgraded = canBeUpgraded;
            bd.CanBeGatheredFrom = canBeGatheredFrom;
            bd.SecondsPerWorkerGenerated = 2f;
            bd.ConstructionRequirements = new List<Good_CraftingRequirements>();
            bd.BuildingType = canGenerateWorkers ? BuildingType.Barracks
                            : canBeGatheredFrom ? (gatheredGood == GoodType.Stone ? BuildingType.StoneMine : BuildingType.Forest)
                            : BuildingType.None;

            if (canBeGatheredFrom)
            {
                var good = ScriptableObject.CreateInstance<GoodDefn>();
                good.Id = $"TestGood_{gatheredGood}";
                good.GoodType = gatheredGood;
                bd.ResourceGatheredFromThisNode = good;
            }
            return bd;
        }
    }
}
