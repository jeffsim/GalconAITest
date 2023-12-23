using System;
using Sirenix.OdinInspector;
using UnityEngine;

[Serializable]
public class BaseDefn : SerializedScriptableObject
{
    public string Id;
    public bool IsTestDefn = false;

    [NonSerialized] public int Index;
}
