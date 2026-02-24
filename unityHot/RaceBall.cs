using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class RaceBall : MonoBehaviour
{
    [Header("Runtime Info")]
    public int ballId;
    public string ballName;
    public bool hasFinished;
    public int finishRank = -1;

    [HideInInspector] public Rigidbody2D rb;
    [HideInInspector] public Collider2D col;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
    }

    public void Init(int id)
    {
        ballId = id;
        ballName = $"Ball {id + 1}";
        hasFinished = false;
        finishRank = -1;
        gameObject.name = ballName;
    }
}