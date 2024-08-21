#if DEBUG
public static class AIDebugger
{
    public static AIDebuggerEntryData topEntry = new();
    public static AIDebuggerEntryData curEntry;
    public static bool TrackForCurrentPlayer;
    public static bool ShouldTrackEntries => AITestScene.Instance.ShowDebuggerAI && TrackForCurrentPlayer;

    public static void Clear()
    {
        if (!ShouldTrackEntries) return;
        curEntry = topEntry;
        curEntry.ChildEntries.Clear();
        AIDebuggerEntryData.ResetPool();
    }

    internal static AIDebuggerEntryData TrackPerformAction_ConstructBuildingInEmptyNode(AI_NodeState fromNode, AI_NodeState toNode, int numSent, BuildingDefn buildingDefn, float scoreAfterActionAndBeforeSubActions, int actionNum, int curDepth, int recurseCount)
    {
        if (!ShouldTrackEntries) return null;
        return PushPerformedAction(AIDebuggerEntryData.GetFromPool(
                   AIActionType.ConstructBuildingInEmptyNode,
                   fromNode,
                   toNode,
                   numSent,
                   buildingDefn,
                   scoreAfterActionAndBeforeSubActions,
                   actionNum,
                   curDepth,
                   curEntry
           ));
    }

    internal static AIDebuggerEntryData TrackPerformAction_SendWorkersToOwnedNode(AI_NodeState fromNode, AI_NodeState toNode, int numSent, float scoreAfterActionAndBeforeSubActions, int actionNum, int curDepth, int recurseCount)
    {
        if (!ShouldTrackEntries) return null;
        return PushPerformedAction(AIDebuggerEntryData.GetFromPool(
                   AIActionType.ConstructBuildingInEmptyNode,
                   fromNode,
                   toNode,
                   numSent,
                   null,
                   scoreAfterActionAndBeforeSubActions,
                   actionNum,
                   curDepth,
                   curEntry
           ));
    }

    internal static AIDebuggerEntryData TrackPerformAction_Attack(AI_NodeState fromNode, AI_NodeState toNode, AttackResult attackResult, int numSent, float scoreAfterActionAndBeforeSubActions, int actionNum, int curDepth, int recurseCount)
    {
        if (!ShouldTrackEntries) return null;
        return PushPerformedAction(AIDebuggerEntryData.GetFromPool(
                  AIActionType.AttackFromNode,
                  fromNode,
                  toNode,
                  numSent,
                  null,
                  scoreAfterActionAndBeforeSubActions,
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