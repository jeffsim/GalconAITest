using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AIDebuggerEntry : MonoBehaviour
{
    public TextMeshProUGUI ActionNumber;
    public TextMeshProUGUI ActionType;
    public TextMeshProUGUI Information;
    public Button ThisButton;

    public void ShowForEntry(AIDebuggerEntryData entry)
    {
        ActionNumber.text = entry.ActionNumber.ToString();
        switch (entry.ActionType)
        {
            case AIActionType.AttackFromNode:
                ActionType.text = "Attack";
                break;
            case AIActionType.ConstructBuildingInOwnedNode:
                ActionType.text = "Construct";
                break;
            case AIActionType.SendWorkersToNode:
                ActionType.text = "Send";
                break;
            default:
                ActionType.text = "TODO: " + entry.ActionType + "";
                break;
        }
        Information.text = entry.InformationString();
        
        // Set ThisButton's Left rect transform to be 10 * entry.RecurseDepth
        var rt = ThisButton.GetComponent<RectTransform>();
        rt.offsetMin = new Vector2(20 * entry.RecurseDepth, rt.offsetMin.y);
    }
}