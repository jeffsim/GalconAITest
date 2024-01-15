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
            // This uses the GameSettingsDefn to set the camera position
            var gameSettingsDefn = GameDefns.Instance.GameSettingsDefns["default"];
            if (gameSettingsDefn == null)
            {
                Debug.LogError("GameSettingsDefn with id 'default' not found in Resources/Defns/GameSettings.  Please create one");
                return;
            }
            gameSettingsDefn.Debug_StartingCameraPosition = script.transform.position;
            EditorUtility.SetDirty(gameSettingsDefn);
            Debug.Log("Camera position saved in Resources/Defns/GameSettings (default)");
        }
    }
}