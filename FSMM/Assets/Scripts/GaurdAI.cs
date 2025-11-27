using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class GaurdAI : MonoBehaviour
{
    public enum State { Patrol, Suspicion, Inspect, Alert, Assist, Search, Chase, Attack }

    // Patrol config data
    public Transform[] patrolPoints;
    public float patrolSpeed = 2.0f;

    // Vision config data
    public Transform eyes;
    public LayerMask obstacleMask;
    public float sightRange = 6.5f;
    public float fovDegrees = 90f;
    public float instantSpotRange = 0.5f;
    public float proximitySpotRange = 2.5f;
    public float sightTick = 0.06f;

    // Sound config data
    public float soundHearRange = 6.0f;
    public float soundAlertLoud = 7.0f;

    // Smell config data
    public float smellRange = 3.5f;
    public float smellAlertGain = 0.25f;

    // Awareness config data
    public float suspicionThreshold = 0.5f;
    public float alertThreshold = 1.0f;
    public float awarenessDecay = 0.2f;

    // Combat config data
    public float chaseSpeed = 3.5f;
    public float attackRange = 1.2f;
    public float attackCooldown = 0.6f;
    public bool attackOnContact = true;

    // Arrival config data
    public float arriveSkin = 0.25f;
    public float inspectBonus = 0.25f;

    // Stuck detection data
    public float inspectTimeout = 2.0f;
    public float stuckSpeedEps = 0.05f;
    public float stuckTimeMax = 0.8f;

    // Search pattern data
    public float searchRadiusStep = 1.8f;
    public int searchRings = 2;
    public int searchSamplesPerRing = 8;
    public float searchSpeed = 2.2f;
    public float searchTimeout = 6.0f;
    public float searchClearance = 0.3f;

    // Debug settings data
    public bool showGizmos = true;
    public bool debugLogs = true;

    // Internal state data
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

    // Search internal data
    Vector2 searchCenterRaw;
    Vector2 searchCenter;
    readonly List<Vector2> searchTargets = new List<Vector2>();
    int searchTargetIndex;

    // Cached components data
    Rigidbody2D rb;
    Transform player;

    // Log throttling data
    float lastLogTime;

    void OnEnable()
    {
        SoundBus.OnSound += OnSoundHeard;
        AlertBus.OnAlert += OnExternalAlert;
        Log("Events subscribed okay");
    }

    void OnDisable()
    {
        SoundBus.OnSound -= OnSoundHeard;
        AlertBus.OnAlert -= OnExternalAlert;
        Log("Events unsubscribed okay");
    }

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
        else
        {
            agentRadius = 0.4f;
        }

        patrolIndex = 0;
        Change(State.Patrol, "Startup patrol state");
    }

    void Update()
    {
        TickSenses();
        TickAwareness();
        RunStateMachine();
        UpdateLookDirection();
    }

    void TickSenses()
    {
        if (Time.time < nextSightTick) return;
        nextSightTick = Time.time + sightTick;
        EvaluateSight();
        EvaluateSmell();
    }

    void TickAwareness()
    {
        awareness = Mathf.Max(0f, awareness - awarenessDecay * Time.deltaTime);

        if (awareness >= alertThreshold && state != State.Chase)
        {
            Change(State.Alert, "Awareness hit alert");
            AlertBus.Broadcast(transform.position);
        }
        else if (awareness >= suspicionThreshold && state == State.Patrol)
        {
            Change(State.Suspicion, "Awareness hit suspicion");
            inspectDeadline = Time.time + inspectTimeout;
        }
    }

    void OnSoundHeard(Vector2 pos, float loud, SoundTag tag)
    {
        float dist = Vector2.Distance(transform.position, pos);
        if (dist > soundHearRange) return;

        float gain = Mathf.Clamp01(loud / soundAlertLoud);
        awareness = Mathf.Max(awareness, suspicionThreshold * 0.6f + gain * 0.4f);

        if (loud >= soundAlertLoud)
        {
            lastKnownPlayer = pos;
            Change(State.Alert, "Loud sound heard");
            AlertBus.Broadcast(pos);
            return;
        }

        investigatePoint = pos;
        inspectDeadline = Time.time + inspectTimeout;
        stuckTimer = 0f;
        if (state == State.Patrol || state == State.Suspicion)
        {
            Change(State.Inspect, "Quiet sound inspect");
        }
    }

    void OnExternalAlert(Vector2 pos)
    {
        if (state == State.Chase || state == State.Attack) return;

        investigatePoint = pos;
        inspectDeadline = Time.time + inspectTimeout;
        stuckTimer = 0f;
        Change(State.Assist, "Teammate alert assist");
    }

    void EvaluateSight()
    {
        if (!player) return;

        Vector2 origin = eyes ? (Vector2)eyes.position : (Vector2)transform.position;
        Vector2 toPlayer = (player.position - (Vector3)origin);
        float dist = toPlayer.magnitude;

        if (dist <= proximitySpotRange)
        {
            bool blockedNear = Physics2D.Raycast(origin, toPlayer.normalized, dist, obstacleMask);
            if (!blockedNear)
            {
                lastKnownPlayer = player.position;
                awareness = alertThreshold;
                Change(State.Chase, "Proximity auto spot");
                return;
            }
        }

        if (dist <= instantSpotRange)
        {
            bool blocked = Physics2D.Raycast(origin, toPlayer.normalized, dist, obstacleMask);
            if (!blocked)
            {
                lastKnownPlayer = player.position;
                awareness = alertThreshold;
                Change(State.Chase, "Instant close sight");
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
                    awareness = Mathf.Min(alertThreshold, awareness + 0.35f);
                    if (awareness >= alertThreshold)
                    {
                        Change(State.Chase, "Vision threshold met");
                    }
                }
            }
        }
    }

    void EvaluateSmell()
    {
        var hits = Physics2D.OverlapCircleAll(transform.position, smellRange);
        ScentNode bestNode = null;
        float best = 0f;

        foreach (var h in hits)
        {
            var node = h.GetComponent<ScentNode>();
            if (!node) continue;
            if (node.intensity > best)
            {
                best = node.intensity;
                bestNode = node;
            }
        }

        if (!bestNode) return;

        investigatePoint = bestNode.transform.position;
        inspectDeadline = Time.time + inspectTimeout;
        stuckTimer = 0f;
        awareness = Mathf.Min(alertThreshold, awareness + smellAlertGain * Time.deltaTime);

        if (state == State.Patrol)
        {
            Change(State.Suspicion, "Smell raised suspicion");
        }

        if (awareness >= alertThreshold && state != State.Chase)
        {
            Change(State.Alert, "Smell raised alert");
            AlertBus.Broadcast(transform.position);
        }
    }

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

    float PatrolArrive()
    {
        return agentRadius + arriveSkin;
    }

    float InspectArrive()
    {
        return agentRadius + arriveSkin + inspectBonus;
    }

    void DoPatrol()
    {
        if (patrolPoints == null || patrolPoints.Length == 0) return;

        Vector2 target = patrolPoints[patrolIndex].position;
        MoveSmart(target, patrolSpeed);

        if (Vector2.Distance(transform.position, target) <= PatrolArrive())
        {
            patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
        }
    }

    void DoSuspicion()
    {
        rb.linearVelocity = Vector2.zero;
        if (Time.time >= inspectDeadline)
        {
            Change(State.Patrol, "Suspicion timed out");
        }
    }

    void DoInspect()
    {
        if (Time.time >= inspectDeadline)
        {
            ToSearchShort("Inspect timed out");
            return;
        }

        MoveSmart(investigatePoint, patrolSpeed * 1.1f);

        if (Vector2.Distance(transform.position, investigatePoint) <= InspectArrive())
        {
            inspectDeadline = Time.time + 1.2f;
            Change(State.Suspicion, "Inspect reached point");
            return;
        }

        UpdateStuckTimer();
        if (stuckTimer >= stuckTimeMax)
        {
            ToSearchShort("Inspect got stuck");
        }
    }

    void DoAlert()
    {
        if (lastKnownPlayer == Vector2.zero)
        {
            ToSearchShort("Alert no memory");
            return;
        }

        MoveSmart(lastKnownPlayer, chaseSpeed * 0.9f);

        if (Vector2.Distance(transform.position, lastKnownPlayer) <= InspectArrive() * 2f)
        {
            ToSearchShort("Alert reached zone");
        }

        UpdateStuckTimer();
        if (stuckTimer >= stuckTimeMax)
        {
            ToSearchShort("Alert got stuck");
        }
    }

    void DoAssist()
    {
        MoveSmart(investigatePoint, chaseSpeed * 0.85f);

        if (Vector2.Distance(transform.position, investigatePoint) <= InspectArrive() * 2f)
        {
            ToSearchShort("Assist reached zone");
        }

        UpdateStuckTimer();
        if (stuckTimer >= stuckTimeMax)
        {
            ToSearchShort("Assist got stuck");
        }
    }

    void DoSearch()
    {
        if (Time.time >= inspectDeadline)
        {
            rb.linearVelocity = Vector2.zero;
            Change(State.Patrol, "Search timed out");
            return;
        }

        if (searchTargets.Count == 0)
        {
            rb.linearVelocity = Vector2.zero;
            Change(State.Patrol, "Search no targets");
            return;
        }

        if (searchTargetIndex >= searchTargets.Count)
        {
            rb.linearVelocity = Vector2.zero;
            Change(State.Patrol, "Search finished path");
            return;
        }

        Vector2 target = searchTargets[searchTargetIndex];
        MoveSmart(target, searchSpeed);
        LogEvery("Search moving target");

        if (Vector2.Distance(transform.position, target) <= InspectArrive())
        {
            searchTargetIndex++;
        }

        UpdateStuckTimer();
        if (stuckTimer >= stuckTimeMax)
        {
            searchTargetIndex++;
            stuckTimer = 0f;
        }
    }

    void DoChase()
    {
        if (!player)
        {
            ToSearchShort("Chase no player");
            return;
        }

        MoveSmart(player.position, chaseSpeed);

        float dist = Vector2.Distance(transform.position, player.position);
        if (dist <= attackRange)
        {
            Change(State.Attack, "Enter attack range");
            return;
        }

        UpdateStuckTimer();
        if (stuckTimer >= stuckTimeMax)
        {
            lastKnownPlayer = player.position;
            ToSearchShort("Chase got stuck");
            return;
        }

        if (!HasLineOfSight())
        {
            lastKnownPlayer = player.position;
            ToSearchShort("Lost line sight");
        }
    }

    void DoAttack()
    {
        if (Time.time < nextAttack) return;

        nextAttack = Time.time + attackCooldown;
        SoundBus.Emit(transform.position, soundAlertLoud, SoundTag.Hit);

        if (player && Vector2.Distance(transform.position, player.position) <= attackRange)
        {
            var td = player.GetComponent<TopDownPlayerController2D>();
            if (td) td.OnCaught();
        }

        Change(State.Chase, "Attack finished now");
    }

    void ToSearchShort(string why)
    {
        inspectDeadline = Time.time + searchTimeout;

        Vector2 center = investigatePoint != Vector2.zero
            ? investigatePoint
            : (lastKnownPlayer != Vector2.zero ? lastKnownPlayer : (Vector2)transform.position);

        searchCenterRaw = center;
        searchCenter = ResolveSearchCenter(center);

        BuildSearchTargets();

        searchTargetIndex = 0;
        stuckTimer = 0f;

        Change(State.Search, why);
    }

    Vector2 ResolveSearchCenter(Vector2 desired)
    {
        Vector2 pos = transform.position;
        Vector2 dir = desired - pos;
        float dist = dir.magnitude;

        if (dist < 0.001f) return desired;

        RaycastHit2D hit = Physics2D.Raycast(pos, dir.normalized, dist, obstacleMask);
        if (hit.collider == null) return desired;

        return hit.point - hit.normal * searchClearance;
    }

    void BuildSearchTargets()
    {
        searchTargets.Clear();
        searchTargets.Add(searchCenter);

        for (int ring = 1; ring <= searchRings; ring++)
        {
            float radius = searchRadiusStep * ring;
            for (int i = 0; i < searchSamplesPerRing; i++)
            {
                float angle = (Mathf.PI * 2f * i) / searchSamplesPerRing;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 candidate = searchCenter + dir * radius;

                if (IsSearchPointReachable(candidate))
                {
                    searchTargets.Add(candidate);
                }
            }
        }
    }

    bool IsSearchPointReachable(Vector2 point)
    {
        Collider2D hitArea = Physics2D.OverlapCircle(point, agentRadius * 0.9f, obstacleMask);
        if (hitArea) return false;

        RaycastHit2D hitCenter = Physics2D.Linecast(searchCenter, point, obstacleMask);
        if (hitCenter.collider != null) return false;

        RaycastHit2D hitGuard = Physics2D.Linecast(transform.position, point, obstacleMask);
        if (hitGuard.collider != null) return false;

        return true;
    }

    void MoveSmart(Vector2 target, float speed)
    {
        Vector2 pos = rb.position;
        Vector2 toTarget = target - pos;
        float dist = toTarget.magnitude;

        if (dist < 0.01f)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        Vector2 dir = toTarget / dist;

        float castRadius = agentRadius * 0.9f;
        float castDist = Mathf.Min(dist, speed * Time.deltaTime * 2f) + castRadius;

        RaycastHit2D hit = Physics2D.CircleCast(pos, castRadius, dir, castDist, obstacleMask);
        if (hit.collider == null)
        {
            rb.linearVelocity = dir * speed;
            return;
        }

        Vector2 normal = hit.normal;
        Vector2 slideDir = new Vector2(-normal.y, normal.x);
        if (Vector2.Dot(slideDir, dir) < 0f)
            slideDir = -slideDir;

        rb.linearVelocity = slideDir * speed;
    }

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

    void UpdateLookDirection()
    {
        if (rb.linearVelocity.sqrMagnitude > 0.01f)
        {
            lookDir = rb.linearVelocity.normalized;
        }
    }

    void UpdateStuckTimer()
    {
        if (rb.linearVelocity.magnitude < stuckSpeedEps)
            stuckTimer += Time.deltaTime;
        else
            stuckTimer = 0f;
    }

    void OnCollisionStay2D(Collision2D col)
    {
        if (!attackOnContact) return;
        if (!col.collider.CompareTag("Player")) return;
        if (Time.time < nextAttack) return;

        Change(State.Attack, "Contact attack hit");
    }

    void Change(State next, string why)
    {
        if (state == next) return;
        Log("State " + state + " to " + next + " because " + why);
        state = next;
    }

    void Log(string msg)
    {
        if (!debugLogs) return;
        Debug.Log("[Guard] " + name + " | " + msg);
    }

    void LogEvery(string msg)
    {
        if (!debugLogs) return;
        if (Time.time - lastLogTime < 0.3f) return;
        Debug.Log("[Guard] " + name + " | " + msg);
        lastLogTime = Time.time;
    }

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

        float pr = agentRadius > 0f ? agentRadius + arriveSkin : 0.6f;
        float ir = pr + inspectBonus;

        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, pr);

        Gizmos.color = Color.gray;
        Gizmos.DrawWireSphere(transform.position, ir);

        Vector2 facing = rb && rb.linearVelocity.sqrMagnitude > 0.01f
            ? rb.linearVelocity.normalized
            : Vector2.right;

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

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(searchCenter, 0.06f);

        Gizmos.color = Color.green;
        foreach (var p in searchTargets)
        {
            Gizmos.DrawWireSphere(p, 0.12f);
        }
    }
}
