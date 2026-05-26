using UnityEngine;

/// Four orthogonal personality dials. Each one is designed to move ONE observable axis of
/// the AI's behaviour without bleeding into the others:
///
///   Aggression  - preference for attacks; also tilts the attack/capture/reinforce mix
///                 when several action families are simultaneously appealing.
///   Expansion   - preference for capturing neutrals and building economy.
///   Caution     - safety margin retained at source nodes after a send, AND the attack
///                 overkill multiplier (more cautious -> ceil(threat * (1 + 0.25*Caution)) ).
///   Tempo       - preference for long-term investment (upgrades, buildings) versus
///                 immediate-force moves (attack, reinforce). Higher = patient/builder.
///
/// All weights are in [0, 2]. 1.0 is "neutral". 0 disables that action family entirely.
public readonly struct PersonalityWeights
{
    public readonly float Aggression;
    public readonly float Expansion;
    public readonly float Caution;
    public readonly float Tempo;

    public PersonalityWeights(float aggression, float expansion, float caution, float tempo)
    {
        Aggression = Mathf.Clamp(aggression, 0f, 2f);
        Expansion = Mathf.Clamp(expansion, 0f, 2f);
        Caution = Mathf.Clamp(caution, 0f, 2f);
        Tempo = Mathf.Clamp(tempo, 0f, 2f);
    }

    public static PersonalityWeights Neutral => new(1f, 1f, 1f, 1f);

    public static PersonalityWeights From(PlayerAIDefn defn)
    {
        if (defn == null) return Neutral;
        return new PersonalityWeights(defn.Aggression, defn.Expansion, defn.Caution, defn.Tempo);
    }

    /// Overkill multiplier applied when sizing an attack or contested-capture force.
    /// Caution=0 -> 1.0x (bare minimum); Caution=1 -> 1.25x; Caution=2 -> 1.5x.
    public float AttackOverkill => 1f + 0.25f * Caution;

    /// Garrison left behind at a source node after a non-emergency send. Higher Caution
    /// keeps interior nodes better staffed at the cost of slower expansion.
    public int MinReserveAtSource => Mathf.Max(1, Mathf.RoundToInt(1f + Caution * 1.5f));
}
