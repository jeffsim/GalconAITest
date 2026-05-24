using System.Collections.Generic;
using UnityEngine;

public class CameraDragger : MonoBehaviour
{
    private Vector3 lastMousePosition;
    private bool isDragging = false;

    // You can adjust these values to control zoom sensitivity and limits
    public float zoomSensitivity = 10.0f;
    public float minZoomDistance = 5.0f;
    public float maxZoomDistance = 50.0f;

    const float FrameViewportMargin = 0.08f;
    const float MaxFrameDistance = 5000f;
    const float ScrollWheelNotch = 0.1f;

    public void FrameTown()
    {
        var scene = AITestScene.Instance;
        if (scene?.Town == null || scene.Town.Nodes.Count == 0)
            return;

        var cam = GetComponent<Camera>();
        if (cam == null)
            return;

        var points = CollectFramePoints(scene);
        if (points.Count == 0)
            return;

        var bounds = new Bounds(points[0], Vector3.zero);
        foreach (var p in points)
            bounds.Encapsulate(p);
        var center = bounds.center;
        center.y = 0f;

        var fitPoints = new List<Vector3>(points);
        AppendBoundsCorners(fitPoints, bounds);

        var forward = transform.forward;
        if (forward.y >= -0.01f)
            return;

        float dLo = minZoomDistance;
        float dHi = MaxFrameDistance;
        for (int i = 0; i < 24; i++)
        {
            float d = (dLo + dHi) * 0.5f;
            transform.position = center - forward * d;
            if (AllPointsVisible(cam, fitPoints))
                dHi = d;
            else
                dLo = d;
        }

        transform.position = center - forward * dHi;
        ApplyScrollZoom(-ScrollWheelNotch);
    }

    static void AppendBoundsCorners(List<Vector3> fitPoints, Bounds bounds)
    {
        var c = bounds.center;
        c.y = 0f;
        var ex = bounds.extents;
        fitPoints.Add(c + new Vector3(ex.x, 0f, ex.z));
        fitPoints.Add(c + new Vector3(ex.x, 0f, -ex.z));
        fitPoints.Add(c + new Vector3(-ex.x, 0f, ex.z));
        fitPoints.Add(c + new Vector3(-ex.x, 0f, -ex.z));
    }

    void ApplyScrollZoom(float scrollDelta)
    {
        transform.position += transform.forward * scrollDelta * zoomSensitivity;
    }

    static List<Vector3> CollectFramePoints(AITestScene scene)
    {
        var points = new List<Vector3>();
        var town = scene.Town;
        if (town == null || town.Nodes.Count == 0)
            return points;

        var nodeById = new Dictionary<int, NodeData>();
        foreach (var node in town.Nodes)
            nodeById[node.NodeId] = node;

        var connectedIds = new HashSet<int>();
        var defn = scene.TestTownDefn;
        if (defn?.NodeConnections != null)
        {
            foreach (var conn in defn.NodeConnections)
            {
                connectedIds.Add(conn.Nodes.x);
                connectedIds.Add(conn.Nodes.y);
            }
        }

        if (connectedIds.Count > 0)
        {
            foreach (var id in connectedIds)
            {
                if (nodeById.TryGetValue(id, out var node))
                    points.Add(node.WorldLoc);
            }
        }

        if (points.Count == 0)
        {
            foreach (var node in town.Nodes)
                points.Add(node.WorldLoc);
            TrimOutliers(points);
        }

        return points;
    }

    static void TrimOutliers(List<Vector3> points)
    {
        if (points.Count < 3)
            return;

        var center = Vector3.zero;
        foreach (var p in points)
            center += p;
        center /= points.Count;

        var distances = new float[points.Count];
        for (int i = 0; i < points.Count; i++)
            distances[i] = Vector3.Distance(points[i], center);
        System.Array.Sort(distances);

        float median = distances[distances.Length / 2];
        float maxDist = Mathf.Max(median * 3f, 50f);

        for (int i = points.Count - 1; i >= 0; i--)
        {
            if (Vector3.Distance(points[i], center) > maxDist)
                points.RemoveAt(i);
        }
    }

    bool AllPointsVisible(Camera cam, List<Vector3> points)
    {
        float min = FrameViewportMargin;
        float max = 1f - FrameViewportMargin;
        foreach (var p in points)
        {
            var vp = cam.WorldToViewportPoint(p);
            if (vp.z <= 0f || vp.x < min || vp.x > max || vp.y < min || vp.y > max)
                return false;
        }

        return true;
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(1))
        {
            lastMousePosition = Input.mousePosition;
            isDragging = true;
        }
        else if (Input.GetMouseButtonUp(1))
        {
            isDragging = false;
        }

        if (isDragging && Input.GetMouseButton(1))
        {
            Vector3 delta = Input.mousePosition - lastMousePosition;
            lastMousePosition = Input.mousePosition;

            float scaleFactor = 0.01f;
            delta *= scaleFactor;

            Vector3 forward = transform.forward;
            forward.y = 0;
            Vector3 right = transform.right;

            Vector3 movement = right * delta.x + forward * delta.y;
            transform.Translate(-movement, Space.World);
        }

        // Zoom functionality
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0)
            ApplyScrollZoom(scroll);
    }
}
