using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(CameraDragger))]
public class CameraDraggerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        CameraDragger script = (CameraDragger)target;

        if (GUILayout.Button("Save Camera Position"))
        {
            // Save the position to the active TownDefn
            var townDefn = AITestScene.Instance != null ? AITestScene.Instance.TestTownDefn : null;
            if (townDefn == null)
            {
                Debug.LogError("No active TownDefn found on AITestScene.Instance.TestTownDefn");
                return;
            }
            townDefn.Debug_StartingCameraPosition = script.transform.position;
            EditorUtility.SetDirty(townDefn);
            Debug.Log("Camera position saved to current TownDefn");
        }
    }
}