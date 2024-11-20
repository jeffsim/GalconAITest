using System.Collections.Generic;

public class AIAgent
{
    private int playerId;
    private List<Goal> goals;
    private List<Action2> availableActions;
    private GOAPPlanner planner;
    private WorldState worldState;
    private AI_TownState townState;

    public Goal CurrentGoal { get; private set; }
    public Queue<Action2> CurrentPlan { get; set; }
    
    public AIAgent(int playerId, AI_TownState townState)
    {
        this.playerId = playerId;
        this.townState = townState;
        worldState = new WorldState(townState, playerId);
        planner = new GOAPPlanner();
        InitializeGoals();
    }

    private void InitializeGoals()
    {
        goals = new List<Goal>();

        // Initialize your goals here
        goals.Add(new ConstructBuildingGoal(BuildingType.Woodcutter));
        // Add other goals as needed
    }

    public void Update()
    {
        // Update world state
        worldState.UpdateWorldState();

        // Determine best goal
        CurrentGoal = DetermineBestGoal();
        if (CurrentGoal == null)
            return;

        // Get available actions
        availableActions = GetAvailableActions();

        // Plan to achieve the goal
        CurrentPlan = planner.Plan(worldState.Conditions, CurrentGoal.GetGoalState(), availableActions);

        if (CurrentPlan == null)
            return;

        // Execute the plan
        ExecutePlan(CurrentPlan);
    }

    public Goal DetermineBestGoal()
    {
        Goal bestGoal = null;
        float highestUtility = float.MinValue;

        foreach (var goal in goals)
        {
            var utility = goal.CalculateUtility(townState, playerId);

            if (utility > highestUtility)
            {
                highestUtility = utility;
                bestGoal = goal;
            }
        }

        return bestGoal;
    }

    public List<Action2> GetAvailableActions()
    {
        return ActionFactory.GetAvailableActions(townState, playerId);
    }

    public void ExecutePlan(Queue<Action2> plan)
    {
        while (plan.Count > 0)
        {
            var action = plan.Dequeue();
            PerformAction(action);
        }
    }

    public void PerformAction(Action2 action)
    {
        action.Perform(townState, playerId);
    }
}