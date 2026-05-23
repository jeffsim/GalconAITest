
using UnityEngine;

[CreateAssetMenu()]
public class WorkerDefn : BaseDefn
{
    public string Name;
    public Color Color = Color.white;

    // Realtime travel speed in world units per second. Used when AITestScene.Realtime is true
    // and a player dispatches workers between nodes; each in-flight worker advances along its
    // path at this rate (scaled by AITestScene.GameSpeed).
    [Tooltip("Display speed for in-flight workers in realtime mode. Internally multiplied by WorkerData.SpeedDisplayToUnitsPerSecond (1/16) before being applied as world-units-per-second, so a Speed of 4 == 0.25 world units per second at GameSpeed=1.")]
    public float Speed = 4f;
}
