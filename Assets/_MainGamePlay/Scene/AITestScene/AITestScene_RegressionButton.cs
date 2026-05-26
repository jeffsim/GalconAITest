using System.Text;
using UnityEngine;
using UnityEngine.UI;

public partial class AITestScene
{
    Button regressionTestsButton;

    /// <summary>
    /// Trigger the headless AI regression suite. Runs every test inside the current frame,
    /// logs a pass/fail summary to console, and copies the full report to the clipboard.
    /// </summary>
    public void OnRunRegressionTestsClicked()
    {
        var results = AIRegressionTests.RunAll();

        // Tally first so we can put the count in the banner. The runner returns a final
        // synthetic "=== completed in Xms ===" record; skip those when counting.
        int passed = 0, failed = 0;
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            if (r.Name.StartsWith("==")) continue;
            if (r.Passed) passed++; else failed++;
        }

        var sb = new StringBuilder(2048);
        sb.AppendLine($"=== AI REGRESSION TESTS ({passed} passed, {failed} failed) ===");
        for (int i = 0; i < results.Count; i++)
            sb.AppendLine(results[i].ToString());
        sb.AppendLine();
        sb.AppendLine($"Summary: {passed} passed, {failed} failed.");

        string report = sb.ToString();
        Debug.Log(report);
        GUIUtility.systemCopyBuffer = report;

        if (regressionTestsButton != null && regressionTestsButton.targetGraphic is Image img)
            img.color = failed == 0 ? new Color(0.2f, 0.6f, 0.2f, 1f) : new Color(0.7f, 0.2f, 0.2f, 1f);
    }

    /// Build a "Run AI Tests" button as a child of the realtime panel (so it's visible from
    /// anywhere in the AITestScene). Called once at first OnEnable of the runtime panel.
    void EnsureRegressionTestsButton(GameObject panel)
    {
        if (regressionTestsButton != null) return;

        var goRect = CreateUIRect(panel.transform, "RunRegressionTestsButton",
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1));
        var rt = goRect.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(300, 26);
        rt.anchoredPosition = new Vector2(10, -130);

        var img = goRect.AddComponent<Image>();
        img.color = new Color(0.25f, 0.35f, 0.55f, 1f);

        regressionTestsButton = goRect.AddComponent<Button>();
        regressionTestsButton.targetGraphic = img;
        regressionTestsButton.onClick.AddListener(OnRunRegressionTestsClicked);

        var label = CreateLabel(goRect.transform, "Label", "Run AI Regression Tests",
            new Vector2(0, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(0, 22));
        var labelRT = label.GetComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0, 0);
        labelRT.anchorMax = new Vector2(1, 1);
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;
        label.alignment = TextAnchor.MiddleCenter;
        label.fontSize = 14;
    }
}
