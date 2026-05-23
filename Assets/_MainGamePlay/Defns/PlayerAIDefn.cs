using UnityEngine;

[CreateAssetMenu()]
public class PlayerAIDefn : BaseDefn
{
    public string Name;
    public Color Color = Color.white;

    [Header("Tactic Weights")]
    [Range(0f, 2f)] public float ExpansionWeight = 1f;
    [Range(0f, 2f)] public float DefenseWeight = 1f;
    [Range(0f, 2f)] public float AggressivenessWeight = 1f;
}
