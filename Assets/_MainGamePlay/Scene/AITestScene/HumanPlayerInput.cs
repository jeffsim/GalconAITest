using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// Drives the human player's mouse-driven input on the map.
///
/// Two interaction kinds, both initiated with LMB on an owned node:
///
///   DRAG -- press LMB on an owned node, move to a different node, release LMB:
///     - while held: yellow line from source center to cursor's ground hit, and the
///       destination tile recolors yellow when it's a legal drop (path exists with every
///       intermediate node owned by the human; dest may be owned/enemy/neutral).
///     - on release at a valid dest:
///         owned (self/enemy) or gatherable neutral -> dispatch half the source's workers
///         empty neutral                            -> open building-picker menu at cursor
///     - on release at an invalid dest: no-op.
///
///   CLICK -- press and release LMB on the same owned node without dragging onto another:
///     - open a node-actions menu at the cursor (Upgrade for now; future actions later).
///
/// Multi-origin selection is planned. WorkerDragSession.SourceNodes is a list rather than
/// a scalar so the future "ctrl-click to add another source" path doesn't need to reshape
/// this class; today every dispatch path treats SourceNodes[0] as the lone origin.
public class HumanPlayerInput : MonoBehaviour
{
    public ContextMenuDialog Menu;

    static readonly Plane GroundPlane = new Plane(Vector3.up, Vector3.zero);
    const float DragLineY = 0.05f;
    const float DragLineWidth = 0.35f;
    static readonly Color DragLineColor = new Color(1f, 0.92f, 0.20f, 1f);

    public class WorkerDragSession
    {
        public readonly List<NodeData> SourceNodes = new();
        public NodeData HoveredDest;
        public bool HoveredDestValid;
    }

    WorkerDragSession _session;
    LineRenderer _dragLine;
    NodeGO _highlightedDest;
    Camera _camera;

    // Suppress new drag/click starts while either menu is open so the player can dismiss
    // or select before initiating another action.
    NodeData _pendingPickerSource;
    NodeData _pendingPickerDest;

    void Awake()
    {
        _camera = Camera.main;
        if (Menu == null)
            Menu = GetComponentInChildren<ContextMenuDialog>();
        EnsureDragLine();
    }

    void EnsureDragLine()
    {
        if (_dragLine != null) return;
        var go = new GameObject("HumanDragLine");
        go.transform.SetParent(transform, false);
        _dragLine = go.AddComponent<LineRenderer>();
        _dragLine.material = new Material(Shader.Find("Sprites/Default"));
        _dragLine.startColor = DragLineColor;
        _dragLine.endColor = DragLineColor;
        _dragLine.widthMultiplier = DragLineWidth;
        _dragLine.alignment = LineAlignment.View;
        _dragLine.positionCount = 0;
        _dragLine.useWorldSpace = true;
    }

    void Update()
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera == null) return;

        var town = AITestScene.Instance?.Town;
        if (town == null) return;
        var human = town.GetHumanPlayer();
        if (human == null) return;

        // Block all input while a menu is up. Cancel any in-flight drag so a dangling
        // yellow line / highlight don't sit behind the modal.
        if (Menu != null && Menu.IsOpen)
        {
            CancelDrag();
            return;
        }

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        if (_session == null)
        {
            if (!overUI && Input.GetMouseButtonDown(0))
                TryBeginDrag(human);
            return;
        }

        if (Input.GetMouseButtonUp(0))
        {
            CompleteDrag(town, human);
            return;
        }

        if (Input.GetMouseButton(0))
            UpdateDrag(town, human);
    }

    void TryBeginDrag(PlayerData human)
    {
        if (!TryRaycastNode(out var nodeGO)) return;
        var node = nodeGO.Data;
        if (node == null || node.OwnedBy != human) return;

        _session = new WorkerDragSession();
        _session.SourceNodes.Add(node);
        // Treat the source as the initially-hovered node so a pure click (mouseup without
        // moving the cursor away) routes through the "click on source" branch in
        // CompleteDrag rather than the no-dest fallback.
        _session.HoveredDest = node;
        _session.HoveredDestValid = false;

        EnsureDragLine();
        _dragLine.positionCount = 2;
        var p0 = node.WorldLoc; p0.y = DragLineY;
        _dragLine.SetPosition(0, p0);
        _dragLine.SetPosition(1, p0);
    }

    void UpdateDrag(TownData town, PlayerData human)
    {
        var source = _session.SourceNodes[0];

        Vector3 lineEnd = source.WorldLoc;
        lineEnd.y = DragLineY;
        if (TryGroundHit(Input.mousePosition, out var ground))
        {
            ground.y = DragLineY;
            lineEnd = ground;
        }
        var lineStart = source.WorldLoc; lineStart.y = DragLineY;
        _dragLine.SetPosition(0, lineStart);
        _dragLine.SetPosition(1, lineEnd);

        // Resolve hovered destination + validity. The source itself never counts as a
        // valid dest -- you can't dispatch workers to yourself -- but we still record it
        // as Hovered so a click that never leaves the source routes to the click handler.
        NodeData hovered = null;
        NodeGO hoveredGO = null;
        if (TryRaycastNode(out var nodeGO))
        {
            hoveredGO = nodeGO;
            hovered = nodeGO.Data;
        }

        bool valid = hovered != null
                  && hovered != source
                  && town.HasValidOwnedPath(human, source, hovered);

        if (hovered != _session.HoveredDest)
        {
            ClearHoverHighlight();
            _session.HoveredDest = hovered;
            _session.HoveredDestValid = valid;
            if (valid && hoveredGO != null)
            {
                hoveredGO.SetDragHighlight(true);
                _highlightedDest = hoveredGO;
            }
        }
        else
        {
            // Same node still hovered; refresh validity in case ownership changed mid-drag.
            _session.HoveredDestValid = valid;
            if (valid && hoveredGO != null && _highlightedDest == null)
            {
                hoveredGO.SetDragHighlight(true);
                _highlightedDest = hoveredGO;
            }
            else if (!valid && _highlightedDest != null)
            {
                _highlightedDest.SetDragHighlight(false);
                _highlightedDest = null;
            }
        }
    }

    void CompleteDrag(TownData town, PlayerData human)
    {
        var source = _session.SourceNodes[0];
        var dest = _session.HoveredDest;
        bool valid = _session.HoveredDestValid;

        ClearDragVisuals();
        _session = null;

        // CLICK on source: mouseup over the same owned node we started on. Open the
        // node-actions menu instead of dispatching workers. (Source-as-dest never passes
        // validity, so we can detect this by checking dest == source explicitly.)
        if (dest == source)
        {
            OpenNodeActionsMenu(town, human, source);
            return;
        }

        if (!valid || dest == null) return;

        // Empty neutral with no building -> open the building picker; defer dispatch
        // until the player chooses. EXCEPT: if I've already committed a build intent
        // for this node (workers from a previous drag are still in flight), reuse that
        // stored intent and dispatch the next wave without re-prompting. The stored
        // intent is cleared automatically by CleanupResolvedCaptureIntents when either
        // (a) the node gets captured by someone (built or otherwise) or (b) every
        // in-flight worker carrying that intent dies before arriving.
        if (dest.OwnedBy == null && dest.Building == null)
        {
            if (dest.PendingCaptureBy == human && dest.PendingConstructBuilding != null)
            {
                town.HumanConstructBuilding(human, source, dest, dest.PendingConstructBuilding);
                return;
            }
            OpenBuildingPicker(town, human, source, dest);
            return;
        }

        town.HumanSendHalfWorkers(human, source, dest);
    }

    void OpenNodeActionsMenu(TownData town, PlayerData human, NodeData node)
    {
        if (Menu == null) return;

        var options = new List<ContextMenuDialog.Option>();
        bool canUpgrade = town.CanHumanUpgrade(human, node);
        string upgradeLabel = node.Building != null
            ? $"Upgrade {node.Building.Defn.Name} (L{node.Building.Level} -> L{node.Building.Level + 1})"
            : "Upgrade";
        if (node.Building != null && node.Building.Defn != null && !node.Building.Defn.CanBeUpgraded)
            upgradeLabel += " (not upgradeable)";
        else if (!canUpgrade && node.NumWorkers < 2)
            upgradeLabel += " (need >=2 workers)";

        options.Add(new ContextMenuDialog.Option
        {
            Label = upgradeLabel,
            Enabled = canUpgrade,
            OnSelected = () =>
            {
                var t = AITestScene.Instance?.Town;
                var h = t?.GetHumanPlayer();
                if (t != null && h != null) t.HumanUpgradeBuilding(h, node);
            },
        });

        Menu.ShowAt(Input.mousePosition, options);
    }

    void OpenBuildingPicker(TownData town, PlayerData human, NodeData source, NodeData dest)
    {
        if (Menu == null) return;
        _pendingPickerSource = source;
        _pendingPickerDest = dest;

        var defns = GetPlayerBuildableDefns();
        var options = new List<ContextMenuDialog.Option>(defns.Count);
        foreach (var defn in defns)
        {
            bool affordable = PlayerEconomy.CanAfford(human, town, defn);
            var captured = defn;
            options.Add(new ContextMenuDialog.Option
            {
                Label = FormatBuildingOption(defn, affordable),
                Enabled = affordable,
                OnSelected = () => OnBuildingPicked(captured),
            });
        }

        Menu.ShowAt(Input.mousePosition, options, OnBuildingPickerDismissed);
    }

    void OnBuildingPicked(BuildingDefn picked)
    {
        var src = _pendingPickerSource;
        var dst = _pendingPickerDest;
        _pendingPickerSource = null;
        _pendingPickerDest = null;
        if (picked == null) return;

        var town = AITestScene.Instance?.Town;
        var human = town?.GetHumanPlayer();
        if (town == null || human == null) return;

        town.HumanConstructBuilding(human, src, dst, picked);
    }

    void OnBuildingPickerDismissed()
    {
        _pendingPickerSource = null;
        _pendingPickerDest = null;
    }

    static List<BuildingDefn> GetPlayerBuildableDefns()
    {
        var result = new List<BuildingDefn>();
        if (GameDefns.Instance == null) return result;
        foreach (var settings in GameDefns.Instance.GameSettingsDefns.Values)
        {
            if (settings != null && settings.PlayerBuildableBuildings.Count > 0)
            {
                result.AddRange(settings.PlayerBuildableBuildings);
                break;
            }
        }
        // Fallback: if the GameSettings list is empty for any reason, iterate BuildingDefns
        // directly so the picker is still useful instead of silently blank.
        if (result.Count == 0)
            foreach (var bd in GameDefns.Instance.BuildingDefns.Values)
                if (bd.CanBeBuiltByPlayer)
                    result.Add(bd);
        return result;
    }

    static string FormatBuildingOption(BuildingDefn defn, bool affordable)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(defn.Name);
        if (defn.ConstructionRequirements != null && defn.ConstructionRequirements.Count > 0)
        {
            sb.Append("  (");
            bool first = true;
            foreach (var req in defn.ConstructionRequirements)
            {
                if (!first) sb.Append(", ");
                sb.Append(req.Amount).Append(' ').Append(req.Good.GoodType);
                first = false;
            }
            sb.Append(')');
        }
        if (!affordable) sb.Append("  -- need more");
        return sb.ToString();
    }

    void CancelDrag()
    {
        if (_session == null) return;
        ClearDragVisuals();
        _session = null;
    }

    void ClearDragVisuals()
    {
        ClearHoverHighlight();
        if (_dragLine != null) _dragLine.positionCount = 0;
    }

    void ClearHoverHighlight()
    {
        if (_highlightedDest != null)
        {
            _highlightedDest.SetDragHighlight(false);
            _highlightedDest = null;
        }
    }

    bool TryRaycastNode(out NodeGO node)
    {
        node = null;
        var ray = _camera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out var hit, 5000f)) return false;
        node = hit.collider.GetComponentInParent<NodeGO>();
        return node != null;
    }

    static bool TryGroundHit(Vector3 screenPos, out Vector3 worldPoint)
    {
        var cam = Camera.main;
        if (cam == null) { worldPoint = default; return false; }
        var ray = cam.ScreenPointToRay(screenPos);
        if (GroundPlane.Raycast(ray, out float enter))
        {
            worldPoint = ray.GetPoint(enter);
            return true;
        }
        worldPoint = default;
        return false;
    }
}
