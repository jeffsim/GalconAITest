using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class AITestScene : MonoBehaviour
{
    [NonSerialized][ShowInInspector] public TownData Town;
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
    public List<PathStep> pathSteps = new();

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
        pathSteps.Clear();
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

        DebugPlayerToViewDetailsOn = Town.Players[2];

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
        Town.Update(); // force an update to get latest AI
        AIDebuggerPanel.ShowBestClicked();
    }

    public class PathStep
    {
        public NodeData Start;
        public NodeData End;
        public LineRenderer LineRenderer;
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
        pathSteps.Add(new PathStep { Start = startNode, End = endNode, LineRenderer = lineRenderer });
    }

    void DrawArrow(Vector3 start, Vector3 end, Color color, string label)
    {

        var draw = Drawing.Draw.ingame;
        draw.PushLineWidth(6);
        draw.Arrow(start, end, color);
        draw.PopLineWidth();
        var pos = (start + end) / 2;

        draw.Label2D(pos, label, 30, Drawing.LabelAlignment.Center, Color.black);
        draw.Label2D(pos + new Vector3(-.02f, 0.02f, .05f), label, 30, Drawing.LabelAlignment.Center, Color.white);

    }
    private void DrawNextAISteps(PlayerData player)
    {
        if (player == null) return;
        var ai = player.AI;
        if (ai == null || ai.BestNextActionToTake == null) return;
        var action = ai.BestNextActionToTake;
        switch (action.Type)
        {
            case AIActionType.SendWorkersToEmptyNode:
                if (action.SourceNode != null && action.DestNode != null)
                    DrawArrow(action.SourceNode.RealNode.WorldLoc, action.DestNode.RealNode.WorldLoc, player.Color, "Send " + action.Count);
                break;

            case AIActionType.DoNothing:
                break;

            case AIActionType.ConstructBuildingInEmptyNode:
                if (action.SourceNode != null && action.DestNode != null)
                    DrawArrow(action.SourceNode.RealNode.WorldLoc, action.DestNode.RealNode.WorldLoc, Color.yellow, "Send " + action.Count + " to construct\n" + action.BuildingToConstruct.Id);
                else
                    Debug.Log("Unknown asdfasdfasdfadf type: " + action.Type);
                break;

            case AIActionType.AttackFromNode:
                if (action.SourceNode != null && action.DestNode != null)
                    DrawArrow(action.SourceNode.RealNode.WorldLoc, action.DestNode.RealNode.WorldLoc, Color.red, "Attack, send " + action.Count);
                break;

            case AIActionType.SendWorkersToOwnedNode:
                if (action.SourceNode != null && action.DestNode != null)
                    DrawArrow(action.SourceNode.RealNode.WorldLoc, action.DestNode.RealNode.WorldLoc, player.Color, "Support " + action.Count);
                break;

            default:
                Debug.Log("Unknown action type: " + action.Type);
                break;
        }

        // if (action.SourceNode != null && action.DestNode != null)
        // DrawArrow(action.SourceNode.RealNode.WorldLoc, action.DestNode.RealNode.WorldLoc, player.Color,"Test");
    }

    void Update()
    {
        Town.Update();

        foreach (var player in Town.Players)
            DrawNextAISteps(player);
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