using UnityEngine;

public enum GatheringWorkerPhase
{
    GoingToDeposit,
    GatheringAtDeposit,
    ReturningHome,
    RestingAtHome,
}

/// <summary>
/// A worker dispatched from a gatherer building node to an adjacent deposit (Forest / StoneMine).
/// Lives in <see cref="NodeData.GatheringWorkers"/> on the home node for the whole trip so
/// returns still resolve if the building is destroyed or replaced mid-run.
/// </summary>
public class GatheringWorkerData
{
    public PlayerData OwnedBy;
    public NodeData HomeNode;
    public NodeData DepositNode;
    public BuildingDefn GathererBuildingDefn;
    public GoodType CarriedGood = GoodType.Unset;

    public GatheringWorkerPhase Phase;
    public float PhaseTimer;
    public float SegmentProgress;
    public Vector3 WorldLoc;

    static readonly Color GathererWorkerColor = new Color(0.28f, 0.28f, 0.28f, 1f);
    public static Color WorkerDisplayColor => GathererWorkerColor;
    /// <summary>Multiplier applied to the worker prefab's localScale (prefab is 0.2; combat workers use 1x).</summary>
    public const float VisualScaleRelativeToWorkerPrefab = 0.5f;

    public GatheringWorkerData(
        PlayerData owner,
        NodeData homeNode,
        NodeData depositNode,
        BuildingDefn gathererDefn)
    {
        OwnedBy = owner;
        HomeNode = homeNode;
        DepositNode = depositNode;
        GathererBuildingDefn = gathererDefn;
        Phase = GatheringWorkerPhase.GoingToDeposit;
        PhaseTimer = 0f;
        SegmentProgress = 0f;
        WorldLoc = homeNode.WorldLoc;
    }

    public bool IsCarrying => CarriedGood != GoodType.Unset;

    public float MoveSpeedUnitsPerSecond(float gameSpeed)
    {
        var defn = OwnedBy?.WorkerDefn;
        float displaySpeed = defn != null ? defn.Speed : 4f;
        return displaySpeed * WorkerData.SpeedDisplayToUnitsPerSecond * gameSpeed;
    }

    public void AdvanceAlongSegment(NodeData from, NodeData to, float deltaSeconds, float gameSpeed)
    {
        var start = from.WorldLoc;
        var end = to.WorldLoc;
        var segLen = (end - start).magnitude;
        if (segLen <= 0.0001f)
        {
            WorldLoc = end;
            SegmentProgress = 1f;
            return;
        }

        float speed = MoveSpeedUnitsPerSecond(gameSpeed);
        SegmentProgress += (speed * deltaSeconds) / segLen;
        if (SegmentProgress >= 1f)
        {
            SegmentProgress = 1f;
            WorldLoc = end;
        }
        else
        {
            WorldLoc = Vector3.Lerp(start, end, SegmentProgress);
        }
    }

    public bool ReachedSegmentEnd => SegmentProgress >= 1f;
}
