using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VNEngine;

public class HomeCutsceneController : MonoBehaviour
{
    [Header("Game Settings")]
    [Tooltip("The in-game semester year shown in all date labels (e.g. 2026).")]
    public int semesterYear = 2026;

    [Header("Overlay")]
    [Tooltip("Prefab spawned at runtime. Remove any placed CutsceneOverlay from the Home scene.")]
    public CutsceneOverlayController overlayPrefab;
    public float holdDuration = 5f;
    public bool  animateAllObjectsOnFirstDay = true;

    // Runtime instance — spawned from overlayPrefab in Awake
    private CutsceneOverlayController overlay;

    [Header("First Day")]
    [Tooltip("Optional wide-angle room sprite shown briefly after 'Move-In Day' fades out, before unpacking begins.")]
    public Sprite arrivalSprite;
    [Tooltip("Calendar component — SyncTimeOfDay() is called to apply day/night sprites.")]
    public Calendar calendar;
    [Tooltip("Button shown after unpacking. Player taps it to trigger the night transition and load orientation.")]
    public Button orientationButton;
    [Tooltip("How long to hold on the night room before fading to black for the Orientation label.")]
    public float nightViewDuration = 2f;
    [Tooltip("Scene loaded after the orientation overlay.")]
    public string orientationScene = "Student Center";
    [Tooltip("How long each home object takes to fade in during the first-day reveal.")]
    public float revealDuration = .2f;
    [Tooltip("Pause between each object fading in during the first-day reveal.")]
    public float pauseBetweenReveals = 3f;

    [Header("Morning Alarm")]
    [Tooltip("CanvasGroup wrapping the full alarm/morning modal UI.")]
    public CanvasGroup alarmModal;
    [Tooltip("Button that sends the player to class.")]
    public Button goToClassButton;
    [Tooltip("Button that dismisses the alarm and keeps the player home.")]
    public Button notYetButton;
    [Tooltip("Button that sends the player to the football game (shown on game days only).")]
    public Button goToGameButton;
    [Tooltip("Button that skips the game prompt and falls through to the class modal.")]
    public Button skipGameButton;
    [Tooltip("Scene name for the class location.")]
    public string classScene    = "Lecture Hall";
    [Tooltip("Scene name for the football game.")]
    public string footballScene = "Football Stadium";

    [Header("Alarm Clock")]
    [Tooltip("Animator on the Alarm Clock GameObject. Uses triggers: On (start ringing), Off (stop ringing), GameDay (reveal mascot).")]
    public Animator alarmClockAnimator;

    [Header("Game Day Modal")]
    public CanvasGroup gameDayModal;
    public TMPro.TMP_Text gameDayTitleText;
    public TMPro.TMP_Text gameDayDescriptionText;
    public Button goToCheerButton;
    public Button dismissGameDayButton;
    public string cheerScene = "Cheer";

    [Header("References")]
    public Home  home;
    public Image backgroundImage;
    [Tooltip("LocationData asset for the Home scene — supplies the building name shown on the arrival overlay.")]
    public LocationData homeLocationData;
    [Tooltip("Optional: the single CanvasGroup that is the parent of all HomeObjects.")]
    public CanvasGroup homeObjectsGroup;
    [Tooltip("The desk lamp Image — same component wired in Calendar.timeImages.")]
    public Image  lampImage;
    [Tooltip("The lamp-off sprite (same as spriteDay on the lamp timeImages entry).")]
    public Sprite lampOffSprite;

    // ── Stats constants ────────────────────────────────────────────────────────
    private const string STAT_HOME_INITIALIZED    = "HomeInitialized";
    private const string STAT_HOMECOMING_SHOWN    = "HomecomingShown";
    private const string STAT_MIDTERMS_SHOWN      = "HomeMidtermsShown";
    private const string STAT_FINALS_SHOWN        = "HomeFinalsShown";
    private const string STAT_CLASSES_START_SHOWN = "HomeClassesStartShown";
    private const string STAT_SKIPPED_CLASS       = "SkippedClass";
    private const string STAT_CLASS_ATTENDED      = "ClassAttendedThisWeek";
    private const string REVEAL_PREFIX            = "HomeObject_Revealed_";
    private const int    HOMECOMING_WEEK          = 9;

    // ── PlayerPrefs checkpoint prefix ──────────────────────────────────────────
    private const string CP_PREFIX = "CP_";

    // ── State ──────────────────────────────────────────────────────────────────
    public static HomeCutsceneController Instance { get; private set; }
    public bool IsCutscenePlaying { get; private set; }

    private bool   _orientationButtonPressed;
    private string _pendingRequiredScene;

    // Called by Calendar when a required event (midterms/finals) is due this week.
    // HomeCutsceneController will navigate there automatically after the arrival overlay.
    public void QueueRequiredRedirect(string scene) { _pendingRequiredScene = scene; }

    private enum MorningChoice { None, GoToClass, SkipForNow, GoToCheer, DismissGameDay }
    private MorningChoice _morningChoice;

    // ══════════════════════════════════════════════════════════════════════════
    // Unity lifecycle
    // ══════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        Instance = this;
        SemesterHelper.SemesterYear = semesterYear;
        overlay = overlayPrefab != null ? Instantiate(overlayPrefab) : null;
        EnsureStatsPopulated();
    }

    private void Start()
    {
        overlay?.ShowImmediate();
        IsCutscenePlaying = true;
        if (orientationButton != null) orientationButton.gameObject.SetActive(false);
        alarmModal?.gameObject.SetActive(false);
        gameDayModal?.gameObject.SetActive(false);
        StartCoroutine(OrchestrateSceneLoad());
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Checkpoint — survives VNEngine save/load stat wipes
    // ══════════════════════════════════════════════════════════════════════════

    private static void WriteCheckpoint()
    {
        PlayerPrefs.SetFloat (CP_PREFIX + "Week",               StatsManager.Get_Numbered_Stat("Week"));
        PlayerPrefs.SetFloat (CP_PREFIX + "DayOffset",          StatsManager.Get_Numbered_Stat("DayOffset"));
        PlayerPrefs.SetFloat (CP_PREFIX + "DayPhase",           StatsManager.Get_Numbered_Stat("DayPhase"));
        PlayerPrefs.SetInt   (CP_PREFIX + "HomeInitialized",    StatsManager.Get_Boolean_Stat(STAT_HOME_INITIALIZED) ? 1 : 0);
        PlayerPrefs.SetInt   (CP_PREFIX + "SkippedClass",       StatsManager.Get_Boolean_Stat(STAT_SKIPPED_CLASS)    ? 1 : 0);
        PlayerPrefs.SetInt   (CP_PREFIX + "ClassAttendedThisWeek", StatsManager.Get_Boolean_Stat(STAT_CLASS_ATTENDED) ? 1 : 0);
        var schedule = StatsManager.Get_String_Stat("FootballSchedule");
        if (!string.IsNullOrEmpty(schedule))
            PlayerPrefs.SetString(CP_PREFIX + "FootballSchedule", schedule);
        PlayerPrefs.Save();
    }

    private static void ReadCheckpoint()
    {
        if (!PlayerPrefs.HasKey(CP_PREFIX + "Week")) return;
        StatsManager.Set_Numbered_Stat("Week",      PlayerPrefs.GetFloat(CP_PREFIX + "Week"));
        StatsManager.Set_Numbered_Stat("DayOffset", PlayerPrefs.GetFloat(CP_PREFIX + "DayOffset", 0f));
        StatsManager.Set_Numbered_Stat("DayPhase",  PlayerPrefs.GetFloat(CP_PREFIX + "DayPhase",  0f));
        StatsManager.Set_Boolean_Stat(STAT_HOME_INITIALIZED, PlayerPrefs.GetInt(CP_PREFIX + "HomeInitialized") == 1);
        StatsManager.Set_Boolean_Stat(STAT_SKIPPED_CLASS,    PlayerPrefs.GetInt(CP_PREFIX + "SkippedClass",          0) == 1);
        StatsManager.Set_Boolean_Stat(STAT_CLASS_ATTENDED,   PlayerPrefs.GetInt(CP_PREFIX + "ClassAttendedThisWeek", 0) == 1);
        var schedule = PlayerPrefs.GetString(CP_PREFIX + "FootballSchedule", "");
        if (!string.IsNullOrEmpty(schedule))
            StatsManager.Set_String_Stat("FootballSchedule", schedule);
    }

    private void EnsureStatsPopulated()
    {
        // Only fall back to the checkpoint if VNEngine actually wiped StatsManager —
        // otherwise this would clobber valid in-session stats (e.g. DayOffset) that were
        // just set by scene-graph nodes moments before this Home scene loaded.
        if (!StatsManager.Numbered_Stat_Exists("Week"))
            ReadCheckpoint();
        if (StatsManager.Numbered_Stat_Exists("Week")) return;

        // Fall back to VNEngine save file
        SaveManager.LoadFromFile();
        var saves = SaveManager.saved_games;
        if (saves != null && saves.Count > 0)
        {
            var latest = saves[saves.Count - 1];
            if (latest.saved_numbered_stats != null)
                StatsManager.numbered_stats = new Dictionary<string, float>(latest.saved_numbered_stats);
            if (latest.saved_boolean_stats != null)
                StatsManager.boolean_stats  = new Dictionary<string, bool>(latest.saved_boolean_stats);
            if (latest.saved_string_stats != null)
                StatsManager.string_stats   = new Dictionary<string, string>(latest.saved_string_stats);
        }

        // Sanitize impossible Week-0 states (e.g. stale checkpoint written by old buggy code).
        // Only valid DayOffsets for orientation week: 4 = Friday Move-In Day, 5 = Saturday return.
        if (StatsManager.Get_Numbered_Stat("Week") == 0)
        {
            float savedOffset = StatsManager.Get_Numbered_Stat("DayOffset");
            if (savedOffset != 4f && savedOffset != 5f)
            {
                StatsManager.Set_Boolean_Stat(STAT_HOME_INITIALIZED, false);
                StatsManager.Set_Numbered_Stat("DayOffset", 4f);
                StatsManager.Set_Numbered_Stat("DayPhase",  0f);
                PlayerPrefs.DeleteKey(CP_PREFIX + "Week");
                PlayerPrefs.Save();
                Debug.Log($"[HomeCutsceneController] Corrupt Week-0 state (DayOffset={savedOffset}); reset to Move-In Day.");
            }
        }

        // Fresh new game — Week=0 and Move-In Day not yet run (HOME_INITIALIZED guards repeat init)
        if (StatsManager.Get_Numbered_Stat("Week") == 0 && !StatsManager.Get_Boolean_Stat(STAT_HOME_INITIALIZED))
        {
            StatsManager.Set_Numbered_Stat("Week",      0f); // week 0 = orientation
            StatsManager.Set_Numbered_Stat("DayOffset", 4f); // Aug 16 + 4 = Aug 20 (Friday, first orientation day)
            StatsManager.Set_Numbered_Stat("DayPhase",  0f);
            StatsManager.Set_Boolean_Stat(STAT_CLASS_ATTENDED, false);
            FootballScheduler.GenerateSchedule();
            GameEvents.EnsureSemesterRequiredEventsRegistered();
            Debug.Log("[HomeCutsceneController] No save found — initialized to Week 0 (orientation, Friday Aug 20).");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Navigation out of Home
    // ══════════════════════════════════════════════════════════════════════════

    // description defaults to the destination's LocationData.displayName (falling back to
    // the raw scene name) — shown on the overlay so the player can confirm where they're headed.
    public static void NavigateOut(string targetScene, string description = null)
    {
        if (Instance != null)
            Instance.StartCoroutine(Instance.LeaveRoom(targetScene, description));
        else
            LocationRouter.Go(targetScene);
    }

    // Called by the Alarm Clock HomeObject's interaction button when the player taps the clock.
    public void ShowAlarmModal()
    {
        if (alarmModal == null || IsCutscenePlaying) return;
        alarmClockAnimator?.SetTrigger("Off");
        StartCoroutine(RunAlarmModalPrompt());
    }

    // Called by the Mascot child object's button on game weeks.
    public void ShowGameDayModal()
    {
        if (gameDayModal == null || IsCutscenePlaying) return;
        if (StatsManager.Get_Numbered_Stat("DayPhase") < 1f) return;
        int w = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("Week"));
        if (FootballScheduler.GetThisWeeksGame(w) == null) return;
        StartCoroutine(RunGameDayModalPrompt());
    }

    private IEnumerator RunGameDayModalPrompt()
    {
        IsCutscenePlaying = true;
        _morningChoice = MorningChoice.None;

        int week = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("Week"));
        var game = FootballScheduler.GetThisWeeksGame(week);
        if (game == null)
        {
            IsCutscenePlaying = false;
            yield break;
        }

        if (gameDayTitleText != null)
            gameDayTitleText.text = $"{game.opponent.mascot} vs. Sentinels";
        if (gameDayDescriptionText != null)
            gameDayDescriptionText.text = BuildGameDayDescription(game);

        goToCheerButton?.onClick.RemoveAllListeners();
        goToCheerButton?.onClick.AddListener(() => _morningChoice = MorningChoice.GoToCheer);
        dismissGameDayButton?.onClick.RemoveAllListeners();
        dismissGameDayButton?.onClick.AddListener(() => _morningChoice = MorningChoice.DismissGameDay);

        gameDayModal.gameObject.SetActive(true);
        yield return new WaitUntil(() => _morningChoice != MorningChoice.None);

        gameDayModal.gameObject.SetActive(false);
        IsCutscenePlaying = false;

        if (_morningChoice == MorningChoice.GoToCheer)
            yield return StartCoroutine(LeaveRoom(cheerScene));
    }

    private string BuildGameDayDescription(FootballGame game)
    {
        var opp = game.opponent;
        if (opp.isRival)
        {
            if (opp.losses == 0 && opp.wins > 0)
                return $"The {opp.mascot} have an unbeatable record this season. We have to change that!";
            return $"The {opp.mascot} are our rivals. No matter what, we have to bring it today!";
        }

        var (ourWins, ourLosses) = FootballScheduler.GetSeasonRecord();
        return $"The {opp.mascot} are {opp.wins}-{opp.losses} this season. " +
               $"With our own {ourWins}-{ourLosses} record, this is make or break!";
    }

    private IEnumerator RunAlarmModalPrompt()
    {
        IsCutscenePlaying = true;
        _morningChoice = MorningChoice.None;

        // Class prompt — notYetButton lives inside alarmModal and shows with it.
        // goToClassButton is the alarm clock itself; player taps it to confirm going to class.
        goToClassButton?.onClick.RemoveAllListeners();
        goToClassButton?.onClick.AddListener(() => _morningChoice = MorningChoice.GoToClass);
        notYetButton?.onClick.RemoveAllListeners();
        notYetButton?.onClick.AddListener(() => _morningChoice = MorningChoice.SkipForNow);

        alarmModal.gameObject.SetActive(true);
        yield return new WaitUntil(() => _morningChoice != MorningChoice.None);

        alarmModal.gameObject.SetActive(false);
        // Remove the GoToClass listener so a future tap on the alarm clock opens a fresh modal
        // rather than immediately navigating.
        goToClassButton?.onClick.RemoveAllListeners();

        if (_morningChoice == MorningChoice.GoToClass)
            yield return StartCoroutine(LeaveRoom(classScene));

        IsCutscenePlaying = false;
    }

    private IEnumerator LeaveRoom(string targetScene, string description = null)
    {
        IsCutscenePlaying = true;

        int  leavingWeek  = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("Week"));
        int  leavingPhase = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("DayPhase"));

        if (targetScene == classScene)
        {
            // Morning class: mark attended. The Lecture Hall VN's NodeCheckpoint (set to
            // SecondHalfOfWeek) advances DayPhase→1 when the conversation ends.
            StatsManager.Set_Boolean_Stat(STAT_CLASS_ATTENDED, true);
        }
        else
        {
            // Any non-class destination: the day is consumed.
            // Mark skip penalty if player is still in the morning phase.
            if (leavingPhase == 0 && leavingWeek >= 1 &&
                targetScene != cheerScene && targetScene != footballScene)
            {
                StatsManager.Set_Boolean_Stat(STAT_SKIPPED_CLASS, true);
            }
        }

        WriteCheckpoint();

        int  dayOffset      = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("DayOffset"));
        bool isGameDeparture = targetScene == cheerScene || targetScene == footballScene;
        int  displayOffset  = isGameDeparture ? 5 : dayOffset;   // Saturday = Monday + 5 days
        int  displayPhase   = isGameDeparture ? 1 : leavingPhase; // game is always afternoon
        string dayAndTime   = SemesterHelper.GetDayAndTimeLabel(leavingWeek, displayOffset, displayPhase);
        string fullDate     = SemesterHelper.GetDateLabel(leavingWeek, displayOffset);
        var    locData      = LocationData.Find(targetScene);
        if (string.IsNullOrEmpty(description))
            description = locData?.displayName ?? LocationData.GetDisplayName(targetScene);
        overlay.SetContent(dayAndTime, description, null, fullDate, locData?.buildingName);

        yield return StartCoroutine(overlay.FadeIn());
        LocationRouter.Go(targetScene);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Scene load orchestration
    // ══════════════════════════════════════════════════════════════════════════

    private IEnumerator OrchestrateSceneLoad()
    {
        int  week      = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("Week"));
        int  dayOffset = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("DayOffset"));
        int  dayPhase  = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("DayPhase"));
        bool isFirstDay = week == 0 && !StatsManager.Get_Boolean_Stat(STAT_HOME_INITIALIZED);

        // Returning from a football/cheer game. If the Post Game VN's NodeCheckpoint ran,
        // DayPhase is already 0 (next morning). If it didn't run, DayPhase is still 1
        // (game-day afternoon) — advance the week manually as a fallback.
        bool justReturnedFromGame = StatsManager.Get_Boolean_Stat("JustReturnedFromGame");
        if (justReturnedFromGame)
        {
            StatsManager.Set_Boolean_Stat("JustReturnedFromGame", false);
            if (dayPhase == 1)
            {
                int newWeek = week + 1;
                StatsManager.Set_Numbered_Stat("Week",      newWeek);
                StatsManager.Set_Numbered_Stat("DayPhase",  0f);
                StatsManager.Set_Numbered_Stat("DayOffset", 0f);
                StatsManager.Set_Boolean_Stat("ClassAttendedThisWeek", false);
                FootballScheduler.SimulateUnplayedPastGames(newWeek);
                WriteCheckpoint();
                week      = newWeek;
                dayOffset = 0;
                dayPhase  = 0;
            }
        }

        // Safety: DayPhase=1 should only occur when class was attended this week.
        // If something left DayPhase=1 without setting ClassAttended, reset to morning.
        if (dayPhase == 1 && week >= 1 && !StatsManager.Get_Boolean_Stat(STAT_CLASS_ATTENDED))
        {
            dayPhase = 0;
            StatsManager.Set_Numbered_Stat("DayPhase", 0f);
        }

        // New week morning: clear ClassAttended for the fresh week.
        if (dayPhase == 0)
            StatsManager.Set_Boolean_Stat(STAT_CLASS_ATTENDED, false);

        // Skipped class: clear the flag (NodeCheckpoint already advanced the week when the
        // location VN ended — no second increment needed here).
        if (dayPhase == 0 && week >= 1 && StatsManager.Get_Boolean_Stat(STAT_SKIPPED_CLASS))
        {
            StatsManager.Set_Boolean_Stat(STAT_SKIPPED_CLASS, false);
            StatsManager.Set_Numbered_Stat("DayOffset", 0f);
            dayOffset = 0;
            WriteCheckpoint();
        }

        var newObjects = FindNewlyRevealedObjects();
        PrepareObjectVisibility(isFirstDay, newObjects);
        if (goToClassButton != null) goToClassButton.interactable = false;

        var gameThisWeek = week >= 1 ? FootballScheduler.GetThisWeeksGame(week) : null;

        // Returning from class on a game week: advance to Saturday so the arrival overlay
        // and home calendar both show game day immediately.
        if (dayPhase == 1 && gameThisWeek != null && !gameThisWeek.played)
        {
            dayOffset = 5;
            StatsManager.Set_Numbered_Stat("DayOffset", 5f);
            WriteCheckpoint();
        }

        string dayAndTime = SemesterHelper.GetDayAndTimeLabel(week, dayOffset, dayPhase);
        string fullDate   = SemesterHelper.GetDateLabel(week, dayOffset);

        if (isFirstDay)
        {
            // ── Move-In Day ──────────────────────────────────────────────────
            overlay.SetContent(dayAndTime, "North Hall", null, fullDate, "Winchester Residential College");
            yield return new WaitForSeconds(holdDuration);
            yield return StartCoroutine(overlay.FadeOut());

            if (arrivalSprite != null && backgroundImage != null)
            {
                var normalSprite = backgroundImage.sprite;
                backgroundImage.sprite = arrivalSprite;
                yield return new WaitForSeconds(0.4f);
                backgroundImage.sprite = normalSprite;
            }

            yield return StartCoroutine(AnimateBaseObjects());

            StatsManager.Set_Boolean_Stat(STAT_HOME_INITIALIZED, true);

            IsCutscenePlaying = false;
            _orientationButtonPressed = false;

            if (orientationButton != null)
            {
                orientationButton.onClick.RemoveAllListeners();
                orientationButton.onClick.AddListener(() => { _orientationButtonPressed = true; });
                orientationButton.gameObject.SetActive(true);
                yield return new WaitUntil(() => _orientationButtonPressed);
                orientationButton.onClick.RemoveAllListeners();
                orientationButton.gameObject.SetActive(false);
            }

            IsCutscenePlaying = true;

            // Orientation is a Friday afternoon departure. Save DayOffset+1 (→ Saturday) so the
            // player returns home on Saturday. The local dayOffset (4 = Friday) is used for the
            // overlay label so the departure still reads "Friday Afternoon."
            StatsManager.Set_Numbered_Stat("DayOffset", dayOffset + 1f);
            StatsManager.Set_Numbered_Stat("DayPhase",  1f);
            calendar?.SyncTimeOfDay();

            WriteCheckpoint();

            var orientLocData = LocationData.Find(orientationScene);
            yield return new WaitForSeconds(nightViewDuration);
            overlay.SetContent(SemesterHelper.GetDayAndTimeLabel(week, dayOffset, 1), "New Student Orientation", null, fullDate, orientLocData?.buildingName);
            yield return StartCoroutine(overlay.FadeIn());
            LocationRouter.Go(orientationScene);
            yield break;
        }

        // ── Normal visit (arriving home) ─────────────────────────────────────
        string description = GetDescription(week, dayOffset, dayPhase);
        overlay.SetContent(dayAndTime, description, null, fullDate, homeLocationData?.buildingName);
        yield return new WaitForSeconds(holdDuration);

        // Start revealing new achievement objects concurrently with the overlay fade-out
        // so they are already visible when the room is uncovered — no separate reveal cutscene.
        foreach (var obj in newObjects)
        {
            StartCoroutine(obj.RevealAnimate());
            StatsManager.Set_Boolean_Stat(REVEAL_PREFIX + obj.gameObject.name, true);
        }
        yield return StartCoroutine(overlay.FadeOut());

        // If Calendar queued a required redirect (midterms/finals), navigate now instead
        // of returning control to the player — show the home arrival label first, then leave.
        if (!string.IsNullOrEmpty(_pendingRequiredScene))
        {
            string dest = _pendingRequiredScene;
            _pendingRequiredScene = null;
            CommitFlags(week, dayOffset, dayPhase);
            yield return StartCoroutine(LeaveRoom(dest));
            yield break;
        }

        CommitFlags(week, dayOffset, dayPhase);

        if (week >= 1)
        {
            // Ensure active before sending a trigger — HomeObject DayTimeOnly condition
            // deactivates the clock in the afternoon, which also hides the mascot child.
            if (alarmClockAnimator != null)
                alarmClockAnimator.gameObject.SetActive(true);

            if (dayPhase == 0)
                alarmClockAnimator?.SetTrigger("On");                        // morning: prompt class
            else if (gameThisWeek != null && !gameThisWeek.played)
                alarmClockAnimator?.SetTrigger("GameDay");                   // afternoon: game day nudge
            else if (dayPhase == 1)
                alarmClockAnimator?.SetTrigger("Off");                 // afternoon: no game
        }

        FinishCutscene();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private string GetDescription(int week, int dayOffset, int dayPhase)
    {
        if (week == HOMECOMING_WEEK && !StatsManager.Get_Boolean_Stat(STAT_HOMECOMING_SHOWN)
            && FootballScheduler.GetThisWeeksGame(week) != null)
            return "Homecoming";

        if (week == SemesterHelper.MidtermsWeek && dayPhase == 0 && !StatsManager.Get_Boolean_Stat(STAT_MIDTERMS_SHOWN))
            return "Midterms";

        if (week == SemesterHelper.FinalsWeek && dayPhase == 0 && !StatsManager.Get_Boolean_Stat(STAT_FINALS_SHOWN))
            return "Finals";

        if (week == 1 && dayOffset == 1 && dayPhase == 0 && !StatsManager.Get_Boolean_Stat(STAT_CLASSES_START_SHOWN))
            return "Sylly Week";

        return "";
    }

    private List<HomeObject> FindNewlyRevealedObjects()
    {
        var result = new List<HomeObject>();
        if (home?.homeObjects == null) return result;

        foreach (var obj in home.homeObjects)
        {
            if (obj == null || obj.condition != HomeObject.Condition.Achievement) continue;
            bool earned   = StatsManager.Get_Boolean_Stat("Achievement_" + obj.achievementKey);
            bool wasShown = StatsManager.Get_Boolean_Stat(REVEAL_PREFIX + obj.gameObject.name);
            if (earned && !wasShown) result.Add(obj);
        }
        return result;
    }

    private void PrepareObjectVisibility(bool isFirstDay, List<HomeObject> newObjects)
    {
        if (home?.homeObjects == null) return;

        foreach (var obj in home.homeObjects)
        {
            if (obj == null) continue;
            bool isNew        = newObjects.Contains(obj);
            bool isFirstDayObj = obj.condition == HomeObject.Condition.Always ||
                                 obj.condition == HomeObject.Condition.DayTimeOnly ||
                                 obj.condition == HomeObject.Condition.ClassAttended;
            bool wasShown     = StatsManager.Get_Boolean_Stat(REVEAL_PREFIX + obj.gameObject.name);
            bool needsAnim    = isNew || (isFirstDay && animateAllObjectsOnFirstDay && isFirstDayObj && !wasShown);

            if (needsAnim)
                obj.gameObject.SetActive(false);
            else
                obj.Refresh();
        }
    }

    private IEnumerator AnimateBaseObjects()
    {
        if (home?.homeObjects == null) yield break;

        if (homeObjectsGroup != null)
            homeObjectsGroup.alpha = 1f;

        // Snapshot to avoid any collection-modification issues during iteration.
        var toReveal = new List<HomeObject>();
        foreach (var obj in home.homeObjects)
        {
            if (obj == null) continue;
            bool isFirstDayObj = obj.condition == HomeObject.Condition.Always ||
                                 obj.condition == HomeObject.Condition.DayTimeOnly ||
                                 obj.condition == HomeObject.Condition.ClassAttended;
            if (!isFirstDayObj) continue;
            if (StatsManager.Get_Boolean_Stat(REVEAL_PREFIX + obj.gameObject.name))
                obj.Refresh();
            else
                toReveal.Add(obj);
        }

        foreach (var obj in toReveal)
        {
            // Get (or add) a CanvasGroup local to this object's own GameObject.
            // obj.canvasGroup points to the shared parent group — using it would fade all objects at once.
            CanvasGroup cg = obj.gameObject.GetComponent<CanvasGroup>();
            if (cg == null)
                cg = obj.gameObject.AddComponent<CanvasGroup>();

            cg.alpha = 0f;
            obj.gameObject.SetActive(true);
            if (obj.interactionButton != null) obj.interactionButton.interactable = false;

            float t = 0f;
            while (t < revealDuration)
            {
                t += Time.deltaTime;
                cg.alpha = Mathf.Clamp01(t / revealDuration);
                yield return null;
            }
            cg.alpha = 1f;

            StatsManager.Set_Boolean_Stat(REVEAL_PREFIX + obj.gameObject.name, true);
            yield return new WaitForSeconds(pauseBetweenReveals);
        }
    }

    private void CommitFlags(int week, int dayOffset, int dayPhase)
    {
        if (week == HOMECOMING_WEEK && FootballScheduler.GetThisWeeksGame(week) != null)
            StatsManager.Set_Boolean_Stat(STAT_HOMECOMING_SHOWN, true);
        if (week == SemesterHelper.MidtermsWeek && dayPhase == 0)
            StatsManager.Set_Boolean_Stat(STAT_MIDTERMS_SHOWN, true);
        if (week == SemesterHelper.FinalsWeek && dayPhase == 0)
            StatsManager.Set_Boolean_Stat(STAT_FINALS_SHOWN, true);
        if (week == 1 && dayOffset == 1 && dayPhase == 0)
            StatsManager.Set_Boolean_Stat(STAT_CLASSES_START_SHOWN, true);
    }

    private void FinishCutscene()
    {
        IsCutscenePlaying = false;
        overlay?.HideImmediate();
        calendar?.SyncTimeOfDay();
        calendar?.SyncDate();
        home?.RefreshHomeObjects();

        int dayPhase = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("DayPhase"));
        int week     = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("Week"));

        // RefreshHomeObjects() may have deactivated the alarm clock via its DayTimeOnly condition.
        // Re-activate it so the animator state and mascot child remain visible.
        if (week >= 1 && alarmClockAnimator != null)
            alarmClockAnimator.gameObject.SetActive(true);

        if (goToClassButton != null)
            goToClassButton.interactable = week >= 1 && dayPhase == 0;
    }

    // Called by the study mini-game (Solo mode) when it completes from within Home.unity.
    // Reads the current DayPhase to decide whether to advance to afternoon or next week.
    public void OnStudyComplete()
    {
        int phase = Mathf.FloorToInt(StatsManager.Get_Numbered_Stat("DayPhase"));
        if (phase == 0)
        {
            // Morning study → advance to afternoon
            StatsManager.Set_Boolean_Stat(STAT_CLASS_ATTENDED, true);
            StatsManager.Set_Numbered_Stat("DayPhase", 1f);
        }
        else
        {
            // Afternoon study → advance to next week's morning
            int week = Mathf.FloorToInt(StatsManager.Get_Numbered_Stat("Week"));
            int next = week + 1;
            StatsManager.Set_Numbered_Stat("Week",      next);
            StatsManager.Set_Numbered_Stat("DayPhase",  0f);
            StatsManager.Set_Numbered_Stat("DayOffset", 0f);
            StatsManager.Set_Boolean_Stat("ClassAttendedThisWeek", false);
            FootballScheduler.SimulateUnplayedPastGames(next);
        }
        WriteCheckpoint();
        LocationRouter.Go("Home");
    }
}
