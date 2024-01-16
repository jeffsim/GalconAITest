using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class AIDebuggerPanel : MonoBehaviour
{
    public GameObject List;
    public AIDebuggerEntry EntryPrefab;
    public Dictionary<int, bool> ExpandedEntries = new();
    public bool ForceExpandAll = false;
    public bool ShowBestOnStart = true;
    public Button[] PlayerSelectButtons;

    void Start()
    {
        ForceExpandAll = false;
        ShowBestOnStart = true;
        EventSystem.current.SetSelectedGameObject(PlayerSelectButtons[AITestScene.Instance.DebugPlayerToViewDetailsOn.Id - 1].gameObject); // harcoded. fuck it.
    }

    public void Refresh()
    {
        if (!AITestScene.Instance.ShowDebuggerAI) return;

        List.RemoveAllChildren();

        initializeExpandedEntries(AIDebugger.topEntry);

        if (ShowBestOnStart)
        {
            initializeExpandedEntries(AIDebugger.topEntry, true, false);

            clearBestStrategyPaths(AIDebugger.topEntry);
            identifyBestStrategyPath(AIDebugger.topEntry.BestNextAction);
            ShowBestOnStart = false;
        }

        AddChildEntries(AIDebugger.topEntry.ChildEntries);
        Debug.Log("Total actions: " +AITestScene.Instance.DebugPlayerToViewDetailsOn.AI.debugOutput_ActionsTried);
    }

    private void initializeExpandedEntries(AIDebuggerEntryData curEntry, bool forceValue = false, bool value = false)
    {
        foreach (var childEntry in curEntry.ChildEntries)
        {
            if (!ExpandedEntries.ContainsKey(childEntry.ActionNumber) || forceValue)
                ExpandedEntries[childEntry.ActionNumber] = value;
            initializeExpandedEntries(childEntry, forceValue, value);
        }
    }

    private void AddChildEntries(List<AIDebuggerEntryData> childEntries)
    {
        // limit to 1000 AIDebuggerEntry objects (recursively) in List
        if (List.gameObject.GetAllChildrenOfType<AIDebuggerEntry>() > 1000) return;
        foreach (var child in childEntries)
        {
            var entryObj = Instantiate(EntryPrefab);
            entryObj.GetComponent<AIDebuggerEntry>().ShowForEntry(child, this);
            entryObj.transform.SetParent(List.transform);

            if (ExpandedEntries[child.ActionNumber])
                AddChildEntries(child.ChildEntries);
        }
    }

    internal void InitializeForTown(TownData town)
    {
#if DEBUG
        town.OnAIDebuggerUpdate += ShowBest;
#endif
    }

    public void ShowBestClicked() => ShowBest(AITestScene.Instance.DebugPlayerToViewDetailsOn.Id);

    public void ShowBest(int playerId)
    {
        if (playerId != AITestScene.Instance.DebugPlayerToViewDetailsOn.Id) return;
        //     recursivelyIdentifyHighestScoreAmongPeers(AIDebugger.topEntry);
        clearBestStrategyPaths(AIDebugger.topEntry);
        identifyBestStrategyPath(AIDebugger.topEntry.BestNextAction);
        Refresh();
    }

    public void OnShowForPlayerIdClicked(int playerId)
    {
        AITestScene.Instance.DebugPlayerToViewDetailsOn = AITestScene.Instance.Town.Players[playerId];
        AITestScene.Instance.Town.Update(); // force an update to get latest AI

        // hardcode because FUCK IT
        EventSystem.current.SetSelectedGameObject(PlayerSelectButtons[playerId - 1].gameObject);
        ShowBest(playerId);
    }

    private void clearBestStrategyPaths(AIDebuggerEntryData curEntry)
    {
        curEntry.IsInBestStrategyPath = false;
        ExpandedEntries[curEntry.ActionNumber] = false;
        foreach (var childEntry in curEntry.ChildEntries)
            clearBestStrategyPaths(childEntry);
    }

    private void identifyBestStrategyPath(AIDebuggerEntryData curEntry)
    {
        if (curEntry == null)
            return;
        curEntry.IsInBestStrategyPath = true;
        ExpandedEntries[curEntry.ActionNumber] = true;
        identifyBestStrategyPath(curEntry.BestNextAction);
    }

    private void recursivelyIdentifyHighestScoreAmongPeers(AIDebuggerEntryData curEntry)
    {
        if (curEntry.ChildEntries.Count == 0) return;
        var highestEntry = curEntry.ChildEntries[0];
        foreach (var childEntry in curEntry.ChildEntries)
        {
            if (childEntry.Score > highestEntry.Score)
                highestEntry = childEntry;
        }
        foreach (var childEntry in curEntry.ChildEntries)
        {
            ExpandedEntries[childEntry.ActionNumber] = childEntry == highestEntry;
            childEntry.IsHighestOptionOfPeers = childEntry == highestEntry;
            recursivelyIdentifyHighestScoreAmongPeers(childEntry);
        }
    }

    public void ExpandAllToggled()
    {
        ForceExpandAll = !ForceExpandAll;
        initializeExpandedEntries(AIDebugger.topEntry, true, ForceExpandAll);

        Refresh();
    }
}