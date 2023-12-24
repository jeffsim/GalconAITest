public abstract class AIStrategy
{
    public string DebugName;
    protected PlayerData Player;
    
    public AIStrategy(PlayerData player)
    {
        Player = player;
    }

    public abstract float Evaluate(AITownData aiTownData);
    public abstract bool Applies(AITownData aiTownData);
}

// Wood is a vital resource and a key part of the early game.  We need to get a woodcutter up and running ASAP.
public class AIStrategy_InitialPlacement_Woodcutter : AIStrategy
{
    public new string DebugName = "AIStrategy_InitialPlacement_Woodcutter";

    public AIStrategy_InitialPlacement_Woodcutter(PlayerData player) : base(player) {}

    public override bool Applies(AITownData aiTownData)
    {
        // Only applies if we don't have a woodcutter yet and in early game
        return aiTownData.PlayerBuildingCount(Player, "woodcutter") == 0 && aiTownData.TownData.Nodes.Count < 10;
    }

    public override float Evaluate(AITownData aiTownData)
    {
        return 0.5f;
    }
}