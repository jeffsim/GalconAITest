using System;
using Sirenix.OdinInspector;

[Serializable]
public class BaseDefn : SerializedScriptableObject
{
    public string Id;
    public bool IsEnabled = true;
}
