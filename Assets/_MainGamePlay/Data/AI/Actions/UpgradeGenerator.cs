using System.Collections.Generic;

/// Generates UpgradeBuilding candidates for owned nodes at capacity. Upgrading halves the
/// node's workers and doubles MaxWorkers; the post-upgrade safety check lives in
/// ActionUtility.ScoreUpgrade (with explicit risk penalty when the halved garrison would
/// fall below current frontier pressure).
public class UpgradeGenerator : IActionGenerator
{
    public void Generate(
        AIWorldView view,
        StrategicAnalysis analysis,
        PersonalityWeights p,
        PlayerAI ai,
        List<AICandidate> sink)
    {
        for (int i = 0; i < view.NumNodes; i++)
        {
            var node = view.Nodes[i];
            if (!analysis.IsOwned[i]) continue;
            if (node.BuildingDefn == null || !node.BuildingDefn.CanBeUpgraded) continue;
            if (node.NumWorkers < node.MaxWorkers) continue;

            var c = ai.AcquireCandidate();
            c.Type = AIActionType.UpgradeBuilding;
            c.SourceNode = node;
            ActionUtility.ScoreUpgrade(c, view, analysis, p);
            if (c.Score > 0f) sink.Add(c); else ai.ReleaseCandidate(c);
        }
    }
}
