using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class GaurdAI : MonoBehaviour
{
    // Guard state machine
    public enum State { Patrol, Suspicion, Inspect, Alert, Assist, Search, Chase, Attack }

    // References and targets
    public Transform[] patrolPoints;
    public LayerMask obstacleMask;
    public Transform eyes;

    Transform player;
    Rigidbody2D rb;

    // Movement parameters
    public float patrolSpeed = 2.0f;
    public float chaseSpeed = 3.6f;
    public float arriveRadius = 0.6f;

    // Sight parameters
    public float sightRange = 8f;
    public float fovDegrees = 70f;
    public float instantSpotRange = 3f;
    public float sightTick = 0.06f;

    // Sound parameters
    public float soundHearRange = 10f;
    public float soundAlertLoud = 7.5f;

    // Smell parameters
    public float smellRange = 5f;
    public float smellAlertGain = 0.25f;

    // Awareness thresholds
    public float suspicionThreshold = 0.5f;
    public float alertThreshold = 1.0f;
    public float awarenessDecay = 0.15f;

    // Combat parameters
    public float attackRange = 1.2f;
    public float attackCooldown = 0.6f;
    public bool attackOnContact = true;

    // Internals storage fields
    State state;
    int patrolIndex;
    Vector2 investigatePoint;
    Vector2 lastKnownPlayer;
    float awareness;
    float nextSightTick;
    float nextAttack;
    bool hasInvestigate;
    bool searching;

    // Subscribe to buses
    void OnEnable()
    {
        SoundBus.OnSound += OnSoundHeard;
        AlertBus.OnAlert += OnExternalAlert;
    }

    // Unsubscribe from buses
    void OnDisable()
    {
        SoundBus.OnSound -= OnSoundHeard;
        AlertBus.OnAlert -= OnExternalAlert;
    }

    // Initialize on start
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        state = State.Patrol;
        patrolIndex = 0;
    }

    // Per frame driver
    void Update()
    {
        TickSenses();
        TickAwareness();
        RunStateMachine();
    }

    // Periodic sight checks
    void TickSenses()
    {
        if (Time.time >= nextSightTick)
        {
            nextSightTick = Time.time + sightTick;
            EvaluateSight();
            EvaluateSmell();
        }
    }

    // Awareness balancing logic
    void TickAwareness()
    {
        awareness = Mathf.Max(0f, awareness - awarenessDecay * Time.deltaTime);

        if (awareness >= alertThreshold && state != State.Chase)
        {
            state = State.Alert;
            AlertBus.Broadcast(lastKnownPlayer);
        }
        else if (awareness >= suspicionThreshold && state == State.Patrol)
        {
            state = State.Suspicion;
        }
    }

    // Handle sound inputs
    void OnSoundHeard(Vector2 pos, float loud, SoundTag tag)
    {
        float dist = Vector2.Distance(transform.position, pos);
        if (dist > soundHearRange) return;

        investigatePoint = pos;
        hasInvestigate = true;

        float gain = Mathf.Clamp01(loud / soundAlertLoud);
        awareness = Mathf.Max(awareness, suspicionThreshold * 0.75f + gain * 0.5f);

        if (loud >= soundAlertLoud)
        {
            lastKnownPlayer = pos;
            state = State.Alert;
            AlertBus.Broadcast(pos);
        }
        else if (state == State.Patrol || state == State.Suspicion)
        {
            state = State.Inspect;
        }
    }

    // Handle external alerts
    void OnExternalAlert(Vector2 pos)
    {
        if (state == State.Patrol || state == State.Suspicion || state == State.Inspect)
        {
            investigatePoint = pos;
            hasInvestigate = true;
            state = State.Assist;
        }
    }

    // Evaluate line of sight
    void EvaluateSight()
    {
        if (!player) return;

        Vector2 origin = eyes ? (Vector2)eyes.position : (Vector2)transform.position;
        Vector2 toPlayer = (player.position - (Vector3)origin);
        float dist = toPlayer.magnitude;

        if (dist <= instantSpotRange)
        {
            if (!Physics2D.Raycast(origin, toPlayer.normalized, dist, obstacleMask))
            {
                lastKnownPlayer = player.position;
                awareness = alertThreshold;
                state = State.Chase;
                return;
            }
        }

        if (dist <= sightRange)
        {
            Vector2 facing = (Vector2)(transform.right * Mathf.Sign(transform.localScale.x));
            float angle = Vector2.Angle(facing, toPlayer);
            if (angle <= fovDegrees * 0.5f)
            {
                if (!Physics2D.Raycast(origin, toPlayer.normalized, dist, obstacleMask))
                {
                    lastKnownPlayer = player.position;
                    awareness = Mathf.Min(alertThreshold, awareness + 0.35f);
                    if (awareness >= alertThreshold) state = State.Chase;
                }
            }
        }
    }

    // Evaluate scent gradient
    void EvaluateSmell()
    {
        var hits = Physics2D.OverlapCircleAll(transform.position, smellRange);
        ScentNode strongest = null;
        float best = 0f;

        foreach (var h in hits)
        {
            var node = h.GetComponent<ScentNode>();
            if (!node) continue;
            if (node.intensity > best)
            {
                best = node.intensity;
                strongest = node;
            }
        }

        if (strongest)
        {
            investigatePoint = strongest.transform.position;
            hasInvestigate = true;
            awareness = Mathf.Min(alertThreshold, awareness + smellAlertGain * Time.deltaTime);
            if (state == State.Patrol) state = State.Suspicion;
            if (awareness >= alertThreshold && state != State.Chase)
            {
                state = State.Alert;
                AlertBus.Broadcast(investigatePoint);
            }
        }
    }

    // Execute finite states
    void RunStateMachine()
    {
        switch (state)
        {
            case State.Patrol: DoPatrol(); break;
            case State.Suspicion: DoSuspicion(); break;
            case State.Inspect: DoInspect(); break;
            case State.Alert: DoAlert(); break;
            case State.Assist: DoAssist(); break;
            case State.Search: DoSearch(); break;
            case State.Chase: DoChase(); break;
            case State.Attack: DoAttack(); break;
        }
    }

    // Move along waypoints
    void DoPatrol()
    {
        if (patrolPoints == null || patrolPoints.Length == 0) return;
        Vector2 target = patrolPoints[patrolIndex].position;
        Vector2 flat = new Vector2(target.x, transform.position.y);
        MoveTowards(flat, patrolSpeed);
        float dx = Mathf.Abs(transform.position.x - target.x);
        if (dx <= arriveRadius) patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
    }

    // Idle suspicious look
    void DoSuspicion()
    {
        Vector2 v = rb.linearVelocity;
        v.x = 0f;
        rb.linearVelocity = v;
        if (hasInvestigate) state = State.Inspect;
    }

    // Walk to investigate
    void DoInspect()
    {
        if (!hasInvestigate) { state = State.Patrol; return; }
        MoveTowards(investigatePoint, patrolSpeed * 1.1f);
        if (Vector2.Distance(transform.position, investigatePoint) <= arriveRadius * 2f)
        {
            StartCoroutine(InspectPause());
            hasInvestigate = false;
        }
    }

    // Pause and decide
    IEnumerator InspectPause()
    {
        State prev = state;
        state = State.Suspicion;
        yield return new WaitForSeconds(1.2f);
        if (state == State.Suspicion) state = State.Patrol;
    }

    // High alert converge
    void DoAlert()
    {
        if (lastKnownPlayer != Vector2.zero)
        {
            MoveTowards(lastKnownPlayer, chaseSpeed * 0.9f);
            if (Vector2.Distance(transform.position, lastKnownPlayer) <= arriveRadius * 3f)
            {
                state = State.Search;
            }
        }
        else
        {
            state = State.Search;
        }
    }

    // Move to assist
    void DoAssist()
    {
        if (!hasInvestigate) { state = State.Patrol; return; }
        MoveTowards(investigatePoint, chaseSpeed * 0.85f);
        if (Vector2.Distance(transform.position, investigatePoint) <= arriveRadius * 3f)
        {
            state = State.Search;
            hasInvestigate = false;
        }
    }

    // Search and reset
    void DoSearch()
    {
        Vector2 v = rb.linearVelocity;
        v.x = 0f;
        rb.linearVelocity = v;
        if (!searching) StartCoroutine(SearchRoutine());
    }

    // Timed search loop
    IEnumerator SearchRoutine()
    {
        searching = true;
        state = State.Suspicion;
        yield return new WaitForSeconds(2.0f);
        state = State.Patrol;
        searching = false;
    }

    // Pursue target player
    void DoChase()
    {
        if (!player)
        {
            state = State.Search;
            return;
        }
        lastKnownPlayer = player.position;
        MoveTowards(player.position, chaseSpeed);

        float dist = Vector2.Distance(transform.position, player.position);
        if (dist <= attackRange) state = State.Attack;

        if (!HasLineOfSight())
        {
            state = State.Search;
        }
    }

    // Execute attack action
    void DoAttack()
    {
        if (Time.time < nextAttack) return;
        nextAttack = Time.time + attackCooldown;

        SoundBus.Emit(transform.position, soundAlertLoud, SoundTag.Hit);

        if (player && Vector2.Distance(transform.position, player.position) <= attackRange)
        {
            var pc = player.GetComponent<PlayerController2D>();
            if (pc) pc.OnCaught();
        }

        state = State.Chase;
    }

    // Basic movement helper
    void MoveTowards(Vector2 target, float speed)
    {
        Vector2 pos = transform.position;
        Vector2 dir = (target - pos).normalized;
        Vector2 v = rb.linearVelocity;
        v.x = dir.x * speed;
        rb.linearVelocity = v;

        if (dir.x != 0f)
        {
            var ls = transform.localScale;
            ls.x = Mathf.Sign(dir.x) * Mathf.Abs(ls.x);
            transform.localScale = ls;
        }
    }

    // Confirm line of sight
    bool HasLineOfSight()
    {
        if (!player) return false;
        Vector2 origin = eyes ? (Vector2)eyes.position : (Vector2)transform.position;
        Vector2 toPlayer = (player.position - (Vector3)origin);
        float dist = toPlayer.magnitude;
        if (dist > sightRange) return false;
        if (Physics2D.Raycast(origin, toPlayer.normalized, dist, obstacleMask)) return false;
        return true;
    }

    // External hit hook
    public void OnHit(Vector2 sourcePosition)
    {
        lastKnownPlayer = sourcePosition;
        awareness = alertThreshold;
        state = State.Alert;
        AlertBus.Broadcast(sourcePosition);
        SoundBus.Emit(sourcePosition, soundAlertLoud, SoundTag.Hit);
    }

    // Contact attack hook
    void OnCollisionStay2D(Collision2D col)
    {
        if (!attackOnContact) return;
        if (col.collider.CompareTag("Player") && Time.time >= nextAttack)
        {
            state = State.Attack;
        }
    }
}
