using System.Collections.Generic;

/// Concrete move payload produced by the AI per tick. Read by TownData.ExecuteRealtimeAction
/// and TownData.Debug_WorldTurn to actually carry out the move on the real world.
///
/// Slimmer than the previous version: dropped the recursive-search debug bloat
/// (DebugOutput_*, AIDebuggerEntry, AttackResult/AttackResults), the per-search pool
/// reset hooks, and the in-progress error states. AIDecisionRecord now owns all debug
/// observation; this class is purely the executor's input contract.
public enum AIActionType
{
    DoNothing,
    SendWorkersToOwnedNode,
    ConstructBuildingInEmptyNode,
    CaptureNeutralResourceNode,
    CaptureNeutralNode,
    AttackToNode,
    UpgradeBuilding,
    SendMultiSourceWorkersToOwnedNode,
}

public class AIAction
{
    public AIActionType Type = AIActionType.DoNothing;
    public float Score;
    public int Count;
    public AI_NodeState SourceNode;
    public AI_NodeState DestNode;
    public BuildingDefn BuildingToConstruct;
    public Dictionary<AI_NodeState, int> AttackFromNodes = new();

    public override string ToString()
    {
        switch (Type)
        {
            case AIActionType.SendWorkersToOwnedNode:
                return $"Send {Count} from #{SourceNode?.NodeId} to #{DestNode?.NodeId}";
            case AIActionType.SendMultiSourceWorkersToOwnedNode:
                return $"Multi-source support #{DestNode?.NodeId}";
            case AIActionType.ConstructBuildingInEmptyNode:
                return $"Send {Count} from #{SourceNode?.NodeId} to #{DestNode?.NodeId} to build {BuildingToConstruct?.Id}";
            case AIActionType.CaptureNeutralResourceNode:
                return $"Capture resource #{DestNode?.NodeId} send {Count} from #{SourceNode?.NodeId}";
            case AIActionType.CaptureNeutralNode:
                return $"Multi-source build {(BuildingToConstruct != null ? BuildingToConstruct.Id : "?")} on #{DestNode?.NodeId}";
            case AIActionType.UpgradeBuilding:
                return $"Upgrade #{SourceNode?.NodeId}";
            case AIActionType.AttackToNode:
                return $"Attack #{DestNode?.NodeId}";
            case AIActionType.DoNothing:
                return "Do nothing";
            default:
                return Type.ToString();
        }
    }

    public void SetToNothing()
    {
        Type = AIActionType.DoNothing;
        Score = 0f;
        Count = 0;
        SourceNode = null;
        DestNode = null;
        BuildingToConstruct = null;
        AttackFromNodes.Clear();
    }

    public void CopyFrom(AIAction other)
    {
        Type = other.Type;
        Score = other.Score;
        Count = other.Count;
        SourceNode = other.SourceNode;
        DestNode = other.DestNode;
        BuildingToConstruct = other.BuildingToConstruct;
        AttackFromNodes.Clear();
        foreach (var kv in other.AttackFromNodes)
            AttackFromNodes[kv.Key] = kv.Value;
    }
}
