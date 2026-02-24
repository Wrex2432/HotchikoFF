using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class RaceBall : MonoBehaviour
{
    [System.Serializable]
    private struct TeamVisual
    {
        public string imageFileName;
        public Sprite sprite;
    }

    [Header("Runtime Info")]
    public int ballId;
    public string ballName;
    public bool hasFinished;
    public int finishRank = -1;

    [Header("Backend Identity")]
    public string uid;
    public string playerName;
    public int teamIndex;
    public string teamImageFileName;

    [HideInInspector] public Rigidbody2D rb;
    [HideInInspector] public Collider2D col;

    [Header("Team Visuals")]
    [Tooltip("Optional per-team sprite overrides. Index must match teamIndex.")]
    [SerializeField] private TeamVisual[] teamVisualOverrides = new TeamVisual[13];

    private SpriteRenderer spriteRenderer;

    private static readonly Color[] TeamColors = new Color[]
    {
        new(1.00f,0.65f,0.00f), new(0.00f,0.50f,0.00f), new(0.00f,0.00f,1.00f), new(0.50f,0.00f,0.50f),
        new(1.00f,0.87f,0.00f), new(0.29f,0.00f,0.51f), new(0.00f,0.66f,0.42f), new(1.00f,0.94f,0.84f),
        new(0.25f,0.41f,0.88f), new(0.96f,0.82f,0.24f), new(0.20f,0.80f,0.20f), new(0.54f,0.81f,0.94f),
        new(1.00f,0.00f,0.00f)
    };

    private static readonly string[] TeamImageNames = new string[]
    {
        "TeamDana&Greggy",
        "TeamMond&Saeid",
        "TeamJill&Alvin",
        "TeamSam&Ninya",
        "TeamYnna",
        "TeamJasper",
        "TeamJordy",
        "MEDIA",
        "STRAT",
        "HR&ADMIN",
        "FINANCE",
        "TeamMicco",
        "TeamBev"
    };

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public void Init(int id, string newUid = "", string newPlayerName = "", int newTeamIndex = 0)
    {
        ballId = id;
        uid = newUid;
        playerName = string.IsNullOrWhiteSpace(newPlayerName) ? $"Player {id + 1}" : newPlayerName;
        teamIndex = Mathf.Clamp(newTeamIndex, 0, TeamColors.Length - 1);
        teamImageFileName = GetTeamImageFileName(teamIndex);

        ballName = !string.IsNullOrWhiteSpace(uid)
            ? $"{playerName} ({uid})"
            : $"Ball {id + 1}";

        hasFinished = false;
        finishRank = -1;
        gameObject.name = ballName;

        if (spriteRenderer != null)
        {
            spriteRenderer.color = TeamColors[teamIndex];

            ApplyTeamSprite(teamIndex);
        }
    }

    private string GetTeamImageFileName(int index)
    {
        if (index >= 0 && index < teamVisualOverrides.Length)
        {
            var inspectorName = teamVisualOverrides[index].imageFileName;
            if (!string.IsNullOrWhiteSpace(inspectorName))
            {
                return inspectorName.EndsWith(".png") ? inspectorName : $"{inspectorName}.png";
            }
        }

        return TeamImageNames[index] + ".png";
    }

    private void ApplyTeamSprite(int index)
    {
        if (index >= 0 && index < teamVisualOverrides.Length)
        {
            var inspectorSprite = teamVisualOverrides[index].sprite;
            if (inspectorSprite != null)
            {
                spriteRenderer.sprite = inspectorSprite;
                return;
            }
        }

        // Fallback to Resources lookup for older prefabs.
        var maybeSprite = Resources.Load<Sprite>($"team-balls/{TeamImageNames[index]}");
        if (maybeSprite != null)
        {
            spriteRenderer.sprite = maybeSprite;
        }
    }
}
