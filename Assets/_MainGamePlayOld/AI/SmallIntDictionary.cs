using System;
using System.Collections.Generic;
using UnityEngine;

public class BuildingScoreDictionary
{
    static BuildingClass[] Keys;
    static public int Count;

    public float[] Values;

    public float this[BuildingClass key]
    {
        get
        {
            for (int i = 0; i < Count; i++)
                if (Keys[i] == key)
                    return Values[i];
            throw new Exception("Key " + key + " not found");
        }
        set
        {
            for (int i = 0; i < Count; i++)
                if (Keys[i] == key)
                {
                    Values[i] = value;
                    return;
                }
            throw new Exception("Key " + key + " not found");
        }
    }

    public BuildingScoreDictionary()
    {
        if (Keys == null)
        {
            // Same for all instances
            Keys = new BuildingClass[GameDefns.Instance.BuildingDefns.Keys.Count];
            foreach (var defn in GameDefns.Instance.BuildingDefns.Values)
                Keys[Count++] = defn.BuildingClass;
        }

        Values = new float[Count];
        for (int i = 0; i < Count; i++)
            Values[i++] = 0;
    }

    internal void CopyFrom(BuildingScoreDictionary source)
    {
        // these are always the same size so can take some liberties here
        // ItemDefnIds are also always the same
        for (int i = 0; i < Count; i++)
            Values[i] = source.Values[i];
    }

    internal void Reset()
    {
        for (int i = 0; i < Count; i++)
            Values[i] = 0;
    }
}

public class BuildingClassCountDictionary
{
    static BuildingClass[] Keys;
    static public int Count;

    public int[] Values;

    public int this[BuildingClass key]
    {
        get
        {
            for (int i = 0; i < Count; i++)
                if (Keys[i] == key)
                    return Values[i];
            throw new Exception("Key " + key + " not found");
        }
        set
        {
            for (int i = 0; i < Count; i++)
                if (Keys[i] == key)
                {
                    Values[i] = value;
                    return;
                }
            throw new Exception("Key " + key + " not found");
        }
    }

    public BuildingClassCountDictionary()
    {
        if (Keys == null)
        {
            // Same for all instances
            Keys = new BuildingClass[GameDefns.Instance.BuildingDefns.Keys.Count];
            foreach (var defn in GameDefns.Instance.BuildingDefns.Values)
                Keys[Count++] = defn.BuildingClass;
        }

        Values = new int[Count];
        for (int i = 0; i < Count; i++)
            Values[i++] = 0;
    }

    internal void CopyFrom(BuildingClassCountDictionary source)
    {
        // these are always the same size so can take some liberties here
        // ItemDefnIds are also always the same
        for (int i = 0; i < Count; i++)
            Values[i] = source.Values[i];
    }

    internal void Reset()
    {
        for (int i = 0; i < Count; i++)
            Values[i] = 0;
    }
}

public class SmallItemCountDictionary
{
    static ItemType[] Keys;
    static public int Count;

    public int[] Values;

    public int this[ItemType key]
    {
        get
        {
            for (int i = 0; i < Count; i++)
                if (Keys[i] == key)
                    return Values[i];
            throw new Exception("Key " + key + " not found");
        }
        set
        {
            for (int i = 0; i < Count; i++)
                if (Keys[i] == key)
                {
                    Values[i] = value;
                    return;
                }
            throw new Exception("Key " + key + " not found");
        }
    }

    public SmallItemCountDictionary()
    {
        if (Keys == null)
        {
            // Same for all instances
            Keys = new ItemType[GameDefns.Instance.ItemDefns.Keys.Count];
            foreach (var itemDefnId in GameDefns.Instance.ItemDefns.Keys)
                Keys[Count++] = GameDefns.Instance.ItemDefns[itemDefnId].ItemType;
        }

        Values = new int[Count];
        for (int i = 0; i < Count; i++)
            Values[i++] = 0;
    }

    internal void CopyFrom(SmallItemCountDictionary source)
    {
        // these are always the same size so can take some liberties here
        // ItemDefnIds are also always the same
        for (int i = 0; i < Count; i++)
            Values[i] = source.Values[i];
    }

    internal void Reset()
    {
        for (int i = 0; i < Count; i++)
            Values[i] = 0;
    }
}

public class SmallIntDictionary
{
    public string[] Keys;
    public int[] Values;
    public int Count;
    public void Clear() => Count = 0;

    const int maxItems = 10;
    public int this[string key]
    {
        get
        {
            for (int i = 0; i < Count; i++)
                if (Keys[i] == key)
                    return Values[i];
            return 0;
        }
        set
        {
            for (int i = 0; i < Count; i++)
                if (Keys[i] == key)
                {
                    Values[i] = value;
                    return;
                }
            throw new Exception("Key " + key + " not found");
        }
    }

    public SmallIntDictionary()
    {
        Keys = new string[maxItems];
        Values = new int[maxItems];
    }

    public bool Contains(string key)
    {
        for (int i = 0; i < Count; i++)
            if (Keys[i] == key)
                return true;
        return false;
    }

    internal void CopyFrom(SmallIntDictionary source)
    {
        for (int i = 0; i < source.Count; i++)
        {
            Keys[i] = source.Keys[i];
            Values[i] = source.Values[i];
        }
        Count = source.Count;
    }

    internal void Reset()
    {
        for (int i = 0; i < Count; i++)
            Values[i] = 0;
    }
}

public class SmallIntStringDictionary
{
    public int[] Keys;
    public string[] Values;
    public int Count;
    public void Clear() => Count = 0;

    const int maxItems = 10;
    public string this[int key]
    {
        get
        {
            for (int i = 0; i < Count; i++)
                if (Keys[i] == key)
                    return Values[i];
            return null;
        }
        set
        {
            for (int i = 0; i < Count; i++)
                if (Keys[i] == key)
                {
                    Values[i] = value;
                    return;
                }
            Add(key, value);
        }
    }

    public SmallIntStringDictionary()
    {
        Keys = new int[maxItems];
        Values = new string[maxItems];
    }

    public bool Contains(int key)
    {
        for (int i = 0; i < Count; i++)
            if (Keys[i] == key)
                return Values[i] != null;
        return false;
    }

    internal void CopyFrom(SmallIntStringDictionary source)
    {
        for (int i = 0; i < source.Count; i++)
        {
            Keys[i] = source.Keys[i];
            Values[i] = source.Values[i];
        }
        Count = source.Count;
    }

    internal void Reset()
    {
        for (int i = 0; i < Count; i++)
            Values[i] = null;
    }

    internal void Add(int playerId, string pendingConstruction)
    {
        Keys[Count] = playerId;
        Values[Count] = pendingConstruction;
        Count++;
    }
}

public class IntNodeDictionary
{
    public int[] Keys;
    public AINode[] Values;
    public int Count;
    public void Clear() => Count = 0;

    const int maxItems = 100; // maximum # of nodes -- was 50
    public AINode this[int key]
    {
        get
        {
            for (int i = 0; i < Count; i++)
                if (Keys[i] == key)
                    return Values[i];
            return null;
        }
        set
        {
            for (int i = 0; i < Count; i++)
                if (Keys[i] == key)
                {
                    Values[i] = value;
                    return;
                }
            Add(key, value);
        }
    }

    public IntNodeDictionary()
    {
        Keys = new int[maxItems];
        Values = new AINode[maxItems];
    }

    public bool Contains(int key)
    {
        for (int i = 0; i < Count; i++)
            if (Keys[i] == key)
                return Values[i] != null;
        return false;
    }

    // Can't use this since Values point to different GameDatas
    // internal void CopyFrom(IntNodeDictionary source)
    // {
    //     for (int i = 0; i < source.Count; i++)
    //     {
    //         Keys[i] = source.Keys[i];
    //         Values[i] = source.Values[i];
    //     }
    //     Count = source.Count;
    // }

    internal void Reset()
    {
        for (int i = 0; i < Count; i++)
            Values[i] = null;
    }

    internal void Add(int nodeId, AINode node)
    {
        Keys[Count] = nodeId;
        Values[Count] = node;
        Count++;
    }
}


public class IntIntDictionary
{
    public int[] Keys;
    public int[] Values;
    public int Count;
    public void Clear() => Count = 0;

    const int maxItems = 100; // maximum # of nodes -- was 50
    public int this[int key]
    {
        get
        {
            for (int i = 0; i < Count; i++)
                if (Keys[i] == key)
                    return Values[i];
            Debug.Assert(false, "fail");
            return -100000;
        }
        set
        {
            for (int i = 0; i < Count; i++)
                if (Keys[i] == key)
                {
                    Values[i] = value;
                    return;
                }
            Add(key, value);
        }
    }

    public IntIntDictionary()
    {
        Keys = new int[maxItems];
        Values = new int[maxItems];
    }

    public bool Contains(int key)
    {
        for (int i = 0; i < Count; i++)
            if (Keys[i] == key)
                return Values[i] != -100000;
        return false;
    }

    // Can't use this since Values point to different GameDatas
    // internal void CopyFrom(IntNodeDictionary source)
    // {
    //     for (int i = 0; i < source.Count; i++)
    //     {
    //         Keys[i] = source.Keys[i];
    //         Values[i] = source.Values[i];
    //     }
    //     Count = source.Count;
    // }

    internal void Reset()
    {
        for (int i = 0; i < Count; i++)
            Values[i] = -100000;
    }

    internal void Add(int nodeId, int node)
    {
        Keys[Count] = nodeId;
        Values[Count] = node;
        Count++;
    }
}

public class IntListIntDictionary
{
    public int[] Keys;
    public List<int>[] Values;
    public int Count;
    public void Clear() => Count = 0;

    const int maxItems = 100; // maximum # of nodes -- was 50
    public List<int> this[int key]
    {
        get
        {
            for (int i = 0; i < Count; i++)
                if (Keys[i] == key)
                    return Values[i];
            return null;
        }
        set
        {
            for (int i = 0; i < Count; i++)
                if (Keys[i] == key)
                {
                    Values[i] = value;
                    return;
                }
            Add(key, value);
        }
    }

    public IntListIntDictionary()
    {
        Keys = new int[maxItems];
        Values = new List<int>[maxItems];
    }

    public bool Contains(int key)
    {
        for (int i = 0; i < Count; i++)
            if (Keys[i] == key)
                return Values[i] != null;
        return false;
    }

    internal void Reset()
    {
        for (int i = 0; i < Count; i++)
            Values[i] = null;
    }

    internal void Add(int nodeId, List<int> nodeList)
    {
        Keys[Count] = nodeId;
        Values[Count] = nodeList;
        Count++;
    }
}