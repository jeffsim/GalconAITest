using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps play mode ticking when the Unity Editor loses OS focus (e.g. alt-tab to another app).
/// PlayerSettings.runInBackground alone is not always applied reliably in the Editor.
/// </summary>
[InitializeOnLoad]
static class EditorPlayModeBackgroundEnforcer
{
    static EditorPlayModeBackgroundEnforcer()
    {
        EnsurePlayerSettingEnabled();
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
            Application.runInBackground = true;
    }

    static void EnsurePlayerSettingEnabled()
    {
        if (PlayerSettings.runInBackground)
            return;

        PlayerSettings.runInBackground = true;
        AssetDatabase.SaveAssets();
    }
}
