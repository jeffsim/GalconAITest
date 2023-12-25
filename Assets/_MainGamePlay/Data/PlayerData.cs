using System;
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

    public void InitializeStaticData(TownData townData)
    {
        AI.InitializeStaticData(townData);
    }
    
    public void Update(TownData townData)
    {
        AI.Update(townData);
    }

    internal bool Hates(PlayerData player)
    {
        // For now everyone hates everyone (except for themselves)
        return player != this;
    }
}