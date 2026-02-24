using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BallRaceManager : MonoBehaviour
{
    public static BallRaceManager Instance { get; private set; }

    [Header("Backend")]
    [SerializeField] private BackendConnector backend;
    [SerializeField] private bool listenToBackendPlayers = true;
    [SerializeField] private bool verboseLogs = true;
    [SerializeField] private bool keepBallWhenPlayerLeaves = true;

    [Header("Ball Setup")]
    [SerializeField] private RaceBall ballPrefab;
    [SerializeField] private int offlineBallCount = 14;

    [Header("Single Spawn Point")]
    [SerializeField] private Transform spawnPoint;

    [Header("Spawn Pulse (outward burst)")]
    [SerializeField] private float spawnPulseForce = 1.2f;
    [SerializeField] private float spawnPulseRandomness = 0.35f;
    [SerializeField] private Vector2 extraInitialImpulse = Vector2.zero;

    [Header("Ball-to-Ball Collision Timing")]
    [SerializeField] private float enableBallBallCollisionAfter = 0.5f;

    [Header("Team Power (debug in editor)")]
    [SerializeField] private KeyCode debugGrantPowerKey = KeyCode.P;
    [SerializeField] private int debugPowerTeamIndex = 0;

    private readonly List<RaceBall> spawnedBalls = new();
    private readonly List<RaceBall> finishOrder = new();
    private readonly Dictionary<string, RaceBall> ballByUid = new();

    public bool RaceStarted { get; private set; }

    private Coroutine collisionRoutine;
    private bool resultSent;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (backend == null) backend = GetComponent<BackendConnector>();
    }

    private void OnEnable()
    {
        if (backend == null) return;
        backend.OnPlayerChanged += HandlePlayerChanged;
        backend.OnPlayerLeft += HandlePlayerLeft;
        backend.OnGameResult += HandleGameResult;
        backend.OnUnityCreated += HandleUnityCreated;
        backend.OnPaused += HandlePaused;
        backend.OnEnded += HandleEnded;
    }

    private void OnDisable()
    {
        if (backend == null) return;
        backend.OnPlayerChanged -= HandlePlayerChanged;
        backend.OnPlayerLeft -= HandlePlayerLeft;
        backend.OnGameResult -= HandleGameResult;
        backend.OnUnityCreated -= HandleUnityCreated;
        backend.OnPaused -= HandlePaused;
        backend.OnEnded -= HandleEnded;
    }

    private void Start()
    {
        if (!listenToBackendPlayers)
        {
            SpawnOfflineBalls();
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(debugGrantPowerKey) && backend != null)
        {
            backend.SendUnityMsg(new Dictionary<string, object>
            {
                { "kind", "powerReady" },
                { "teamIndex", Mathf.Max(0, debugPowerTeamIndex) },
                { "powerId", $"dbg_{System.DateTime.UtcNow:HHmmssfff}" },
            });
        }
    }

    private void HandleUnityCreated(string _)
    {
        ClearBalls();
        RaceStarted = false;
        resultSent = false;
    }

    private void HandlePlayerChanged(BackendConnector.FacechinkoPlayerMsg msg)
    {
        if (!listenToBackendPlayers || msg?.player == null) return;

        var p = msg.player;
        if (string.IsNullOrWhiteSpace(p.uid)) return;

        if (ballByUid.TryGetValue(p.uid, out var existing) && existing != null)
        {
            existing.Init(existing.ballId, p.uid, p.name, p.teamIndex);
            return;
        }

        if (spawnPoint == null || ballPrefab == null)
        {
            Debug.LogError("[Facechinko] Missing spawn point or ball prefab.");
            return;
        }

        var ball = Instantiate(ballPrefab, spawnPoint.position, Quaternion.identity);
        ball.Init(spawnedBalls.Count, p.uid, p.name, p.teamIndex);
        ball.rb.simulated = false;
#if UNITY_6000_0_OR_NEWER
        ball.rb.linearVelocity = Vector2.zero;
#else
        ball.rb.velocity = Vector2.zero;
#endif
        ball.rb.angularVelocity = 0f;

        spawnedBalls.Add(ball);
        ballByUid[p.uid] = ball;

        if (verboseLogs) Debug.Log($"[Facechinko] Spawned {p.name} on team {p.teamIndex}.");
    }



    private void HandlePlayerLeft(BackendConnector.FacechinkoPlayerMsg msg)
    {
        if (!listenToBackendPlayers || msg?.player == null) return;

        var p = msg.player;
        if (string.IsNullOrWhiteSpace(p.uid)) return;

        if (keepBallWhenPlayerLeaves)
        {
            if (verboseLogs) Debug.Log($"[Facechinko] Player left ({p.uid}); keeping spawned ball.");
            return;
        }

        if (!ballByUid.TryGetValue(p.uid, out var ball) || ball == null) return;

        spawnedBalls.Remove(ball);
        finishOrder.Remove(ball);
        ballByUid.Remove(p.uid);
        Destroy(ball.gameObject);

        if (verboseLogs) Debug.Log($"[Facechinko] Removed ball for player {p.uid}.");
    }

    private void HandlePaused(string reason)
    {
        RaceStarted = false;
        if (verboseLogs) Debug.Log($"[Facechinko] Paused: {reason}");
    }

    private void HandleEnded()
    {
        RaceStarted = false;
        resultSent = true;
        if (verboseLogs) Debug.Log("[Facechinko] Session ended.");
    }
    private void HandleGameResult(BackendConnector.FacechinkoGameResultMsg msg)
    {
        RaceStarted = false;
        if (verboseLogs)
            Debug.Log($"[Facechinko] Backend gameResult winnerTeamIndex={msg.winningTeamIndex}");
    }

    public void StartRace()
    {
        if (spawnedBalls.Count == 0) return;

        resultSent = false;
        RaceStarted = true;
        finishOrder.Clear();

        SetBallToBallCollisionIgnored(true);

        for (int i = 0; i < spawnedBalls.Count; i++)
        {
            RaceBall ball = spawnedBalls[i];
            if (ball == null) continue;

            ball.hasFinished = false;
            ball.finishRank = -1;
            ball.rb.simulated = true;

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

        if (collisionRoutine != null)
            StopCoroutine(collisionRoutine);

        collisionRoutine = StartCoroutine(EnableBallToBallCollisionAfterDelay(enableBallBallCollisionAfter));
    }

    public void NotifyTeamPowerPickup(int teamIndex, string powerId = null)
    {
        if (backend == null) return;

        backend.SendUnityMsg(new Dictionary<string, object>
        {
            { "kind", "powerReady" },
            { "teamIndex", teamIndex },
            { "powerId", string.IsNullOrWhiteSpace(powerId) ? $"pickup_{teamIndex}_{Time.frameCount}" : powerId },
        });

        if (verboseLogs)
            Debug.Log($"[Facechinko] Power pickup granted for team {teamIndex}.");
    }

    public void RegisterFinish(RaceBall ball)
    {
        if (!RaceStarted || ball == null || ball.hasFinished) return;

        ball.hasFinished = true;
        finishOrder.Add(ball);
        ball.finishRank = finishOrder.Count;

#if UNITY_6000_0_OR_NEWER
        ball.rb.linearVelocity = Vector2.zero;
#else
        ball.rb.velocity = Vector2.zero;
#endif
        ball.rb.angularVelocity = 0f;
        ball.rb.simulated = false;

        if (verboseLogs)
            Debug.Log($"{ball.ballName} finished #{ball.finishRank}");

        if (!resultSent)
        {
            resultSent = true;
            RaceStarted = false;

            backend?.SendGameOver(ball.teamIndex, ball.uid);

            if (verboseLogs)
                Debug.Log($"[Facechinko] Winner team={ball.teamIndex}, uid={ball.uid}");
        }
    }

    public List<RaceBall> GetResults() => new(finishOrder);

    private void SpawnOfflineBalls()
    {
        ClearBalls();

        if (spawnPoint == null)
        {
            Debug.LogError("Spawn Point is not assigned.");
            return;
        }

        for (int i = 0; i < offlineBallCount; i++)
        {
            RaceBall ball = Instantiate(ballPrefab, spawnPoint.position, Quaternion.identity);
            ball.Init(i, $"offline_{i + 1}", $"Offline {i + 1}", i % 13);
            ball.rb.simulated = false;
#if UNITY_6000_0_OR_NEWER
            ball.rb.linearVelocity = Vector2.zero;
#else
            ball.rb.velocity = Vector2.zero;
#endif
            ball.rb.angularVelocity = 0f;
            spawnedBalls.Add(ball);
            ballByUid[ball.uid] = ball;
        }

        SetBallToBallCollisionIgnored(false);
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
    }

    private void ClearBalls()
    {
        if (collisionRoutine != null)
        {
            StopCoroutine(collisionRoutine);
            collisionRoutine = null;
        }

        foreach (var b in spawnedBalls.Where(b => b != null))
        {
            Destroy(b.gameObject);
        }

        spawnedBalls.Clear();
        ballByUid.Clear();
        finishOrder.Clear();
        RaceStarted = false;
        resultSent = false;
    }
}
