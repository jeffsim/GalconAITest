using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AIDebuggerEntry : MonoBehaviour
{
    public TextMeshProUGUI ActionNumber;
    public TextMeshProUGUI ActionType;
    public TextMeshProUGUI Information;
    public TextMeshProUGUI Score;
    public Button ThisButton;

    public void ShowForEntry(AIDebuggerEntryData entry)
    {
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
        
        // Indent
        var rt = ThisButton.GetComponent<RectTransform>();
        rt.offsetMin = new Vector2(20 * entry.RecurseDepth, rt.offsetMin.y);
    }

    public void OnClicked()
    {
        Debug.Log("Clicked " + ActionNumber.text);
    }
}