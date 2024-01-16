using UnityEngine;
using UnityEditor;
using System;
using Sirenix.OdinInspector.Editor;

[CustomEditor(typeof(TownDefn))]
public class TownDefnEditor : OdinEditor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var townDefn = (TownDefn)target;

        if (GUILayout.Button("Save Nodes"))
        {
            var nodeGOs = FindObjectsByType<NodeGO>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var nodeGO in nodeGOs)
            {
                var nodeDefn = getTownDefnNodeById(townDefn, nodeGO.Data.NodeId);
                if (nodeDefn != null)
                    nodeDefn.WorldLoc = nodeGO.transform.position;
            }
            EditorUtility.SetDirty(townDefn);
            Debug.Log("sa");
        }
    }

    private NodeDefn getTownDefnNodeById(TownDefn townDefn, int nodeId)
    {
        foreach (var node in townDefn.Nodes)
            if (node.NodeId == nodeId)
                return node;
        Debug.Log("Failed to find node with id " + nodeId);
        return null;
    }
}