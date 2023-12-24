using UnityEngine;

public class PlayerData
{
    public WorkerDefn WorkerDefn;
    public string Name;
    public Color Color = Color.white;
    public bool ControlledByAI;
    PlayerAI AI;

    public PlayerData()
    {
        AI = new PlayerAI(this);
    }

    public void Update(TownData townData)
    {
        AI.Update(townData);
    }
}