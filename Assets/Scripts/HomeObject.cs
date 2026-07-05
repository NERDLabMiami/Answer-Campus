using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VNEngine;

public class HomeObject : MonoBehaviour
{
    public enum Condition
    {
        Always,
        Achievement,
        FootballGameThisWeek,
        DayTimeOnly,
        FootballGameAfternoon,
        ClassAttended,
    }

    [Header("Condition")]
    public Condition condition;
    [Tooltip("Achievement name (without 'Achievement_' prefix). Used when condition = Achievement.")]
    public string achievementKey;
    [Tooltip("When set to anything other than Always, controls button interactability independently of visibility. Use on objects that are always visible once earned but only interactable under specific conditions (e.g. Mascot bobblehead).")]
    public Condition interactionCondition = Condition.Always;

    [Header("Display")]
    [Tooltip("Human-readable name shown in the cutscene label when this object is first revealed.")]
    public string displayName;

    [Header("Unavailable State")]
    [Tooltip("When true the object stays visible but is grayed out. When false it is hidden.")]
    public bool disableRatherThanHide;
    public Button interactionButton;
    public CanvasGroup canvasGroup;
    [Range(0f, 1f)] public float dimmedAlpha = 0.4f;

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
    }

    public System.Collections.IEnumerator RevealAnimate(float duration = 0.5f)
    {
        // Use a CanvasGroup on this exact GameObject so the fade only affects this object.
        // canvasGroup may point to a shared parent group; GetComponent finds one local to this GO.
        CanvasGroup cg = gameObject.GetComponent<CanvasGroup>();
        if (cg == null)
            cg = gameObject.AddComponent<CanvasGroup>();

        cg.alpha = 0f;
        gameObject.SetActive(true);

        if (interactionButton != null)
            interactionButton.interactable = false;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            cg.alpha = Mathf.Clamp01(t / duration);
            yield return null;
        }
        cg.alpha = 1f;
    }

    public void Refresh()
    {
        bool available = Evaluate(condition);

        // Achievement objects must stay fully hidden until earned — player shouldn't know they exist
        if (condition == Condition.Achievement && !available)
        {
            gameObject.SetActive(false);
            return;
        }

        // interactionCondition defaults to Always, which preserves the pre-existing behaviour:
        // button interactable iff the object itself is available.
        bool canInteract = interactionCondition == Condition.Always
            ? available
            : Evaluate(interactionCondition);

        if (disableRatherThanHide)
        {
            gameObject.SetActive(true);
            if (interactionButton != null)
            {
                interactionButton.gameObject.SetActive(true);
                interactionButton.interactable = canInteract;
            }
            if (canvasGroup != null)
                canvasGroup.alpha = available ? 1f : dimmedAlpha;
        }
        else
        {
            gameObject.SetActive(available);
            if (interactionButton != null)
            {
                interactionButton.gameObject.SetActive(available);
                interactionButton.interactable = canInteract;
            }
        }
    }

    private bool Evaluate(Condition c)
    {
        switch (c)
        {
            case Condition.Achievement:
                bool avail = StatsManager.Get_Boolean_Stat("Achievement_" + achievementKey);
                Debug.Log($"Achievement {achievementKey}: {avail}");
                return avail;
            case Condition.FootballGameThisWeek:
            {
                int week = (int)StatsManager.Get_Numbered_Stat("Week");
                FootballGame game = FootballScheduler.GetThisWeeksGame(week);
                return game != null && !game.played;
            }
            case Condition.FootballGameAfternoon:
            {
                int week = (int)StatsManager.Get_Numbered_Stat("Week");
                FootballGame game = FootballScheduler.GetThisWeeksGame(week);
                return game != null && !game.played
                    && StatsManager.Get_Numbered_Stat("DayPhase") >= 1f;
            }
            case Condition.DayTimeOnly:
                return StatsManager.Get_Numbered_Stat("DayPhase") < 1f;
            case Condition.ClassAttended:
                return StatsManager.Get_Boolean_Stat("ClassAttendedThisWeek");
            default:
                return true;
        }
    }
}
