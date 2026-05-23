using System.Collections.Generic;
using UnityEngine;

// Realtime in-flight worker. Travels along a precomputed multi-segment Path of NodeData
// (node-to-node, following the actual NodeConnection graph). Speed is constant world units
// per second; segments may differ in length so traversal time per segment varies. On arrival
// at the final node its Intent decides what happens at the destination -- reinforce, attack,
// or capture-and-build.
public enum WorkerIntent
{
    Reinforce,
    Attack,
    CaptureAndConstruct,
}

public class WorkerData
{
    public PlayerData OwnedBy;
    public WorkerDefn Defn;

    // Multi-segment path. Path[0] is the source node, Path[Count-1] is the final destination.
    // The active segment is Path[PathIndex] -> Path[PathIndex+1]. Shared (referenced, not copied)
    // across all workers in the same dispatched group.
    public List<NodeData> Path;
    public int PathIndex;
    public float SegmentProgress;

    // Convenience accessors used by existing call sites.
    public NodeData FromNode => Path != null && Path.Count > 0 ? Path[0] : null;
    public NodeData ToNode => Path != null && Path.Count > 0 ? Path[Path.Count - 1] : null;

    public Vector3 WorldLoc;

    // Overall 0..1 progress across the entire path. 1 == fully arrived. Other code uses this
    // to detect arrival rather than walking PathIndex/SegmentProgress directly.
    public float Progress;

    // World-time at which to start moving. Used to stagger workers in a group so they form a
    // visible column rather than colliding into one cube. Until WorldTime >= SpawnAtTime the
    // worker stays parked at FromNode and has not yet been added to the simulation's incoming
    // counts.
    public float SpawnAtTime;
    public bool HasSpawned;

    public WorkerIntent Intent;
    public BuildingDefn ConstructBuildingIntent;

    public bool ArrivedThisTick = false;

    // Random per-worker offset perpendicular to the direction of travel, so a column of
    // workers fans out a tiny amount instead of overlapping. Damped to zero at each path node
    // so the cubes visibly converge at every waypoint.
    public Vector3 LateralOffset;

    public WorkerData(PlayerData owner, WorkerDefn defn, List<NodeData> path, WorkerIntent intent, BuildingDefn buildingIntent, float spawnAtTime)
    {
        OwnedBy = owner;
        Defn = defn;
        Path = path;
        PathIndex = 0;
        SegmentProgress = 0f;
        Intent = intent;
        ConstructBuildingIntent = buildingIntent;
        SpawnAtTime = spawnAtTime;
        HasSpawned = false;
        Progress = 0f;
        WorldLoc = path != null && path.Count > 0 ? path[0].WorldLoc : Vector3.zero;
        LateralOffset = new Vector3(Random.Range(-.18f, .18f), 0, Random.Range(-.18f, .18f));
    }

    // Internal scale applied to WorkerDefn.Speed before it becomes world-units/sec. Lets us
    // pick comfortable display values (e.g. 4 in the inspector) while keeping actual movement
    // gentle. Tuned so a display Speed of 4 -> 0.25 world units/sec at GameSpeed=1.
    public const float SpeedDisplayToUnitsPerSecond = 0.0625f; // == 1/16

    // Returns true when the worker just stepped onto a new node this call (PathIndex
    // incremented). The caller (TownData.AdvanceInFlightWorkers) inspects ownership of
    // Path[PathIndex] and decides to keep walking (friendly) or resolve combat at that node
    // (hostile / final destination). The Path itself is NEVER mutated mid-flight -- it is set
    // once at dispatch.
    public bool AdvanceTowardDestination(float deltaSeconds, float gameSpeed)
    {
        if (!HasSpawned) return false;
        if (Path == null || Path.Count < 2)
        {
            // Degenerate path: snap to whatever endpoint we have and report arrival so the
            // simulation can resolve it on the next tick.
            Progress = 1f;
            if (Path != null && Path.Count > 0)
                WorldLoc = Path[Path.Count - 1].WorldLoc;
            return true;
        }
        if (PathIndex >= Path.Count - 1)
        {
            Progress = 1f;
            return false;
        }

        var segStart = Path[PathIndex].WorldLoc;
        var segEnd = Path[PathIndex + 1].WorldLoc;
        var segLen = (segEnd - segStart).magnitude;

        float speed = (Defn != null ? Defn.Speed : 4f) * SpeedDisplayToUnitsPerSecond * gameSpeed;
        bool crossedNode = false;

        if (segLen <= 0.0001f)
        {
            // Skip zero-length segment.
            PathIndex++;
            SegmentProgress = 0f;
            crossedNode = true;
        }
        else
        {
            SegmentProgress += (speed * deltaSeconds) / segLen;
            // Advance at most ONE segment per call: when we cross into a new node we yield
            // back to TownData so it can decide whether the worker can keep walking (friendly
            // node) or has to attack (hostile / neutral node).
            if (SegmentProgress >= 1f)
            {
                PathIndex++;
                SegmentProgress = 0f; // worker is AT the new node, not past it
                crossedNode = true;
            }
        }

        if (PathIndex >= Path.Count - 1)
        {
            PathIndex = Path.Count - 1;
            SegmentProgress = 0f;
            Progress = 1f;
            WorldLoc = Path[Path.Count - 1].WorldLoc;
            return true;
        }

        segStart = Path[PathIndex].WorldLoc;
        segEnd = Path[PathIndex + 1].WorldLoc;
        var pos = Vector3.Lerp(segStart, segEnd, SegmentProgress);

        // Damp the lateral offset toward zero at each path node so workers visually re-form
        // into a tight cluster at every waypoint.
        float lateralFactor = Mathf.Clamp01(1f - Mathf.Abs(2f * SegmentProgress - 1f));
        WorldLoc = pos + LateralOffset * lateralFactor;

        // Overall progress as a fraction of total path segments.
        int totalSegments = Path.Count - 1;
        Progress = ((float)PathIndex + SegmentProgress) / totalSegments;

        return crossedNode;
    }
}
