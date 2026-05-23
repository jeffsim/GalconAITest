using UnityEngine;

[CreateAssetMenu()]
public class PlayerAIDefn : BaseDefn
{
    public string Name;
    public Color Color = Color.white;

    [Header("Tactic Weights")]
    [Tooltip("Multiplier for capturing neutral nodes and other territory-growth actions.")]
    [Range(0f, 2f)] public float TerritoryExpansionWeight = 1f;
    [Tooltip("Multiplier for upgrades, construction, and other in-place economic growth.")]
    [Range(0f, 2f)] public float EconomicExpansionWeight = 1f;
    [Range(0f, 2f)] public float DefenseWeight = 1f;
    [Range(0f, 2f)] public float AggressivenessWeight = 1f;

    [Header("Realtime Decision")]
    [Tooltip("Average seconds between this AI's decisions in realtime mode.")]
    [Range(0.05f, 10f)] public float DecisionIntervalSeconds = 1.0f;
    [Tooltip("Uniform +/- variance applied to DecisionIntervalSeconds each time the next decision is scheduled.")]
    [Range(0f, 5f)] public float DecisionVarianceSeconds = 0.5f;
}
