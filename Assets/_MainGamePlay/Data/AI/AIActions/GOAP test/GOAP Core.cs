using System.Collections.Generic;
using System.Linq;

public class AINode_State
{
    public int OwnerId;
    public Dictionary<ResourceType, int> Resources;
    public List<AINode_State> Neighbors;
    public bool IsUnderThreat;
    public int MilitaryStrength;
    // Add other relevant properties for a node
}

public class AIMap_State
{
    public List<AINode_State> AllNodes { get; set; }

    // public AINode_State FindClosestResourceNode(int playerId, ResourceType resourceType)
    // {
    //     // Find the nearest node with the resource, avoiding enemy-controlled nodes
    //     return AllNodes.Where(n => n.Resources.ContainsKey(resourceType) && n.OwnerId == playerId)
    //                    .OrderBy(n => n.DistanceTo(playerId)) // Assuming DistanceTo is a method to calculate distance
    //                    .FirstOrDefault();
    // }

    public float CalculatePathCost(int playerId, AINode_State targetNode)
    {
        // Placeholder for pathfinding logic that computes cost to reach a node
        return targetNode.Neighbors.Where(n => n.OwnerId != playerId).Sum(n => n.MilitaryStrength * 1.2f);
    }

    public float GetCriticalityOfResource(int playerId, ResourceType resourceType)
    {
        // Placeholder for a method that evaluates how critical a resource is
        return AllNodes.Where(n => n.OwnerId == playerId && n.Resources.ContainsKey(resourceType))
                       .Sum(n => 100 - n.Resources[resourceType]); // Hypothetical criticality assessment
    }
}

public abstract class Goal
{
    // Now also includes a method to estimate the cost of achieving the goal
    public abstract float CalculateUtility(AIMap_State mapState, int playerId);
    public abstract float EstimateCost(AIMap_State mapState, int playerId);

    public float CalculateScore(AIMap_State mapState, int playerId)
    {
        float utility = CalculateUtility(mapState, playerId);
        float cost = EstimateCost(mapState, playerId);
        return utility - cost; // Adjust this formula based on game specifics
    }
    
    protected List<AINode_State> GetPlayerNodes(AIMap_State mapState, int playerId)
    {
        return mapState.AllNodes.Where(node => node.OwnerId == playerId).ToList();
    }
}

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
            // new ResourceExtractionGoal(),
            // new SpecificResourceCollectionGoal(ResourceType.Wood), // Example for a specific resource

            new StrategicExpansionGoal(),
            new TacticalExpansionGoal(),
            new ResourceGatheringGoal(),
            new DefensiveInfrastructureGoal(),
            new OffensiveFleetConstructionGoal(),
            new EstablishAlliancesGoal(),
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
