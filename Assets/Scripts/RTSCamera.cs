using UnityEngine;

/// <summary>
/// LEGACY — superseded by OpenRtsCamera. Spawn Test strips this component.
/// </summary>
public class RTSCamera : MonoBehaviour
{
    [Header("Pan")]
    public float panSpeed = 80f;
    public float edgeScrollSpeed = 80f;
    public float edgeThickness = 18f;
    public bool enableEdgeScroll = true;

    [Header("Zoom")]
    public float zoomSpeed = 160f;
    public float minHeightAboveTerrain = 25f;
    public float maxHeightAboveTerrain = 2800f;

    [Header("Zoom-scaled pan (huge maps)")]
    [Tooltip("At min height, multiplier = 1. At/above reference height, multiplier reaches maxPanSpeedMultiplier.")]
    public float panSpeedReferenceHeight = 400f;
    public float maxPanSpeedMultiplier = 10f;

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

    float CurrentPanMultiplier()
    {
        var ground = SampleGroundY(transform.position);
        var height = Mathf.Max(0f, transform.position.y - ground);
        var t = Mathf.InverseLerp(minHeightAboveTerrain, panSpeedReferenceHeight, height);
        return Mathf.Lerp(1f, maxPanSpeedMultiplier, Mathf.Clamp01(t));
    }

    void PanFromKeyboard()
    {
        var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        if (input.sqrMagnitude < 0.001f) return;

        var yaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        var move = yaw * input.normalized;
        transform.position += move * panSpeed * CurrentPanMultiplier() * Time.deltaTime;
    }

    void PanFromScreenEdge()
    {
        if (!Application.isFocused) return;

        var mouse = Input.mousePosition;
        if (mouse.x < 0 || mouse.y < 0 || mouse.x > Screen.width || mouse.y > Screen.height)
            return;

        var dir = Vector3.zero;
        if (mouse.x <= edgeThickness) dir.x -= 1f;
        if (mouse.x >= Screen.width - edgeThickness) dir.x += 1f;
        if (mouse.y <= edgeThickness) dir.z -= 1f;
        if (mouse.y >= Screen.height - edgeThickness) dir.z += 1f;
        if (dir.sqrMagnitude < 0.001f) return;

        var yaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        transform.position += yaw * dir.normalized * edgeScrollSpeed * CurrentPanMultiplier() * Time.deltaTime;
    }

    void ZoomFromScroll()
    {
        var scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) < 0.01f) return;

        var mul = CurrentPanMultiplier();
        transform.position += transform.forward * scroll * zoomSpeed * Mathf.Lerp(1f, 2.5f, (mul - 1f) / (maxPanSpeedMultiplier - 1f)) * Time.deltaTime;
    }

    void ClampAboveTerrain()
    {
        var pos = transform.position;
        var groundY = SampleGroundY(pos);
        var minY = groundY + minHeightAboveTerrain + terrainSamplePadding;
        var maxY = groundY + maxHeightAboveTerrain;
        pos.y = Mathf.Clamp(pos.y, minY, maxY);
        transform.position = pos;

        // Keep reference Terrain as whichever tile we are over (for Inspector / tools).
        var under = FindTerrainAt(pos);
        if (under != null)
            terrain = under;
    }

    static float SampleGroundY(Vector3 worldPos)
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
