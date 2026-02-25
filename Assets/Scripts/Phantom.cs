using System.Collections;
using UnityEngine;

/// <summary>
/// Simplified "phantom" enemy behaviour inspired by Minecraft's phantom.
/// - Spawns (or is enabled) somewhere around the player between spawnMinDistance/spawnMaxDistance.
/// - Has a sphere trigger collider used to detect the player (Rocket).
/// - When the player is spotted it makes a diving attack.
/// - If the attack lasts longer than attackTimeout without hitting the Rocket, or if the phantom
///   takes any damage while attacking, the attack "fails" and the phantom ascends/upwards,
///   then chooses a new spawn point and waits for the next opportunity.
/// - On collision with the Rocket during the attack the phantom can deal damage (not implemented).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class Phantom : MonoBehaviour
{
    [Header("Spawn Settings")]
    public float spawnMinDistance = 20f;  // horizontal distance from player
    public float spawnMaxDistance = 30f;

    [Header("Height")]
    public float spawnMinHeight = 15f;
    public float spawnMaxHeight = 25f;
    public float circleHeight = 40f;          // height at which to begin circling

    [Header("Attack Settings")]
    public float attackSpeed = 20f;
    public float ascendSpeed = 5f;
    public float attackTimeout = 3f;
    public float detectionRadius = 30f; // match spawn radius so phantom immediately detects player

    [Header("Health")]
    public float maxHealth = 10f;

    private Transform player;
    private Rigidbody rb;
    private float currentHealth;

    private enum State { Waiting, Attacking, Ascending, Circling }

    private float circleTimer;
    private float circleDuration;
    private float circleRadius;
    private State state = State.Waiting;

    private float attackTimer;
    private Vector3 attackDirection;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        SphereCollider sc = GetComponent<SphereCollider>();
        sc.isTrigger = true;
        sc.radius = detectionRadius; // ensure trigger covers entire detection zone
    }

    void OnEnable()
    {
        // try to find the player by tag; if it fails also look by name
        GameObject pObj = GameObject.FindWithTag("Rocket");
        if (pObj == null) pObj = GameObject.Find("Rocket");
        player = pObj ? pObj.transform : null;
        if (player == null)
            Debug.LogWarning("[Phantom] could not find player object tagged or named 'Rocket'");

        currentHealth = maxHealth;
        ChooseSpawnPosition();
        state = State.Waiting;
    }

    void Update()
    {
        if (player == null) return;
        switch (state)
        {
            case State.Waiting:
                float dist = Vector3.Distance(transform.position, player.position);
                if (dist <= detectionRadius)
                {
                    Debug.Log("[Phantom] Player spotted by distance, commencing attack");
                    StartAttack();
                }
                break;
            case State.Attacking:
                attackTimer += Time.deltaTime;
                if (attackTimer >= attackTimeout)
                {
                    Debug.Log("[Phantom] Attack timed out");
                    FailAttack();
                }
                break;
            case State.Ascending:
                rb.velocity = Vector3.up * ascendSpeed;
                // face upward
                transform.rotation = Quaternion.LookRotation(rb.velocity);
                if (transform.position.y >= circleHeight)
                {
                    // switch to circling
                    state = State.Circling;
                    circleTimer = 0f;
                    circleDuration = Random.Range(1f, 3f);
                    circleRadius = Vector3.Distance(transform.position, player.position);
                }
                break;
            case State.Circling:
                circleTimer += Time.deltaTime;
                // move horizontally around player
                float angle = circleTimer * 2f; // speed factor
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * circleRadius;
                Vector3 targetPos = player.position + offset + Vector3.up * circleHeight;
                Vector3 move = (targetPos - transform.position).normalized * attackSpeed;
                rb.velocity = move;
                transform.rotation = Quaternion.LookRotation(move);
                if (circleTimer >= circleDuration)
                {
                    StartAttack();
                }
                break;
        }
    }

    void FixedUpdate()
    {
        if (state == State.Attacking)
        {
            rb.velocity = attackDirection * attackSpeed;
            // keep facing direction of travel
            if (rb.velocity.sqrMagnitude > 0.1f)
                transform.rotation = Quaternion.LookRotation(rb.velocity);
        }
    }

    private void ChooseSpawnPosition()
    {
        if (player == null) return;
        Vector3 randomDir = Random.onUnitSphere;
        randomDir.y = 0f; // keep horizontal
        float dist = Random.Range(spawnMinDistance, spawnMaxDistance);
        transform.position = player.position + randomDir.normalized * dist + Vector3.up * 10f;
        transform.LookAt(player.position);
    }

    private void StartAttack()
    {
        if (player == null) return;
        state = State.Attacking;
        attackTimer = 0f;
        attackDirection = (player.position - transform.position).normalized;
        Debug.Log("[Phantom] Beginning dive attack");
    }

    private void FailAttack()
    {
        state = State.Ascending;
        // clear velocity, let ascend handled in Update/Fixed
        rb.velocity = Vector3.up * ascendSpeed;
        Debug.Log("[Phantom] Attack failed, ascending");
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Rocket"))
        {
            Debug.Log("[Phantom] trigger entered by Rocket, state="+state);
            if (state == State.Waiting)
                StartAttack();
            else if (state == State.Attacking)
                FailAttack();
        }
    }

    void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Rocket") && state == State.Waiting)
        {
            Debug.Log("[Phantom] trigger staying with Rocket, starting attack");
            StartAttack();
        }
    }

    // call this method from player weapons when hitting the phantom
    public void TakeDamage(float amount)
    {
        if (state == State.Attacking)
        {
            FailAttack();
        }
        currentHealth -= amount;
        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    private void Die()
    {
        // simple destroy, could play effects
        Destroy(gameObject);
    }
}
