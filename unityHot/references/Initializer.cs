using System;
using System.IO;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class Initializer : MonoBehaviour
{
    [Serializable]
    public class ControlConfig
    {
        public string gameType = "facechinko";
        public string location = "CINEMA_A";
        public int allowedNumberOfPlayers = 56;
        public int teamCount = 13;
        public int lobbyDurationSeconds = 30;
        public string backendWsUrl = "ws://localhost:3000";
        public int roomCodeLength = 4;
    }

    [SerializeField] private BackendConnector backend;
    [SerializeField] private BallRaceManager raceManager;

    private ControlConfig cfg;
    private float lobbyEndTime = float.PositiveInfinity;
    private bool started;
    private bool sessionReady;

    private void Start()
    {
        if (backend == null) backend = GetComponent<BackendConnector>();
        if (raceManager == null) raceManager = FindObjectOfType<BallRaceManager>();

        cfg = LoadControl();

        if (backend == null)
        {
            Debug.LogError("[Initializer] Missing BackendConnector reference/component.");
            enabled = false;
            return;
        }

        if (raceManager == null)
        {
            Debug.LogError("[Initializer] Missing BallRaceManager in scene.");
            enabled = false;
            return;
        }

        backend.SetServerUrl(cfg.backendWsUrl);
        backend.OnConnected += HandleConnected;
        backend.OnUnityCreated += HandleUnityCreated;

        Debug.Log($"[Initializer] Boot: gameType={cfg.gameType}, location={cfg.location}, players={cfg.allowedNumberOfPlayers}, teams={cfg.teamCount}, lobby={cfg.lobbyDurationSeconds}s");
        backend.Connect();
    }

    private void OnDestroy()
    {
        if (backend != null)
        {
            backend.OnConnected -= HandleConnected;
            backend.OnUnityCreated -= HandleUnityCreated;
        }
    }

    private void Update()
    {
        if (!sessionReady) return;

        if (!started && Time.time >= lobbyEndTime)
        {
            Debug.Log("[Initializer] Lobby timer elapsed -> starting match.");
            StartMatch();
            return;
        }

        if (!started && IsManualStartPressed())
        {
            Debug.Log("[Initializer] Manual start pressed (N) -> starting match.");
            StartMatch();
            return;
        }
    }

    private bool IsManualStartPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.nKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.N);
#endif
    }

    private void HandleConnected()
    {
        var requestedCode = GenerateRoomCode(cfg.roomCodeLength);

        backend.SendUnityCreate(new BackendConnector.UnityCreateMsg
        {
            gameType = cfg.gameType,
            location = cfg.location,
            teamCount = cfg.teamCount,
            allowedNumberOfPlayers = cfg.allowedNumberOfPlayers,
            requestedCode = requestedCode
        });
    }

    private void HandleUnityCreated(string code)
    {
        sessionReady = true;
        started = false;
        lobbyEndTime = Time.time + Mathf.Max(1, cfg.lobbyDurationSeconds);

        Debug.Log($"[Initializer] unityCreated -> code={code}, lobbyEndsIn={cfg.lobbyDurationSeconds}s");
        backend.SendPhase("join");
    }

    private void StartMatch()
    {
        if (!sessionReady || started) return;

        started = true;
        backend.SendPhase("active");
        raceManager.StartRace();
    }

    private ControlConfig LoadControl()
    {
        var root = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        var candidatePaths = new[]
        {
            Path.Combine(root, "control.json"),
            Path.Combine(Application.dataPath, "control.json"),
        };

        string path = null;
        for (int i = 0; i < candidatePaths.Length; i++)
        {
            if (File.Exists(candidatePaths[i]))
            {
                path = candidatePaths[i];
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            Debug.LogError($"[Initializer] Missing control.json. Tried: {string.Join(" | ", candidatePaths)}. Using defaults.");
            return new ControlConfig();
        }

        try
        {
            var json = File.ReadAllText(path);
            var parsed = JsonUtility.FromJson<ControlConfig>(json);
            return parsed ?? new ControlConfig();
        }
        catch (Exception e)
        {
            Debug.LogError($"[Initializer] Failed to read/parse control.json. Using defaults. Error: {e.Message}");
            return new ControlConfig();
        }
    }

    private string GenerateRoomCode(int length)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        var rng = new System.Random();
        var finalLen = Mathf.Max(4, length);
        var result = new char[finalLen];

        for (int i = 0; i < result.Length; i++)
            result[i] = chars[rng.Next(chars.Length)];

        return new string(result);
    }
}
