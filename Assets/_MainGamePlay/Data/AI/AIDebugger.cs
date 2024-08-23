#if DEBUG
using System.Diagnostics;

public static class AIDebugger
{
    public static AIDebuggerEntryData rootEntry = new() { ActionType = AIActionType.RootAction };
    public static AIDebuggerEntryData curEntry;
    public static bool TrackForCurrentPlayer;
    public static bool ShouldTrackEntries => AITestScene.Instance.ShowDebuggerAI && TrackForCurrentPlayer;

    public static void Clear()
    {
        if (!ShouldTrackEntries) return;
        curEntry = rootEntry;
        curEntry.ChildEntries.Clear();
        AIDebuggerEntryData.ResetPool();
    }

    internal static AIDebuggerEntryData PushPerformedAction_ConstructBuildingInEmptyNode(AI_NodeState fromNode, AI_NodeState toNode, int numSent, BuildingDefn buildingDefn, float finalActionScore, int actionNum, int curDepth)
    {
        Debug.Assert(buildingDefn != null);
        if (!ShouldTrackEntries) return null;
        return PushPerformedAction(AIDebuggerEntryData.GetFromPool(
                                    AIActionType.ConstructBuildingInEmptyNode,
                                    fromNode,
                                    toNode,
                                    numSent,
                                    buildingDefn,
                                    finalActionScore,
                                    actionNum,
                                    curDepth,
                                    curEntry
           ));
    }

    internal static AIDebuggerEntryData TrackPerformAction_SendWorkersToOwnedNode(AI_NodeState fromNode, AI_NodeState toNode, int numSent, float finalActionScore, int actionNum, int curDepth)
    {
        if (!ShouldTrackEntries) return null;
        return PushPerformedAction(AIDebuggerEntryData.GetFromPool(
                                    AIActionType.SendWorkersToOwnedNode,
                                    fromNode,
                                    toNode,
                                    numSent,
                                    null,
                                    finalActionScore,
                                    actionNum,
                                    curDepth,
                                    curEntry
           ));
    }

    internal static AIDebuggerEntryData TrackPerformAction_Attack(AI_NodeState fromNode, AI_NodeState toNode, AttackResult attackResult, int numSent, float finalActionScore, int actionNum, int curDepth)
    {
        if (!ShouldTrackEntries) return null;
        return PushPerformedAction(AIDebuggerEntryData.GetFromPool(
                                    AIActionType.AttackFromNode,
                                    fromNode,
                                    toNode,
                                    numSent,
                                    null,
                                    finalActionScore,
                                    actionNum,
                                    curDepth,
                                    curEntry
          ));
    }

    private static AIDebuggerEntryData PushPerformedAction(AIDebuggerEntryData aIDebuggerEntryData)
    {
        if (!ShouldTrackEntries) return null;

        aIDebuggerEntryData.DebugOutput();
        curEntry.ChildEntries.Add(aIDebuggerEntryData);

        var prev = curEntry;
        curEntry = aIDebuggerEntryData;
        return prev;
    }

    internal static void PopPerformedAction(AIDebuggerEntryData prevEntry)
    {
        if (!ShouldTrackEntries) return;

        curEntry = prevEntry;// curEntry.ParentEntry;
    }
}
#endif