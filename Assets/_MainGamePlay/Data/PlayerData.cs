using System;
using UnityEngine;

public class PlayerData
{
    public override string ToString() => $"Player ({Name[^1]})";

    public WorkerDefn WorkerDefn;
    public string Name;
    public int Id;
    public Color Color = Color.white;
    public bool ControlledByAI;
    public PlayerAI AI;

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
        return player != this && player != null;
    }
}