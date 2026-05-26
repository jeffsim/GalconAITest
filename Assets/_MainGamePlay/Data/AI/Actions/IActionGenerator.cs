using System.Collections.Generic;

/// One method, one job: produce candidate moves for one action family.
///
/// Implementations should:
///   - read worldView and analysis to determine viable targets
///   - allocate force from valid source nodes (single or multi)
///   - construct AICandidate instances via PlayerAI.AcquireCandidate
///   - score each candidate via the appropriate ActionUtility scorer
///   - add to `sink` only if the score is > 0 (vetoed candidates are discarded)
public interface IActionGenerator
{
    void Generate(
        AIWorldView worldView,
        StrategicAnalysis analysis,
        PersonalityWeights personality,
        PlayerAI ai,
        List<AICandidate> sink);
}
