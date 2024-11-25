using TMPro;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.UI;

public class AIDebuggerEntry : MonoBehaviour
{
    public TextMeshProUGUI ActionNumber;
    public TextMeshProUGUI Information;
    public TextMeshProUGUI Score;
    public TextMeshProUGUI Count;
    public Button ThisButton;
    AIDebuggerPanel panel;
    int ActionNum;
    public void ShowForEntry(AIDebuggerEntryData entry, AIDebuggerPanel panel)
    {
        this.panel = panel;
        
        ActionNum = entry.ActionNumber;
        ActionNumber.text = ActionNum.ToString();
        ActionNumber.color = AITestScene.Instance.DebugPlayerToViewDetailsOn.Color;

        switch (entry.ActionType)
        {
            case AIActionType.ConstructBuildingInEmptyNode:
                break;
            case AIActionType.SendWorkersToOwnedNode:
                break;
            default:
                break;
        }
        Information.text = entry.InformationString();
        if (entry.BestNextAction != null)
        {
            if (AITestScene.Instance.DebugOutputActionBeforeScore)
                Score.text = entry.Debug_ActionScoreBeforeSubactions.ToString("0.0") + ", " + entry.FinalActionScore.ToString("0.0");
            else
                Score.text = entry.FinalActionScore.ToString("0.0");
        }
        else
            Score.text = entry.FinalActionScore.ToString("0.0");

        // Count shows "(X,Y)" where X is the # of immediate children and Y is the # of all children
        Count.text = "(" + entry.ChildEntries.Count + ", " + entry.AllChildEntriesCount + ")";

        // Indent
        var rt = ThisButton.GetComponent<RectTransform>();
        rt.offsetMin = new Vector2(20 * entry.RecurseDepth, rt.offsetMin.y);

        if (entry.IsInBestStrategyPath)
        {
            var colors = ThisButton.colors;
            colors.normalColor = new Color(0.2f, 0.13f, .46f);
            colors.highlightedColor = new Color(0, 0.32f, .63f);
            ThisButton.colors = colors;

            Information.color = Color.yellow;
            Score.color = Color.yellow;
        }
        else if (entry.IsHighestOptionOfPeers)
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
        panel.ExpandedEntries[ActionNum] = !panel.ExpandedEntries[ActionNum];
        panel.Refresh();
    } 
}