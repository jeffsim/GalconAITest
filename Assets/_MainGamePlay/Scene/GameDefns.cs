using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[Serializable]
public class GameDefnsMgr
{
    [SerializeReference] public Dictionary<string, TownDefn> TownDefns = new();
    [SerializeReference] public Dictionary<string, WorkerDefn> WorkerDefns = new();
    [SerializeReference] public Dictionary<string, BuildingDefn> BuildingDefns = new();

    public void RefreshDefns()
    {
        // Find all Defn objects and add them.
        // TODO (PERF, LATER): This makes my development life easier, but when I get closer to prod, these should be stored 
        // persistently in the GameDefns prefab so that I don't have to do this on every scene load
        loadDefns("Towns", TownDefns);
        loadDefns("Workers", WorkerDefns);
        loadDefns("Buildings", BuildingDefns);
    }

    private void loadDefns<T>(string folderName, Dictionary<string, T> defnDict) where T : BaseDefn
    {
        defnDict.Clear();
        var defns = Resources.LoadAll<T>("Defns/" + folderName);
        foreach (var defn in defns)
            defnDict[defn.Id] = defn as T;
    }
}

public class GameDefns : SerializedMonoBehaviour
{
    public static GameDefns Instance;

    private GameDefnsMgr GameDefnsMgr;
    public Dictionary<string, TownDefn> TownDefns => GameDefnsMgr.TownDefns;
    public Dictionary<string, WorkerDefn> WorkerDefns => GameDefnsMgr.WorkerDefns;
    public Dictionary<string, BuildingDefn> BuildingDefns => GameDefnsMgr.BuildingDefns;

    void Awake()
    {
        GameDefns[] objs = GameObject.FindObjectsByType<GameDefns>(FindObjectsSortMode.None);
        if (objs.Length > 1)
            Destroy(this.gameObject);
        DontDestroyOnLoad(this.gameObject);
        Instance = this;
        GameDefnsMgr = new GameDefnsMgr();
        GameDefnsMgr.RefreshDefns();
    }

    void OnEnable()
    {
        Instance = this;
        GameDefnsMgr.RefreshDefns();
    }
}
