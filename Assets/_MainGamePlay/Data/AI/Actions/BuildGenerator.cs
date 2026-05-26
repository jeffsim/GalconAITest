using System.Collections.Generic;
using UnityEngine;

/// Generates ConstructBuildingInEmptyNode candidates: from an owned node, send workers to
/// an adjacent empty neutral and construct a building there. The single-source variant
/// only -- multi-source captures of empty neutrals are emitted by CaptureGenerator.
public class BuildGenerator : IActionGenerator
{
    /// Per (source, target) pair, emit at most this many building options. Limits branching
    /// when 6 different buildings would all be technically buildable but only the top-2
    /// are actually worth scoring against everything else.
    const int MaxBuildingsPerSite = 2;

    public void Generate(
        AIWorldView view,
        StrategicAnalysis analysis,
        PersonalityWeights p,
        PlayerAI ai,
        List<AICandidate> sink)
    {
        if (GameDefns.Instance == null) return;

        var buildable = new List<BuildingDefn>();
        foreach (var bd in GameDefns.Instance.BuildingDefns.Values)
            if (bd.CanBeBuiltByPlayer) buildable.Add(bd);

        for (int i = 0; i < view.NumNodes; i++)
        {
            var source = view.Nodes[i];
            if (!analysis.IsOwned[i]) continue;
            int safe = analysis.SafeToSendFrom[i];
            if (safe < StrategicAnalysis.MinCaptureWave) continue;

            for (int k = 0; k < source.NumNeighbors; k++)
            {
                var target = source.NeighborNodes[k];
                if (target.OwnedBy != null) continue;
                if (target.HasBuilding) continue; // CaptureGenerator handles resource neutrals
                if (target.PendingMyCapture) continue; // a capture wave is already converging here
                if (target.NumWorkers > safe) continue;

                EmitTopBuildings(source, target, safe, buildable, view, analysis, p, ai, sink);
            }
        }
    }

    static void EmitTopBuildings(
        AI_NodeState source,
        AI_NodeState target,
        int safeToSend,
        List<BuildingDefn> buildable,
        AIWorldView view,
        StrategicAnalysis analysis,
        PersonalityWeights p,
        PlayerAI ai,
        List<AICandidate> sink)
    {
        // Light preselect: keep only buildings we can afford AND that are buildable here.
        // The terrain-adjacency check rejects e.g. a StoneMiner proposal on a grass node
        // (would gather nothing) before it ever gets a score.
        var affordable = new List<(BuildingDefn defn, float quickScore)>();
        foreach (var bd in buildable)
        {
            if (!CanAfford(bd, view)) continue;
            if (!HasMatchingAdjacentResource(target, bd)) continue;
            float s = 1f;
            if (analysis.IsBuildingTypeMissing(bd.BuildingType)) s += 3f;
            if (bd.CanGenerateWorkers) s += 2f;
            if (bd.CanGatherResources && bd.ResourceThisNodeCanGoGather != null)
            {
                int shortage = analysis.GetResourceShortage(bd.ResourceThisNodeCanGoGather.GoodType);
                s += Mathf.Min(4f, shortage * 0.2f);
            }
            affordable.Add((bd, s));
        }
        if (affordable.Count == 0) return;
        affordable.Sort((a, b) => b.quickScore.CompareTo(a.quickScore));

        int defenderGarrison = target.NumWorkers;
        int required = Mathf.CeilToInt(Mathf.Max(1, defenderGarrison) * p.AttackOverkill);
        int send = Mathf.Clamp(required, StrategicAnalysis.MinCaptureWave, safeToSend);

        int emitted = 0;
        for (int i = 0; i < affordable.Count && emitted < MaxBuildingsPerSite; i++)
        {
            var bd = affordable[i].defn;
            var c = ai.AcquireCandidate();
            c.Type = AIActionType.ConstructBuildingInEmptyNode;
            c.SourceNode = source;
            c.DestNode = target;
            c.Count = send;
            c.BuildingToConstruct = bd;
            ActionUtility.ScoreBuild(c, view, analysis, p);
            if (c.Score > 0f)
            {
                sink.Add(c);
                emitted++;
            }
            else
            {
                ai.ReleaseCandidate(c);
            }
        }
    }

    static bool CanAfford(BuildingDefn defn, AIWorldView view)
    {
        if (defn.ConstructionRequirements == null) return true;
        foreach (var req in defn.ConstructionRequirements)
            if (view.GetInventory(req.Good.GoodType) < req.Amount) return false;
        return true;
    }

    /// True iff this gatherer building would have at least one adjacent resource node that
    /// produces its required good. Non-gatherer buildings (Outpost, Barracks, etc.) always
    /// return true. A StoneMiner with no adjacent Stone deposit returns false -- it would
    /// generate zero resource per turn so it is purely an economy waste.
    static bool HasMatchingAdjacentResource(AI_NodeState target, BuildingDefn bd)
    {
        if (!bd.CanGatherResources || bd.ResourceThisNodeCanGoGather == null) return true;
        var needed = bd.ResourceThisNodeCanGoGather.GoodType;
        for (int k = 0; k < target.NumNeighbors; k++)
        {
            var nb = target.NeighborNodes[k];
            if (nb.CanBeGatheredFrom && nb.ResourceGatheredFromThisNode == needed)
                return true;
        }
        return false;
    }
}
