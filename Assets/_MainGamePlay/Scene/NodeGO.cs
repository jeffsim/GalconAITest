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
    }

    void Update()
    {
    }
}
