using UnityEngine;

/// Personality definition for an AI player. Four orthogonal dials -- see
/// PersonalityWeights for full semantics. Each one is designed to move ONE observable
/// axis of behavior without bleeding into the others.
[CreateAssetMenu()]
public class PlayerAIDefn : BaseDefn
{
    public string Name;
    public Color Color = Color.white;

    [Header("Personality (each dial moves one observable axis)")]
    [Tooltip("Preference for attacks vs. all other actions. 0 = will never attack; 2 = strongly weights attack scoring.")]
    [Range(0f, 2f)] public float Aggression = 1f;

    [Tooltip("Preference for capturing neutrals and building economy. 0 = stagnant; 2 = aggressive expander.")]
    [Range(0f, 2f)] public float Expansion = 1f;

    [Tooltip("Safety margin on every send AND attack overkill multiplier. 0 = drains nodes to the legal minimum; 2 = leaves a large garrison and over-commits on attacks.")]
    [Range(0f, 2f)] public float Caution = 1f;

    [Tooltip("Patience -- preference for upgrades & long-term investment over immediate force. 0 = always prefer force; 2 = always prefer building up.")]
    [Range(0f, 2f)] public float Tempo = 1f;

    [Header("Realtime decision cadence")]
    [Tooltip("Average seconds between decisions in realtime mode.")]
    [Range(0.05f, 10f)] public float DecisionIntervalSeconds = 1.0f;
    [Tooltip("Uniform +/- jitter applied to the interval each time the next decision is scheduled.")]
    [Range(0f, 5f)] public float DecisionVarianceSeconds = 0.5f;

    [Header("Resource stockpile targets")]
    [Tooltip("Desired minimum wood on hand. Drives demand for wood-gathering captures and builds.")]
    public int TargetWoodStockpile = 15;
    [Tooltip("Desired minimum stone on hand. Drives demand for stone-gathering captures and builds.")]
    public int TargetStoneStockpile = 15;

    public int GetTargetStockpile(GoodType goodType)
    {
        return goodType switch
        {
            GoodType.Wood => TargetWoodStockpile,
            GoodType.Stone => TargetStoneStockpile,
            _ => 0,
        };
    }
}
