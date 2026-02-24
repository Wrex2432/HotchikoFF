using System.Collections;
using UnityEngine;

public class StageMaster : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BallRaceManager ballRaceManager;
    [SerializeField] private RectTransform startMenuPanel;
    [SerializeField] private GameObject gameEndScreen;

    [Header("State Objects")]
    [SerializeField] private GameObject[] preGameObjects;
    [SerializeField] private GameObject[] gameplayObjects;
    [SerializeField] private GameObject[] endGameObjects;

    [Header("Flow Timing")]
    [SerializeField] private bool autoStartFlowOnEnable = true;
    [SerializeField] private float menuVisibleSeconds = 2f;
    [SerializeField] private float menuSlideOutSeconds = 0.65f;
    [SerializeField] private float menuSlideLeftDistance = 1400f;
    [SerializeField] private float raceStartDelayAfterMenuSlide = 3f;

    private Coroutine flowRoutine;
    private Vector2 menuStartAnchoredPosition;

    private void Awake()
    {
        if (ballRaceManager == null) ballRaceManager = FindFirstObjectByType<BallRaceManager>();
        if (startMenuPanel != null) menuStartAnchoredPosition = startMenuPanel.anchoredPosition;
    }

    private void OnEnable()
    {
        if (ballRaceManager != null) ballRaceManager.OnBallFinished += HandleBallFinished;
        if (autoStartFlowOnEnable) BeginStageFlow();
    }

    private void OnDisable()
    {
        if (ballRaceManager != null) ballRaceManager.OnBallFinished -= HandleBallFinished;
    }

    public void BeginStageFlow()
    {
        if (flowRoutine != null) StopCoroutine(flowRoutine);
        flowRoutine = StartCoroutine(StageFlowRoutine());
    }

    private IEnumerator StageFlowRoutine()
    {
        SetGroupActive(preGameObjects, true);
        SetGroupActive(gameplayObjects, false);
        SetGroupActive(endGameObjects, false);
        if (gameEndScreen != null) gameEndScreen.SetActive(false);

        if (startMenuPanel != null)
        {
            startMenuPanel.gameObject.SetActive(true);
            startMenuPanel.anchoredPosition = menuStartAnchoredPosition;
        }

        if (menuVisibleSeconds > 0f)
            yield return new WaitForSeconds(menuVisibleSeconds);

        if (startMenuPanel != null)
        {
            yield return SlideMenuOffLeft();
            startMenuPanel.gameObject.SetActive(false);
        }

        SetGroupActive(gameplayObjects, true);

        if (raceStartDelayAfterMenuSlide > 0f)
            yield return new WaitForSeconds(raceStartDelayAfterMenuSlide);

        ballRaceManager?.StartRace();
        flowRoutine = null;
    }

    private IEnumerator SlideMenuOffLeft()
    {
        float duration = Mathf.Max(0.01f, menuSlideOutSeconds);
        Vector2 from = menuStartAnchoredPosition;
        Vector2 to = from + Vector2.left * Mathf.Abs(menuSlideLeftDistance);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            startMenuPanel.anchoredPosition = Vector2.Lerp(from, to, t);
            yield return null;
        }

        startMenuPanel.anchoredPosition = to;
    }

    private void HandleBallFinished(RaceBall _)
    {
        SetGroupActive(endGameObjects, true);
        if (gameEndScreen != null) gameEndScreen.SetActive(true);
    }

    private static void SetGroupActive(GameObject[] objects, bool active)
    {
        if (objects == null) return;
        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] != null) objects[i].SetActive(active);
        }
    }
}
