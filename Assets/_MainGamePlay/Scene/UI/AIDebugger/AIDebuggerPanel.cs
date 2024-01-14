using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Analytics;

public class AIDebuggerPanel : MonoBehaviour
{
    public GameObject List;
    public AIDebuggerEntry EntryPrefab;
    public Dictionary<int, bool> ExpandedEntries = new();
    public bool ForceExpandAll = false;
    public bool ShowBestOnStart = true;
    
    void Start()
    {
        ForceExpandAll = false;
        ShowBestOnStart = true;
    }

    public void Refresh()
    {
        if (!GameMgr.Instance.DebugOutputStrategy) return;

        List.RemoveAllChildren();

        initializeExpandedEntries(AIDebugger.topEntry);

        if (ShowBestOnStart)
        {
            expandOnlyBestEntry(AIDebugger.topEntry);
            ShowBestOnStart = false;
        }

        AddChildEntries(AIDebugger.topEntry.ChildEntries);
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
        // limit to 1000 entries in List
        if (List.transform.childCount > 1000)
            return;
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
        town.OnAIDebuggerUpdate += Refresh;
#endif
    }

    public void ShowBest()
    {
        expandOnlyBestEntry(AIDebugger.topEntry);
        Refresh();
    }

    private void expandOnlyBestEntry(AIDebuggerEntryData curEntry)
    {
        if (curEntry.ChildEntries.Count == 0) return;
        var bestEntry = curEntry.ChildEntries[0];
        foreach (var childEntry in curEntry.ChildEntries)
        {
            if (childEntry.Score > bestEntry.Score)
                bestEntry = childEntry;
        }
        foreach (var childEntry in curEntry.ChildEntries)
        {
            ExpandedEntries[childEntry.ActionNumber] = childEntry == bestEntry;
            childEntry.IsBestOption = childEntry == bestEntry;
            expandOnlyBestEntry(childEntry);
        }
    }

    public void ExpandAllToggled()
    {
        ForceExpandAll = !ForceExpandAll;
        initializeExpandedEntries(AIDebugger.topEntry, true, ForceExpandAll);

        Refresh();
    }
}