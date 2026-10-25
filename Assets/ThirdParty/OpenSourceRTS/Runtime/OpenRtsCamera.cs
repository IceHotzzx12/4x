using UnityEngine;

/// <summary>
/// Open-field RTS camera adapted from MinaPecheux CameraManager (MIT).
/// WASD + edge pan + scroll zoom; pan speed scales when zoomed out (huge maps).
/// </summary>
[RequireComponent(typeof(Camera))]
public class OpenRtsCamera : MonoBehaviour
{
    public float translationSpeed = 80f;
    public float zoomSpeed = 80f;
    public float minAltitude = 30f;
    public float maxAltitude = 2800f;
    public float edgeThickness = 18f;
    public float zoomPanMultiplier = 10f;
    public float panReferenceAltitude = 400f;

    Camera _camera;
    Vector3 _forwardOnGround;

    void Awake()
    {
        _camera = GetComponent<Camera>();
        _forwardOnGround = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (_forwardOnGround.sqrMagnitude < 0.01f)
            _forwardOnGround = Vector3.forward;
    }

    void Update()
    {
        var mul = PanMultiplier();
        PanKeyboard(mul);
        PanEdge(mul);
        Zoom();
        ClampAltitude();
    }

    float PanMultiplier()
    {
        var ground = SampleGroundY(transform.position);
        var alt = Mathf.Max(0f, transform.position.y - ground);
        var t = Mathf.InverseLerp(minAltitude, panReferenceAltitude, alt);
        return Mathf.Lerp(1f, zoomPanMultiplier, Mathf.Clamp01(t));
    }

    void PanKeyboard(float mul)
    {
        var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        if (input.sqrMagnitude < 0.001f) return;
        var yaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        transform.position += yaw * input.normalized * translationSpeed * mul * Time.deltaTime;
    }

    void PanEdge(float mul)
    {
        if (!Application.isFocused) return;
        var m = Input.mousePosition;
        if (m.x < 0 || m.y < 0 || m.x > Screen.width || m.y > Screen.height) return;

        var dir = Vector3.zero;
        if (m.x <= edgeThickness) dir.x -= 1f;
        if (m.x >= Screen.width - edgeThickness) dir.x += 1f;
        if (m.y <= edgeThickness) dir.z -= 1f;
        if (m.y >= Screen.height - edgeThickness) dir.z += 1f;
        if (dir.sqrMagnitude < 0.001f) return;

        var yaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        transform.position += yaw * dir.normalized * translationSpeed * mul * Time.deltaTime;
    }

    void Zoom()
    {
        // Input Manager (Old): Mouse ScrollWheel axis is reliable; mouseScrollDelta is often 0.
        var scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 0.0001f)
            scroll = Input.mouseScrollDelta.y * 0.1f;
        if (Mathf.Abs(scroll) < 0.0001f) return;

        // Dolly along look; zoom-out also feels faster via pan multiplier altitude.
        var mul = Mathf.Lerp(1f, 2.5f, Mathf.InverseLerp(1f, zoomPanMultiplier, PanMultiplier()));
        transform.position += transform.forward * scroll * zoomSpeed * 40f * mul * Time.deltaTime;
    }

    void ClampAltitude()
    {
        var pos = transform.position;
        var ground = SampleGroundY(pos);
        pos.y = Mathf.Clamp(pos.y, ground + minAltitude, ground + maxAltitude);
        transform.position = pos;
    }

    public static float SampleGroundY(Vector3 worldPos)
    {
        var t = FindTerrainAt(worldPos);
        if (t != null)
            return t.SampleHeight(worldPos) + t.transform.position.y;

        if (Physics.Raycast(worldPos + Vector3.up * 8000f, Vector3.down, out var hit, 40000f))
            return hit.point.y;
        return 0f;
    }

    public static Terrain FindTerrainAt(Vector3 worldPos)
    {
        var terrains = Terrain.activeTerrains;
        for (var i = 0; i < terrains.Length; i++)
        {
            var t = terrains[i];
            if (t == null || t.terrainData == null) continue;
            var local = worldPos - t.transform.position;
            var size = t.terrainData.size;
            if (local.x >= 0f && local.x <= size.x && local.z >= 0f && local.z <= size.z)
                return t;
        }

        return Terrain.activeTerrain;
    }
}
