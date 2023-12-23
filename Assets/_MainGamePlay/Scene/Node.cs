using UnityEngine;

public class Node : MonoBehaviour
{
    public NodeData Data;

    public void InitializeForNodeData(NodeData data)
    {
        Data = data;
        transform.position = data.WorldLoc;
        if (data.OwnedBy != null)
            GetComponent<MeshRenderer>().material.color = data.OwnedBy.Color;
        else
            GetComponent<MeshRenderer>().material.color = Color.gray;
    }

    void Update()
    {
    }
}
