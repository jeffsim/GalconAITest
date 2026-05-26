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
        Run(results, "no gatherer build without adjacent resource", Test_GathererNeedsAdjacentResource);
        Run(results, "no reinforce on neutral with capture in flight", Test_NoReinforceOnPendingCapture);

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
    //    enemy (#4) -- this way the interior sources aren't drained by their own defensive
    //    reserve and have spare workers to allocate to the wave.
    static void Test_MultiSourceAttack()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 8, hasGenerator: true); // interior source
        w.AddNode(2, owner: 1, workers: 8, hasGenerator: true); // interior source
        w.AddNode(3, owner: 1, workers: 5, hasGenerator: true); // frontier relay
        w.AddNode(4, owner: 2, workers: 6, hasGenerator: true); // enemy (single src can't muster ceil(6*1.25)=8 alone after MinReserve)
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

    // 8) Resource shortage drives capture: when wood inventory is zero and target stockpile
    //    is high, the AI should prefer to capture a Forest resource node next door.
    static void Test_ResourceShortage()
    {
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 20, hasGenerator: true);
        w.AddForestNeutral(2);                                 // gatherable Wood
        w.Connect(1, 2);
        w.Build();

        var defn = w.GetPlayer(1).AIDefn;
        defn.TargetWoodStockpile = 30;                          // high shortage
        defn.TargetStoneStockpile = 0;
        defn.Expansion = 2f;                                    // strongly weights captures

        var p1 = w.GetPlayer(1);
        p1.AI.InvalidateDecisionCache();
        p1.AI.Update(w.Town);
        var act = p1.AI.BestNextActionToTake;

        AssertEq(AIActionType.CaptureNeutralResourceNode, act.Type, $"expected resource capture, got {act.Type}");
        AssertEq(2, act.DestNode.NodeId, "capture should target the Forest neutral");
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
        w.AddForestNeutral(2);
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

        AssertTrue(act.Type != AIActionType.CaptureNeutralResourceNode || act.DestNode.NodeId != 2,
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
        // Source workers must be <= MaxWorkers so the over-cap surplus bonus does not pull
        // UpgradeBuilding into competition with the attack we want to isolate. workers=9
        // means safe = 9-MinReserve(2)-pressure(2) = 5 = exactly enough for a 5-worker attack
        // (required = ceil(2 * 1.25) = 3, but we send at least 3).
        var w = new TestWorld();
        w.AddNode(1, owner: 1, workers: 9, hasGenerator: true);
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
        w.AddForestNeutral(2);
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
        AssertTrue(act.Type != AIActionType.CaptureNeutralResourceNode,
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
    //     hemorrhaging economy (Debug_WorldTurn decays 1 per turn while over-cap). The AI
    //     must recognise this and choose UpgradeBuilding rather than DoNothing.
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
        w.AddForestNeutral(2);
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
            act.Type == AIActionType.CaptureNeutralResourceNode
            && act.DestNode != null && act.DestNode.NodeId == 2;
        AssertTrue(!reCapturing,
            "should not double-dispatch capture while one is already in flight");
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
