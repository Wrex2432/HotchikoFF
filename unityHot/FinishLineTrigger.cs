using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class FinishLineTrigger : MonoBehaviour
{
    private void Reset()
    {
        Collider2D col = GetComponent<Collider2D>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        RaceBall ball = other.GetComponent<RaceBall>();
        if (ball == null) return;

        if (BallRaceManager.Instance != null)
        {
            BallRaceManager.Instance.RegisterFinish(ball);
        }
    }
}