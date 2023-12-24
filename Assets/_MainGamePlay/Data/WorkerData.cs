using UnityEngine;

public class WorkerData
{
    public PlayerData OwnedBy;
    public Vector3 WorldLoc;

    public WorkerData(Vector3 worldLoc, PlayerData player)
    {
        OwnedBy = player;

        var randomOffset = new Vector3(Random.Range(-.1f, .1f), 0, Random.Range(-.1f, .1f));
        WorldLoc = worldLoc + randomOffset;
    }
}