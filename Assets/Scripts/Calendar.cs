
using System.Collections.Generic;
using UnityEngine;
using VNEngine;
using TMPro;
using System;
using UnityEngine.UI;
using FMODUnity;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine.SceneManagement;

[Serializable]
public struct TimeImage
{
    public SpriteRenderer image;
    public Image uiImage;
    public Sprite spriteDay;
    public Sprite spriteNight;
    public enum timeOfDay {DAY, NIGHT}
}
public class Calendar : MonoBehaviour
{
    public TimeImage[] timeImages;

    [Header("Background")]
    [Tooltip("Direct reference to the room background Image. Bypasses the struct array, which can silently drop object references at runtime.")]
    public Image  backgroundImage;
    public Sprite backgroundSpriteDay;
    public Sprite backgroundSpriteNight;

    public TextMeshProUGUI month;
    public TextMeshProUGUI studyPrompt;
    public Transform calendarGrid;
    public GameObject checkmark;
    public int week; 
    public EventReference ambientFMODEventReference;
    public EventReference musicFMODEventReference;
    public Location finalExamLocation;
    public Characters characters;
    public GameObject finalReport;
    public TextMeshProUGUI finalText;
    public Image finalCharacterImage;

    private bool isDay = true;
    private static bool _isRedirecting;
    // Start is called before the first frame update
    


    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // We have arrived somewhere. Allow future redirects.
        _isRedirecting = false;
    }

    void Start()
    {
        if (!ambientFMODEventReference.IsNull)
        {
            if (FMODAudioManager.Instance != null)
            {
                FMODAudioManager.Instance.PlayAmbient(ambientFMODEventReference);
            }
        }

        if (!musicFMODEventReference.IsNull)
        {
            if (FMODAudioManager.Instance != null)
            {
                FMODAudioManager.Instance.PlayMusic(musicFMODEventReference);
            }
        }
        GameEvents.EnsureSemesterRequiredEventsRegistered();

        if(StatsManager.Numbered_Stat_Exists("Week"))
        {
            week = (int)StatsManager.Get_Numbered_Stat("Week");
            Debug.Log($"It's week {week}");

            if (week <= 0 && !StatsManager.String_Stat_Exists("FootballSchedule"))
            {
                Debug.Log("New Game, generating football schedule and exam events");
                FootballScheduler.GenerateSchedule();
            }
            PopulateCalendarCheckmarks();
            TryRedirectToRequiredEvent();
        }

        isDay = StatsManager.Get_Numbered_Stat("DayPhase") < 1f;
        ApplyTimeOfDayVisuals();
        string json = StatsManager.Get_String_Stat("FootballSchedule");
        Debug.Log($"[Schedule JSON] {json}");
        month.text = SemesterHelper.GetMonthForWeek(week);
        string prompt = SemesterHelper.GetStudyPrompt(week);
        if (!string.IsNullOrEmpty(prompt))
        {
            studyPrompt.text = prompt;
        }

        foreach (var evt in GameEvents.GetWeekPreview(week))
            Debug.Log($"[Calendar Preview] Week {evt.week}: {evt.type} - {evt.label} @ {evt.location}");

        if (week >= SemesterHelper.FinalsWeek + 1)
        {
            EndSemester();
        }
    }
    private void PopulateCalendarCheckmarks()
    {
        for (int i = calendarGrid.childCount - 1; i >= 0; i--)
            Destroy(calendarGrid.GetChild(i).gameObject);

        int dayOffset = StatsManager.Numbered_Stat_Exists("DayOffset")
            ? (int)StatsManager.Get_Numbered_Stat("DayOffset")
            : 0;

        var currentDate = SemesterHelper.GetDate(week, dayOffset);
        int currentDay  = currentDate.Day;

        for (int position = 1; position <= 35; position++)
        {
            if (position < currentDay)
            {
                Instantiate(checkmark, calendarGrid, false);
            }
            else
            {
                var placeholder = new GameObject("Placeholder", typeof(RectTransform));
                placeholder.transform.SetParent(calendarGrid, false);
            }
        }
    }

    private void TryRedirectToRequiredEvent()
    {
        if (_isRedirecting) return;

        int week = (int)StatsManager.Get_Numbered_Stat("Week");
        var due = GameEvents.GetWeekPreview(week);

        // Priority: Finals, Midterms, Football
        EventInfo chosen = default;
        bool hasChosen = false;

        int idx = due.FindIndex(e => e.id == GameEvents.FinalsEventId);
        if (idx >= 0 && !GameEvents.IsCustomEventCompleted(GameEvents.FinalsEventId))
            { chosen = due[idx]; hasChosen = true; }

        if (!hasChosen)
        {
            idx = due.FindIndex(e => e.id == GameEvents.MidtermsEventId);
            if (idx >= 0 && !GameEvents.IsCustomEventCompleted(GameEvents.MidtermsEventId))
                { chosen = due[idx]; hasChosen = true; }
        }
        
        if (hasChosen && !string.IsNullOrEmpty(chosen.location))
        {
            _isRedirecting = true;
            HomeCutsceneController.Instance?.QueueRequiredRedirect(chosen.location);
        }
    }


    public void ToggleDaytime()
    {
        isDay = !isDay;
        StatsManager.Set_Numbered_Stat("DayPhase", isDay ? 0f : 1f);
        ApplyTimeOfDayVisuals();
    }

    // Re-reads DayPhase and re-applies sprites.
    // Call this when DayPhase changes after Start() has already run.
    public void SyncTimeOfDay()
    {
        isDay = StatsManager.Get_Numbered_Stat("DayPhase") < 1f;
        ApplyTimeOfDayVisuals();
    }

    // Re-reads Week and DayOffset and refreshes the calendar grid and month label.
    // Call this when DayOffset is set after Start() has already run (e.g. game-week Saturday).
    public void SyncDate()
    {
        if (StatsManager.Numbered_Stat_Exists("Week"))
            week = (int)StatsManager.Get_Numbered_Stat("Week");
        month.text = SemesterHelper.GetMonthForWeek(week);
        PopulateCalendarCheckmarks();
    }

    private void ApplyTimeOfDayVisuals()
    {
        for (int i = 0; i < timeImages.Length; i++)
        {
            if (timeImages[i].uiImage != null)
                timeImages[i].uiImage.sprite = isDay ? timeImages[i].spriteDay : timeImages[i].spriteNight;

            if (timeImages[i].image != null)
                timeImages[i].image.sprite = isDay ? timeImages[i].spriteDay : timeImages[i].spriteNight;
        }

        // Direct background control — separate from the struct array to avoid serialization null issues.
        if (backgroundImage != null)
            backgroundImage.sprite = isDay ? backgroundSpriteDay : backgroundSpriteNight;
    }

    private static Character ParseBestFriendEnum(string rawValue)
    {
        // Normalize: lowercase, remove non-alphanumeric characters, then PascalCase it
        string cleaned = Regex.Replace(rawValue, @"[^a-zA-Z0-9]", ""); // Remove symbols
        cleaned = char.ToUpper(cleaned[0]) + cleaned.Substring(1).ToLower(); // Simple PascalCase

        if (Enum.TryParse(typeof(Character), cleaned, out var result))
        {
            return (Character)result;
        }

        Debug.LogWarning($"Could not parse '{rawValue}' into Character enum. Defaulting.");
        return Character.NONE; // Replace with a safe default in your enum
    }

    void EndSemester()
    {
        string bestFriendRaw = StatsManager.Get_String_Stat("Best Friend");
        Character bestFriendEnum = ParseBestFriendEnum(bestFriendRaw);
        foreach (var profile in characters.profiles)
        {
            if (profile.character == bestFriendEnum)
            {
                finalCharacterImage.sprite = profile.pictureLarge;
            }
        }
        string json = StatsManager.Get_String_Stat("FootballSchedule");
        string player_name = StatsManager.Get_String_Stat("Player Name");
        var schedule = JsonUtility.FromJson<FootballGameListWrapper>(json);
        int wins = schedule.games.Count(g => g.played && g.won);
        int losses = schedule.games.Count(g => g.played && !g.won);
        float gpa = StatsManager.Get_Numbered_Stat("Grades");
        string finalNarrative = $"{player_name}! Can you believe the semester is over already? You've been an incredible friend. ";
        finalNarrative += "Can't wait to see what next semester brings.";
        finalText.text = finalNarrative;
        finalReport.SetActive(true);
    }
    
}
