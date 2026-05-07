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
        FootballGameThisWeek
    }

    [Header("Condition")]
    public Condition condition;
    [Tooltip("Achievement name (without 'Achievement_' prefix). Used when condition = Achievement.")]
    public string achievementKey;

    [Header("Unavailable State")]
    [Tooltip("When true the object stays visible but is grayed out. When false it is hidden.")]
    public bool disableRatherThanHide;
    public Button interactionButton;
    public CanvasGroup canvasGroup;
    [Range(0f, 1f)] public float dimmedAlpha = 0.4f;

    public void Refresh()
    {
        bool available = EvaluateCondition();

        if (disableRatherThanHide)
        {
            gameObject.SetActive(true);
            if (interactionButton != null)
                interactionButton.interactable = available;
            if (canvasGroup != null)
                canvasGroup.alpha = available ? 1f : dimmedAlpha;
        }
        else
        {
            gameObject.SetActive(available);
        }
    }

    private bool EvaluateCondition()
    {
        switch (condition)
        {
            case Condition.Achievement:
                bool avail = StatsManager.Get_Boolean_Stat("Achievement_" + achievementKey);
                Debug.Log($"Achievement {achievementKey}: {avail}");
                return avail;
            case Condition.FootballGameThisWeek:
                int week = (int)StatsManager.Get_Numbered_Stat("Week");
                FootballGame game = FootballScheduler.GetThisWeeksGame(week);
                return game != null && !game.played;
            default:
                return true;
        }
    }
}
