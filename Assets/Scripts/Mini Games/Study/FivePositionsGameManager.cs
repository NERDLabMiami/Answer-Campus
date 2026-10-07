using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using VNEngine;
using FMODUnity;

[System.Serializable]
public class QuestionAnswerPair
{
    public string question;
    public string definition;
    public string answer; // Must be 5 letters
    public bool alreadyUsed = false;
    public bool isPrimary = false;
}

public class FivePositionsGameManager : MonoBehaviour
{
    public enum SpawnMode { Sequential, Random }
    public GameMode currentMode;
    public SpawnMode spawnMode;    private int lastSpawnedColumn = -1;

    // Per-mode rules, set by ApplyModeRules and overridden by ConfigureChallenge.
    //   Solo : timed, wrong letter costs time, ends when the clock hits 0
    //   Group: untimed, shared strike pool, ends when the pool is empty or wordCap words are solved
    //   Exam : untimed, strike pool = the exam's GPA points, ends on strike-out or wordCap words solved
    private bool timed;
    private int strikePool;   // 0 = strikes disabled
    private int strikesUsed;
    private int wordCap;      // 0 = unlimited
    private bool gradeAtStake;
    private string currentExamId = "";

    public StudyQuestionLoader questionLoader;

    
    private QuestionAnswerPair _currentQuestion;
[Header("Scene References")]
    public RectTransform[] boxPositions = new RectTransform[5];
    public TextMeshProUGUI[] boxLetterDisplays = new TextMeshProUGUI[5];
    public TextMeshProUGUI targetDefinitionText;
    public TextMeshProUGUI countdownText; // "3-2-1" countdown text

    [System.Serializable]
    public class SoloUI
    {
        public GameObject root;

        [System.Serializable]
        public class PhoneBlock
        {
            public TextMeshProUGUI clockText;        // remaining time
            public TextMeshProUGUI penaltyFlashText; // "-0:20" flash on a wrong letter
        }

        [System.Serializable]
        public class NotepadBlock
        {
            public TextMeshProUGUI scoreText;
            public TextMeshProUGUI scoreCaptionText;
            public TextMeshProUGUI solvedWordsText; // newline-joined list of solved words this session
        }

        public PhoneBlock phone;
        public NotepadBlock notepad;
    }

    [System.Serializable]
    public class GroupUI
    {
        public GameObject root;

        [System.Serializable]
        public class PhoneBlock
        {
            public TextMeshProUGUI streakText;        // consecutive words solved without a mistake
            public TextMeshProUGUI sessionStatusText; // persistent: "N more mistake(s) until session ends"
            public TextMeshProUGUI penaltyFlashText;  // transient flash: "Chance lost", "Last chance!", etc.
        }

        [System.Serializable]
        public class NotepadBlock
        {
            public TextMeshProUGUI scoreText;
            public TextMeshProUGUI scoreCaptionText;
            public TextMeshProUGUI solvedWordsText; // newline-joined list of solved words this session
        }

        public PhoneBlock phone;
        public NotepadBlock notepad;
    }

    [System.Serializable]
    public class ExamUI
    {
        public GameObject root;

        [System.Serializable]
        public class PhoneBlock
        {
            public TextMeshProUGUI currentGradeText; // "Midterm Exam: A" / "Final Exam: A"
            public TextMeshProUGUI previousExamText; // e.g. "Midterm Exam: A-" (blank if none taken yet)
            public TextMeshProUGUI currentGpaText;   // "Projected GPA 3.9"
            public TextMeshProUGUI penaltyFlashText; // "GRADE AFFECTED\n-0.3"
        }

        [System.Serializable]
        public class NotepadBlock
        {
            public TextMeshProUGUI scoreText;
            public TextMeshProUGUI scoreCaptionText;
            public TextMeshProUGUI progressText; // "Question\n2 of 4"
        }

        public PhoneBlock phone;
        public NotepadBlock notepad;
    }

    [Header("Mode UI (Notepad + Phone are children of each mode's root)")]
    public SoloUI soloUI;
    public GroupUI groupUI;
    public ExamUI examUI;

    private List<string> solvedWords = new List<string>();
    private int currentStreak = 0; // Group only
    private int longestStreak = 0; // Group only — peak value of currentStreak this session

    public ConversationManager conversationManager;
    [Header("Prefabs/Assets")]
    public GameObject letterPrefab;
    public AudioClip correctClip;
    public AudioClip incorrectClip;
    public GameObject boxSpritePrefab;
    public GameObject eraserPrefab;
    public EventReference studyMusicEvent;
    [Header("Offsets")]
    public float spawnYOffset = 100f; // how far above each box the letter should spawn
    public float boxYOffset = 0f;         // Optional adjustment (e.g., -0.5f if needed)
    public float eraserYOffset = 2f;      // How far above the first box to place the eraser
    [Header("Spawn Settings")]
    public float minSpawnInterval = 1f;
    public float maxSpawnInterval = 3f;
    [Range(0f, 1f)] public float chanceOfCorrectLetter = 0.3f;
    public float letterSpeed = 2f;
    [Header("No-Timer Rules (defaults; a ChallengeProfile overrides these)")]
    public int defaultGroupStrikePool = 5;
    public int defaultExamStrikePool = 4;
    public int maxWordAttempts = 5;   // words per Group/Exam session; Solo ignores this (clock only)

    private int wordsAttempted = 0;

    private string targetWord = "";
    private char[] targetLetters = new char[5];
    private bool[] boxFilled = new bool[5];
    private string alphabet = "abcdefghijklmnopqrstuvwxyz";
    private LetterMovement[] waitingLetters = new LetterMovement[5];
    private List<LetterMovement> hangingLetters = new List<LetterMovement>();

    private int score = 0;
    [Header("Letter Timing")]
    public float preDropHangTime = 0.35f;

    [Header("Timer Settings")]
    public float gameDuration = 180f;       // Total game time in seconds
    public float penaltyTime = 5f;        // Solo: seconds to remove on incorrect answer
    public float lowTimeWarning = 15f;    // Solo: timer text turns red below this
    public float flashDuration = 1.25f;   // how long the line under the timer stays up
    public GameObject studyGameParent;       // Panel to show when time runs out
    [SerializeField] private GameObject gameStuff;
    public TextMeshProUGUI finalScoreText; // Display final score on game over panel
    private GameObject eraser;
    public float timeLeft;
    private bool gameIsOver = false;
    private Coroutine spawnRoutine;
    private Coroutine timerRoutine;
    private Coroutine flashRoutine;
    private Color timerBaseColor = Color.white;
    private bool timerColorCaptured;
    // This bool will pause the timer when true
    private bool isTimerPaused = false;
    private List<GameObject> boxVisuals = new List<GameObject>();
    public ChallengeProfile pendingChallengeProfile;
    public ConversationManager pendingEndConversation;

    [Header("Input")]
    [SerializeField] private PlayerInput playerInput;
    private InputAction _pressDown;
    private InputAction _submit;
    private LetterMovement activeLetter;

    private void Awake() {
        if (playerInput == null) playerInput = GetComponent<PlayerInput>();
        if (playerInput == null) playerInput = GetComponentInParent<PlayerInput>();
        if (playerInput == null) playerInput = FindAnyObjectByType<PlayerInput>();
        if (playerInput == null) {
            Debug.LogWarning("FivePositionsGameManager: no PlayerInput found; down/submit input disabled.");
            return;
        }
        _pressDown = playerInput.actions["PressDown"];
        _submit = playerInput.actions["Submit"];
    }

    private void OnEnable() {
        if (playerInput == null) return;
        playerInput.ActivateInput();
        playerInput.SwitchCurrentActionMap("Play");
        playerInput.actions.Enable();
        if (_submit != null) _submit.performed += OnSubmitPressed;
    }

    private void OnDisable() {
        if (_submit != null) _submit.performed -= OnSubmitPressed;
    }

    private void Update() {
        if (activeLetter != null && _pressDown != null) {
            activeLetter.SetBoosted(_pressDown.IsPressed());
        }
    }

    private void OnSubmitPressed(InputAction.CallbackContext context) {
        if (activeLetter != null) activeLetter.ResolveNow();
    }

    public void Initialize()
    {
        SetMode(currentMode);
        SpawnVisuals();
    }

    public void LaunchExam()
    {
        Initialize();

        if (pendingChallengeProfile != null)
            ConfigureChallenge(pendingChallengeProfile);

        StartGame();
    }


    private void SpawnVisuals()
    {
        for (int i = 0; i < boxPositions.Length; i++)
        {
            RectTransform box = boxPositions[i];
            if (box == null) continue;

            // ✅ This respects the entire transform hierarchy, including y = -3
            Vector3 worldBoxCenter = box.position;

            // Add world-space vertical offset if needed
            Vector3 worldPos = worldBoxCenter + new Vector3(0, boxYOffset, 0);
            worldPos.z = 0;

            GameObject visual = Instantiate(boxSpritePrefab, worldPos, Quaternion.identity);
            boxVisuals.Add(visual);
            SpriteRenderer sr = visual.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.sortingLayerName = "Foreground";
                sr.sortingOrder = 100;
            }

        }



        // Spawn the eraser above the first column
// Spawn the eraser above the first column
        Transform firstBox = boxPositions[0];
        if (firstBox != null)
        {
            Vector3 eraserPos = firstBox.position + new Vector3(0, eraserYOffset, 0);
            eraser = Instantiate(eraserPrefab, eraserPos, Quaternion.identity);

            // Set sprite rendering layer
            SpriteRenderer sr = eraser.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.sortingLayerName = "Foreground";
                sr.sortingOrder = 200;
            }

            // Connect the eraser to the box positions
            EraserController eraserController = eraser.GetComponent<EraserController>();
            if (eraserController != null)
            {
                eraserController.boxPositions = boxPositions;
            }
            else
            {
                Debug.LogWarning("Eraser prefab is missing EraserController.");
            }
        }
    }

    private IEnumerator DelayedGameStart()
    {
        yield return new WaitForEndOfFrame();
        StartGame();
    }
    
    private Vector3 GetSpawnPosAboveBox(int index)
    {
        if (boxPositions == null || index < 0 || index >= boxPositions.Length)
        {
            Debug.LogError("Invalid box index.");
            return Vector3.zero;
        }

        RectTransform rect = boxPositions[index];
        if (rect == null)
        {
            Debug.LogError("Box is not a RectTransform.");
            return Vector3.zero;
        }

        // World-space center of the box, including parent offset
        Vector3 worldBoxCenter = rect.position;

        // Offset vertically in world units
        Vector3 spawnPos = worldBoxCenter + new Vector3(0, spawnYOffset, 0);
        spawnPos.z = 0;

        return spawnPos;
    }




    public void StartGame()
    {
        FMODAudioManager.Instance?.PushMusic(studyMusicEvent);

        // Rules may not have been applied yet (e.g. GroupStudyManager starts without Initialize)
        ApplyModeRules(currentMode);

        score = 0;
        wordsAttempted = 0;
        strikesUsed = 0;
        solvedWords.Clear();
        currentStreak = 0;
        longestStreak = 0;
        currentExamId = StatsManager.Get_String_Stat("CurrentExamId");

        ActivateUiForMode(currentMode);
        if (soloUI.phone.clockText != null && !timerColorCaptured)
        {
            timerBaseColor = soloUI.phone.clockText.color;
            timerColorCaptured = true;
        }

        // Hide the flash line(s) and show the game panel
        HideAllFlashes();
        if (gameStuff != null) gameStuff.SetActive(true);

        // Clock only exists in Solo. Paused until the first countdown finishes.
        if (timed) timeLeft = gameDuration;
        isTimerPaused = true;
        gameIsOver = false;

        // Exactly one timer coroutine per game (a new one per word made the clock run faster each round)
        if (timerRoutine != null) StopCoroutine(timerRoutine);
        timerRoutine = StartCoroutine(GameTimerCoroutine());

        if (questionLoader != null)
            questionLoader.currentMode = currentMode;
        questionLoader.LoadQuestionsForMode();

        // Exam length comes from the profile, but can't exceed the words available
        if (currentMode == GameMode.Exam && questionLoader.currentQuestions.Count > 0)
            wordCap = wordCap > 0
                ? Mathf.Min(wordCap, questionLoader.currentQuestions.Count)
                : questionLoader.currentQuestions.Count;

        // Solo ends the moment every word in the current week's list has been solved.
        if (currentMode == GameMode.Solo)
            wordCap = questionLoader.currentQuestions.Count;

        RefreshHud();
        StartCoroutine(CountdownCoroutine());
    }

    /// <summary>
    /// Solo clock. Only decrements timeLeft while unpaused; other modes have no clock.
    /// </summary>
    private IEnumerator GameTimerCoroutine() {
        if (!timed) yield break;

        while (timeLeft > 0 && !gameIsOver) {
            yield return null; // Wait one frame
            if (isTimerPaused) continue;

            timeLeft -= Time.deltaTime;
            RefreshHud();

            if (timeLeft <= 0 && !gameIsOver)
            {
                timeLeft = 0;
                RefreshHud();
                StartCoroutine(EndGame());
            }
        }
    }

    // ---------------------------------------------------------------- HUD
    // Notepad (Solo/Group): score + the list of words solved this session
    // Notepad (Exam)      : score + "Word 2 of 4 - Wrong answers: 3"
    // Phone (Solo)        : clock app - remaining time, with a penalty flash below it
    // Phone (Group)       : group-study app - current streak + mistakes-until-session-ends
    // Phone (Exam)        : gradebook - previous exam score + current GPA, with a penalty flash

    private int StrikesLeft => Mathf.Max(0, strikePool - strikesUsed);

    /// <summary>Shows only the mode root (and its Phone/Notepad children) belonging to this mode.</summary>
    private void ActivateUiForMode(GameMode mode)
    {
        if (soloUI.root != null) soloUI.root.SetActive(mode == GameMode.Solo);
        if (groupUI.root != null) groupUI.root.SetActive(mode == GameMode.Group);
        if (examUI.root != null) examUI.root.SetActive(mode == GameMode.Exam);
    }

    private void RefreshHud()
    {
        switch (currentMode)
        {
            case GameMode.Solo: RefreshSoloHud(); break;
            case GameMode.Group: RefreshGroupHud(); break;
            case GameMode.Exam: RefreshExamHud(); break;
        }
    }

    private void RefreshSoloHud()
    {
        SetText(soloUI.notepad.scoreText, score.ToString());
        SetText(soloUI.notepad.scoreCaptionText, "solved");
        SetText(soloUI.notepad.solvedWordsText, BuildSolvedWordsLine());

        int t = Mathf.CeilToInt(Mathf.Max(0f, timeLeft));
        SetText(soloUI.phone.clockText, string.Format("{0:0}:{1:00}", t / 60, t % 60));

        if (soloUI.phone.clockText != null && timerColorCaptured)
            soloUI.phone.clockText.color = (timeLeft <= lowTimeWarning) ? Color.red : timerBaseColor;
    }

    private void RefreshGroupHud()
    {
        SetText(groupUI.notepad.scoreText, score.ToString());
        SetText(groupUI.notepad.scoreCaptionText, "solved");
        SetText(groupUI.notepad.solvedWordsText, BuildSolvedWordsLine());

        SetText(groupUI.phone.streakText, BuildStreakLine());
        SetText(groupUI.phone.sessionStatusText, strikesUsed > 0 ? $"{StrikesLeft} chance(s) left!" : "");
    }

    private string BuildStreakLine()
    {
        if (currentStreak == 0) return "No Streak Yet";
        if (currentStreak == 1) return "1 correct!";
        return $"{currentStreak} in a row!";
    }

    private void RefreshExamHud()
    {
        SetText(examUI.notepad.scoreText, score.ToString());
        SetText(examUI.notepad.scoreCaptionText, "correct");
        SetText(examUI.notepad.progressText, BuildExamProgressLine());

        string letter = GradeCalculator.CurrentExamLetterGrade(strikesUsed, strikePool);
        string examLabel = GradeCalculator.IsFinal(currentExamId) ? "Final Exam" : "Midterm Exam";
        SetText(examUI.phone.currentGradeText, examLabel + ": " + letter);
        SetText(examUI.phone.previousExamText, GradeCalculator.PreviousExamLabel(currentExamId));
        SetText(examUI.phone.currentGpaText, "Projected GPA " + GradeCalculator.ProjectedGpa(currentExamId, strikesUsed, strikePool).ToString("0.0"));
    }

    private string BuildSolvedWordsLine()
        => string.Join("\n", solvedWords);

    private string BuildExamProgressLine()
    {
        int word = wordCap > 0 ? Mathf.Min(wordsAttempted, wordCap) : wordsAttempted;
        if (word <= 0) return "";

        return $"Question\n{word} of {wordCap}";
    }

    private static void SetText(TextMeshProUGUI target, string value)
    {
        if (target != null && target.text != value) target.text = value;
    }

    /// <summary>The penalty-flash TMP field for the active mode's Phone block.</summary>
    private TextMeshProUGUI CurrentPenaltyFlashText()
    {
        switch (currentMode)
        {
            case GameMode.Solo: return soloUI.phone.penaltyFlashText;
            case GameMode.Group: return groupUI.phone.penaltyFlashText;
            case GameMode.Exam: return examUI.phone.penaltyFlashText;
            default: return null;
        }
    }

    private void HideAllFlashes()
    {
        if (soloUI.phone.penaltyFlashText != null) soloUI.phone.penaltyFlashText.gameObject.SetActive(false);
        if (groupUI.phone.penaltyFlashText != null) groupUI.phone.penaltyFlashText.gameObject.SetActive(false);
        if (examUI.phone.penaltyFlashText != null) examUI.phone.penaltyFlashText.gameObject.SetActive(false);

        // The Group flash shares screen space with the streak text; make sure the streak is visible again.
        if (groupUI.phone.streakText != null) groupUI.phone.streakText.gameObject.SetActive(true);
    }

    /// <summary>The TMP field that occupies the same spot as the active mode's flash line, and must hide while it's up.</summary>
    private TextMeshProUGUI CurrentFlashOverlayText()
    {
        switch (currentMode)
        {
            case GameMode.Group: return groupUI.phone.streakText;
            default: return null;
        }
    }

    /// <summary>Flashes a message on the active mode's Phone flash line; a new flash restarts the hide timer.</summary>
    private void ShowFlash(string message)
    {
        var target = CurrentPenaltyFlashText();
        if (target == null) return;
        target.text = message;
        target.gameObject.SetActive(true);

        var overlay = CurrentFlashOverlayText();
        if (overlay != null) overlay.gameObject.SetActive(false);

        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(HideFlashAfterDelay(target, overlay));
    }

    private IEnumerator HideFlashAfterDelay(TextMeshProUGUI target, TextMeshProUGUI overlay)
    {
        yield return new WaitForSeconds(flashDuration);
        if (target != null) target.gameObject.SetActive(false);
        if (overlay != null) overlay.gameObject.SetActive(true);
        flashRoutine = null;
    }

    /// <summary>
    /// Shows a short "3-2-1" countdown, then unpauses the timer and spawns letters.
    /// </summary>
    private IEnumerator CountdownCoroutine(bool skipCountdown = false) {
        // Start or reset the round�s target word
        StartNewRound();
        if (gameIsOver) yield break; // word cap reached; don't run a countdown into an ended game
        if (!skipCountdown)
        {
            
            if (countdownText != null) {
                countdownText.gameObject.SetActive(true);

                countdownText.text = "3";
                yield return new WaitForSeconds(1f);

                countdownText.text = "2";
                yield return new WaitForSeconds(1f);

                countdownText.text = "1";
                yield return new WaitForSeconds(1f);

                countdownText.gameObject.SetActive(false);
            }
        }

        // Now that the countdown is done, unpause the timer and spawn letters
        if (!gameIsOver) {
            isTimerPaused = false;
            if (spawnRoutine != null)
            {
                Debug.LogWarning("Spawn routine already running — not starting another.");
                yield break;
            }
            spawnRoutine = StartCoroutine(SpawnLettersRoutine());
        }
    }

    /// <summary>
    /// Reveals the round's puzzle: all 5 columns show their shuffled target letter at
    /// once (the anagram clue), parked idle until SpawnLettersRoutine activates them
    /// one at a time.
    /// </summary>
    private void SpawnInitialJumble()
    {
        char[] shuffled = BuildShuffledWordLetters();
        for (int i = 0; i < boxPositions.Length; i++)
        {
            LetterMovement letterMovement = InstantiateLetterObject(i, shuffled[i]);
            letterMovement.InitializeIdle(this, i, shuffled[i], boxPositions[i].position, letterSpeed);
            waitingLetters[i] = letterMovement;
        }
    }

    /// <summary>
    /// Activates one parked letter at a time until boxes are filled or game ends. Every
    /// unfilled column always has a parked letter waiting (seeded by SpawnInitialJumble,
    /// and re-seeded immediately by SpawnReplacementLetter whenever a column frees up),
    /// so this loop only ever needs to decide which parked letter goes next.
    /// </summary>
    private IEnumerator SpawnLettersRoutine() {
        SpawnInitialJumble();

        while (!AllBoxesFilled() && !gameIsOver)
        {
            List<int> eligibleIndices = new List<int>();
            for (int i = 0; i < boxPositions.Length; i++)
            {
                if (!boxFilled[i] && waitingLetters[i] != null)
                    eligibleIndices.Add(i);
            }

            // ✅ Nothing eligible? The one active letter hasn't resolved yet; wait for it.
            if (eligibleIndices.Count == 0)
            {
                yield return null;
                continue;
            }

            int selectedIndex;

            if (spawnMode == SpawnMode.Sequential)
            {
                int attempts = 0;
                do
                {
                    lastSpawnedColumn = (lastSpawnedColumn + 1) % boxPositions.Length;
                    selectedIndex = lastSpawnedColumn;
                    attempts++;
                }
                while ((!eligibleIndices.Contains(selectedIndex)) && attempts <= boxPositions.Length);
            }
            else // Random
            {
                selectedIndex = eligibleIndices[Random.Range(0, eligibleIndices.Count)];
            }

            // One column drops at a time: activate its parked letter, let its timer run
            // down and let it drop, then wait until THIS SPECIFIC letter is gone (landed
            // or erased) before picking the next one. Watching the object itself (rather
            // than the column) matters: a replacement gets parked into this same column
            // immediately once this letter resolves, so a column-based check would never
            // see the column go "free" and would stall the whole routine.
            float hangTime = preDropHangTime + Random.Range(minSpawnInterval, maxSpawnInterval);
            LetterMovement activated = waitingLetters[selectedIndex];
            waitingLetters[selectedIndex] = null;
            activeLetter = activated;
            activated.Activate(hangTime);

            yield return new WaitUntil(() => activated == null || gameIsOver);
            if (activeLetter == activated) activeLetter = null;
        }
    }

    /// <summary>
    /// Builds a full, no-repeat shuffle of the target word's letters (a true anagram),
    /// e.g. MORAL -> RMLAO. A plain uniform shuffle may coincidentally leave a letter
    /// in its own column, which is expected and required for the round to be solvable.
    /// </summary>
    private char[] BuildShuffledWordLetters()
    {
        char[] shuffled = (char[])targetLetters.Clone();
        for (int i = shuffled.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        return shuffled;
    }

    /// <summary>Instantiates a letter prefab above the given column showing the given letter.</summary>
    private LetterMovement InstantiateLetterObject(int index, char letter)
    {
        Vector3 spawnPos = GetSpawnPosAboveBox(index);
        GameObject newLetter = Instantiate(letterPrefab, spawnPos, Quaternion.identity);

        TextMeshPro textComp = newLetter.GetComponentInChildren<TextMeshPro>();
        if (textComp != null) {
            textComp.text = letter.ToString();
        }

        return newLetter.GetComponent<LetterMovement>();
    }

    /// <summary>True if `letter` is the correct answer for `boxIndex`'s position in the current word.</summary>
    public bool IsCorrectLetterForBox(int boxIndex, char letter)
    {
        if (boxIndex < 0 || boxIndex >= targetLetters.Length) return false;
        return letter == targetLetters[boxIndex];
    }

    /// <summary>
    /// Called by LetterMovement.Erase() when the erased letter was actually the correct
    /// answer for its box. Previously this was consequence-free (just a replacement letter
    /// for the same word), which meant an eraser that never moves -- an AFK player, or
    /// headless automation providing no input -- could park in front of a column and erase
    /// every correct letter that column ever receives, leaving that box permanently unfilled
    /// with no timer, strike, or word-cap progression able to end the round: a genuine
    /// stuck-forever state. Erasing a correct letter is now penalized the same way a wrong
    /// letter reaching the bottom is (time penalty in Solo, a strike in Group/Exam), and
    /// additionally forces the current word to end immediately so the round can never stall.
    /// </summary>
    public void OnCorrectLetterErased(int boxIndex, LetterMovement erasedInstance)
    {
        if (gameIsOver) return;

        // The erased letter may still be sitting in waitingLetters[boxIndex] (it was
        // never "activated"/picked yet). Clear that slot now so the still-running
        // spawnRoutine can't treat this fading-out letter as eligible and re-Activate() it.
        if (erasedInstance != null && waitingLetters[boxIndex] == erasedInstance)
            waitingLetters[boxIndex] = null;

        if (timed)
        {
            ApplyTimePenalty();
        }
        else if (ApplyStrike())
        {
            StartCoroutine(StrikeOut());
            return;
        }

        StartCoroutine(RestartGameRoutine());
    }

    /// <summary>
    /// Spawns this column's next letter immediately (idle/parked, not yet counting down)
    /// so the player can see it well before it's ever chosen to drop. Called the instant
    /// a column frees up: a wrong landing (from OnLetterArrived) or an erase (from
    /// LetterMovement.Erase, which passes itself as erasedInstance).
    /// </summary>
    public void SpawnReplacementLetter(int index, LetterMovement erasedInstance = null)
    {
        // The erased letter may itself have still been an unconsumed parked preview;
        // clear its stale slot first so the guard below doesn't think one already exists.
        if (erasedInstance != null && waitingLetters[index] == erasedInstance)
            waitingLetters[index] = null;

        if (gameIsOver || boxFilled[index] || waitingLetters[index] != null) return;

        char letterToSpawn = Random.value < chanceOfCorrectLetter
            ? targetLetters[index]
            : targetLetters[Random.Range(0, targetLetters.Length)];

        LetterMovement letterMovement = InstantiateLetterObject(index, letterToSpawn);
        letterMovement.InitializeIdle(this, index, letterToSpawn, boxPositions[index].position, letterSpeed);
        waitingLetters[index] = letterMovement;
    }

    /// <summary>Tracks letters currently hanging (pre-drop) so only one shakes at a time.</summary>
    public void RegisterHangingLetter(LetterMovement letter)
    {
        if (!hangingLetters.Contains(letter))
            hangingLetters.Add(letter);
    }

    public void UnregisterHangingLetter(LetterMovement letter)
    {
        hangingLetters.Remove(letter);
    }

    /// <summary>True if no other hanging letter is closer to dropping than this one.</summary>
    public bool IsNextToFall(LetterMovement letter)
    {
        foreach (var other in hangingLetters)
        {
            if (other != null && other != letter && other.HangTimeRemaining < letter.HangTimeRemaining)
                return false;
        }
        return true;
    }


    private void StartNewRound()
    {
        if (activeLetter != null) Destroy(activeLetter.gameObject);
        activeLetter = null;
        wordsAttempted++;

        if (wordCap > 0 && wordsAttempted > wordCap)
        {
            StartCoroutine(EndGame());
            return;
        }
        RefreshHud();

        QuestionAnswerPair question = SelectRandomQuestion();
        _currentQuestion = question;
        if (question != null)
        {
            targetWord = question.answer.ToLower(); // Ensure lowercase for consistency
            if (targetDefinitionText != null)
            {
                targetDefinitionText.text = questionLoader.GetDisplayPrompt(question, questionLoader.useDefinitions);
            }
            else
            {
                targetWord = "error";
                targetDefinitionText.text = "No valid 5-letter questions!";
            }

            // Reset box UI
            for (int i = 0; i < 5; i++)
            {
                targetLetters[i] = targetWord[i];
                boxFilled[i] = false;
                if (boxLetterDisplays[i] != null)
                {
                    boxLetterDisplays[i].text = " ";
                }
            }
            for (int i = 0; i < waitingLetters.Length; i++)
            {
                if (waitingLetters[i] != null) Destroy(waitingLetters[i].gameObject);
                waitingLetters[i] = null;
            }
        }
        else
        {
            Debug.LogWarning($"Invalid question selected. Length is {question.answer.Length}");
        }
    }

    public void ConfigureChallenge(ChallengeProfile profile)
    {
        ApplyModeRules(currentMode);

        minSpawnInterval = profile.minSpawnInterval;
        maxSpawnInterval = profile.maxSpawnInterval;
        chanceOfCorrectLetter = profile.chanceOfCorrectLetter;
        preDropHangTime = profile.preDropHangTime;

        // Solo is clock only, so the profile's timer duration only matters there
        if (currentMode == GameMode.Solo)
        {
            gameDuration = profile.timerDuration;
        }

        // Solo is clock only, so profile strikes/word caps apply to Group and Exam
        if (currentMode == GameMode.Group)
        {
            strikePool = profile.sharedStrikePool;
            wordCap = profile.maxWordAttempts;
        }
        else if (currentMode == GameMode.Exam)
        {
            strikePool = profile.examStrikePool;
            wordCap = profile.maxWordAttempts;
        }

        // Decide whether we're using definitions or questions
        bool useDefinitions = profile.promptType == ChallengeProfile.PromptType.Definitions;
        questionLoader.useDefinitions = useDefinitions;
        questionLoader.LoadQuestionsForMode(); // fallback
    }

    private QuestionAnswerPair SelectRandomQuestion()
    {
        if (questionLoader == null || questionLoader.currentQuestions == null || questionLoader.currentQuestions.Count == 0)
        {
            Debug.LogWarning("No loaded questions available. Returning default.");
            return new QuestionAnswerPair { question = "Missing data", answer = "error" };
        }

        return questionLoader.GetRandomQuestion();
    }

    private bool AllBoxesFilled() {
        int filledCount = 0;
        foreach (bool filled in boxFilled) {
            if (!filled) return false;
            filledCount++;
        }
        return true;
    }

    /// <summary>
    /// Called by LetterMovement when a letter reaches its box.
    /// </summary>
    public void OnLetterArrived(int boxIndex, char arrivedLetter, GameObject letterObj) { 
        if (gameIsOver) { 
            if (letterObj != null) Destroy(letterObj); 
            return;
        }
        
        if (boxIndex < 0 || boxIndex >= boxFilled.Length) { 
            if (letterObj != null) Destroy(letterObj); 
            return;
        }
        if (boxFilled[boxIndex]) { 
            // Already filled with a correct letter
            if (letterObj != null) Destroy(letterObj); 
            return;
        }

        // Check if correct letter
        bool correct = (arrivedLetter == targetLetters[boxIndex]);
        if (correct) {
            boxFilled[boxIndex] = true;
            if (boxLetterDisplays[boxIndex] != null) {
                boxLetterDisplays[boxIndex].text = arrivedLetter.ToString();
            }
            DestroyLettersOnSameX(letterObj.transform.position.x);
            if (letterObj != null) Destroy(letterObj);
        } else {
            // Incorrect letter that reached the bottom (the eraser didn't catch it)
            //   Solo         -> time penalty
            //   Group / Exam -> one strike from the pool; an empty pool ends the game
            if (timed) {
                ApplyTimePenalty();
            }
            else if (ApplyStrike()) {
                if (letterObj != null) Destroy(letterObj);
                StartCoroutine(StrikeOut());
                return;
            }

            // Not struck out: let the letter play its miss animation (red/bounce/fade)
            // instead of disappearing instantly.
            LetterMovement missedLetter = letterObj != null ? letterObj.GetComponent<LetterMovement>() : null;
            if (missedLetter != null) missedLetter.PlayMissEffect();
            else if (letterObj != null) Destroy(letterObj);

            SpawnReplacementLetter(boxIndex);
        }

        // If all boxes are filled, increase score & start next round
        if (AllBoxesFilled()) {
            if (questionLoader != null && _currentQuestion != null) questionLoader.MarkSuccess(_currentQuestion.answer);
            score++;
            solvedWords.Add(targetWord);
            if (currentMode == GameMode.Group) currentStreak++;
            if (currentMode == GameMode.Group) longestStreak = Mathf.Max(longestStreak, currentStreak);
            RefreshHud();
            StartCoroutine(RestartGameRoutine());
        }

        // If timer is out, end game
        if (timed && timeLeft <= 0 && !gameIsOver) {
            StartCoroutine(EndGame());
        }
    }

    private void ApplyTimePenalty()
    {
        timeLeft = Mathf.Max(0f, timeLeft - penaltyTime);
        RefreshHud();
        int p = Mathf.RoundToInt(penaltyTime);
        ShowFlash(string.Format("-{0}:{1:00}", p / 60, p % 60));
    }

    /// <summary>Spends one strike. Returns true when the pool is empty (struck out).</summary>
    private bool ApplyStrike()
    {
        if (strikePool <= 0) return false; // strikes disabled

        strikesUsed++;
        if (currentMode == GameMode.Group) currentStreak = 0;
        RefreshHud();

        int left = StrikesLeft;
        if (gradeAtStake)
        {
            float before = GradeCalculator.ExamHalfPointsFromPercent(GradeCalculator.ExamPercent(strikesUsed - 1, strikePool));
            float after = GradeCalculator.ExamHalfPointsFromPercent(GradeCalculator.ExamPercent(strikesUsed, strikePool));
            ShowFlash($"GRADE AFFECTED\n-{(before - after).ToString("0.0")}");
        }
        else
        {
            ShowFlash(left == 1 ? "One more mistake ends the session!" : "Streak Broken");
        }
        return left <= 0;
    }

    private IEnumerator StrikeOut()
    {
        gameIsOver = true; // stop further letters from landing (and striking again) while the flash plays
        if (spawnRoutine != null)
        {
            StopCoroutine(spawnRoutine);
            spawnRoutine = null;
        }
        // Struck out mid-word: that word counts as a miss for mastery tracking
        if (questionLoader != null && _currentQuestion != null) questionLoader.MarkFail(_currentQuestion.answer);

        yield return new WaitForSeconds(flashDuration * 0.6f); // let "Struck out" land
        StartCoroutine(EndGame());
    }

    /// <summary>
    /// Destroy all letters at a given x-position (to remove duplicates).
    /// </summary>
    private void DestroyLettersOnSameX(float xPosition) {
        GameObject[] allLetters = GameObject.FindGameObjectsWithTag("Letter");
        foreach (GameObject letter in allLetters) {
            if (Mathf.Abs(letter.transform.position.x - xPosition) < 0.1f) {
                Destroy(letter);
            }
        }
    }

    /// <summary>
    /// After a short delay, pause the timer, clear boxes, do a new countdown, then unpause.
    /// </summary>
    private IEnumerator RestartGameRoutine() {
        // Stop the old spawn loop immediately so it can't keep running (and activating
        // stale letters from the word we're leaving) during the delay below.
        if (spawnRoutine != null)
        {
            StopCoroutine(spawnRoutine);
            spawnRoutine = null;
        }

        //half the time between spawns
        minSpawnInterval = Mathf.Max(minSpawnInterval * 0.9f, 0.3f); // limit how fast it gets
        maxSpawnInterval = Mathf.Max(maxSpawnInterval * 0.9f, 1f);
        // Wait briefly so the player can see the filled boxes
        yield return new WaitForSeconds(2f);

        if (!gameIsOver) {
            // Solved the last word of a Group/Exam session
            if (wordCap > 0 && wordsAttempted >= wordCap) {
                StartCoroutine(EndGame());
                yield break;
            }

            // Pause timer during the countdown
            isTimerPaused = true;
            // Clear boxes
            for (int i = 0; i < 5; i++) {
                if (boxLetterDisplays[i] != null) {
                    boxLetterDisplays[i].text = " ";
                }
                boxFilled[i] = false;
            }
            // Run another "3-2-1" countdown, which will unpause the timer again
            StartCoroutine(CountdownCoroutine(false));
        }
    }

    /// <summary>
    /// Ends the game, stops coroutines, and shows the Game Over panel.
    /// </summary>
    private IEnumerator EndGame()
    {
        FMODAudioManager.Instance?.PopMusic();

        gameIsOver = true;
        activeLetter = null;
        if (spawnRoutine != null)
            StopCoroutine(spawnRoutine);
        spawnRoutine = null;
        clearLeftoverLetters();

        // Read the exam id before clearing it: the grade is recorded against it.
        string examId = StatsManager.Get_String_Stat("CurrentExamId");

        if (currentMode == GameMode.Exam)
        {
            // Exam result = recorded against strikes used vs. this exam's strike pool
            GradeCalculator.RecordExam(examId, strikesUsed, strikePool);
        }
        else
        {
            float existing = StatsManager.Get_Numbered_Stat("StudyGameScore");
            if (score > existing)
                StatsManager.Set_Numbered_Stat("StudyGameScore", score);

            // Before any exam is taken the class grade tracks the study score
            GradeCalculator.RefreshGrades();
        }
        // Mark exam event complete and clear the stale exam ID so non-exam study
        // sessions don't accidentally re-complete a future exam.
        if (!string.IsNullOrEmpty(examId))
        {
            GameEvents.MarkCustomEventCompleted(examId, true);
            StatsManager.Set_String_Stat("CurrentExamId", "");
        }

        // Hold a completion message before hiding/transitioning away
        string endMessage = currentMode == GameMode.Exam ? "Exam Complete" : "Study Session Complete";
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);
            countdownText.text = endMessage;
        }
        yield return new WaitForSeconds(5f);
        if (countdownText != null)
            countdownText.gameObject.SetActive(false);

        // Branch by context
        if (VNSceneManager.scene_manager != null)
        {
            VNSceneManager.scene_manager.Show_UI(true);
            switch (currentMode)
            {
                case GameMode.Exam:
                    VNSceneManager.scene_manager.Start_Conversation(pendingEndConversation);
                    break;
                case GameMode.Group:
                    StatsManager.Set_Numbered_Stat("GroupStudyLongestStreak", longestStreak);
                    StatsManager.Set_String_Stat("StudyGameScore", longestStreak.ToString());
                    VNSceneManager.scene_manager.Start_Conversation(conversationManager);
                    break;
                case GameMode.Solo:
                    HomeCutsceneController.Instance?.OnStudyComplete();
                    yield break;
            }
        }
        else
        {
            // Fallback: no VN manager present (Home.unity Solo context)
            HomeCutsceneController.Instance?.OnStudyComplete();
            yield break;
        }

        if (questionLoader != null && questionLoader.currentQuestions != null)
        {
            foreach (var q in questionLoader.currentQuestions)
                q.alreadyUsed = false;
        }

        studyGameParent.SetActive(false);
        pendingChallengeProfile = null;
        pendingEndConversation  = null;

    }
    
    /// <summary>
    /// Destroys any leftover letters still on the screen.
    /// </summary>
    private void clearLeftoverLetters() {
        var leftoverLetters = GameObject.FindGameObjectsWithTag("Letter");
        foreach (var letter in leftoverLetters) {
            Destroy(letter);
        }

        foreach (var box in boxVisuals)
        {
            Destroy(box);
        }
        Destroy(eraser);
    }

    public void SetMode(GameMode mode)
    {
        currentMode = mode;
        if (questionLoader != null)
            questionLoader.currentMode = mode;

        ApplyModeRules(mode, force: true);
        ActivateUiForMode(mode);

        if (mode == GameMode.Group)
        {
            GroupStudyManager groupStudyManager = FindAnyObjectByType<GroupStudyManager>();
            if (groupStudyManager != null)
                conversationManager = groupStudyManager.conversationManager;
        }
    }

    private GameMode? rulesAppliedFor;

    /// <summary>
    /// Sets the per-mode rules. Without force, does nothing if this mode's rules are already
    /// active so profile overrides from ConfigureChallenge survive StartGame.
    /// </summary>
    private void ApplyModeRules(GameMode mode, bool force = false)
    {
        if (!force && rulesAppliedFor == mode) return;
        rulesAppliedFor = mode;

        switch (mode)
        {
            case GameMode.Solo:
                spawnMode = SpawnMode.Random;
                timed = true;
                strikePool = 0;
                wordCap = 0; // clock only
                gradeAtStake = false;
                if (questionLoader != null) questionLoader.useDefinitions = false;
                break;

            case GameMode.Group:
                spawnMode = SpawnMode.Random;
                timed = false;
                strikePool = defaultGroupStrikePool;
                wordCap = maxWordAttempts;
                gradeAtStake = false;
                if (questionLoader != null) questionLoader.useDefinitions = true;
                break;

            case GameMode.Exam:
                spawnMode = SpawnMode.Random;
                timed = false;
                strikePool = defaultExamStrikePool;
                wordCap = maxWordAttempts;
                gradeAtStake = true;
                break;
        }
    }

}
