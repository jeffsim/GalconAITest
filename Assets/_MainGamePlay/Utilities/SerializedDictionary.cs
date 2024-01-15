using System.Collections.Generic;
using UnityEngine;
using System;

[Serializable]
public class SerializedDictionary<T1, T2> : Dictionary<T1, T2>, ISerializationCallbackReceiver
{
    [HideInInspector, SerializeField] List<T1> exported_Keys = new List<T1>();
    [HideInInspector, SerializeReference] List<T2> exported_Values = new List<T2>();
    
    public void OnBeforeSerialize()
    {
        exported_Keys = new List<T1>();
        exported_Values = new List<T2>();
        foreach (var pair in this)
        {
            exported_Keys.Add(pair.Key);
            exported_Values.Add(pair.Value);
        }
    }

    public void OnAfterDeserialize() 
    {
        // exported_Keys will be empty on reload, but populated on recompile
        for (int i = 0; i < exported_Keys.Count; i++)
            this[exported_Keys[i]] = exported_Values[i];
    }
}
 