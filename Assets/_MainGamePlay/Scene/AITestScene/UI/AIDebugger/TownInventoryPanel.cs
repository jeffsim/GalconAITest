using TMPro;
using UnityEngine;

public class TownInventoryPanel : MonoBehaviour
{
    public TextMeshProUGUI Wood;
    public TextMeshProUGUI Stone;

    const float LabelFontSize = 36f;

    static readonly System.Collections.Generic.Dictionary<GoodType, int> ScratchTotals = new();

    void Awake()
    {
        ApplyFontSize(Wood);
        ApplyFontSize(Stone);
    }

    static void ApplyFontSize(TextMeshProUGUI label)
    {
        if (label != null)
            label.fontSize = LabelFontSize;
    }

    void Update()
    {
        var town = AITestScene.Instance?.Town;
        if (town == null) return;

        // Human test scene: show the human's town-wide inventory, not the AI debug target.
        var player = town.GetHumanPlayer() ?? AITestScene.Instance.DebugPlayerToViewDetailsOn;
        if (player == null) return;

        PlayerEconomy.GetTotalInventory(player, town, ScratchTotals);
        ScratchTotals.TryGetValue(GoodType.Wood, out int wood);
        ScratchTotals.TryGetValue(GoodType.Stone, out int stone);
        Wood.text = "Wood: " + wood;
        Stone.text = "Stone: " + stone;
    }
}