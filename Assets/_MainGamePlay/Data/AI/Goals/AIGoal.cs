// Strategic intent that the AI is currently pursuing. Goals are NOT actions: they describe
// "what the player wants to be true" (e.g. "capture node #25 because it has wood",
// "build a Barracks because attacking matters"). Actions are the recursive search's job;
// goals are the source from which resource demand and other priors are derived.
//
// One goal collection is rebuilt per real-game Update via AIGoalEnumerator and frozen for
// the duration of the recursive search.
public enum AIGoalType
{
    None,

    // "Take that node." Triggered by enemy/neutral nodes adjacent to our territory.
    // Drives demand for attack-enabling buildings (Barracks) and the resource gathers
    // those need.
    CaptureNode,

    // "Don't lose that node." Triggered by owned nodes with enemy force in their neighbors.
    // Currently informs the buttress heuristic; v2 will let it drive demand for defensive
    // structures.
    DefendFrontier,

    // "We want this kind of building in our economy." Triggered by buildable buildings the
    // player doesn't yet own. Drives demand for the building's construction requirements.
    EconomicTier,

    // "Keep a buffer of this resource on hand." Triggered when inventory falls below the
    // PlayerAIDefn stockpile target. Drives demand for the resource and staffing of gatherers.
    MaintainStockpile,
}

public class AIGoal
{
    public AIGoalType Type;

    // For CaptureNode / DefendFrontier
    public AI_NodeState TargetNode;

    // For EconomicTier
    public BuildingDefn TargetBuilding;

    // For MaintainStockpile
    public GoodType TargetGoodType;

    // Higher = more important; combines personality weights with the situational fit.
    public float Value;

    // Estimated turns until this goal is achieved if pursued. Used together with Value to
    // form an "urgency" (Value / Horizon) that scales the goal's contribution to demand.
    public int HorizonTurns;

    // Human-readable note used by the simulation dump and debugger output.
    public string DebugReason;

    public void Reset()
    {
        Type = AIGoalType.None;
        TargetNode = null;
        TargetBuilding = null;
        TargetGoodType = GoodType.Unset;
        Value = 0f;
        HorizonTurns = 0;
        DebugReason = null;
    }
}
