using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class GaurdAI : MonoBehaviour
{
    public enum State { Patrol, Suspicion, Inspect, Alert, Assist, Search, Chase, Attack }

    // References here
    public Transform[] patrolPoints;
    public LayerMask obstacleMask;
    public Transform eyes;

    // Movement here
    public float patrolSpeed = 2.0f;
    public float chaseSpeed = 3.6f;

    // Vision here
    public float sightRange = 8f;
    public float fovDegrees = 80f;
    public float instantSpotRange = 0.5f;
    public float sightTick = 0.06f;

    // Sound here
    public float soundHearRange = 6.5f;
    public float soundAlertLoud = 7.5f;

    // Smell here
    public float smellRange = 4.5f;
    public float smellAlertGain = 0.25f;

    // Awareness here
    public float suspicionThreshold = 0.5f;
    public float alertThreshold = 1.0f;
    public float awarenessDecay = 0.15f;

    // Combat here
    public float attackRange = 1.2f;
    public float attackCooldown = 0.6f;
    public bool attackOnContact = true;

    // Arrive radii here
    public float arriveSkin = 0.25f;
    public float inspectBonus = 0.25f;

    // Unstuck here
    public float inspectTimeout = 2.0f;
    public float stuckSpeedEps = 0.05f;
    public float stuckTimeMax = 0.9f;

    // Search settings
    public float searchRadius = 2.5f;
    public int searchPointsCount = 4;
    public float searchSpeed = 2.2f;
    public bool avoidWallsInSearch = true;

    // Debug toggles here
    public bool showGizmos = true;
    public bool debugLogs = true;

    // Internals here
    State state;
    int patrolIndex;
    Vector2 investigatePoint;
    Vector2 lastKnownPlayer;
    float awareness;
    float nextSightTick;
    float nextAttack;
    float agentRadius;
    float inspectDeadline;
    float stuckTimer;
    Vector2 lookDir = Vector2.right;

    Vector2 searchCenter;
    int searchIndex;

    Rigidbody2D rb;
    Transform player;

    // Subscribe buses
    void OnEnable()
    {
        SoundBus.OnSound += OnSoundHeard;
        AlertBus.OnAlert += OnExternalAlert;
        Log("Subscribed sound alert");
    }

    // Unsubscribe buses
    void OnDisable()
    {
        SoundBus.OnSound -= OnSoundHeard;
        AlertBus.OnAlert -= OnExternalAlert;
        Log("Unsubscribed sound alert");
    }

    // Start setup
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;

        player = GameObject.FindGameObjectWithTag("Player")?.transform;

        var cap = GetComponent<CapsuleCollider2D>();
        var box = GetComponent<BoxCollider2D>();
        if (cap)
        {
            float rx = cap.size.x * 0.5f * Mathf.Abs(transform.lossyScale.x);
            float ry = cap.size.y * 0.5f * Mathf.Abs(transform.lossyScale.y);
            agentRadius = Mathf.Max(rx, ry);
        }
        else if (box)
        {
            float rx = box.size.x * 0.5f * Mathf.Abs(transform.lossyScale.x);
            float ry = box.size.y * 0.5f * Mathf.Abs(transform.lossyScale.y);
            agentRadius = Mathf.Max(rx, ry);
        }
        else agentRadius = 0.4f;

        Change(State.Patrol, "startup initialize");
        patrolIndex = 0;

        Log("ObstacleMask value " + obstacleMask.value);
        Log("Patrol points " + (patrolPoints != null ? patrolPoints.Length : 0));
    }

    // Frame loop
    void Update()
    {
        TickSenses();
        TickAwareness();
        RunStateMachine();
        UpdateLookDirection();
    }

    // Senses tick
    void TickSenses()
    {
        if (Time.time >= nextSightTick)
        {
            nextSightTick = Time.time + sightTick;
            EvaluateSight();
            EvaluateSmell();
        }
    }

    // Awareness tick
    void TickAwareness()
    {
        float old = awareness;
        awareness = Mathf.Max(0f, awareness - awarenessDecay * Time.deltaTime);
        if (Mathf.Abs(awareness - old) > 0.001f) Log("Awareness decayed " + awareness.ToString("F2"));

        if (awareness >= alertThreshold && state != State.Chase)
        {
            Change(State.Alert, "awareness reached alert");
            Vector2 ping = transform.position;
            AlertBus.Broadcast(ping);
        }
        else if (awareness >= suspicionThreshold && state == State.Patrol)
        {
            Change(State.Suspicion, "awareness reached suspicion");
        }
    }

    // Sound handler
    void OnSoundHeard(Vector2 pos, float loud, SoundTag tag)
    {
        float dist = Vector2.Distance(transform.position, pos);
        if (dist > soundHearRange) return;

        Log("Heard " + tag + " loud " + loud.ToString("F1") + " at " + pos + " dist " + dist.ToString("F1"));

        float gain = Mathf.Clamp01(loud / soundAlertLoud);
        float before = awareness;
        awareness = Mathf.Max(awareness, suspicionThreshold * 0.6f + gain * 0.4f);
        if (awareness != before) Log("Awareness raised " + awareness.ToString("F2"));

        if (loud >= soundAlertLoud)
        {
            lastKnownPlayer = pos;
            Change(State.Alert, "loud sound alert");
            AlertBus.Broadcast(pos);
            return;
        }

        investigatePoint = pos;
        inspectDeadline = Time.time + inspectTimeout;
        stuckTimer = 0f;
        if (state == State.Patrol || state == State.Suspicion)
            Change(State.Inspect, "quiet sound inspect");
    }

    // Alert receiver
    void OnExternalAlert(Vector2 pos)
    {
        Log("Received alert at " + pos);
        if (state == State.Chase || state == State.Attack) return;

        investigatePoint = pos;
        inspectDeadline = Time.time + inspectTimeout;
        stuckTimer = 0f;
        Change(State.Assist, "team alert assist");
    }

    // Evaluate sight
    void EvaluateSight()
    {
        if (!player) return;

        Vector2 origin = eyes ? (Vector2)eyes.position : (Vector2)transform.position;
        Vector2 toPlayer = (player.position - (Vector3)origin);
        float dist = toPlayer.magnitude;

        if (dist <= instantSpotRange)
        {
            bool blockedNear = Physics2D.Raycast(origin, toPlayer.normalized, dist, obstacleMask);
            if (!blockedNear)
            {
                lastKnownPlayer = player.position;
                awareness = alertThreshold;
                Change(State.Chase, "instant spot range");
                return;
            }
        }

        if (dist <= sightRange)
        {
            Vector2 facing = lookDir.sqrMagnitude > 0.001f ? lookDir : Vector2.right;
            float angle = Vector2.Angle(facing, toPlayer);
            if (angle <= fovDegrees * 0.5f)
            {
                bool blocked = Physics2D.Raycast(origin, toPlayer.normalized, dist, obstacleMask);
                if (!blocked)
                {
                    lastKnownPlayer = player.position;
                    float before = awareness;
                    awareness = Mathf.Min(alertThreshold, awareness + 0.35f);
                    if (awareness != before) Log("Vision increased awareness " + awareness.ToString("F2"));
                    if (awareness >= alertThreshold) Change(State.Chase, "vision threshold met");
                }
            }
        }
    }

    // Evaluate smell
    void EvaluateSmell()
    {
        var hits = Physics2D.OverlapCircleAll(transform.position, smellRange);
        ScentNode bestNode = null; float best = 0f;
        foreach (var h in hits)
        {
            var node = h.GetComponent<ScentNode>();
            if (!node) continue;
            if (node.intensity > best) { best = node.intensity; bestNode = node; }
        }
        if (bestNode)
        {
            investigatePoint = bestNode.transform.position;
            inspectDeadline = Time.time + inspectTimeout;
            stuckTimer = 0f;
            awareness = Mathf.Min(alertThreshold, awareness + smellAlertGain * Time.deltaTime);
            Log("Smell sensed at " + bestNode.transform.position + " intensity " + best.ToString("F2"));
            if (state == State.Patrol) Change(State.Suspicion, "smell suspicion");
            if (awareness >= alertThreshold && state != State.Chase)
            {
                Change(State.Alert, "smell raised alert");
                AlertBus.Broadcast(transform.position);
            }
        }
    }

    // Brain driver
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

    // Arrive helpers
    float PatrolArrive() { return agentRadius + arriveSkin; }
    float InspectArrive() { return agentRadius + arriveSkin + inspectBonus; }

    // Patrol state
    void DoPatrol()
    {
        if (patrolPoints == null || patrolPoints.Length == 0) return;
        Vector2 target = patrolPoints[patrolIndex].position;
        MoveTowards(target, patrolSpeed);
        if (Vector2.Distance(transform.position, target) <= PatrolArrive())
        {
            patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
            Log("Reached patrol index " + patrolIndex);
        }
    }

    // Suspicion state
    void DoSuspicion()
    {
        rb.linearVelocity = Vector2.zero;
        if (Time.time >= inspectDeadline) Change(State.Patrol, "suspicion timeout");
    }

    // Inspect state
    void DoInspect()
    {
        if (Time.time >= inspectDeadline) { ToSearchShort("inspect timeout"); return; }

        MoveTowards(investigatePoint, patrolSpeed * 1.1f);
        LogEvery("Inspect moving to " + investigatePoint);

        if (Vector2.Distance(transform.position, investigatePoint) <= InspectArrive())
        {
            inspectDeadline = Time.time + 1.2f;
            Change(State.Suspicion, "inspect arrived idle");
            return;
        }

        if (rb.linearVelocity.magnitude < stuckSpeedEps) stuckTimer += Time.deltaTime; else stuckTimer = 0f;

        Vector2 pos = transform.position;
        Vector2 dir = (investigatePoint - pos).normalized;
        float dist = Vector2.Distance(pos, investigatePoint);
        bool blocked = Physics2D.Raycast(pos, dir, dist, obstacleMask);

        if (stuckTimer >= stuckTimeMax || blocked)
        {
            ToSearchShort(blocked ? "inspect blocked ray" : "inspect stall timer");
        }
    }

    // Alert state
    void DoAlert()
    {
        if (lastKnownPlayer == Vector2.zero) { ToSearchShort("alert no last"); return; }
        MoveTowards(lastKnownPlayer, chaseSpeed * 0.9f);
        LogEvery("Alert moving to " + lastKnownPlayer);
        if (Vector2.Distance(transform.position, lastKnownPlayer) <= InspectArrive() * 2f)
        {
            ToSearchShort("alert arrived zone");
        }
    }

    // Assist state
    void DoAssist()
    {
        MoveTowards(investigatePoint, chaseSpeed * 0.85f);
        LogEvery("Assist moving to " + investigatePoint);
        if (Vector2.Distance(transform.position, investigatePoint) <= InspectArrive() * 2f)
        {
            ToSearchShort("assist arrived zone");
        }
    }

    // Search state
    void DoSearch()
    {
        if (Time.time >= inspectDeadline)
        {
            rb.linearVelocity = Vector2.zero;
            Change(State.Patrol, "search timeout");
            return;
        }

        if (searchPointsCount <= 0 || searchRadius <= 0.01f)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        int count = Mathf.Max(1, searchPointsCount);
        int safety = 0;

        while (safety < count)
        {
            float step = 360f / count;
            float angleRad = step * searchIndex * Mathf.Deg2Rad;
            Vector2 offset = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * searchRadius;
            Vector2 target = searchCenter + offset;

            Vector2 pos = transform.position;
            Vector2 toTarget = target - pos;
            float dist = toTarget.magnitude;

            if (dist < 0.05f)
            {
                searchIndex = (searchIndex + 1) % count;
                safety++;
                continue;
            }

            Vector2 dir = toTarget / dist;

            if (avoidWallsInSearch)
            {
                RaycastHit2D hit = Physics2D.Raycast(pos, dir, dist, obstacleMask);
                if (hit.collider != null)
                {
                    searchIndex = (searchIndex + 1) % count;
                    safety++;
                    continue;
                }
            }

            MoveTowards(target, searchSpeed);
            LogEvery("Search moving to " + target);

            if (Vector2.Distance(pos, target) <= InspectArrive())
            {
                searchIndex = (searchIndex + 1) % count;
            }

            return;
        }

        rb.linearVelocity = Vector2.zero;
    }


    // Chase state
    void DoChase()
    {
        if (!player) { ToSearchShort("chase no player"); return; }
        MoveTowards(player.position, chaseSpeed);
        LogEvery("Chasing player now");
        float dist = Vector2.Distance(transform.position, player.position);
        if (dist <= attackRange) Change(State.Attack, "enter attack range");
        if (!HasLineOfSight())
        {
            lastKnownPlayer = player.position;
            ToSearchShort("lost line sight");
        }
    }

    // Attack state
    void DoAttack()
    {
        if (Time.time < nextAttack) return;
        nextAttack = Time.time + attackCooldown;
        SoundBus.Emit(transform.position, soundAlertLoud, SoundTag.Hit);
        Log("Attack fired player");
        if (player && Vector2.Distance(transform.position, player.position) <= attackRange)
        {
            var td = player.GetComponent<TopDownPlayerController2D>(); if (td) td.OnCaught();
            var old = player.GetComponent<TopDownPlayerController2D>(); if (old) old.OnCaught();
        }
        Change(State.Chase, "attack complete");
    }

    // To search helper
    void ToSearchShort(string why)
    {
        inspectDeadline = Time.time + 3.5f;
        Vector2 center = investigatePoint != Vector2.zero
            ? investigatePoint
            : (lastKnownPlayer != Vector2.zero ? lastKnownPlayer : (Vector2)transform.position);
        searchCenter = center;
        searchIndex = 0;
        Change(State.Search, why);
        stuckTimer = 0f;
    }

    // Movement helper
    void MoveTowards(Vector2 target, float speed)
    {
        Vector2 pos = transform.position;
        Vector2 dir = (target - pos).normalized;
        rb.linearVelocity = dir * speed;
    }

    // Vision helper
    bool HasLineOfSight()
    {
        if (!player) return false;
        Vector2 origin = eyes ? (Vector2)eyes.position : (Vector2)transform.position;
        Vector2 toPlayer = (player.position - (Vector3)origin);
        float dist = toPlayer.magnitude;
        if (dist > sightRange) return false;
        bool blocked = Physics2D.Raycast(origin, toPlayer.normalized, dist, obstacleMask);
        return !blocked;
    }

    // Facing update
    void UpdateLookDirection()
    {
        if (rb.linearVelocity.sqrMagnitude > 0.01f)
            lookDir = rb.linearVelocity.normalized;
    }

    // Contact hook
    void OnCollisionStay2D(Collision2D col)
    {
        if (!attackOnContact) return;
        if (col.collider.CompareTag("Player") && Time.time >= nextAttack)
            Change(State.Attack, "contact attack fired");
    }

    // State change log
    void Change(State next, string why)
    {
        if (state == next) return;
        Log("State " + state + " to " + next + " because " + why);
        state = next;
    }

    // Throttled logger
    float lastLog;
    void LogEvery(string msg)
    {
        if (Time.time - lastLog > 0.5f)
        {
            Log(msg);
            lastLog = Time.time;
        }
    }

    // Conditional log
    void Log(string msg)
    {
        if (debugLogs) Debug.Log("[Guard] " + name + " | " + msg);
    }

    // Gizmos draw
    void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;
        Vector3 o = eyes ? eyes.position : transform.position;

        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(o, soundHearRange);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(o, sightRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(o, instantSpotRange);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(o, smellRange);

        float pr = (agentRadius > 0f ? agentRadius + arriveSkin : 0.6f);
        float ir = pr + inspectBonus;
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, pr);
        Gizmos.color = Color.gray;
        Gizmos.DrawWireSphere(transform.position, ir);

        Vector2 facing = (rb && rb.linearVelocity.sqrMagnitude > 0.01f) ? rb.linearVelocity.normalized : Vector2.right;
        float half = 0.5f * fovDegrees * Mathf.Deg2Rad;
        Vector2 left = new Vector2(
            facing.x * Mathf.Cos(-half) - facing.y * Mathf.Sin(-half),
            facing.x * Mathf.Sin(-half) + facing.y * Mathf.Cos(-half));
        Vector2 right = new Vector2(
            facing.x * Mathf.Cos(half) - facing.y * Mathf.Sin(half),
            facing.x * Mathf.Sin(half) + facing.y * Mathf.Cos(half));
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(o, o + (Vector3)(left.normalized * sightRange));
        Gizmos.DrawLine(o, o + (Vector3)(right.normalized * sightRange));

        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(investigatePoint, 0.08f);
    }
}
