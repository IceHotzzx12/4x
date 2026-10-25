using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Selectable marching unit adapted from MinaPecheux CharacterManager / Unit patterns (MIT).
/// NavMeshAgent move + simple combat vs enemies.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Collider))]
public class OpenRtsUnit : MonoBehaviour
{
    public bool isPlayerOwned = true;
    public float health = 100f;
    public float attack = 12f;
    public float speed = 8f;
    public float combatRange = 10f;
    public float attackInterval = 1f;

    public bool IsAlive => health > 0f;
    public bool IsSelected { get; private set; }

    NavMeshAgent _agent;
    Renderer _renderer;
    Color _baseColor;
    OpenRtsUnit _combatTarget;
    float _attackTimer;
    static readonly Color SelectedTint = new Color(0.35f, 0.85f, 0.45f);

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        // Avoid "Failed to create agent because it is not close enough to the NavMesh"
        _agent.enabled = false;
        _agent.speed = speed;
        _agent.stoppingDistance = 1.5f;
        _agent.acceleration = 40f;
        _agent.angularSpeed = 360f;

        _renderer = GetComponentInChildren<Renderer>();
        if (_renderer != null)
            _baseColor = _renderer.material.color;

        var col = GetComponent<Collider>();
        col.isTrigger = true;

        var rb = GetComponent<Rigidbody>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    void Start()
    {
        EnsureOnNavMesh();
    }

    public bool EnsureOnNavMesh(float maxDistance = 250f)
    {
        if (_agent == null)
            _agent = GetComponent<NavMeshAgent>();
        if (_agent == null)
            return false;

        _agent.enabled = false;
        var pos = transform.position;
        if (NavMesh.SamplePosition(pos, out var hit, maxDistance, NavMesh.AllAreas))
        {
            transform.position = hit.position;
            _agent.enabled = true;
            _agent.Warp(hit.position);
            return _agent.isOnNavMesh;
        }

        for (var r = 20f; r <= maxDistance; r += 20f)
        {
            for (var a = 0; a < 8; a++)
            {
                var ang = a * Mathf.PI * 0.25f;
                var probe = pos + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                if (!NavMesh.SamplePosition(probe, out hit, 25f, NavMesh.AllAreas))
                    continue;
                transform.position = hit.position;
                _agent.enabled = true;
                _agent.Warp(hit.position);
                return _agent.isOnNavMesh;
            }
        }

        Debug.LogWarning("[Fantasy4x] OpenRtsUnit could not find NavMesh near " + name + " @ " + pos);
        return false;
    }

    void Update()
    {
        if (!IsAlive) return;
        if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;
        _agent.speed = speed;

        if (_combatTarget != null && _combatTarget.IsAlive)
        {
            if (_agent.isOnNavMesh)
                _agent.SetDestination(_combatTarget.transform.position);

            if (Vector3.Distance(transform.position, _combatTarget.transform.position) <= combatRange)
            {
                if (_agent.isOnNavMesh)
                {
                    _agent.ResetPath();
                    _agent.isStopped = true;
                }

                _attackTimer += Time.deltaTime;
                if (_attackTimer >= attackInterval)
                {
                    _attackTimer = 0f;
                    _combatTarget.TakeDamage(attack);
                }
            }
            else if (_agent.isOnNavMesh)
            {
                _agent.isStopped = false;
            }
        }
        else
        {
            _combatTarget = null;
            if (_agent.isOnNavMesh)
                _agent.isStopped = false;
        }
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        if (_renderer == null) return;
        _renderer.material.color = selected ? SelectedTint : _baseColor;
    }

    /// <summary>Mina-style MoveTo via NavMeshAgent.</summary>
    public bool MoveTo(Vector3 worldPoint)
    {
        if (!IsAlive || !_agent.isOnNavMesh) return false;
        _combatTarget = null;
        _agent.isStopped = false;
        return _agent.SetDestination(worldPoint);
    }

    public void Attack(OpenRtsUnit target)
    {
        if (!IsAlive || target == null || !target.IsAlive) return;
        if (isPlayerOwned == target.isPlayerOwned) return;
        _combatTarget = target;
        _attackTimer = 0f;
        if (_agent.isOnNavMesh)
        {
            _agent.isStopped = false;
            _agent.SetDestination(target.transform.position);
        }
    }

    public void TakeDamage(float amount)
    {
        if (!IsAlive) return;
        health = Mathf.Max(0f, health - amount);
        if (health <= 0f)
        {
            SetSelected(false);
            OpenRtsSelection.NotifyUnitDestroyed(this);
            Destroy(gameObject);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsAlive || _combatTarget == null) return;
        var u = other.GetComponent<OpenRtsUnit>();
        if (u == _combatTarget)
        {
            // enter melee range — Update handles ticks
        }
    }
}
