
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

public static class FootballScheduler
{
    static FootballTeam[] opponents = new FootballTeam[]
    {
        new FootballTeam("Northport", "Grizzlies"),
        new FootballTeam("Central Tech", "Shock"),
        new FootballTeam("Valley State", "Hornets"),
        new FootballTeam("Eastern Pines", "Wolves"),
        new FootballTeam("Bayfront College", "Surge"),
        new FootballTeam("Riverside A&M", "Gators"),
        new FootballTeam("Highland University", "Stags"),
        new FootballTeam("Wheatley", "Titans") { isRival = true }
    };

    public static void GenerateSchedule()
    {
        List<FootballGame> schedule = new List<FootballGame>();
        // Every other week (odd weeks), skipping week 7 (Midterms) and week 16 (Finals)
        List<int> possibleWeeks = new List<int> { 3, 5, 7, 9, 11, 13 };

        // Shuffle opponents so matchups are random, but keep weeks sorted so that
        // gamesPlayed scales correctly with when in the season each game occurs.
        var shuffledOpponents = opponents.ToList();
        Shuffle(shuffledOpponents);

        for (int i = 0; i < possibleWeeks.Count; i++)
        {
            int gamesPlayed = Mathf.Clamp(i * 2, 0, 10);
            int oppWins = UnityEngine.Random.Range(0, gamesPlayed + 1);
            shuffledOpponents[i].wins   = oppWins;
            shuffledOpponents[i].losses = gamesPlayed - oppWins;

            schedule.Add(new FootballGame
            {
                week = possibleWeeks[i],
                opponent = shuffledOpponents[i],
                isHome = true,
                played = false
            });
        }

        string json = JsonUtility.ToJson(new FootballGameListWrapper { games = schedule });
        StatsManager.Set_String_Stat("FootballSchedule", json);
    }
    public static FootballGame GetThisWeeksGame(int currentWeek)
    {
        if (!StatsManager.String_Stat_Exists("FootballSchedule"))
        {
            FootballScheduler.GenerateSchedule(); // only if safe to call
        }

        string json = StatsManager.Get_String_Stat("FootballSchedule");
        if (string.IsNullOrEmpty(json)) return null;

        var wrapper = JsonUtility.FromJson<FootballGameListWrapper>(json);
        if (wrapper?.games == null) return null;

        return wrapper.games.Find(g => g.week == currentWeek);
    }


    public static (int wins, int losses) GetSeasonRecord(int currentWeek = 0)
    {
        if (!StatsManager.String_Stat_Exists("FootballSchedule")) return (0, 0);
        string json = StatsManager.Get_String_Stat("FootballSchedule");
        if (string.IsNullOrEmpty(json)) return (0, 0);
        var wrapper = JsonUtility.FromJson<FootballGameListWrapper>(json);
        if (wrapper?.games == null) return (0, 0);
        int wins   = wrapper.games.Count(g => g.played && g.won);
        int losses = wrapper.games.Count(g => (g.played && !g.won) ||
                                              (currentWeek > 0 && g.week < currentWeek && !g.played));
        return (wins, losses);
    }

    public static List<FootballGame> GetAllGames()
    {
        if (!StatsManager.String_Stat_Exists("FootballSchedule")) return new List<FootballGame>();
        string json = StatsManager.Get_String_Stat("FootballSchedule");
        if (string.IsNullOrEmpty(json)) return new List<FootballGame>();
        var wrapper = JsonUtility.FromJson<FootballGameListWrapper>(json);
        return wrapper?.games ?? new List<FootballGame>();
    }

    public static void SimulateUnplayedPastGames(int currentWeek)
    {
        if (!StatsManager.String_Stat_Exists("FootballSchedule")) return;
        string json = StatsManager.Get_String_Stat("FootballSchedule");
        if (string.IsNullOrEmpty(json)) return;
        var wrapper = JsonUtility.FromJson<FootballGameListWrapper>(json);
        if (wrapper?.games == null) return;

        bool changed = false;
        foreach (var game in wrapper.games)
        {
            if (game.week < currentWeek && !game.played)
            {
                game.played   = true;
                game.won      = false;
                // Deterministic scores seeded by week so values are stable across renders
                game.homeScore = (game.week * 3) % 14;
                game.awayScore = game.homeScore + 7 + (game.week % 14);
                changed = true;
            }
        }

        if (changed)
            StatsManager.Set_String_Stat("FootballSchedule", JsonUtility.ToJson(wrapper));
    }

    static void Shuffle<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int rnd = UnityEngine.Random.Range(i, list.Count);
            T temp = list[rnd];
            list[rnd] = list[i];
            list[i] = temp;
        }
    }

}

[Serializable]
public class FootballGameListWrapper
{
    public List<FootballGame> games = new List<FootballGame>();
}

public static class SemesterHelper
{
    public const int FinalsWeek = 14;
    public const int MidtermsWeek = 6;
    public const int MidtermsWarningStart = 4;
    public const int FinalsWarningStart = 5;
    public const int DaysPerWeek = 7;

    // Set this at game start from a GameSettings component or HomeCutsceneController.
    public static int SemesterYear = 2026;

    // Returns the grid-Monday anchor date for a given week.
    // All anchors are days {2,9,16,23} (or {16,23} for August) — always column 1 (grid-Monday).
    // Adding dayOffset=5 stays within the same month and always lands on column 6 (grid-Saturday).
    private static DateTime GetGridMonday(int week)
    {
        if (week <= 0) return new DateTime(SemesterYear, 8, 16);  // orientation week
        if (week == 1) return new DateTime(SemesterYear, 8, 23);  // last Aug week (first class)
        if (week >= 14) return new DateTime(SemesterYear, 12, 2); // finals
        // Weeks 2–13: Sep/Oct/Nov, 4 weeks per month starting on day 2
        int idx   = week - 2;
        int month = 9 + idx / 4;        // 9=Sep, 10=Oct, 11=Nov
        int day   = 2 + (idx % 4) * 7; // 2, 9, 16, 23
        return new DateTime(SemesterYear, month, day);
    }

    // Week 0 = orientation (Aug 16 anchor; activities start at dayOffset=4 = Aug 20).
    // Week 1 = first class week (Aug 23). Weeks 2–13: Sep/Oct/Nov. Week 14 = finals (Dec 2).
    public static DateTime GetDate(int week, int dayOffset)
    {
        return GetGridMonday(week).AddDays(dayOffset);
    }

    public static string GetDateLabel(int week, int dayOffset = 0)
    {
        var date = GetDate(week, dayOffset);
        return $"{date:MMMM} {date.Day}{OrdinalSuffix(date.Day)}, {SemesterYear}";
    }

    // Grid-based day names: column (day-1)%7 maps to these names (0=Sun…6=Sat).
    private static readonly string[] _gridDayNames =
        { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

    // Returns the grid day name for a date based on its day-of-month column, not real DayOfWeek.
    public static string GetGridDayName(DateTime date) =>
        _gridDayNames[(date.Day - 1) % 7];

    // Returns "Saturday" — just the day of the week (grid-column based).
    public static string GetDayOfWeekLabel(int week, int dayOffset = 0)
    {
        return GetGridDayName(GetDate(week, dayOffset));
    }

    // Returns "Morning" (DayPhase 0) or "Afternoon" (DayPhase 1).
    private static string GetTimeLabel(int dayPhase) =>
        dayPhase == 1 ? "Afternoon" : "Morning";

    // Returns "Saturday Morning" — day of week combined with time of day.
    public static string GetDayAndTimeLabel(int week, int dayOffset, int dayPhase) =>
        $"{GetDayOfWeekLabel(week, dayOffset)} {GetTimeLabel(dayPhase)}";

    private static string OrdinalSuffix(int day)
    {
        if (day >= 11 && day <= 13) return "th";
        return (day % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
    }

    public static string GetMonthForWeek(int week)
    {
        return GetGridMonday(week).ToString("MMMM");
    }
    
    public static string GetStudyPrompt(int currentWeek)
    {
        int weeksUntilMidterms = MidtermsWeek - currentWeek;
        int weeksUntilFinals = FinalsWeek - currentWeek;

        if (weeksUntilMidterms >= 0 && weeksUntilMidterms <= MidtermsWarningStart)
        {
            return GetUrgencyMessage(weeksUntilMidterms, "Midterms");
        }
        else if (weeksUntilFinals >= 0 && weeksUntilFinals <= FinalsWarningStart)
        {
            return GetUrgencyMessage(weeksUntilFinals, "Finals");
        }
        else
        {
            return null; // No prompt needed
        }
    }

    private static string GetUrgencyMessage(int weeksLeft, string examName)
    {
        if (weeksLeft > 2)
        {
            return GetRandomPhrase(new List<string>
            {
                $"{examName} are coming up. Start preparing!",
                $"{examName} are on the horizon. Get ready!",
                $"{examName} are approaching. Plan your study time!"
            });
        }
        else if (weeksLeft == 2)
        {
            return GetRandomPhrase(new List<string>
            {
                $"{examName} are getting closer. Hit the books!",
                $"{examName} are around the corner. Stay sharp!",
                $"Only 2 weeks left until {examName}. Let's focus!"
            });
        }
        else if (weeksLeft == 1)
        {
            return GetRandomPhrase(new List<string>
            {
                $"{examName} are next week. Time to crunch!",
                $"{examName} are just days away. Study hard!",
                $"{examName} are almost here. Finish strong!"
            });
        }
        else // weeksLeft == 0
        {
            return GetRandomPhrase(new List<string>
            {
                $"{examName} are this week. Give it your all!",
                $"{examName} have arrived. Stay focused!",
                $"It's {examName} week. You've got this!"
            });
        }
    }

    private static string GetRandomPhrase(List<string> phrases)
    {
        int index = UnityEngine.Random.Range(0, phrases.Count);
        return phrases[index];
    }
}
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
