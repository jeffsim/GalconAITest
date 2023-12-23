using UnityEngine;

public class Settings
{
    public static float DraggedRoomRotationTime = .15f;
    public static float DraggedRoomFlipTime = .15f;

    public static int AIGameDataPoolSize = 5000;
    public static int PoolSizes = 15000;

    // Set to following to false to disable Debug.Assert checks.  This is only useful when profiling while in Debug mode
    // and there is no reason to set this to false in Release mode (or when not profiling)
    public static bool DoAssertsInDebugMode = true;
    public static bool ExhaustAISearchTree = false;

    public static void Initialize()
    {
        if (!DoAssertsInDebugMode) Debug.Log("Disabling Asserts");
        if (ExhaustAISearchTree) Debug.Log("Performing entire AI tree search - performance may be slow");
    }
}