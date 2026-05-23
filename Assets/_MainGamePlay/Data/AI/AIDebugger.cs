#if DEBUG
public static class AIDebugger
{
    public static AIDebuggerEntryData rootEntry = new() { ActionType = AIActionType.RootAction };
    public static AIDebuggerEntryData curEntry;
    public static bool TrackForCurrentPlayer;
    public static bool ShouldTrackEntries => AITestScene.Instance.ShowDebuggerAI && AITestScene.Instance.TrackSearchDebugger && TrackForCurrentPlayer;

    public static void Clear()
    {
        if (!ShouldTrackEntries) return;
        curEntry = rootEntry;
        curEntry.ChildEntries.Clear();
        AIDebuggerEntryData.ResetPool();
    }
}
#endif