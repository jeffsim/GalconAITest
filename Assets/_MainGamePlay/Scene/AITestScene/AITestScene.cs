using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public partial class AITestScene : MonoBehaviour
{
    [NonSerialized] public TownData Town;
    [SerializeReference] public TownDefn TestTownDefn;

    [FoldoutGroup("Players", false)] public PlayerAIDefn Player1AIDefn;
    [FoldoutGroup("Players", false)] public PlayerAIDefn Player2AIDefn;
    [FoldoutGroup("Players", false)] public PlayerAIDefn Player3AIDefn;

    [FoldoutGroup("Nodes", false)] public NodeGO NodePrefab;
    [HideInInspector][FoldoutGroup("Nodes", false)] public List<NodeGO> Nodes = new();
    [FoldoutGroup("Nodes", false)] public Material NodeConnectionMat;
    [FoldoutGroup("Nodes", false)] public GameObject NodesFolder;

    [FoldoutGroup("Workers", false)] public Worker WorkerPrefab;
    [HideInInspector][FoldoutGroup("Workers", false)] public List<Worker> Workers = new();
    [FoldoutGroup("Workers", false)] public GameObject WorkersFolder;
    [FoldoutGroup("Workers", false)] public WorkerDefn TestWorkerDefn;

    [Header("Debug Overlay")]
    [Tooltip("When true, draws yellow rings on structural chokepoint nodes.")]
    public bool ShowChokepointOverlay = true;

    [Header("Realtime")]
    [Range(0f, 8f)]
    [Tooltip("Multiplier on real time. 0 pauses the simulation, 8 runs it 8x faster than real time.")]
    public float GameSpeed = 1f;

    // Lookup so AdvanceRealtime can pair WorkerData (data) with its rendered Worker (GO) and
    // remove the GO when the data leaves WorkersInFlight.
    Dictionary<WorkerData, Worker> workerGoByData = new Dictionary<WorkerData, Worker>();

    /// Player whose decision record is shown on the on-screen overlay and copied first by the
    /// AI debug dump button. The new AI has no recursive tree to "view details on"; this is
    /// just a focus hint for the dump.
    public PlayerData DebugPlayerToViewDetailsOn;

    public static AITestScene Instance;
    public List<PathStep> pathSteps = new();

    // Refs to the runtime-built realtime control panel; persisted across ResetTown so we
    // don't rebuild the UI every reset.
    Slider gameSpeedSlider;
    Text gameSpeedLabel;
    Text statusLabel;
    float speedBeforePause = 1f;

    void OnEnable()
    {
        Instance = this;
        EnsureRealtimeControlPanel();
        EnsureHumanPlayerInput();
        ResetTown();

        // Application.targetFrameRate = 60;
    }

    HumanPlayerInput humanInput;

    void EnsureHumanPlayerInput()
    {
        if (humanInput != null) return;

        // Hosted on a dedicated child GO so its runtime-built canvas and drag line don't
        // clutter the AITestScene transform. ContextMenuDialog lives on the same GO so
        // HumanPlayerInput.Awake can wire to it via GetComponentInChildren.
        var go = new GameObject("HumanPlayerInput");
        go.transform.SetParent(transform, false);
        var menu = go.AddComponent<ContextMenuDialog>();
        humanInput = go.AddComponent<HumanPlayerInput>();
        humanInput.Menu = menu;
    }

    void ResetTown()
    {
        Town = new TownData(TestTownDefn, TestWorkerDefn, new[] { Player1AIDefn, Player2AIDefn, Player3AIDefn });

        NodesFolder.RemoveAllChildren();
        WorkersFolder.RemoveAllChildren();
        Nodes.Clear();
        Workers.Clear();
        workerGoByData.Clear();

        // Initialize each AI's first realtime decision time to "now" so the simulation kicks
        // off immediately instead of sitting idle for one DecisionInterval before the very
        // first move. Human players have no AI and are skipped by the null-conditional.
        foreach (var p in Town.Players)
            p?.AI?.ScheduleNextRealtimeDecision(Town.WorldTime);

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

        // Default the AI debug view to the first AI player (humans have no decision record).
        DebugPlayerToViewDetailsOn = null;
        foreach (var p in Town.Players)
            if (p != null && p.AI != null) { DebugPlayerToViewDetailsOn = p; break; }

        var cameraDragger = FindAnyObjectByType<CameraDragger>();
        cameraDragger?.FrameTown();
    }

    public void OnResetClicked()
    {
        ResetTown();
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

    // Half-width of a node's visible square in world units (Node.prefab base mesh is a
    // unit cube scaled 1x1 in XZ). Used to clip debug arrows so the line/arrow-head sits
    // on the edge of the source/dest squares rather than at their centers, where the
    // arrow head was previously indistinguishable from the destination node itself.
    const float NodeSquareHalfExtent = 0.5f;

    void DrawArrow(Vector3 start, Vector3 end, Color color, string label)
    {
        var draw = Drawing.Draw.ingame;
        draw.PushLineWidth(4);
        start.y += 0.1f;
        end.y += 0.1f;

        // Pull each endpoint inward to the edge of its node's XZ square so the arrow
        // visibly starts/ends at the node boundary instead of being buried in the
        // square (where the arrow head was the same size and color as the dest node).
        var clippedEnd = ClipPointToNodeEdge(start, end, NodeSquareHalfExtent);
        var clippedStart = ClipPointToNodeEdge(end, start, NodeSquareHalfExtent);

        draw.Arrow(clippedStart, clippedEnd, new Unity.Mathematics.float3(0, 1, 0), 0.4f, color);
        draw.PopLineWidth();
        var pos = (clippedStart + clippedEnd) / 2;

        draw.Label2D(pos, label, 20, Drawing.LabelAlignment.Center, Color.black);
        draw.Label2D(pos + new Vector3(-.02f, 0.02f, .05f), label, 20, Drawing.LabelAlignment.Center, Color.white);
    }

    // Given a line from lineFrom to nodeCenter, returns the point where the line first
    // crosses the axis-aligned XZ square of half-extent `halfExtent` centered on
    // nodeCenter (i.e. the edge of the destination node's visible square). Used to
    // shorten debug arrows so they terminate on the node boundary instead of its center.
    static Vector3 ClipPointToNodeEdge(Vector3 lineFrom, Vector3 nodeCenter, float halfExtent)
    {
        float absDx = Mathf.Abs(lineFrom.x - nodeCenter.x);
        float absDz = Mathf.Abs(lineFrom.z - nodeCenter.z);
        // If lineFrom is already inside the square in XZ, no clipping (degenerate).
        if (absDx <= halfExtent && absDz <= halfExtent) return nodeCenter;
        // For each axis, find the parameter t in [0,1] along (lineFrom -> nodeCenter)
        // where the line first enters that axis's slab. The line enters the box at the
        // MAX of the two per-axis entry params (both slabs must be satisfied).
        float tx = absDx > halfExtent ? 1f - halfExtent / absDx : 0f;
        float tz = absDz > halfExtent ? 1f - halfExtent / absDz : 0f;
        float tEnter = Mathf.Clamp01(Mathf.Max(tx, tz));
        return lineFrom + (nodeCenter - lineFrom) * tEnter;
    }

    // Overlay a yellow "★ score" ring around every chokepoint node. Radius scales with
    // ChokepointScore so the eye can immediately tell which chokepoint is structurally
    // most important (peak = 1.0 yields the biggest ring). Drawn every frame because
    // Drawing.Draw.ingame state is not persistent and the rest of the overlay redraws here.
    void DrawChokepointOverlay()
    {
        if (!ShowChokepointOverlay || Town == null) return;
        var draw = Drawing.Draw.ingame;
        for (int i = 0; i < Town.Nodes.Count; i++)
        {
            var node = Town.Nodes[i];
            float score = node.ChokepointScore;
            if (score <= 0.05f) continue;

            float radius = 0.55f + score * 0.7f;
            // Bright yellow at peak, dimmer for lower-scoring chokepoints so the user can
            // visually rank them at a glance.
            var color = new Color(1f, 0.85f, 0f, 0.35f + 0.55f * score);
            var pos = node.WorldLoc + new Vector3(0, 0.02f, 0);
            draw.PushLineWidth(3);
            draw.PushColor(color);
            draw.Circle(pos, Vector3.up, radius);
            draw.PopColor();
            draw.PopLineWidth();

            string label = $"\u2605 {score:F2}";
            var labelPos = pos + new Vector3(0, 0.02f, -radius - 0.25f);
            draw.Label2D(labelPos, label, 16, Drawing.LabelAlignment.Center, Color.black);
            draw.Label2D(labelPos + new Vector3(-.02f, 0.02f, .05f), label, 16, Drawing.LabelAlignment.Center, new Color(1f, 0.85f, 0f, 1f));
        }
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
        if (player == null || player.AI == null) return;
        var move = player.AI.GetActionForArrowDisplay();
        if (move == null) return;
        // Single arrow per player -- the new AI is a single-pass evaluator, so there's no
        // recursive "and then..." chain of follow-up actions to draw.
        drawActionArrow(0, move, player, player.Color);
    }

    private void drawActionArrow(int actionIndex, AIAction action, PlayerData player, Color color)
    {
        switch (action.Type)
        {
            case AIActionType.DoNothing: break;

            case AIActionType.ConstructBuildingInEmptyNode:
                if (action.SourceNode != null && action.DestNode != null)
                    DrawArrow(action.SourceNode.RealNode.WorldLoc, action.DestNode.RealNode.WorldLoc, color,
                        actionIndex + ". Send " + action.Count + ", build\n" + action.BuildingToConstruct.Id);
                break;

            case AIActionType.CaptureNeutralResourceNode:
                if (action.SourceNode != null && action.DestNode != null)
                {
                    string resourceLabel = action.DestNode.CanBeGatheredFrom
                        ? action.DestNode.ResourceGatheredFromThisNode.ToString()
                        : "resource";
                    DrawArrow(action.SourceNode.RealNode.WorldLoc, action.DestNode.RealNode.WorldLoc, color,
                        actionIndex + ". Capture " + resourceLabel + "\n" + action.Count + " workers");
                }
                break;

            case AIActionType.CaptureNeutralNode:
                if (action.AttackFromNodes != null && action.DestNode != null)
                {
                    string buildLabel = action.BuildingToConstruct != null ? action.BuildingToConstruct.Id : "?";
                    foreach (var kvp in action.AttackFromNodes)
                        DrawArrow(kvp.Key.RealNode.WorldLoc, action.DestNode.RealNode.WorldLoc, color,
                            actionIndex + ". Build " + buildLabel + "\n" + kvp.Value + " workers");
                }
                break;

            case AIActionType.AttackToNode:
                if (action.AttackFromNodes != null && action.DestNode != null)
                {
                    foreach (var kvp in action.AttackFromNodes)
                        DrawArrow(kvp.Key.RealNode.WorldLoc, action.DestNode.RealNode.WorldLoc, color,
                            actionIndex + ". Attack " + kvp.Value);
                }
                break;

            case AIActionType.SendWorkersToOwnedNode:
                if (action.SourceNode != null && action.DestNode != null)
                    DrawArrow(action.SourceNode.RealNode.WorldLoc, action.DestNode.RealNode.WorldLoc, color,
                        actionIndex + ". Support " + action.Count);
                break;

            case AIActionType.SendMultiSourceWorkersToOwnedNode:
                if (action.AttackFromNodes != null && action.DestNode != null)
                {
                    foreach (var kvp in action.AttackFromNodes)
                        DrawArrow(kvp.Key.RealNode.WorldLoc, action.DestNode.RealNode.WorldLoc, color,
                            actionIndex + ". Support " + kvp.Value);
                }
                break;

            case AIActionType.UpgradeBuilding:
                if (action.SourceNode != null)
                    DrawCircle(action.SourceNode.RealNode.WorldLoc, 1, color, actionIndex + ". Upgrade");
                break;

            default:
                Debug.Log("Unknown action type: " + action.Type);
                break;
        }
    }

    void Update()
    {
        // GameSpeed=0 pauses time entirely (still allow UI/debugger panel updates). Other
        // values scale dt; the engine cap sits inside RealtimeTick which only acts on dt > 0.
        // AI searches run inside RealtimeTick -> DriveRealtimeAI on each player's own
        // decision timer; the arrow overlay just reads whatever was last computed.
        float dt = Time.deltaTime * GameSpeed;
        if (dt > 0f)
        {
            Town.RealtimeTick(dt, GameSpeed);
            SyncInFlightWorkerGOs();
        }

        HandleRealtimeSpeedInput();
        RefreshRealtimeControlLabels();

        foreach (var player in Town.Players)
            DrawNextAISteps(player);

        DrawChokepointOverlay();
    }

    void SyncInFlightWorkerGOs()
    {
        // Source of truth: Town.WorkersInFlight. Every frame we (a) instantiate a Worker GO
        // for any new data we haven't seen, (b) drive each GO's transform and renderer state
        // directly from its WorkerData here -- NOT from a Worker.Update -- because pre-spawn
        // workers need to be invisible without deactivating the GO, and (c) destroy GOs whose
        // data has left the list (worker arrived).
        var inFlight = Town.WorkersInFlight;
        for (int i = 0; i < inFlight.Count; i++)
        {
            var data = inFlight[i];
            if (!workerGoByData.TryGetValue(data, out var go))
            {
                go = Instantiate(WorkerPrefab);
                go.transform.SetParent(WorkersFolder.transform);
                go.InitializeForData(data);
                workerGoByData[data] = go;
                Workers.Add(go);
            }

            // Position + visibility from data each frame. The MeshRenderer is toggled rather
            // than gameObject.SetActive so the GO stays alive (otherwise the prefab's
            // children/components are inert and we miss spawn/move signals).
            go.transform.position = data.WorldLoc + new Vector3(0, .2f, 0);
            if (go.MeshRenderer != null && go.MeshRenderer.enabled != data.HasSpawned)
                go.MeshRenderer.enabled = data.HasSpawned;
        }

        if (workerGoByData.Count > inFlight.Count)
        {
            var alive = new HashSet<WorkerData>(inFlight);
            var stale = new List<WorkerData>();
            foreach (var kvp in workerGoByData)
                if (!alive.Contains(kvp.Key))
                    stale.Add(kvp.Key);
            foreach (var dead in stale)
            {
                var go = workerGoByData[dead];
                workerGoByData.Remove(dead);
                Workers.Remove(go);
                if (go != null) Destroy(go.gameObject);
            }
        }
    }

    // Runtime UGUI panel parented to this AITestScene GameObject. Built once on first enable
    // and persisted thereafter. Uses a dedicated Canvas with sortingOrder=9999 so it always
    // renders ON TOP of any other Canvas already in the scene; that's what the IMGUI version
    // was failing at (Canvas UI in the scene was drawing over it). The Toggle/Slider write
    // straight back to the public fields each frame via OnValueChanged callbacks.
    void EnsureRealtimeControlPanel()
    {
        if (gameSpeedSlider != null) return;

        // Make sure there's an EventSystem so the Slider receives input. The scene may
        // already have one for the existing buttons; if so, leave it alone.
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("RealtimeEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            es.transform.SetParent(transform, false);
        }

        var canvasGO = new GameObject("RealtimeControls", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;
        canvasGO.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        // Background panel anchored to the top-right corner so it sits clear of the existing
        // bottom-left Reset button.
        var panel = CreateUIRect(canvasGO.transform, "Panel", new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1));
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.sizeDelta = new Vector2(320, 130);
        panelRect.anchoredPosition = new Vector2(-10, -10);
        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.65f);

        // Slider (GameSpeed 0..8).
        gameSpeedLabel = CreateLabel(panel.transform, "GameSpeedLabel", $"Game Speed: {GameSpeed:F2}x",
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -10), new Vector2(300, 22));

        var sliderGO = CreateUIRect(panel.transform, "GameSpeedSlider", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1));
        var sliderRect = sliderGO.GetComponent<RectTransform>();
        sliderRect.sizeDelta = new Vector2(300, 18);
        sliderRect.anchoredPosition = new Vector2(10, -36);
        gameSpeedSlider = sliderGO.AddComponent<Slider>();
        gameSpeedSlider.minValue = 0f;
        gameSpeedSlider.maxValue = 8f;

        var sliderBg = CreateUIRect(sliderGO.transform, "Background", new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f));
        var sliderBgRect = sliderBg.GetComponent<RectTransform>();
        sliderBgRect.anchorMin = new Vector2(0, 0.25f);
        sliderBgRect.anchorMax = new Vector2(1, 0.75f);
        sliderBgRect.offsetMin = Vector2.zero;
        sliderBgRect.offsetMax = Vector2.zero;
        var sliderBgImg = sliderBg.AddComponent<Image>();
        sliderBgImg.color = new Color(0.25f, 0.25f, 0.25f, 1f);

        var fillArea = CreateUIRect(sliderGO.transform, "Fill Area", new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f));
        var fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0, 0.25f);
        fillAreaRect.anchorMax = new Vector2(1, 0.75f);
        fillAreaRect.offsetMin = new Vector2(5, 0);
        fillAreaRect.offsetMax = new Vector2(-15, 0);

        var fill = CreateUIRect(fillArea.transform, "Fill", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f));
        var fillRT = fill.GetComponent<RectTransform>();
        // Default RectTransform sizeDelta is (100,100); with stretch anchors that ADDS 100px
        // to width/height past the parent. Without zeroing this the Fill renders as a giant
        // blue rectangle bleeding outside the slider track.
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;
        var fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.2f, 0.6f, 0.9f, 1f);

        var handleSlideArea = CreateUIRect(sliderGO.transform, "Handle Slide Area", new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f));
        var handleSlideRect = handleSlideArea.GetComponent<RectTransform>();
        handleSlideRect.anchorMin = new Vector2(0, 0);
        handleSlideRect.anchorMax = new Vector2(1, 1);
        handleSlideRect.offsetMin = new Vector2(10, 0);
        handleSlideRect.offsetMax = new Vector2(-10, 0);

        var handle = CreateUIRect(handleSlideArea.transform, "Handle", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        var handleRect = handle.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(20, 22);
        var handleImg = handle.AddComponent<Image>();
        handleImg.color = Color.white;

        gameSpeedSlider.fillRect = fill.GetComponent<RectTransform>();
        gameSpeedSlider.handleRect = handleRect;
        gameSpeedSlider.targetGraphic = handleImg;
        gameSpeedSlider.value = GameSpeed;
        gameSpeedSlider.onValueChanged.AddListener(v => GameSpeed = v);

        statusLabel = CreateLabel(panel.transform, "StatusLabel", "",
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -58), new Vector2(300, 18));
        statusLabel.fontSize = 12;

        EnsureRegressionTestsButton(panel);
    }

    static GameObject CreateUIRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        return go;
    }

    static Text CreateLabel(Transform parent, string name, string text, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size)
    {
        var go = CreateUIRect(parent, name, anchorMin, anchorMax, new Vector2(0, 1));
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;
        var t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 14;
        t.color = Color.white;
        t.alignment = TextAnchor.MiddleLeft;
        t.text = text;
        return t;
    }

    void HandleRealtimeSpeedInput()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (GameSpeed > 0f)
            {
                speedBeforePause = GameSpeed;
                GameSpeed = 0f;
            }
            else
                GameSpeed = speedBeforePause > 0f ? speedBeforePause : 1f;
            return;
        }

        for (int speed = 1; speed <= 6; speed++)
        {
            if (!Input.GetKeyDown(KeyCode.Alpha0 + speed) && !Input.GetKeyDown(KeyCode.Keypad0 + speed))
                continue;

            GameSpeed = speed;
            speedBeforePause = speed;
            return;
        }
    }

    void RefreshRealtimeControlLabels()
    {
        // Slider can be changed externally (inspector during play); keep widget in sync with
        // the underlying public field.
        if (gameSpeedSlider != null && !Mathf.Approximately(gameSpeedSlider.value, GameSpeed))
            gameSpeedSlider.SetValueWithoutNotify(GameSpeed);
        if (gameSpeedLabel != null)
            gameSpeedLabel.text = $"Game Speed: {GameSpeed:F2}x";
        if (statusLabel != null && Town != null)
            statusLabel.text = $"World Time: {Town.WorldTime:F1}s   In-flight: {Town.WorkersInFlight.Count}";
    }
}