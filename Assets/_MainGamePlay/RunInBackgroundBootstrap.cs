using UnityEngine;

static class RunInBackgroundBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void EnableRunInBackground()
    {
        Application.runInBackground = true;
    }
}
