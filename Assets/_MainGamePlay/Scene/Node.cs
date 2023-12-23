using TMPro;
using UnityEngine;

public class Node : MonoBehaviour
{
    public NodeData Data;
    public TextMeshPro NodeIdText;
    public TextMeshPro BuildingText;

    public void InitializeForNodeData(NodeData data)
    {
        name = "Node " + data.NodeId + " - " + data.WorldLoc;
        NodeIdText.text = data.NodeId.ToString();
        BuildingText.text = data.Building?.Defn.Name ?? "empty";

        Data = data;
        transform.position = data.WorldLoc;
        if (data.OwnedBy != null)
        {
            BuildingText.color = data.OwnedBy.Color;
            if (data.Building != null)
                GetComponent<MeshRenderer>().material.color = data.Building.Defn.Color;
            else
                GetComponent<MeshRenderer>().material.color = data.OwnedBy.Color;
        }
        else
        {
            BuildingText.color = Color.white;
            var color = data.Building != null ? data.Building.Defn.Color : Color.gray;
            GetComponent<MeshRenderer>().material.color = color;
        }
    }

    void Update()
    {
    }
}
