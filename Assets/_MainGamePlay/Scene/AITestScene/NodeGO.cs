using TMPro;
using UnityEngine;

public class NodeGO : MonoBehaviour
{
    public NodeData Data;
    public TextMeshPro NodeIdText;
    public TextMeshPro BuildingText;
    public MeshRenderer BaseObject;
    public MeshRenderer BuildingObject;

    public void InitializeForNodeData(NodeData data)
    {
        name = "Node " + data.NodeId + " - " + data.WorldLoc;
        NodeIdText.text = data.NodeId.ToString();
        BuildingText.text = data.Building?.Defn.Name ?? "";

        Data = data;
        transform.position = data.WorldLoc;

        BuildingText.color = data.OwnedBy?.Color ?? Color.white;
        if (data.OwnedBy != null)
            BaseObject.material.color = data.OwnedBy.Color;
        BuildingObject.material.color = data.Building?.Defn.Color ?? Color.gray;
        BuildingObject.gameObject.SetActive(data.Building != null);

        data.OnBuildingConstructed += () =>
        {
            BuildingText.text = data.Building?.Defn.Name ?? "";
            BuildingObject.material.color = data.Building?.Defn.Color ?? Color.gray;
            BuildingObject.gameObject.SetActive(data.Building != null);
            if (data.OwnedBy != null)
                BaseObject.material.color = data.OwnedBy.Color;
        };
    }

    void Update()
    {
        NodeIdText.text = Data.NodeId + " (" + Data.NumWorkers + ")";

        // If this node is the target node of the best action in the current AI then add * to the nodeidetxt
        // foreach (var player in AITestScene.Instance.Town.Players)
        // {
        //     if (player == null) continue;
        //     var ai = player.AI;
        //     if (ai != null && ai.BestNextActionToTake != null && ai.BestNextActionToTake.DestNode != null &&
        //         Data == ai.BestNextActionToTake.DestNode.RealNode)
        //     {
        //         NodeIdText.text += " (" + player.Id + ")";
        //         var color = new Color(player.Color.r, player.Color.g, player.Color.b, 0.5f);
        //         BaseObject.material.color = color;

        //         // get the path line from this node to the target node and set it to reed
        //         var pathSteps = AITestScene.Instance.pathSteps;
        //         foreach (var pathStep in pathSteps)
        //         {
        //             if (pathStep.Start == Data || pathStep.End == Data)
        //             {
        //                 if (pathStep.End == ai.BestNextActionToTake.DestNode.RealNode ||
        //                 pathStep.End == ai.BestNextActionToTake.SourceNode.RealNode)
        //                     pathStep.LineRenderer.material.color = Color.red;
        //             }
        //         }
        //     }
        // }
    }
}
