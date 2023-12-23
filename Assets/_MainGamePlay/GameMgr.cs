using UnityEngine;

public class GameMgr : MonoBehaviour
{
    public TownData Town;

    void Start()
    {
        var world = GameDefns.Instance.WorldDefns["mainWorld"];
        Town = new TownData(world.Towns[0].Town);
    }

    void Update()
    {
        Town.Update();
    }
}
