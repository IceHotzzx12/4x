using UnityEngine;

/// <summary>
/// Rise-of-Kingdoms style top-down camera: WASD + screen-edge pan + scroll zoom.
/// Zoom is clamped so the camera never sinks under the terrain surface.
/// </summary>
public class RTSCamera : MonoBehaviour
{
    [Header("Pan")]
    public float panSpeed = 80f;
    public float edgeScrollSpeed = 80f;
    public float edgeThickness = 18f;
    public bool enableEdgeScroll = true;

    [Header("Zoom")]
    public float zoomSpeed = 120f;
    public float minHeightAboveTerrain = 25f;
    public float maxHeightAboveTerrain = 900f;

    [Header("Terrain clamp")]
    public Terrain terrain;
    public float terrainSamplePadding = 2f;

    Camera _cam;

    void Awake()
    {
        _cam = GetComponent<Camera>();
        if (terrain == null)
            terrain = Terrain.activeTerrain;
    }

    void LateUpdate()
    {
        if (terrain == null)
            terrain = Terrain.activeTerrain;

        PanFromKeyboard();
        if (enableEdgeScroll)
            PanFromScreenEdge();
        ZoomFromScroll();
        ClampAboveTerrain();
    }

    void PanFromKeyboard()
    {
        var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        if (input.sqrMagnitude < 0.001f) return;

        // Pan on XZ relative to camera yaw (keep strategy map orientation).
        var yaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        var move = yaw * input.normalized;
        transform.position += move * panSpeed * Time.deltaTime;
    }

    void PanFromScreenEdge()
    {
        // Don't pan until the Game view has focus (avoids flying off on Play click).
        if (!Application.isFocused) return;

        var mouse = Input.mousePosition;
        // Ignore when cursor is outside the game view.
        if (mouse.x < 0 || mouse.y < 0 || mouse.x > Screen.width || mouse.y > Screen.height)
            return;

        var dir = Vector3.zero;
        if (mouse.x <= edgeThickness) dir.x -= 1f;
        if (mouse.x >= Screen.width - edgeThickness) dir.x += 1f;
        if (mouse.y <= edgeThickness) dir.z -= 1f;
        if (mouse.y >= Screen.height - edgeThickness) dir.z += 1f;
        if (dir.sqrMagnitude < 0.001f) return;

        var yaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        transform.position += yaw * dir.normalized * edgeScrollSpeed * Time.deltaTime;
    }

    void ZoomFromScroll()
    {
        var scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) < 0.01f) return;

        // Move along view forward for natural zoom; clamp happens after.
        transform.position += transform.forward * scroll * zoomSpeed * Time.deltaTime;
    }

    void ClampAboveTerrain()
    {
        var pos = transform.position;
        float groundY = 0f;

        if (terrain != null)
        {
            var td = terrain.terrainData;
            var local = pos - terrain.transform.position;
            var nx = Mathf.Clamp01(local.x / td.size.x);
            var nz = Mathf.Clamp01(local.z / td.size.z);
            groundY = terrain.SampleHeight(pos) + terrain.transform.position.y;
        }
        else if (Physics.Raycast(pos + Vector3.up * 5000f, Vector3.down, out var hit, 20000f))
        {
            groundY = hit.point.y;
        }

        var minY = groundY + minHeightAboveTerrain + terrainSamplePadding;
        var maxY = groundY + maxHeightAboveTerrain;
        pos.y = Mathf.Clamp(pos.y, minY, maxY);
        transform.position = pos;
    }
}
