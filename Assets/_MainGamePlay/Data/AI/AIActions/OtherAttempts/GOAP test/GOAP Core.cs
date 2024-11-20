using System.Collections.Generic;

public partial class PlayerAI
{
    private AIMap_State mapState;
    private int playerId;
    private List<Goal> goals;

    public void InitializeGOAP(AIMap_State initialState, int playerId)
    {
        mapState = initialState;
        this.playerId = playerId;
        InitializeGoals();
    }

    private void InitializeGoals()
    {
        goals = new List<Goal>  {
            new ConstructBuildingGoal(),

            // new SpecificResourceCollectionGoal(GoodType.Wood),
            // new SpecificResourceCollectionGoal(GoodType.Stone),
            // new ResourceExtractionGoal(),
            // new StrategicExpansionGoal(),
            // new TacticalExpansionGoal(),
            // new ResourceGatheringGoal(),
            // new DefensiveInfrastructureGoal(),
            // new OffensiveFleetConstructionGoal(),
            // new EstablishAlliancesGoal(),
        };
    }

    public Goal DetermineBestGoal()
    {
        Goal bestGoal = null;
        float highestUtility = float.MinValue;

        foreach (Goal goal in goals)
        {
            var playerNodes = mapState.GetPlayerNodes(playerId);
            float utility = goal.CalculateUtility(mapState, playerId, playerNodes);
            if (utility > highestUtility)
            {
                highestUtility = utility;
                bestGoal = goal;
            }
        }

        return bestGoal;
    }
}
