using System.Collections.Generic;
using UnityEngine;

public class GameMgr : MonoBehaviour
{
    public TownData Town;
    public Node NodePrefab;
    public List<Node> Nodes = new();
    public Material NodeConnectionMat;
    public GameObject NodesFolder;

    void OnEnable()
    {
        NodesFolder.RemoveAllChildren();
        Nodes.Clear();
        Town = new TownData();
        foreach (var nodeData in Town.Nodes)
        {
            var nodeGO = Instantiate(NodePrefab);
            nodeGO.transform.SetParent(NodesFolder.transform);
            nodeGO.InitializeForNodeData(nodeData);
            Nodes.Add(nodeGO);
        }

        addLineRenderer(Nodes[0], Nodes[5]);
        addLineRenderer(Nodes[0], Nodes[1]);
        addLineRenderer(Nodes[1], Nodes[2]);
        addLineRenderer(Nodes[2], Nodes[4]);
        addLineRenderer(Nodes[4], Nodes[7]);
    }

    private void addLineRenderer(Node startNode, Node endNode)
    {
        LineRenderer lineRenderer = new GameObject("Path Line").AddComponent<LineRenderer>();
        lineRenderer.transform.SetParent(NodesFolder.transform); 
        lineRenderer.material = NodeConnectionMat;
        lineRenderer.widthMultiplier = 0.05f;

        List<Vector3> points = new() { startNode.Data.WorldLoc, endNode.Data.WorldLoc };

        lineRenderer.positionCount = points.Count;
        lineRenderer.SetPositions(points.ToArray());
        // lineRenderers.Add(lineRenderer);
    }

    void Update()
    {
        Town.Update();
    }
}
