public partial class PlayerAI
{
    private void TryConstructBuildingInNode(AI_NodeState node, ref AIAction bestAction, int curDepth, int recurseCount, int thisActionNum)
    {
        if (node.HasBuilding)
            return; // Node already has a building

        // TODO: Only attempt to construct buildings that we have resources within 'reach' to build.
        for (int i = 0; i < numBuildingDefns; i++)
        {
            var buildingDefn = buildableBuildingDefns[i];

            // Verify we can perform the action
            if (!aiTownState.ConstructionResourcesCanBeReachedFromNode(node, buildingDefn.ConstructionRequirements)) continue;

            // Perform the action and get the score of the state after the action is performed
            aiTownState.BuildBuilding(node, buildingDefn, out GoodType res1Id, out int resource1Amount, out GoodType res2Id, out int resource2Amount);
            aiTownState.EvaluateScore(out float scoreAfterActionAndBeforeSubActions, out DebugAIStateReasons debugOutput_actionScoreReasons);

            // Recursively determine what the best action is after this action is performed
            var actionScore = RecursivelyDetermineBestAction(curDepth + 1, scoreAfterActionAndBeforeSubActions);
            if (actionScore.Score > bestAction.Score)
            {
                // This is the best action so far; save the action so we can return it
                bestAction.Score = actionScore.ScoreBeforeSubActions;
                bestAction.Type = AIActionType.ConstructBuildingInOwnedNode;
                bestAction.SourceNode = node;
                bestAction.BuildingToConstruct = buildingDefn;
#if DEBUG
                bestAction.TrackStrategyDebugInfoInAction(actionScore, debugOutput_actionScoreReasons, thisActionNum, recurseCount, curDepth);
#endif
            }

            // Undo the action
            aiTownState.Undo_BuildBuilding(node, res1Id, resource1Amount, res2Id, resource2Amount);
        }
    }
}