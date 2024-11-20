public class GameLoop
{
    private AIAgent aiAgent;
    private AI_TownState townState;
    private int playerId = 1;

    public GameLoop(TownData townData, PlayerData playerData)
    {
        townState = new AI_TownState(playerData);
        townState.InitializeStaticData(townData);

        aiAgent = new AIAgent(playerId, townState);
    }

    public void Update(TownData townData)
    {
        // Update town state
        townState.UpdateState(townData);

        // Update AI Agent
        aiAgent.Update();
    }
}
