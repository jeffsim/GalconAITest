using TMPro;
using UnityEngine;

public class TownInventoryPanel : MonoBehaviour
{
    public TextMeshProUGUI Wood;
    public TextMeshProUGUI Stone;

    void Update()
    {
        var player = GameMgr.Instance.DebugPlayerToViewDetailsOn;

        // todo: cache
        Wood.text = "Wood: " + getNumItemInPlayerInventory(player, GoodType.Wood);
        Stone.text = "Stone: " + getNumItemInPlayerInventory(player, GoodType.Stone);
    }

    private int getNumItemInPlayerInventory(PlayerData player, GoodType good)
    {
        int count = 0;
        foreach (var node in GameMgr.Instance.Town.Nodes)
            if (node.OwnedBy == player)
                count += node.Inventory[good];
        return count;
    }
}