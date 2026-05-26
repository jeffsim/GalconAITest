using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Left-side list of player-buildable buildings and have/need resource rows for the human player.
/// </summary>
public class BuildableBuildingsPanel : MonoBehaviour
{
    public TextMeshProUGUI Body;

    const float BodyFontSize = 26f;

    static readonly Dictionary<GoodType, int> ScratchTotals = new();
    static readonly StringBuilder ScratchText = new StringBuilder(512);
    static readonly List<BuildingDefn> ScratchBuildings = new();

    void Update()
    {
        if (Body == null)
        {
            CreateDefaultUI();
            if (Body == null) return;
        }

        if (!Mathf.Approximately(Body.fontSize, BodyFontSize))
            Body.fontSize = BodyFontSize;

        var town = AITestScene.Instance?.Town;
        if (town == null)
        {
            Body.text = "";
            return;
        }

        var player = town.GetHumanPlayer() ?? AITestScene.Instance.DebugPlayerToViewDetailsOn;
        if (player == null)
        {
            Body.text = "";
            return;
        }

        PlayerEconomy.GetTotalInventory(player, town, ScratchTotals);
        ScratchBuildings.Clear();
        ScratchBuildings.AddRange(PlayerBuildingCatalog.GetPlayerBuildableDefns());
        ScratchBuildings.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.Ordinal));

        ScratchText.Clear();
        ScratchText.AppendLine("Buildings");
        ScratchText.AppendLine();

        for (int i = 0; i < ScratchBuildings.Count; i++)
        {
            var defn = ScratchBuildings[i];
            if (defn == null) continue;

            bool affordable = PlayerEconomy.CanAfford(player, town, defn);
            ScratchText.Append(affordable ? "<color=#88FF88>" : "<color=#FFFFFF>");
            ScratchText.Append(defn.Name);
            ScratchText.AppendLine("</color>");

            AppendRequirements(defn);
            if (i < ScratchBuildings.Count - 1)
                ScratchText.AppendLine();
        }

        Body.text = ScratchText.ToString();
    }

    void AppendRequirements(BuildingDefn defn)
    {
        if (defn.ConstructionRequirements == null || defn.ConstructionRequirements.Count == 0)
        {
            ScratchText.AppendLine("  (no cost)");
            return;
        }

        foreach (var req in defn.ConstructionRequirements)
        {
            if (req?.Good == null) continue;
            var good = req.Good.GoodType;
            ScratchTotals.TryGetValue(good, out int have);
            int need = req.Amount;
            string label = FormatGoodLabel(req.Good);
            bool enough = have >= need;
            ScratchText.Append("  ");
            ScratchText.Append(enough ? "<color=#88FF88>" : "<color=#FF8888>");
            ScratchText.Append(label).Append(": ").Append(have).Append('/').Append(need);
            ScratchText.AppendLine("</color>");
        }
    }

    static string FormatGoodLabel(GoodDefn good)
    {
        if (good == null) return "?";
        if (!string.IsNullOrEmpty(good.FriendlyName)) return good.FriendlyName;
        return good.GoodType.ToString().ToLowerInvariant();
    }

    void CreateDefaultUI()
    {
        var canvas = FindMainHudCanvas();
        if (canvas == null) return;

        var panelGO = new GameObject("BuildableBuildingsPanel", typeof(RectTransform), typeof(Image));
        panelGO.transform.SetParent(canvas.transform, false);
        var panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0f, 1f);
        panelRT.anchorMax = new Vector2(0f, 1f);
        panelRT.pivot = new Vector2(0f, 1f);
        panelRT.anchoredPosition = new Vector2(10f, -100f);
        panelRT.sizeDelta = new Vector2(420f, 720f);

        var bg = panelGO.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.75f);

        var textGO = new GameObject("Body", typeof(RectTransform));
        textGO.transform.SetParent(panelGO.transform, false);
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(12f, 12f);
        textRT.offsetMax = new Vector2(-12f, -12f);

        Body = textGO.AddComponent<TextMeshProUGUI>();
        Body.font = TMP_Settings.defaultFontAsset;
        Body.fontSize = BodyFontSize;
        Body.color = Color.white;
        Body.alignment = TextAlignmentOptions.TopLeft;
        Body.richText = true;
        Body.textWrappingMode = TextWrappingModes.Normal;
        Body.overflowMode = TextOverflowModes.Overflow;
    }

    static Canvas FindMainHudCanvas()
    {
        var canvases = Object.FindObjectsByType<Canvas>();
        Canvas best = null;
        for (int i = 0; i < canvases.Length; i++)
        {
            var c = canvases[i];
            if (c == null || !c.isActiveAndEnabled) continue;
            if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            // Skip the runtime realtime debug canvas (high sort order).
            if (c.sortingOrder >= 1000) continue;
            if (best == null || c.sortingOrder < best.sortingOrder)
                best = c;
        }
        return best;
    }
}
