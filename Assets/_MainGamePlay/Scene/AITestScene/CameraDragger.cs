using UnityEngine;

public class CameraDragger : MonoBehaviour
{
    private Vector3 lastMousePosition;
    private bool isDragging = false;

    // You can adjust these values to control zoom sensitivity and limits
    public float zoomSensitivity = 10.0f;
    public float minZoomDistance = 5.0f;
    public float maxZoomDistance = 50.0f;

    void Start()
    {
        // Set the camera position from the active TownDefn instead of GameSettings
        var townDefn = AITestScene.Instance != null ? AITestScene.Instance.TestTownDefn : null;
        if (townDefn != null)
            transform.position = townDefn.Debug_StartingCameraPosition;
        
        // The value is set using the CameraDraggerEditor class and stored in the TownDefn ScriptableObject
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
        {
            Vector3 direction = transform.forward;
            float zoomAmount = scroll * zoomSensitivity;
            Vector3 newPosition = transform.position + direction * zoomAmount;

            // Optional: Clamp the zoom to prevent the camera from going too far or too close
            float distance = Vector3.Distance(newPosition, transform.position);
            // Debug.Log(distance);
            //    if (distance >= minZoomDistance && distance <= maxZoomDistance)
            {
                transform.position = newPosition;
            }
        }
    }
}
