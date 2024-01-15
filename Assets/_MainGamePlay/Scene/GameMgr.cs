using System.Collections.Generic;
using UnityEngine;

public class GameMgr : MonoBehaviour
{
    public TownData Town;
    public NodeGO NodePrefab;
    public Worker WorkerPrefab;

    public int MaxAIDepth = 7;
    public bool DebugOutputStrategyToConsole = false;
    public bool DebugOutputStrategyReasons = false;

    public List<NodeGO> Nodes = new();
    public List<Worker> Workers = new();
    public Material NodeConnectionMat;

    public GameObject NodesFolder;
    public GameObject WorkersFolder;

    public TownDefn TestTownDefn;
    public WorkerDefn TestWorkerDefn;

#if DEBUG
    // Debugger panel
    public bool ShowDebuggerAI = true;
    bool lastShowDebuggerAI;
    public AIDebuggerPanel AIDebuggerPanel;
    public int ShowDebuggerAI_PlayerId = 2;
#endif

    public static GameMgr Instance;

    void OnEnable()
    {
        Instance = this;
        ResetTown();

        Application.targetFrameRate = 60;
    }

    void ResetTown()
    {
        Town = new TownData(TestTownDefn, TestWorkerDefn);

        NodesFolder.RemoveAllChildren();
        WorkersFolder.RemoveAllChildren();
        Nodes.Clear();
        Workers.Clear();

        foreach (var nodeData in Town.Nodes)
        {
            var nodeGO = Instantiate(NodePrefab);
            nodeGO.transform.SetParent(NodesFolder.transform);
            nodeGO.InitializeForNodeData(nodeData);
            Nodes.Add(nodeGO);
        }

        foreach (var nodeData in Town.Nodes)
            foreach (var conn in nodeData.NodeConnections)
                addLineRenderer(conn.Start, conn.End);

        // Workers
        // foreach (var workerData in Town.Workers)
        // {
        //     var workerGO = Instantiate(WorkerPrefab);
        //     workerGO.transform.SetParent(WorkersFolder.transform);
        //     workerGO.InitializeForData(workerData);
        //     Workers.Add(workerGO);
        // }

        //   Camera.main.transform.position = new Vector3(1.3f, 14, -3.6f);
        //     Camera.main.transform.rotation = Quaternion.Euler(80, 0, 0);

        lastShowDebuggerAI = ShowDebuggerAI;

        AIDebuggerPanel.InitializeForTown(Town);
    }

    public void OnResetClicked()
    {
        ResetTown();
    }

    public void OnStepClicked()
    {
        // move the world forward one turn
        Debug.Log("OnStepClicked");
    }

    private void addLineRenderer(NodeData startNode, NodeData endNode)
    {
        LineRenderer lineRenderer = new GameObject("Path Line").AddComponent<LineRenderer>();
        lineRenderer.transform.SetParent(NodesFolder.transform);
        lineRenderer.material = NodeConnectionMat;
        lineRenderer.widthMultiplier = 0.4f;
        lineRenderer.transform.rotation = Quaternion.Euler(90, 0, 0);
        lineRenderer.alignment = LineAlignment.TransformZ;
        List<Vector3> points = new() { startNode.WorldLoc + new Vector3(0, .01f, 0), endNode.WorldLoc + new Vector3(0, .01f, 0) };

        lineRenderer.positionCount = points.Count;
        lineRenderer.SetPositions(points.ToArray());
        // lineRenderers.Add(lineRenderer);
    }

    void Update()
    {
        Town.Update();

#if DEBUG
        if (lastShowDebuggerAI != ShowDebuggerAI)
        {
            lastShowDebuggerAI = ShowDebuggerAI;
            AIDebuggerPanel.gameObject.SetActive(lastShowDebuggerAI);
            if (lastShowDebuggerAI)
                AIDebuggerPanel.Refresh();
        }
    }
#endif
}