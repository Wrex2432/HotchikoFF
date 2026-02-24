using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BallRaceManager : MonoBehaviour
{
    public static BallRaceManager Instance { get; private set; }

    [Header("Ball Setup")]
    [SerializeField] private RaceBall ballPrefab;
    [SerializeField] private int ballCount = 14;

    [Header("Single Spawn Point")]
    [SerializeField] private Transform spawnPoint;

    [Header("Spawn Pulse (outward burst)")]
    [Tooltip("Impulse applied outward from spawn point.")]
    [SerializeField] private float spawnPulseForce = 1.2f;

    [Tooltip("Adds randomness so directions don't look perfectly uniform.")]
    [SerializeField] private float spawnPulseRandomness = 0.35f;

    [Tooltip("Optional extra impulse added to every ball on release (e.g. slight upward or downward bias).")]
    [SerializeField] private Vector2 extraInitialImpulse = Vector2.zero;

    [Header("Ball-to-Ball Collision Timing")]
    [Tooltip("Balls start with NO collision with each other, then collision is enabled after this delay.")]
    [SerializeField] private float enableBallBallCollisionAfter = 0.5f;

    [Header("Power-Up: Machine Nudge (Spacebar)")]
    [SerializeField] private KeyCode nudgeKey = KeyCode.Space;

    [Tooltip("Instant velocity kick amount (machine jolt feel).")]
    [SerializeField] private float nudgeForce = 1.0f;

    [Tooltip("Base direction of the machine nudge. Example (1,0)=right, (-1,0)=left.")]
    [SerializeField] private Vector2 nudgeDirection = new Vector2(1f, 0f);

    [Tooltip("Small random spread so balls don't all get identical velocity.")]
    [Range(0f, 1f)]
    [SerializeField] private float nudgeRandomness = 0.1f;

    [Tooltip("Cooldown between nudges in seconds.")]
    [SerializeField] private float nudgeCooldown = 1.0f;

    [Tooltip("Optional limit. Set -1 for unlimited uses.")]
    [SerializeField] private int maxNudgeUses = -1;

    [Tooltip("Optional little spin added on nudge for more chaotic machine-bump feel.")]
    [SerializeField] private float nudgeSpinAmount = 40f;

    [Header("Race State")]
    [SerializeField] private bool autoStartOnPlay = true;

    private readonly List<RaceBall> spawnedBalls = new List<RaceBall>();
    private readonly List<RaceBall> finishOrder = new List<RaceBall>();

    public bool RaceStarted { get; private set; }

    private Coroutine collisionRoutine;
    private float nextNudgeTime = 0f;
    private int nudgeUses = 0;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        SpawnBalls();

        if (autoStartOnPlay)
            StartRace();
    }

    private void Update()
    {
        if (!RaceStarted) return;

        if (Input.GetKeyDown(nudgeKey))
        {
            TryUseGlobalNudge();
        }
    }

    public void SpawnBalls()
    {
        ClearBalls();

        if (spawnPoint == null)
        {
            Debug.LogError("Spawn Point is not assigned.");
            return;
        }

        for (int i = 0; i < ballCount; i++)
        {
            RaceBall ball = Instantiate(ballPrefab, spawnPoint.position, Quaternion.identity);
            ball.Init(i);

            // Freeze until StartRace so all begin together
            ball.rb.simulated = false;

            // Reset motion
#if UNITY_6000_0_OR_NEWER
            ball.rb.linearVelocity = Vector2.zero;
#else
            ball.rb.velocity = Vector2.zero;
#endif
            ball.rb.angularVelocity = 0f;

            spawnedBalls.Add(ball);
        }

        // Default collisions ON before race starts (we'll ignore them on StartRace)
        SetBallToBallCollisionIgnored(false);

        finishOrder.Clear();
        RaceStarted = false;

        // Reset power-up state
        nextNudgeTime = 0f;
        nudgeUses = 0;
    }

    public void StartRace()
    {
        if (spawnedBalls.Count == 0) return;

        RaceStarted = true;
        finishOrder.Clear();

        // Start with NO ball-to-ball collision for clean burst from one point
        SetBallToBallCollisionIgnored(true);

        // Release all balls + apply radial pulse
        for (int i = 0; i < spawnedBalls.Count; i++)
        {
            RaceBall ball = spawnedBalls[i];
            if (ball == null) continue;

            ball.hasFinished = false;
            ball.finishRank = -1;
            ball.rb.simulated = true;

            // Evenly distribute directions around a circle + randomness
            float angle01 = (float)i / spawnedBalls.Count;
            float angleDeg = angle01 * 360f + Random.Range(-20f, 20f) * spawnPulseRandomness;
            float angleRad = angleDeg * Mathf.Deg2Rad;

            Vector2 dir = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)).normalized;

            float randomForceMul = 1f + Random.Range(-spawnPulseRandomness, spawnPulseRandomness);
            float finalForce = Mathf.Max(0f, spawnPulseForce * randomForceMul);

            ball.rb.AddForce(dir * finalForce, ForceMode2D.Impulse);

            if (extraInitialImpulse != Vector2.zero)
                ball.rb.AddForce(extraInitialImpulse, ForceMode2D.Impulse);
        }

        // After delay, ENABLE ball-to-ball collisions
        if (collisionRoutine != null)
            StopCoroutine(collisionRoutine);

        collisionRoutine = StartCoroutine(EnableBallToBallCollisionAfterDelay(enableBallBallCollisionAfter));
    }

    private void TryUseGlobalNudge()
    {
        if (Time.time < nextNudgeTime) return;
        if (maxNudgeUses >= 0 && nudgeUses >= maxNudgeUses) return;

        ApplyGlobalNudge();

        nudgeUses++;
        nextNudgeTime = Time.time + nudgeCooldown;

        Debug.Log($"Machine Nudge used ({(maxNudgeUses < 0 ? nudgeUses.ToString() : $"{nudgeUses}/{maxNudgeUses}")})");
    }

    // MACHINE-LIKE JOLT: instant velocity kick (not AddForce)
    private void ApplyGlobalNudge()
    {
        for (int i = 0; i < spawnedBalls.Count; i++)
        {
            RaceBall ball = spawnedBalls[i];
            if (ball == null) continue;
            if (ball.hasFinished) continue;
            if (!ball.rb.simulated) continue;

            // Random LEFT or RIGHT per ball (chaotic machine shake)
            float lr = Random.value < 0.5f ? -1f : 1f;
            Vector2 baseDir = new Vector2(lr, 0f);

            // Add extra randomness per ball (slight vertical / angle variation)
            Vector2 dir = baseDir;
            if (nudgeRandomness > 0f)
            {
                Vector2 randomOffset = Random.insideUnitCircle * nudgeRandomness;
                dir = baseDir + randomOffset;

                if (dir.sqrMagnitude < 0.0001f)
                    dir = baseDir;
                else
                    dir.Normalize();
            }

            // Slight force variance per ball
            float kick = nudgeForce * Random.Range(0.85f, 1.15f);

            // Instant velocity kick (machine jolt feel)
#if UNITY_6000_0_OR_NEWER
            ball.rb.linearVelocity += dir * kick;
#else
        ball.rb.velocity += dir * kick;
#endif

            // Optional spin for extra chaos
            if (nudgeSpinAmount > 0f)
                ball.rb.angularVelocity += Random.Range(-nudgeSpinAmount, nudgeSpinAmount);
        }
    }

    private void SetBallToBallCollisionIgnored(bool ignored)
    {
        for (int i = 0; i < spawnedBalls.Count; i++)
        {
            if (spawnedBalls[i] == null || spawnedBalls[i].col == null) continue;

            for (int j = i + 1; j < spawnedBalls.Count; j++)
            {
                if (spawnedBalls[j] == null || spawnedBalls[j].col == null) continue;

                Physics2D.IgnoreCollision(spawnedBalls[i].col, spawnedBalls[j].col, ignored);
            }
        }
    }

    private IEnumerator EnableBallToBallCollisionAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        SetBallToBallCollisionIgnored(false);
        Debug.Log("Ball-to-ball collisions ENABLED.");
    }

    public void RegisterFinish(RaceBall ball)
    {
        if (!RaceStarted || ball == null || ball.hasFinished) return;

        ball.hasFinished = true;
        finishOrder.Add(ball);
        ball.finishRank = finishOrder.Count;

        // Freeze after finish
#if UNITY_6000_0_OR_NEWER
        ball.rb.linearVelocity = Vector2.zero;
#else
        ball.rb.velocity = Vector2.zero;
#endif
        ball.rb.angularVelocity = 0f;
        ball.rb.simulated = false;

        Debug.Log($"{ball.ballName} finished #{ball.finishRank}");

        if (finishOrder.Count == spawnedBalls.Count)
        {
            Debug.Log("Race complete!");
            PrintResults();
        }
    }

    public List<RaceBall> GetResults()
    {
        return new List<RaceBall>(finishOrder);
    }

    private void PrintResults()
    {
        for (int i = 0; i < finishOrder.Count; i++)
        {
            Debug.Log($"{i + 1}. {finishOrder[i].ballName}");
        }
    }

    private void ClearBalls()
    {
        if (collisionRoutine != null)
        {
            StopCoroutine(collisionRoutine);
            collisionRoutine = null;
        }

        foreach (var b in spawnedBalls)
        {
            if (b != null)
                Destroy(b.gameObject);
        }

        spawnedBalls.Clear();
        finishOrder.Clear();
        RaceStarted = false;
    }
}