using System;
using UnityEngine;

public class PlayerData
{
    public override string ToString() => $"Player ({Name[^1]})";

    public PlayerAIDefn AIDefn;
    public WorkerDefn WorkerDefn;
    public string Name;
    public int Id;
    public Color Color = Color.white;
    public bool ControlledByAI;
    public bool IsHuman => !ControlledByAI;
    public PlayerAI AI;

    public PlayerData(int id, PlayerAIDefn aiDefn, WorkerDefn workerDefn)
    {
        Id = id;
        AIDefn = aiDefn;
        WorkerDefn = workerDefn;
        if (aiDefn != null)
        {
            Name = aiDefn.Name;
            Color = aiDefn.Color;
        }
        else
        {
            Name = $"Player {id}";
            Color = id switch { 1 => Color.red, 2 => Color.green, 3 => Color.blue, _ => Color.white };
        }
        ControlledByAI = aiDefn == null || aiDefn.ControlType == PlayerControlType.AI;
        if (ControlledByAI)
            AI = new PlayerAI(this);
    }

    public void InitializeStaticData(TownData townData)
    {
        AI?.InitializeStaticData(townData);
    }
    
    public void Update(TownData townData)
    {
        AI?.Update(townData);
    }

    internal bool Hates(PlayerData player)
    {
        // For now everyone hates everyone (except for themselves)
        return player != this && player != null;
    }
}