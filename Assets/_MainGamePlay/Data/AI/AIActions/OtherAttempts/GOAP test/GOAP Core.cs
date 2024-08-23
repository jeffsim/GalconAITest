using System.Collections.Generic;

public partial class PlayerAI
{
    private AIMap_State mapState;
    private int playerId;
    private List<Goal> goals;

    public void InitializeGOAP(AIMap_State initialState, int playerId)
    {
        this.mapState = initialState;
        this.playerId = playerId;
        InitializeGoals();
    }

    private void InitializeGoals()
    {
        goals = new List<Goal>  {
            new SpecificResourceCollectionGoal(GoodType.Wood),
            new SpecificResourceCollectionGoal(GoodType.Stone),
            // new ResourceExtractionGoal(),
            // new StrategicExpansionGoal(),
            // new TacticalExpansionGoal(),
            // new ResourceGatheringGoal(),
            // new DefensiveInfrastructureGoal(),
            // new OffensiveFleetConstructionGoal(),
            // new EstablishAlliancesGoal(),
            // Add other specific resource goals as needed
        };
    }


    public Goal DetermineBestGoal()
    {
        Goal bestGoal = null;
        float highestUtility = float.MinValue;

        foreach (Goal goal in goals)
        {
            float utility = goal.CalculateUtility(mapState, playerId);
            if (utility > highestUtility)
            {
                highestUtility = utility;
                bestGoal = goal;
            }
        }

        return bestGoal;
    }
}
