using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AIDebuggerEntry : MonoBehaviour
{
    public TextMeshProUGUI ActionNumber;
    public TextMeshProUGUI ActionType;
    public TextMeshProUGUI Information;
    public TextMeshProUGUI Score;
    public TextMeshProUGUI Count;
    public Button ThisButton;
    AIDebuggerEntryData entry;
    AIDebuggerPanel panel;

    public void ShowForEntry(AIDebuggerEntryData entry, AIDebuggerPanel panel)
    {
        this.panel = panel;
        this.entry = entry;
        ActionNumber.text = entry.ActionNumber.ToString();
        switch (entry.ActionType)
        {
            case AIActionType.AttackFromNode:
                ActionType.text = "Attack";
                break;
            case AIActionType.ConstructBuildingInOwnedEmptyNode:
                ActionType.text = "Construct";
                break;
            case AIActionType.ConstructBuildingInEmptyNode:
                ActionType.text = "Construct";
                break;
            case AIActionType.SendWorkersToOwnedNode:
                ActionType.text = "Send";
                break;
            case AIActionType.SendWorkersToEmptyNode:
                ActionType.text = "Send";
                break;
            default:
                ActionType.text = "TODO: " + entry.ActionType + "";
                break;
        }
        Information.text = entry.InformationString();
        Score.text = entry.Score.ToString("0.0");

        // Count shows "(X,Y)" where X is the # of immediate children and Y is the # of all children
        Count.text = "(" + entry.ChildEntries.Count + ", " + entry.AllChildEntriesCount + ")";

        // Indent
        var rt = ThisButton.GetComponent<RectTransform>();
        rt.offsetMin = new Vector2(20 * entry.RecurseDepth, rt.offsetMin.y);

        if (entry.IsBestOption)
        {
            var colors = ThisButton.colors;
            colors.normalColor = new Color(0, 0.13f, .46f);
            colors.highlightedColor = new Color(0, 0.32f, .63f);
            ThisButton.colors = colors;

            Information.color = Color.yellow;
            Score.color = Color.yellow;
        }
    }

    public void OnClicked()
    {
        panel.ExpandedEntries[entry.ActionNumber] = !panel.ExpandedEntries[entry.ActionNumber];
        panel.Refresh();
    }
}