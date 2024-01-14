using System;
using UnityEngine;

public class AIDebuggerPanel : MonoBehaviour
{
    public GameObject List;
    public AIDebuggerEntry EntryPrefab;

    public bool ForceExpandAll = true;

    void Start()
    {
    }

    public void Refresh()
    {
        Debug.Log("refresh");
        List.RemoveAllChildren();
        int i = 0;
        foreach (var entry in AIDebugger.Entries)
        {
            var entryObj = Instantiate(EntryPrefab);
            entryObj.GetComponent<AIDebuggerEntry>().ShowForEntry(entry);
            entryObj.transform.SetParent(List.transform);
            if (i++ > 1000)
                return;
        }
    }

    internal void InitializeForTown(TownData town)
    {
#if DEBUG
        town.OnAIDebuggerUpdate += Refresh;
#endif
    }

    public void ExpandAllToggled()
    {
        ForceExpandAll = !ForceExpandAll;

        // if ForceExpandAll is true, then we want to forcibly expand all entries now
        // if ForceExpandAll is false, then we want to forcibly collapse all entries now
    }
}