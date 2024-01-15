#if DEBUG
public static class AIDebugger
{
    public static AIDebuggerEntryData topEntry = new();
    public static AIDebuggerEntryData curEntry;
    public static bool TrackForCurrentPlayer;
    public static bool ShouldTrackEntries => GameMgr.Instance.ShowDebuggerAI && TrackForCurrentPlayer;

    public static void Clear()
    {
        if (!AIDebugger.ShouldTrackEntries) return;
        curEntry = topEntry;
        curEntry.ChildEntries.Clear();
        AIDebuggerEntryData.ResetPool();
    }

    internal static AIDebuggerEntryData TrackPerformAction_ConstructBuildingInEmptyNode(AI_NodeState fromNode, AI_NodeState toNode, int numSent, BuildingDefn buildingDefn, float scoreAfterActionAndBeforeSubActions, int actionNum, int curDepth, int recurseCount)
    {
        if (!AIDebugger.ShouldTrackEntries) return null;
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

    internal static AIDebuggerEntryData TrackPerformAction_Attack(AI_NodeState fromNode, AI_NodeState toNode, AttackResult attackResult, int numSent, float scoreAfterActionAndBeforeSubActions, int actionNum, int curDepth, int recurseCount)
    {
        if (!AIDebugger.ShouldTrackEntries) return null;
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
        if (!AIDebugger.ShouldTrackEntries) return null;

        aIDebuggerEntryData.DebugOutput();
        curEntry.ChildEntries.Add(aIDebuggerEntryData);

        var prev = curEntry;
        curEntry = aIDebuggerEntryData;
        return prev;
    }

    internal static void PopPerformedAction(AIDebuggerEntryData prevEntry)
    {
        if (!AIDebugger.ShouldTrackEntries) return;

        curEntry = prevEntry;// curEntry.ParentEntry;
    }

    // internal static void TrackPerformAction_ConstructBuildingInEmptyNode(AI_NodeState toNode, AI_NodeState toNode1, BuildingDefn buildingDefn, float scoreAfterActionAndBeforeSubActions)
    // {
    //     Debug.Log(CurSpacing + "Construct building " + buildingDefn.Id + " in node " + toNode.NodeId + "; scoreAfterActionAndBeforeSubActions: " + scoreAfterActionAndBeforeSubActions.ToString("0.0"));
    //     Entries.Add(new AIDebuggerEntryData()
    //     {
    //         ActionNumber = CurActionNum,
    //         RecurseDepth = RecurseDepth,
    //         FromNode = ActionFromNode,
    //         ActionType = AIActionType.ConstructBuildingInEmptyNode,
    //         ToNode = toNode,
    //         BuildingDefn = buildingDefn,
    //         Score = scoreAfterActionAndBeforeSubActions,
    // ParentEntry = curEntry
    //     });
    // }



















    internal static void TrackPerformAction_ConstructBuildingInOwnedEmptyNode(AI_NodeState toNode, BuildingDefn buildingDefn, float scoreAfterActionAndBeforeSubActions)
    {
        // Debug.Log(CurSpacing + "Construct building " + buildingDefn.Id + " in node " + toNode.NodeId + "; scoreAfterActionAndBeforeSubActions: " + scoreAfterActionAndBeforeSubActions.ToString("0.0"));
        // Entries.Add(new AIDebuggerEntryData()
        // {
        //     ActionNumber = CurActionNum,
        //     RecurseDepth = RecurseDepth,
        //     FromNode = ActionFromNode,
        //     ActionType = AIActionType.ConstructBuildingInOwnedEmptyNode,
        //     ToNode = toNode,
        //     BuildingDefn = buildingDefn,
        //     Score = scoreAfterActionAndBeforeSubActions,
        //     ParentEntry = curEntry
        // });
    }

    internal static void TrackPerformAction_Attack2(AI_NodeState toNode, AttackResult attackResult, int numSent, float scoreAfterActionAndBeforeSubActions)
    {
        // Debug.Log(CurSpacing + "Attack on node " + toNode.NodeId + " result: " + attackResult + "; numSent: " + numSent + "; scoreAfterActionAndBeforeSubActions: " + scoreAfterActionAndBeforeSubActions.ToString("0.0"));
        // Entries.Add(new AIDebuggerEntryData()
        // {
        //     ActionNumber = CurActionNum,
        //     RecurseDepth = RecurseDepth,
        //     FromNode = ActionFromNode,
        //     ActionType = AIActionType.AttackFromNode,
        //     ToNode = toNode,
        //     AttackResult = attackResult,
        //     NumSent = numSent,
        //     Score = scoreAfterActionAndBeforeSubActions,
        //     ParentEntry = curEntry
        // });
    }


    internal static void TrackPerformAction_SendWorkersToOwnedNode(AI_NodeState toNode, int numSent, float scoreAfterActionAndBeforeSubActions)
    {
        // Debug.Log(CurSpacing + "Send " + numSent + " workers to node " + toNode.NodeId + "; scoreAfterActionAndBeforeSubActions: " + scoreAfterActionAndBeforeSubActions.ToString("0.0"));
        // Entries.Add(new AIDebuggerEntryData()
        // {
        //     ActionNumber = CurActionNum,
        //     RecurseDepth = RecurseDepth,
        //     FromNode = ActionFromNode,
        //     ActionType = AIActionType.SendWorkersToOwnedNode,
        //     ToNode = toNode,
        //     NumSent = numSent,
        //     Score = scoreAfterActionAndBeforeSubActions,
        //     ParentEntry = curEntry
        // });
    }

    internal static void TrackPerformAction_SendWorkersToEmptyNode(AI_NodeState toNode, int numSent, float scoreAfterActionAndBeforeSubActions)
    {
        // Debug.Log(CurSpacing + "Send " + numSent + " workers to node " + toNode.NodeId + "; scoreAfterActionAndBeforeSubActions: " + scoreAfterActionAndBeforeSubActions.ToString("0.0"));
        // Entries.Add(new AIDebuggerEntryData()
        // {
        //     ActionNumber = CurActionNum,
        //     RecurseDepth = RecurseDepth,
        //     FromNode = ActionFromNode,
        //     ActionType = AIActionType.SendWorkersToEmptyNode,
        //     ToNode = toNode,
        //     NumSent = numSent,
        //     Score = scoreAfterActionAndBeforeSubActions,
        //     ParentEntry = curEntry
        // });
    }
}
#endif