using UnityEngine;

public partial class PlayerAI
{
    AIAction RecursivelyDetermineBestAction_GOAP(int curDepth, float scoreOnEntry)
    {
        var waitAction = new AIAction
        {
            Type = AIActionType.DoNothing,
            Score = scoreOnEntry
        };
        return waitAction;
    }
}