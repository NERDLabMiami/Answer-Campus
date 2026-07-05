using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CutsceneOverlayController : MonoBehaviour
{
    [Header("Components")]
    public CanvasGroup     canvasGroup;
    public Image           backgroundImage;
    public TextMeshProUGUI dateLabel;
    public TextMeshProUGUI timeLabel;
    public TextMeshProUGUI buildingLabel;
    public TextMeshProUGUI descriptionLabel;
    [Tooltip("The RectTransform whose VerticalLayoutGroup wraps the labels — rebuilt after each SetContent call.")]
    public RectTransform   labelsContainer;
    [Tooltip("Spawned as a child of this overlay when ShowSaveMessage() is called.")]
    public GameObject      saveMessagePrefab;

    [Header("Timing")]
    public float fadeDuration = 0.4f;

    // Scene-local singleton — lives only while Home.unity is loaded.
    public static CutsceneOverlayController Instance { get; private set; }

    private Coroutine  _active;
    private GameObject _saveMessageInstance;
    private Canvas     _canvas;

    private void Awake()
    {
        Instance = this;
        _canvas  = GetComponent<Canvas>();
        HideImmediate();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Full sequence: fade in with content → hold → fade out.
    public IEnumerator Show(string date, string description, float holdDuration, Sprite bgSprite = null,
                            string time = null, string building = null)
    {
        return RunExclusive(ShowRoutine(date, description, holdDuration, bgSprite, time, building));
    }

    private IEnumerator ShowRoutine(string date, string description, float holdDuration, Sprite bgSprite,
                                    string time, string building)
    {
        if (_canvas != null) _canvas.enabled = true;
        SetContent(date, description, bgSprite, time, building);
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable   = true;
        yield return Fade(0f, 1f);
        yield return new WaitForSeconds(holdDuration);
        yield return Fade(1f, 0f);
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable   = false;
        if (_canvas != null) _canvas.enabled = false;
    }

    // Fade the overlay in (to opaque). Does NOT set any content — call SetContent first if needed.
    public IEnumerator FadeIn()
    {
        return RunExclusive(FadeInRoutine());
    }

    private IEnumerator FadeInRoutine()
    {
        if (_canvas != null) _canvas.enabled = true;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable   = true;
        yield return Fade(canvasGroup.alpha, 5f);
    }

    // Fade the overlay out (to transparent).
    public IEnumerator FadeOut()
    {
        return RunExclusive(FadeOutRoutine());
    }

    private IEnumerator FadeOutRoutine()
    {
        float startAlpha = canvasGroup != null ? canvasGroup.alpha : 1f;
        if (startAlpha > 0f)
            yield return Fade(startAlpha, 0f);
        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable   = false;
        }
        if (_canvas != null) _canvas.enabled = false;
    }

    // Runs routine as the single active coroutine, stopping any previous one first.
    private IEnumerator RunExclusive(IEnumerator routine)
    {
        if (_active != null) StopCoroutine(_active);
        _active = StartCoroutine(Wrap(routine));
        yield return _active;
    }

    private IEnumerator Wrap(IEnumerator routine)
    {
        yield return StartCoroutine(routine);
        _active = null;
    }

    // Set label content while the overlay is already visible (e.g. after FadeIn).
    public void SetContent(string date, string description, Sprite bgSprite = null,
                           string time = null, string building = null)
    {
        if (bgSprite != null && backgroundImage != null)
        {
            backgroundImage.sprite = bgSprite;
            backgroundImage.color  = Color.white;
        }

        if (dateLabel != null)
        {
            bool hasDate = !string.IsNullOrEmpty(date);
            dateLabel.gameObject.SetActive(hasDate);
            if (hasDate) dateLabel.text = date;
        }

        if (timeLabel != null)
        {
            bool hasTime = !string.IsNullOrEmpty(time);
            timeLabel.gameObject.SetActive(hasTime);
            if (hasTime) timeLabel.text = time;
        }

        if (buildingLabel != null)
        {
            bool hasBuilding = !string.IsNullOrEmpty(building);
            buildingLabel.gameObject.SetActive(hasBuilding);
            if (hasBuilding) buildingLabel.text = building;
        }

        if (descriptionLabel != null)
        {
            bool hasDescription = !string.IsNullOrEmpty(description);
            descriptionLabel.gameObject.SetActive(hasDescription);
            if (hasDescription) descriptionLabel.text = description;
        }

        if (labelsContainer != null)
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(labelsContainer);
    }

    public void ShowSaveMessage()
    {
        if (saveMessagePrefab == null || _saveMessageInstance != null) return;
        _saveMessageInstance = Instantiate(saveMessagePrefab, transform);
    }

    public void HideSaveMessage()
    {
        if (_saveMessageInstance == null) return;
        Destroy(_saveMessageInstance);
        _saveMessageInstance = null;
    }

    // Snaps to fully hidden AND clears content.
    public void HideImmediate()
    {
        if (_active != null) { StopCoroutine(_active); _active = null; }
        if (canvasGroup == null) return;
        canvasGroup.alpha          = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable   = false;
        if (_canvas != null) _canvas.enabled = false;
        if (dateLabel != null)        { dateLabel.text = "";        dateLabel.gameObject.SetActive(false); }
        if (timeLabel != null)        { timeLabel.text = "";        timeLabel.gameObject.SetActive(false); }
        if (buildingLabel != null)    { buildingLabel.text = "";    buildingLabel.gameObject.SetActive(false); }
        if (descriptionLabel != null) { descriptionLabel.text = ""; descriptionLabel.gameObject.SetActive(false); }
    }

    public void ShowImmediate()
    {
        if (_active != null) { StopCoroutine(_active); _active = null; }
        if (canvasGroup == null) return;
        if (_canvas != null) _canvas.enabled = true;
        canvasGroup.alpha          = 1f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable   = true;
    }

    private IEnumerator Fade(float from, float to)
    {
        if (canvasGroup == null) yield break;
        float t = 0f;
        canvasGroup.alpha = from;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / fadeDuration));
            yield return null;
        }
        canvasGroup.alpha = to;
    }
}
