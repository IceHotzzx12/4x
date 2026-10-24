using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Player army on the open-field map. Marches via NavMeshAgent; fights barbarians on contact.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class ArmyUnit : MonoBehaviour
{
    [Header("Combat stats")]
    public float Health = 100f;
    public float Attack = 12f;
    public float Speed = 8f;

    [Header("Selection")]
    public bool isSelected;

    [Header("Combat")]
    public float CombatRange = 8f;
    public float AttackInterval = 1f;

    public bool IsAlive => Health > 0f;
    public bool InCombat { get; private set; }
    public BarbarUnit CombatTarget { get; private set; }

    NavMeshAgent _agent;
    float _attackTimer;
    Renderer _renderer;
    Color _baseColor;
    static readonly Color SelectedTint = new Color(0.35f, 0.75f, 1f);

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.speed = Speed;
        _agent.stoppingDistance = 1.25f;
        _agent.autoBraking = true;
        _agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

        _renderer = GetComponentInChildren<Renderer>();
        if (_renderer != null)
            _baseColor = _renderer.material.color;

        var col = GetComponent<Collider>();
        col.isTrigger = true;

        var rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    void Update()
    {
        if (!IsAlive) return;

        _agent.speed = Speed;
        ApplySelectionVisual();

        if (CombatTarget != null && CombatTarget.IsAlive)
        {
            if (!InCombat)
            {
                // Chase with live repath until contact / combat range.
                if (_agent.isOnNavMesh)
                    _agent.SetDestination(CombatTarget.transform.position);

                if (Vector3.Distance(transform.position, CombatTarget.transform.position) <= CombatRange)
                    BeginCombat(CombatTarget);
            }
            else
            {
                TickCombat();
            }
        }
        else if (InCombat)
        {
            EndCombat();
        }
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        ApplySelectionVisual();
    }

    public void MarchTo(Vector3 worldPoint)
    {
        if (!IsAlive) return;
        EndCombat();
        CombatTarget = null;
        if (_agent.isOnNavMesh)
            _agent.SetDestination(worldPoint);
    }

    public void IssueAttack(BarbarUnit target)
    {
        if (!IsAlive || target == null || !target.IsAlive) return;
        CombatTarget = target;
        InCombat = false;
        _attackTimer = 0f;
        if (_agent.isOnNavMesh)
            _agent.SetDestination(target.transform.position);
    }

    public void BeginCombat(BarbarUnit target)
    {
        if (target == null || !target.IsAlive) return;
        CombatTarget = target;
        InCombat = true;
        _attackTimer = 0f;
        if (_agent.isOnNavMesh)
        {
            _agent.ResetPath();
            _agent.isStopped = true;
        }

        target.BeginCombat(this);
    }

    public void TakeDamage(float amount)
    {
        if (!IsAlive) return;
        Health = Mathf.Max(0f, Health - amount);
        if (Health <= 0f)
            Die();
    }

    void TickCombat()
    {
        if (CombatTarget == null || !CombatTarget.IsAlive)
        {
            EndCombat();
            return;
        }

        _attackTimer += Time.deltaTime;
        if (_attackTimer < AttackInterval) return;
        _attackTimer = 0f;
        CombatTarget.TakeDamage(Attack);
    }

    void EndCombat()
    {
        InCombat = false;
        CombatTarget = null;
        _attackTimer = 0f;
        if (_agent != null && _agent.isOnNavMesh)
            _agent.isStopped = false;
    }

    void Die()
    {
        EndCombat();
        isSelected = false;
        if (_agent != null && _agent.isOnNavMesh)
            _agent.isStopped = true;
        Destroy(gameObject);
    }

    void ApplySelectionVisual()
    {
        if (_renderer == null) return;
        _renderer.material.color = isSelected ? SelectedTint : _baseColor;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsAlive || InCombat) return;
        var barb = other.GetComponent<BarbarUnit>();
        if (barb != null && barb.IsAlive && CombatTarget == barb)
            BeginCombat(barb);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, CombatRange);
    }
}
