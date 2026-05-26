using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// Lightweight runtime UGUI popup menu that lists a vertical stack of buttons at the
/// cursor and fires exactly one callback (the chosen option's OnSelected, or the
/// dialog's OnDismissed when the player clicks outside or presses escape).
///
/// Shared by both human-player flows that need a "pick from N options" dialog:
///   - building selection on empty neutral targets (item per BuildingDefn)
///   - node-action selection on click of an owned node (Upgrade, etc.)
///
/// One instance per scene. ShowAt() rebuilds the button list every open so callers can
/// recompute enablement (affordability, eligibility) without holding state across opens.
public class ContextMenuDialog : MonoBehaviour
{
    public class Option
    {
        public string Label;
        public bool Enabled = true;
        public Action OnSelected;
    }

    public bool IsOpen => _canvasGO != null && _canvasGO.activeSelf;

    Action _onDismissed;
    GameObject _canvasGO;
    GameObject _panelGO;
    GameObject _backdropGO;
    readonly List<Button> _buttonPool = new();
    bool _resolved;

    const int UiScale = 2;
    const int ButtonHeight = 28 * UiScale;
    const int ButtonSpacing = 4 * UiScale;
    const int PanelPaddingX = 10 * UiScale;
    const int PanelPaddingY = 8 * UiScale;
    const int PanelWidth = 240 * UiScale;
    const int LabelFontSize = 13 * UiScale;
    const int LabelPaddingX = 8 * UiScale;

    void Awake()
    {
        BuildCanvas();
        Hide();
    }

    /// Open the menu at `screenPos` (Input.mousePosition convention). `onDismissed`
    /// fires when the user clicks the backdrop / presses escape WITHOUT selecting an
    /// option; it does NOT fire when an option is chosen (the option's OnSelected fires
    /// instead).
    public void ShowAt(Vector2 screenPos, List<Option> options, Action onDismissed = null)
    {
        _onDismissed = onDismissed;
        _resolved = false;

        RebuildButtons(options);
        PositionPanel(screenPos, options.Count);

        _canvasGO.SetActive(true);
    }

    void Update()
    {
        if (!IsOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape))
            DismissAndHide();
    }

    void SelectAndHide(Option opt)
    {
        if (_resolved) return;
        _resolved = true;
        Hide();
        opt?.OnSelected?.Invoke();
    }

    void DismissAndHide()
    {
        if (_resolved) return;
        _resolved = true;
        var cb = _onDismissed;
        _onDismissed = null;
        Hide();
        cb?.Invoke();
    }

    void Hide()
    {
        if (_canvasGO != null) _canvasGO.SetActive(false);
    }

    void RebuildButtons(List<Option> options)
    {
        // Grow the pool to match the current option count; never shrink so we recycle
        // button GOs across reopens.
        while (_buttonPool.Count < options.Count)
            _buttonPool.Add(CreateButton(_panelGO.transform));

        for (int i = 0; i < _buttonPool.Count; i++)
        {
            var btn = _buttonPool[i];
            if (i >= options.Count)
            {
                btn.gameObject.SetActive(false);
                continue;
            }
            var opt = options[i];
            btn.gameObject.SetActive(true);

            var rt = btn.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(-PanelPaddingX * 2, ButtonHeight);
            rt.anchoredPosition = new Vector2(0, -(PanelPaddingY + i * (ButtonHeight + ButtonSpacing)));

            var label = btn.GetComponentInChildren<Text>();
            label.fontSize = LabelFontSize;
            label.text = opt.Label ?? "";
            label.color = opt.Enabled ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);

            btn.interactable = opt.Enabled;
            btn.onClick.RemoveAllListeners();
            var captured = opt;
            btn.onClick.AddListener(() => SelectAndHide(captured));
        }
    }

    void PositionPanel(Vector2 screenPos, int rowCount)
    {
        int rows = Mathf.Max(1, rowCount);
        float height = PanelPaddingY * 2 + rows * ButtonHeight + (rows - 1) * ButtonSpacing;

        var rt = _panelGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(PanelWidth, height);

        // ScreenSpaceOverlay canvas: pixel-perfect mapping. Panel pivot is (0.5, 0.5) and
        // its anchors sit at canvas center, so anchoredPosition is just the offset of the
        // panel center from the screen center. Place the panel below-right of the cursor
        // (top-left corner near the cursor) then clamp so it stays on-screen.
        float halfW = PanelWidth * 0.5f;
        float halfH = height * 0.5f;
        float targetX = screenPos.x + halfW;
        float targetY = screenPos.y - halfH;

        targetX = Mathf.Clamp(targetX, halfW, Screen.width - halfW);
        targetY = Mathf.Clamp(targetY, halfH, Screen.height - halfH);

        rt.anchoredPosition = new Vector2(targetX - Screen.width * 0.5f, targetY - Screen.height * 0.5f);
    }

    void BuildCanvas()
    {
        _canvasGO = new GameObject("ContextMenuDialog", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        _canvasGO.transform.SetParent(transform, false);
        var canvas = _canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10000;
        _canvasGO.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        // Full-screen transparent backdrop: clicking it dismisses the modal. Sits on the
        // same canvas (below the panel in sibling order) so any click outside the panel
        // bubbles into it.
        _backdropGO = new GameObject("Backdrop", typeof(RectTransform), typeof(Image), typeof(Button));
        _backdropGO.transform.SetParent(_canvasGO.transform, false);
        var backdropRT = _backdropGO.GetComponent<RectTransform>();
        backdropRT.anchorMin = Vector2.zero;
        backdropRT.anchorMax = Vector2.one;
        backdropRT.offsetMin = Vector2.zero;
        backdropRT.offsetMax = Vector2.zero;
        var backdropImg = _backdropGO.GetComponent<Image>();
        backdropImg.color = new Color(0f, 0f, 0f, 0.001f); // nearly invisible but still raycast-able
        var backdropBtn = _backdropGO.GetComponent<Button>();
        backdropBtn.transition = Selectable.Transition.None;
        backdropBtn.onClick.AddListener(DismissAndHide);

        _panelGO = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        _panelGO.transform.SetParent(_canvasGO.transform, false);
        var panelRT = _panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot = new Vector2(0.5f, 0.5f);
        var panelImg = _panelGO.GetComponent<Image>();
        panelImg.color = new Color(0.08f, 0.08f, 0.08f, 0.95f);
    }

    Button CreateButton(Transform parent)
    {
        var go = new GameObject("MenuOption", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.sizeDelta = new Vector2(-PanelPaddingX * 2, ButtonHeight);

        var img = go.GetComponent<Image>();
        img.color = new Color(0.18f, 0.18f, 0.22f, 1f);
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.normalColor = new Color(0.18f, 0.18f, 0.22f, 1f);
        colors.highlightedColor = new Color(0.30f, 0.30f, 0.40f, 1f);
        colors.pressedColor = new Color(0.10f, 0.10f, 0.15f, 1f);
        colors.disabledColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        btn.colors = colors;

        var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelGO.transform.SetParent(go.transform, false);
        var labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = new Vector2(LabelPaddingX, 0);
        labelRT.offsetMax = new Vector2(-LabelPaddingX, 0);
        var text = labelGO.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = LabelFontSize;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft;

        return btn;
    }
}
