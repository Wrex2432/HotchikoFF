using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class FinishLineTrigger : MonoBehaviour
{
    [SerializeField] private bool lockAfterFirstFinisher = true;
    private bool hasWinner;

    private void OnEnable()
    {
        hasWinner = false;
    }

    private void Reset()
    {
        Collider2D col = GetComponent<Collider2D>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (lockAfterFirstFinisher && hasWinner) return;

        RaceBall ball = other.GetComponent<RaceBall>();
        if (ball == null) return;

        if (BallRaceManager.Instance != null)
        {
            BallRaceManager.Instance.RegisterFinish(ball);
            if (lockAfterFirstFinisher)
                hasWinner = true;
        }
    }
}
