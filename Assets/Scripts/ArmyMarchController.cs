using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RTS selection + march orders for player armies.
/// Left-drag: box select. Right-click terrain: march. Right-click barbarian: chase/attack.
/// Mid-march destination changes repath via NavMeshAgent.SetDestination.
/// </summary>
public class ArmyMarchController : MonoBehaviour
{
    [Header("References")]
    public Camera rtsCamera;
    public LayerMask groundMask = ~0;
    public LayerMask unitMask = ~0;

    [Header("Box select")]
    public Color boxFill = new Color(0.2f, 0.6f, 1f, 0.15f);
    public Color boxBorder = new Color(0.3f, 0.75f, 1f, 0.9f);

    readonly List<ArmyUnit> _selected = new List<ArmyUnit>();
    Vector2 _boxStart;
    Vector2 _boxEnd;
    bool _isBoxing;
    bool _boxMoved;

    const float BoxDragThresholdPx = 4f;

    void Awake()
    {
        if (rtsCamera == null)
            rtsCamera = Camera.main;
    }

    void Update()
    {
        if (rtsCamera == null)
            rtsCamera = Camera.main;
        if (rtsCamera == null) return;

        HandleSelectionInput();
        HandleMarchInput();
    }

    void HandleSelectionInput()
    {
        if (Input.GetMouseButtonDown(0))
        {
            _boxStart = Input.mousePosition;
            _boxEnd = _boxStart;
            _isBoxing = true;
            _boxMoved = false;
        }

        if (_isBoxing && Input.GetMouseButton(0))
        {
            _boxEnd = Input.mousePosition;
            if ((_boxEnd - _boxStart).sqrMagnitude > BoxDragThresholdPx * BoxDragThresholdPx)
                _boxMoved = true;
        }

        if (_isBoxing && Input.GetMouseButtonUp(0))
        {
            _boxEnd = Input.mousePosition;
            if (_boxMoved)
                SelectArmiesInBox(_boxStart, _boxEnd);
            else
                SelectClickedArmy(_boxEnd);

            _isBoxing = false;
            _boxMoved = false;
        }
    }

    void HandleMarchInput()
    {
        if (!Input.GetMouseButtonDown(1)) return;
        if (_selected.Count == 0) return;

        var ray = rtsCamera.ScreenPointToRay(Input.mousePosition);
        var hits = Physics.RaycastAll(ray, 10000f, ~0, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0) return;
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        // Prefer attacking a barbarian under the cursor.
        foreach (var hit in hits)
        {
            var barb = hit.collider.GetComponentInParent<BarbarUnit>();
            if (barb == null || !barb.IsAlive) continue;

            foreach (var army in _selected)
            {
                if (army != null && army.IsAlive)
                    army.IssueAttack(barb);
            }
            return;
        }

        // Open-field march: first non-unit hit (TerrainCollider / ground).
        foreach (var hit in hits)
        {
            if (hit.collider.GetComponentInParent<ArmyUnit>() != null) continue;
            if (hit.collider.GetComponentInParent<BarbarUnit>() != null) continue;

            var point = hit.point;
            foreach (var army in _selected)
            {
                if (army != null && army.IsAlive)
                    army.MarchTo(point);
            }
            return;
        }
    }

    void SelectClickedArmy(Vector2 screenPos)
    {
        var ray = rtsCamera.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out var hit, 10000f, unitMask, QueryTriggerInteraction.Collide))
        {
            var army = hit.collider.GetComponentInParent<ArmyUnit>();
            if (army != null && army.IsAlive)
            {
                ClearSelection();
                AddSelection(army);
                return;
            }
        }

        ClearSelection();
    }

    void SelectArmiesInBox(Vector2 start, Vector2 end)
    {
        ClearSelection();

        var min = Vector2.Min(start, end);
        var max = Vector2.Max(start, end);
        var rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);

        var armies = FindObjectsOfType<ArmyUnit>();
        foreach (var army in armies)
        {
            if (army == null || !army.IsAlive) continue;
            var sp = rtsCamera.WorldToScreenPoint(army.transform.position);
            if (sp.z < 0f) continue; // behind camera
            if (rect.Contains(new Vector2(sp.x, sp.y)))
                AddSelection(army);
        }
    }

    void AddSelection(ArmyUnit army)
    {
        if (_selected.Contains(army)) return;
        _selected.Add(army);
        army.SetSelected(true);
    }

    void ClearSelection()
    {
        for (var i = 0; i < _selected.Count; i++)
        {
            if (_selected[i] != null)
                _selected[i].SetSelected(false);
        }
        _selected.Clear();
    }

    void OnGUI()
    {
        if (!_isBoxing || !_boxMoved) return;

        var start = FlipY(_boxStart);
        var end = FlipY(_boxEnd);
        var min = Vector2.Min(start, end);
        var max = Vector2.Max(start, end);
        var rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);

        DrawRect(rect, boxFill);
        DrawRectBorder(rect, boxBorder, 2f);
    }

    static Vector2 FlipY(Vector2 screen)
    {
        return new Vector2(screen.x, Screen.height - screen.y);
    }

    static void DrawRect(Rect rect, Color color)
    {
        var old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = old;
    }

    static void DrawRectBorder(Rect rect, Color color, float thickness)
    {
        DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, thickness), color);
        DrawRect(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), color);
        DrawRect(new Rect(rect.xMin, rect.yMin, thickness, rect.height), color);
        DrawRect(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), color);
    }
}
