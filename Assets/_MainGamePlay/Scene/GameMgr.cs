using System.Collections.Generic;
using PlasticPipe.PlasticProtocol.Messages;
using UnityEngine;

public class GameMgr : MonoBehaviour
{
    public TownData Town;
    public Node NodePrefab;
    public List<Node> Nodes = new();
    public Material NodeConnectionMat;
    public GameObject NodesFolder;
    public TownDefn TestTownDefn;

    void OnEnable()
    {
        ResetTown();
    }

    void ResetTown()
    {
        Town = new TownData(TestTownDefn);

        NodesFolder.RemoveAllChildren();
        Nodes.Clear();

        foreach (var nodeData in Town.Nodes)
        {
            var nodeGO = Instantiate(NodePrefab);
            nodeGO.transform.SetParent(NodesFolder.transform);
            nodeGO.InitializeForNodeData(nodeData);
            Nodes.Add(nodeGO);
        }

        foreach (var nodeData in Town.Nodes)
            foreach (var conn in nodeData.ConnectedNodes)
                addLineRenderer(conn.Start, conn.End);

        Camera.main.transform.position = new Vector3(1.3f, 14, -3.6f);
        Camera.main.transform.rotation = Quaternion.Euler(80, 0, 0);
    }

    public void OnResetClicked()
    {
        ResetTown();
    }

    private void addLineRenderer(NodeData startNode, NodeData endNode)
    {
        LineRenderer lineRenderer = new GameObject("Path Line").AddComponent<LineRenderer>();
        lineRenderer.transform.SetParent(NodesFolder.transform);
        lineRenderer.material = NodeConnectionMat;
        lineRenderer.widthMultiplier = 0.05f;

        List<Vector3> points = new() { startNode.WorldLoc, endNode.WorldLoc };

        lineRenderer.positionCount = points.Count;
        lineRenderer.SetPositions(points.ToArray());
        // lineRenderers.Add(lineRenderer);
    }

    void Update()
    {
        Town.Update();
    }
}
