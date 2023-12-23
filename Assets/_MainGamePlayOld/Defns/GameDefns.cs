using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[Serializable]
public class GameDefnsMgr
{
    [SerializeReference] public Dictionary<string, TownDefn> TownDefns = new();
    [SerializeReference] public Dictionary<string, WorldDefn> WorldDefns = new();
    [SerializeReference] public Dictionary<string, WorkerDefn> WorkerDefns = new();
    [SerializeReference] public Dictionary<string, BuildingDefn> BuildingDefns = new();
    [SerializeReference] public Dictionary<string, NodeDefn> NodeDefns = new();
    [SerializeReference] public Dictionary<string, NodeConnectionTypeDefn> NodeConnectionTypeDefns = new();
    [SerializeReference] public Dictionary<string, RaceDefn> RaceDefns = new();
    [SerializeReference] public Dictionary<string, ItemDefn> ItemDefns = new();

    public void RefreshDefns()
    {
        // Find all Defn objects and add them.
        // TODO (PERF, LATER): This makes my development life easier, but when I get closer to prod, these should be stored 
        // persistently in the GameDefns prefab so that I don't have to do this on every scene load
        loadDefns("Towns", TownDefns);
        loadDefns("Worlds", WorldDefns);
        loadDefns("Workers", WorkerDefns);
        loadDefns("Nodes", NodeDefns);
        loadDefns("NodeConnections", NodeConnectionTypeDefns);
        loadDefns("Races", RaceDefns);
        loadDefns("Items", ItemDefns);
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
    public Dictionary<string, WorldDefn> WorldDefns => GameDefnsMgr.WorldDefns;
    public Dictionary<string, WorkerDefn> WorkerDefns => GameDefnsMgr.WorkerDefns;
    public Dictionary<string, BuildingDefn> BuildingDefns => GameDefnsMgr.BuildingDefns;
    public Dictionary<string, NodeDefn> NodeDefns => GameDefnsMgr.NodeDefns;
    public Dictionary<string, NodeConnectionTypeDefn> NodeConnectionTypeDefns => GameDefnsMgr.NodeConnectionTypeDefns;
    public Dictionary<string, RaceDefn> RaceDefns => GameDefnsMgr.RaceDefns;
    public Dictionary<string, ItemDefn> ItemDefns => GameDefnsMgr.ItemDefns;

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
