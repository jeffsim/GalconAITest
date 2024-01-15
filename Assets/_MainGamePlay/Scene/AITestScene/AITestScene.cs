using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class AITestScene : MonoBehaviour
{
    public TownData Town;
    public TownDefn TestTownDefn;

    [FoldoutGroup("Nodes", false)] public NodeGO NodePrefab;
    [FoldoutGroup("Nodes", false)] public List<NodeGO> Nodes = new();
    [FoldoutGroup("Nodes", false)] public Material NodeConnectionMat;
    [FoldoutGroup("Nodes", false)] public GameObject NodesFolder;

    [FoldoutGroup("Workers", false)] public Worker WorkerPrefab;
    [FoldoutGroup("Workers", false)] public List<Worker> Workers = new();
    [FoldoutGroup("Workers", false)] public GameObject WorkersFolder;
    [FoldoutGroup("Workers", false)] public WorkerDefn TestWorkerDefn;

#if DEBUG
    public PlayerData DebugPlayerToViewDetailsOn;
   
    // Debugger panel
    public AIDebuggerPanel AIDebuggerPanel;
    public int MaxAIDepth = 7;
    public bool ShowDebuggerAI = true;
    bool lastShowDebuggerAI;
    public bool DebugOutputStrategyToConsole = false;
    public bool DebugOutputStrategyReasons = false;
#endif

    public static AITestScene Instance;

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

        DebugPlayerToViewDetailsOn = Town.Players[1];

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
        Town.Debug_WorldTurn();
        AITestScene.Instance.Town.Update(); // force an update to get latest AI
        AIDebuggerPanel.ShowBestClicked();
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