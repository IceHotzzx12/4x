using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Neutral barbarian camp unit. Idles until a player army engages; then fights tick-per-second.
/// </summary>
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class BarbarUnit : MonoBehaviour
{
    [Header("Combat stats")]
    public float Health = 80f;
    public float Attack = 8f;
    public float Speed = 5f;

    [Header("Combat")]
    public float CombatRange = 8f;
    public float AttackInterval = 1f;

    public bool IsAlive => Health > 0f;
    public bool InCombat { get; private set; }
    public ArmyUnit CombatTarget { get; private set; }

    NavMeshAgent _agent;
    float _attackTimer;

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        if (_agent != null)
        {
            _agent.speed = Speed;
            _agent.stoppingDistance = 1.25f;
            // Idle: do not wander unless chased into combat.
            _agent.isStopped = true;
        }

        var col = GetComponent<Collider>();
        col.isTrigger = true;

        var rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    void Update()
    {
        if (!IsAlive) return;

        if (InCombat && CombatTarget != null && CombatTarget.IsAlive)
            TickCombat();
        else if (InCombat)
            EndCombat();
    }

    public void BeginCombat(ArmyUnit army)
    {
        if (army == null || !army.IsAlive || !IsAlive) return;
        CombatTarget = army;
        InCombat = true;
        _attackTimer = 0f;

        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.ResetPath();
            _agent.isStopped = true;
        }
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
    }

    void Die()
    {
        EndCombat();
        Destroy(gameObject);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsAlive || InCombat) return;
        var army = other.GetComponent<ArmyUnit>();
        if (army != null && army.IsAlive && army.CombatTarget == this)
            army.BeginCombat(this);
    }
}
