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
    public bool ShowFullActionPath = true;
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

        draw.Label2D(pos, label, 20, Drawing.LabelAlignment.Center, Color.black);
        draw.Label2D(pos + new Vector3(-.02f, 0.02f, .05f), label, 20, Drawing.LabelAlignment.Center, Color.white);
    }

    void DrawCircle(Vector3 pos, float radius, Color color, string label)
    {
        var draw = Drawing.Draw.ingame;
        draw.PushLineWidth(6);
        draw.PushColor(color);
        draw.Circle(pos, Vector3.up, radius);
        draw.PopColor();
        draw.PopLineWidth();

        draw.Label2D(pos, label, 20, Drawing.LabelAlignment.Center, Color.black);
        draw.Label2D(pos + new Vector3(-.02f, 0.02f, .05f), label, 20, Drawing.LabelAlignment.Center, Color.white);
    }

    private void DrawNextAISteps(PlayerData player)
    {
        if (player == null || player.AI == null || player.AI.BestNextActionToTake == null) return;
        var action = AIDebugger.rootEntry;

        if (action.BestNextAction == null) return;
        var color = player.Color;

        if (!ShowFullActionPath)
            drawActionArrow(0, action.BestNextAction, player, color);
        else
        {
            int i = 1;
            while (action != null)
            {
                drawActionArrow(i, action, player, i <= 2 ? Color.red : Color.gray);
                i++;
                action = action.BestNextAction;
            }
        }
    }

    private void drawActionArrow(int actionIndex, AIDebuggerEntryData action, PlayerData player, Color color)
    {
        switch (action.ActionType)
        {
            case AIActionType.DoNothing: break;
            case AIActionType.RootAction: break;

            case AIActionType.ConstructBuildingInEmptyNode:
                if (action.FromNode != null && action.ToNode != null)
                    DrawArrow(action.FromNode.RealNode.WorldLoc, action.ToNode.RealNode.WorldLoc, color, actionIndex + ". Send " + action.NumSent + ", build\n" + action.BuildingDefn.Id);
                break;

            case AIActionType.AttackFromNode:
                if (action.FromNode != null && action.ToNode != null)
                    DrawArrow(action.FromNode.RealNode.WorldLoc, action.ToNode.RealNode.WorldLoc, color, actionIndex + ". Attack " + action.NumSent);
                break;

            case AIActionType.SendWorkersToOwnedNode:
                if (action.FromNode != null && action.ToNode != null)
                    DrawArrow(action.FromNode.RealNode.WorldLoc, action.ToNode.RealNode.WorldLoc, color, actionIndex + ". Support " + action.NumSent);
                break;

            case AIActionType.UpgradeBuilding:
                if (action.FromNode != null)
                    DrawCircle(action.FromNode.RealNode.WorldLoc, 1, color, actionIndex + ". Upgrade");
                break;
                
            default:
                Debug.Log("Unknown action type: " + action.ActionType);
                break;
        }
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