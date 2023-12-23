using UnityEngine;

public class GameMgr : MonoBehaviour
{
    public TownData Town;

    void Start()
    {
        Town = new TownData();
    }

    void Update() 
    {
        Town.Update();
    }
}
